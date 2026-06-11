using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AppShelf.Core.Models;

namespace AppShelf.App;

/// <summary>Brush helpers for the converters below.</summary>
internal static class FrozenBrush
{
    /// <summary>A frozen (immutable, render-thread-shareable) solid brush — cheaper than an
    /// unfrozen one when the same instance is bound into many cards and animated subtrees.</summary>
    public static SolidColorBrush Of(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>LaunchStatus → a colored brush for the status dot.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = FrozenBrush.Of(0x34, 0xC9, 0x8E);
    private static readonly SolidColorBrush Starting = FrozenBrush.Of(0xE5, 0xA8, 0x4B);
    private static readonly SolidColorBrush Error = FrozenBrush.Of(0xE5, 0x64, 0x5A);
    private static readonly SolidColorBrush Stopped = FrozenBrush.Of(0x56, 0x5B, 0x66);

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        LaunchStatus.Running => Running,
        LaunchStatus.Starting => Starting,
        LaunchStatus.Error or LaunchStatus.PortInUse => Error,
        _ => Stopped,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>bool → Visibility. Pass ConverterParameter="invert" to flip.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
            flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>GroupAggregateStatus → a colored brush for the group status pill.</summary>
public sealed class GroupStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Running = FrozenBrush.Of(0x34, 0xC9, 0x8E);
    private static readonly SolidColorBrush Starting = FrozenBrush.Of(0x7C, 0x6A, 0xEF);
    private static readonly SolidColorBrush Partial = FrozenBrush.Of(0xE5, 0xA8, 0x4B);
    private static readonly SolidColorBrush Stopped = FrozenBrush.Of(0x56, 0x5B, 0x66);

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        GroupAggregateStatus.Running => Running,
        GroupAggregateStatus.Starting => Starting,
        GroupAggregateStatus.Partial => Partial,
        _ => Stopped,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible, else Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
