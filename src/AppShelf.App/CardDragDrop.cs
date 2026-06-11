using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using AppShelf.App.ViewModels;
using AppShelf.Core.Models;

namespace AppShelf.App;

/// <summary>
/// Drag-and-drop layer for the card grid (Slice 2). Wired onto each card's root
/// <see cref="Grid"/> (app and group cards; member cards still use a <see cref="Border"/>) via
/// the <see cref="IsCardAttached"/> attached property and onto the items host via
/// <see cref="IsCanvasAttached"/>.
///
/// Drag source: arms on left-button-down (recording the point + the card's app id), starts a
/// <see cref="DragDrop.DoDragDrop"/> once the pointer moves past the system drag threshold. Drags
/// are suppressed when the gesture starts on a <see cref="ButtonBase"/> so the card's own
/// Launch/Stop/Open/Logs/Edit/Remove buttons keep working.
///
/// Drop target: resolves a <see cref="DropTarget"/> from the element under the cursor and calls
/// <see cref="MainViewModel.Regroup"/>. Affordances (lift, highlight + hint, insertion line) are
/// drawn with adorners; invalid drops report <see cref="DragDropEffects.None"/>.
/// </summary>
public static class CardDragDrop
{
    /// <summary>The <see cref="DataObject"/> format carrying the dragged app id.</summary>
    public const string AppIdFormat = "appId";

    // --- Drag-arming state (a single in-flight gesture at a time) ---
    private static Point _startPoint;
    private static string? _armedAppId;
    private static FrameworkElement? _armedCard;
    private static bool _dragging;

    // --- Active affordance adorners (cleaned up on drag end / leave) ---
    private static LiftAdorner? _lift;
    private static HighlightAdorner? _highlight;
    private static InsertionLineAdorner? _insertion;

    // --- Cursor-following ghost + the source card's pre-drag opacity (restored on drag end) ---
    private static DragGhostWindow? _ghost;
    private static double _sourceOpacity = 1.0;

    /// <summary>Move/grab cursor shown while dragging and when hovering a draggable card body.</summary>
    private static readonly Cursor GrabCursor = Cursors.SizeAll;

    // ------------------------------------------------------------------ attached properties

