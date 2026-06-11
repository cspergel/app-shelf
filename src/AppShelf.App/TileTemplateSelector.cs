using System.Windows;
using System.Windows.Controls;
using AppShelf.App.ViewModels;

namespace AppShelf.App;

/// <summary>Picks the group-card template for <see cref="GroupCardViewModel"/> and the
/// existing app-card template for <see cref="AppCardViewModel"/> in the main tile list.</summary>
public sealed class TileTemplateSelector : DataTemplateSelector
{
    public DataTemplate? GroupTemplate { get; set; }
    public DataTemplate? AppTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        GroupCardViewModel => GroupTemplate,
        AppCardViewModel => AppTemplate,
        _ => base.SelectTemplate(item, container),
    };
}
