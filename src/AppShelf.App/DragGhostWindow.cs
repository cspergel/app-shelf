using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AppShelf.App;

/// <summary>
/// A cursor-following "drag ghost": a borderless, transparent, topmost, click-through window that
/// shows a semi-transparent snapshot of the card being dragged. WPF's <see cref="DragDrop.DoDragDrop"/>
/// renders no content preview, so we draw our own and reposition it to the OS cursor on every
/// <c>GiveFeedback</c>/<c>DragOver</c> tick.
///
/// Lifecycle: <see cref="Show"/> on drag start, <see cref="MoveToCursor"/> while dragging, and
/// <see cref="Dispose"/> in the drag's <c>finally</c>. Click-through is achieved with
/// <c>WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE</c> so the ghost never
/// intercepts hit-testing, never steals focus, and never appears in the alt-tab list.
/// </summary>
internal sealed class DragGhostWindow : IDisposable
{
    private readonly Window _window;
    private readonly double _dipWidth;
    private readonly double _dipHeight;
    // Cursor offset (DIPs) so the snapshot tracks where the user grabbed, not its top-left corner.
    private readonly double _offsetX;
    private readonly double _offsetY;
    private bool _disposed;

    private DragGhostWindow(ImageSource snapshot, double dipWidth, double dipHeight,
                            double offsetX, double offsetY)
    {
        _dipWidth = dipWidth;
        _dipHeight = dipHeight;
        _offsetX = offsetX;
        _offsetY = offsetY;

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            IsHitTestVisible = false,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.Manual,
            Width = dipWidth,
            Height = dipHeight,
            // Position is driven manually from the cursor; start off-screen to avoid a flash at (0,0).
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
            Content = new System.Windows.Controls.Image
            {
                Source = snapshot,
                Stretch = Stretch.Fill,
                Opacity = 0.72,
                // A soft accent glow so the ghost reads as a "lifted" card, matching the dark theme.
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0x7C, 0x6A, 0xEF),
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.55,
                },
            },
        };

        _window.SourceInitialized += (_, _) => MakeClickThrough();
    }

    /// <summary>Render <paramref name="card"/> to a bitmap and show the ghost. Returns null if the
    /// card cannot be snapshotted (zero size); the drag then simply proceeds without a ghost.</summary>
    public static DragGhostWindow? Show(FrameworkElement card, Point grabPointInCard)
    {
        var w = card.ActualWidth;
        var h = card.ActualHeight;
        if (w < 1 || h < 1)
            return null;

        var dpi = VisualTreeHelper.GetDpi(card);
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(w * dpi.DpiScaleX),
            (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(card);
        bmp.Freeze();

        // Keep the grab offset inside the card so the snapshot stays under the cursor.
        var offX = Math.Clamp(grabPointInCard.X, 0, w);
        var offY = Math.Clamp(grabPointInCard.Y, 0, h);

        var ghost = new DragGhostWindow(bmp, w, h, offX, offY);
        ghost._window.Show();
        return ghost;
    }

    /// <summary>Reposition the ghost so the original grab point sits under the OS cursor.</summary>
    public void MoveToCursor()
    {
        if (_disposed || !GetCursorPos(out var pt))
            return;

        // Cursor is in physical pixels; the window's Left/Top are in DIPs. Convert via the source.
        var source = PresentationSource.FromVisual(_window);
        var m = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var dip = m.Transform(new Point(pt.X, pt.Y));

        _window.Left = dip.X - _offsetX;
        _window.Top = dip.Y - _offsetY;
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(_window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _window.Close();
    }

    // ------------------------------------------------------------------ Win32

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