    /// <summary>Set true on a card's root Border to make it a drag source + drop target.</summary>
    public static readonly DependencyProperty IsCardAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsCardAttached", typeof(bool), typeof(CardDragDrop),
            new PropertyMetadata(false, OnIsCardAttachedChanged));

    public static void SetIsCardAttached(DependencyObject o, bool v) => o.SetValue(IsCardAttachedProperty, v);
    public static bool GetIsCardAttached(DependencyObject o) => (bool)o.GetValue(IsCardAttachedProperty);

    /// <summary>Set true on the items host so empty/background drops resolve to <see cref="OnCanvas"/>.</summary>
    public static readonly DependencyProperty IsCanvasAttachedProperty =
        DependencyProperty.RegisterAttached(
            "IsCanvasAttached", typeof(bool), typeof(CardDragDrop),
            new PropertyMetadata(false, OnIsCanvasAttachedChanged));

    public static void SetIsCanvasAttached(DependencyObject o, bool v) => o.SetValue(IsCanvasAttachedProperty, v);
    public static bool GetIsCanvasAttached(DependencyObject o) => (bool)o.GetValue(IsCanvasAttachedProperty);

    // ------------------------------------------------------------------ wiring

    private static void OnIsCardAttachedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
            return;

        if ((bool)e.NewValue)
        {
            fe.PreviewMouseLeftButtonDown += Card_PreviewMouseLeftButtonDown;
            fe.PreviewMouseMove += Card_PreviewMouseMove;
            fe.QueryCursor += Card_QueryCursor;
            fe.AllowDrop = true;
            fe.DragOver += Card_DragOver;
            fe.DragLeave += Card_DragLeave;
            fe.Drop += Card_Drop;
            fe.GiveFeedback += Card_GiveFeedback;
        }
        else
        {
            fe.PreviewMouseLeftButtonDown -= Card_PreviewMouseLeftButtonDown;
            fe.PreviewMouseMove -= Card_PreviewMouseMove;
            fe.QueryCursor -= Card_QueryCursor;
            fe.DragOver -= Card_DragOver;
            fe.DragLeave -= Card_DragLeave;
            fe.Drop -= Card_Drop;
            fe.GiveFeedback -= Card_GiveFeedback;
        }
    }

    private static void OnIsCanvasAttachedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
            return;

        if ((bool)e.NewValue)
        {
            fe.AllowDrop = true;
            fe.DragOver += Canvas_DragOver;
            fe.Drop += Canvas_Drop;
        }
        else
        {
            fe.DragOver -= Canvas_DragOver;
            fe.Drop -= Canvas_Drop;
        }
    }

    // ------------------------------------------------------------------ drag source

    private static void Card_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Don't start a drag from the card's own buttons (Launch/Stop/Edit/…) — let them click.
        if (IsOnInteractiveControl(e.OriginalSource as DependencyObject))
        {
            _armedCard = null;
            _armedAppId = null;
            return;
        }

        if (sender is not FrameworkElement card)
            return;

        // Only standalone cards and group MEMBERS carry an app id worth dragging. Group cards
        // themselves are not draggable (you drag apps onto them, not them around).
        _armedAppId = card.DataContext switch
        {
            AppCardViewModel app => app.Entry.Id,
            _ => null,
        };
        if (_armedAppId is null)
            return;

        _armedCard = card;
        _startPoint = e.GetPosition(null);
    }

    private static void Card_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging || _armedAppId is null || _armedCard is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _startPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _startPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var card = _armedCard;
        var appId = _armedAppId;
        _dragging = true;
        _lift = LiftAdorner.Attach(card);

        // Dim the source card so it reads as "lifted out" while the ghost trails the cursor.
        _sourceOpacity = card.Opacity;
        card.Opacity = 0.55;

        // Snapshot the card into a cursor-following ghost (grab point keeps it under the cursor).
        _ghost = DragGhostWindow.Show(card, e.GetPosition(card));
        _ghost?.MoveToCursor();

        try
        {
            DragDrop.DoDragDrop(card, new DataObject(AppIdFormat, appId), DragDropEffects.Move);
        }
        finally
        {
            _ghost?.Dispose();
            _ghost = null;
            card.Opacity = _sourceOpacity;
            ClearAdorners();
            _dragging = false;
            _armedCard = null;
            _armedAppId = null;
        }
    }

    /// <summary>Show the move/grab cursor when hovering a draggable area of the card (anywhere that is
    /// not a button or text control), so the whole card body is discoverable as a drag handle.</summary>
    private static void Card_QueryCursor(object sender, QueryCursorEventArgs e)
    {
        if (_dragging)
            return; // GiveFeedback owns the cursor during the drag.
        if (IsOnInteractiveControl(e.OriginalSource as DependencyObject))
            return; // Buttons/text keep their own (Hand / I-beam) cursor so they stay clickable.

        e.Cursor = GrabCursor;
        e.Handled = true;
    }

    /// <summary>During the drag: keep the grab cursor and trail the ghost to the OS cursor. WPF's
    /// drag loop supplies no pointer position, so the ghost reads it from Win32 GetCursorPos.</summary>
    private static void Card_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        e.UseDefaultCursors = false;
        Mouse.SetCursor(GrabCursor);
        _ghost?.MoveToCursor();
        e.Handled = true;
    }

    // ------------------------------------------------------------------ drop target: cards

    private static void Card_DragOver(object sender, DragEventArgs e)
    {
        _ghost?.MoveToCursor();
        if (sender is not FrameworkElement card || !TryGetDraggedId(e, out var draggedId))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var (target, reorderInsert, suppressHighlight) = ResolveCardTarget(card, draggedId, e);
        var valid = IsValidDrop(card, draggedId, target);

        e.Effects = valid ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;

        if (!valid)
        {
            ClearAdorners();
            return;
        }

        if (reorderInsert is { } ins)
        {
            ClearHighlight();
            EnsureInsertionLine(ins.Container, ins.Y);
        }
        else if (suppressHighlight)
        {
            // No-op hover (e.g. over the dragged app's own group): don't imply a join.
            ClearInsertion();
            ClearHighlight();
        }
        else
        {
            ClearInsertion();
            EnsureHighlight(card, HintFor(card, draggedId));
        }
    }

    private static void Card_DragLeave(object sender, DragEventArgs e)
    {
        // Only clear when truly leaving the card (DragLeave also fires moving onto children).
        if (sender is FrameworkElement card && !IsStillInside(card, e))
        {
            ClearHighlight();
            ClearInsertion();
        }
    }

    private static void Card_Drop(object sender, DragEventArgs e)
    {
        ClearAdorners();
        if (sender is not FrameworkElement card || !TryGetDraggedId(e, out var draggedId))
            return;

        var (target, _, _) = ResolveCardTarget(card, draggedId, e);
        if (target is null || !IsValidDrop(card, draggedId, target))
        {
            e.Handled = true;
            return;
        }

        ViewModelOf(card)?.Regroup(draggedId, target);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ drop target: canvas

    private static void Canvas_DragOver(object sender, DragEventArgs e)
    {
        _ghost?.MoveToCursor();
        // Cards handle their own DragOver and mark it handled; an unhandled event here means the
        // pointer is over the background → leave-group / no-op.
        if (!TryGetDraggedId(e, out _))
        {
            e.Effects = DragDropEffects.None;
            return;
        }
        ClearHighlight();
        ClearInsertion();
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private static void Canvas_Drop(object sender, DragEventArgs e)
    {
        ClearAdorners();
        if (sender is not FrameworkElement host || !TryGetDraggedId(e, out var draggedId))
            return;

        ViewModelOf(host)?.Regroup(draggedId, new OnCanvas());
        e.Handled = true;
    }

    // ------------------------------------------------------------------ target resolution

    private readonly record struct Insertion(UIElement Container, double Y);

    /// <summary>Resolve the <see cref="DropTarget"/> for a drop landing on <paramref name="card"/>.
    /// Returns an optional insertion hint when the drop is a within-group reorder so the caller can
    /// draw the insertion line, plus a <c>SuppressHighlight</c> flag for no-op hovers (e.g. hovering
    /// over a card's OWN group) where the "＋ add to group" highlight would be misleading.</summary>
    private static (DropTarget? Target, Insertion? Reorder, bool SuppressHighlight) ResolveCardTarget(
        FrameworkElement card, string draggedId, DragEventArgs e)
    {
        switch (card.DataContext)
        {
            case GroupCardViewModel group:
            {
                // Dropping onto a group card. If the dragged app already belongs to this group and
                // the pointer is over the expanded member list, treat it as a reorder.
                var sameGroup = IsMemberOfGroup(card, draggedId, group.Name);
                if (sameGroup
                    && TryResolveReorder(card, group, draggedId, e, out var reorder, out var insert))
                    return (reorder, insert, false);

                // Glitch (a): hovering a card over its OWN group (but not over the member list) is a
                // no-op join — keep the target for the drop, but suppress the "＋ add to group" hint.
                return (new OnGroup(group.Name), null, sameGroup);
            }

            case AppCardViewModel app when string.IsNullOrWhiteSpace(app.Entry.Group):
                // Standalone card → group the two apps (or pull the standalone into the dragged's
                // group; the planner decides).
                return (new OnApp(app.Entry.Id), null, false);

            case AppCardViewModel app:
            {
                // A member card inside a group.
                var groupName = app.Entry.Group!.Trim();
                var draggedSameGroup = IsMemberOfGroup(card, draggedId, groupName);
                if (draggedSameGroup)
                {
                    // Glitch (b): within-group reorder — route through the DRAGGED app's role peers
                    // (like the group-card path), not the hovered member's band, so the insertion
                    // line lands in the correct role band and matches the computed index.
                    if (TryResolveMemberReorder(card, groupName, draggedId, e,
                            out var memberReorder, out var memberInsert))
                        return (memberReorder, memberInsert, false);

                    // Dragged app is in this group but has no reorderable peers (or the list could
                    // not be resolved) — a no-op; suppress the highlight rather than implying a join.
                    return (new OnGroup(groupName), null, true);
                }
                // Cross-group: dropping onto a member joins the member's group.
                return (new OnGroup(groupName), null, false);
            }

            default:
                return (null, null, false);
        }
    }

    /// <summary>When dragging a member already in <paramref name="group"/> over the group card's
    /// expanded member list, compute the target index from the nearest member boundary.</summary>
    private static bool TryResolveReorder(
        FrameworkElement card, GroupCardViewModel group, string draggedId, DragEventArgs e,
        out DropTarget? target, out Insertion? insertion)
    {
        target = null;
        insertion = null;

        var dragged = group.Members.FirstOrDefault(m => m.Entry.Id == draggedId);
        if (dragged is null)
            return false;

        var role = dragged.Entry.Role;
        var membersList = FindMembersItemsControl(card);
        if (membersList is null)
            return false;

        // Role peers in display order; the planner spreads Order within the role band.
        var peers = group.Members.Where(m => m.Entry.Role == role).ToList();
        if (peers.Count < 2)
            return false; // nothing to reorder

        var index = NearestPeerIndex(membersList, peers, draggedId, e);
        target = new AtMemberIndex(group.Name, index);
        insertion = new Insertion(membersList, InsertionYForIndex(membersList, peers, index, e));
        return true;
    }

    /// <summary>Glitch (b): when hovering a MEMBER card whose group the dragged app already belongs
    /// to, resolve the within-group reorder through the dragged app's role peers (delegating to the
    /// same <see cref="TryResolveReorder"/> path the group card uses) instead of the hovered member's
    /// own band. Walks up to the owning group card to find the shared member list.</summary>
    private static bool TryResolveMemberReorder(
        FrameworkElement memberCard, string groupName, string draggedId, DragEventArgs e,
        out DropTarget? target, out Insertion? insertion)
    {
        target = null;
        insertion = null;

        var vm = ViewModelOf(memberCard);
        var group = vm?.Tiles.OfType<GroupCardViewModel>()
            .FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return false;

        var groupCard = FindAncestorGroupCard(memberCard);
        if (groupCard is null)
            return false;

        return TryResolveReorder(groupCard, group, draggedId, e, out target, out insertion);
    }

    /// <summary>Walk up the visual tree from a member card to the enclosing group card's root element
    /// (the one whose DataContext is a <see cref="GroupCardViewModel"/>).</summary>
    private static FrameworkElement? FindAncestorGroupCard(DependencyObject from)
    {
        var node = VisualTreeHelper.GetParent(from);
        while (node is not null)
        {
            if (node is FrameworkElement fe && fe.DataContext is GroupCardViewModel)
                return fe;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    // ------------------------------------------------------------------ validity + hints

    private static bool IsValidDrop(FrameworkElement card, string draggedId, DropTarget? target)
    {
        if (target is null)
            return false;

        return target switch
        {
            // Dropping a card onto itself.
            OnApp a => a.AppId != draggedId,
            // Joining the group the dragged app is already the sole representative of is allowed
            // (planner returns NoOp harmlessly); but block the obvious self case via the VM check.
            _ => true,
        };
    }

    private static string HintFor(FrameworkElement card, string draggedId) => card.DataContext switch
    {
        GroupCardViewModel => "＋ add to group",
        AppCardViewModel app when !string.IsNullOrWhiteSpace(app.Entry.Group) => "＋ add to group",
        AppCardViewModel => "＋ new group",
        _ => "",
    };

    // ------------------------------------------------------------------ helpers

    private static bool TryGetDraggedId(DragEventArgs e, out string draggedId)
    {
        draggedId = "";
        if (!e.Data.GetDataPresent(AppIdFormat))
            return false;
        if (e.Data.GetData(AppIdFormat) is not string id || string.IsNullOrEmpty(id))
            return false;
        draggedId = id;
        return true;
    }

    /// <summary>True when <paramref name="source"/> is, or sits within, a button/clickable control
    /// so we don't hijack its click as a drag.</summary>
    private static bool IsOnInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            // A TextBox (e.g. the logs panel) should keep normal mouse handling too.
            if (source is TextBox)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    /// <summary>The owning <see cref="MainViewModel"/> (the window's DataContext).</summary>
    private static MainViewModel? ViewModelOf(DependencyObject element) =>
        Window.GetWindow(element)?.DataContext as MainViewModel;

    /// <summary>True when the dragged app is a member of the named group (so a drop on that group
    /// is a within-group gesture rather than a join).</summary>
    private static bool IsMemberOfGroup(DependencyObject element, string draggedId, string groupName)
    {
        var vm = ViewModelOf(element);
        if (vm is null) return false;
        foreach (var tile in vm.Tiles)
        {
            if (tile is GroupCardViewModel g && string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase))
                return g.Members.Any(m => m.Entry.Id == draggedId);
        }
        return false;
    }

    private static int MemberRoleIndex(DependencyObject element, string groupName, AppEntry member)
    {
        var vm = ViewModelOf(element);
        if (vm is null) return 0;
        var group = vm.Tiles.OfType<GroupCardViewModel>()
            .FirstOrDefault(g => string.Equals(g.Name, groupName, StringComparison.OrdinalIgnoreCase));
        if (group is null) return 0;
        var peers = group.Members.Where(m => m.Entry.Role == member.Role).ToList();
        var idx = peers.FindIndex(m => m.Entry.Id == member.Id);
        return idx < 0 ? 0 : idx;
    }

    private static double MemberInsertionY(FrameworkElement card, DragEventArgs e)
    {
        var p = e.GetPosition(card);
        return p.Y < card.ActualHeight / 2 ? 0 : card.ActualHeight;
    }

    /// <summary>Find the ItemsControl hosting a group card's member cards.</summary>
    private static ItemsControl? FindMembersItemsControl(DependencyObject root)
    {
        // The members ItemsControl is the one bound to a GroupCardViewModel whose items are app cards.
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ItemsControl ic && ic.Items.Count > 0 && ic.Items[0] is AppCardViewModel)
                return ic;
            var nested = FindMembersItemsControl(child);
            if (nested is not null)
                return nested;
        }
        return null;
    }

    /// <summary>Index among role peers nearest the drop point inside the member list.</summary>
    private static int NearestPeerIndex(ItemsControl list, List<AppCardViewModel> peers, string draggedId, DragEventArgs e)
    {
        for (var i = 0; i < peers.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromItem(peers[i]) is FrameworkElement container)
            {
                var top = container.TranslatePoint(new Point(0, 0), list).Y;
                var mid = top + container.ActualHeight / 2;
                if (e.GetPosition(list).Y < mid)
                    return i;
            }
        }
        return peers.Count;
    }

    private static double InsertionYForIndex(ItemsControl list, List<AppCardViewModel> peers, int index, DragEventArgs e)
    {
        if (peers.Count == 0)
            return 0;
        if (index < peers.Count &&
            list.ItemContainerGenerator.ContainerFromItem(peers[index]) is FrameworkElement top)
            return top.TranslatePoint(new Point(0, 0), list).Y;
        if (list.ItemContainerGenerator.ContainerFromItem(peers[^1]) is FrameworkElement last)
            return last.TranslatePoint(new Point(0, 0), list).Y + last.ActualHeight;
        return 0;
    }

    /// <summary>True while the pointer is still over <paramref name="card"/> (DragLeave fires when
    /// entering child elements too).</summary>
    private static bool IsStillInside(FrameworkElement card, DragEventArgs e)
    {
        var p = e.GetPosition(card);
        return p.X >= 0 && p.Y >= 0 && p.X <= card.ActualWidth && p.Y <= card.ActualHeight;
    }

    // ------------------------------------------------------------------ adorner lifecycle

    private static void EnsureHighlight(UIElement card, string hint)
    {
        if (_highlight?.AdornedElement == card)
            return;
        ClearHighlight();
        _highlight = HighlightAdorner.Attach(card, hint);
    }

    private static void EnsureInsertionLine(UIElement container, double y)
    {
        if (_insertion?.AdornedElement != container)
        {
            ClearInsertion();
            _insertion = InsertionLineAdorner.Attach(container);
        }
        _insertion?.SetY(y);
    }

    private static void ClearHighlight() { _highlight?.Detach(); _highlight = null; }
    private static void ClearInsertion() { _insertion?.Detach(); _insertion = null; }

    private static void ClearAdorners()
    {
        ClearHighlight();
        ClearInsertion();
        _lift?.Detach();
        _lift = null;
    }
}
