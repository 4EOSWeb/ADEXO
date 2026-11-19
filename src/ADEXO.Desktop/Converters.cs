using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ADEXO.Desktop;

/// <summary>Maps a semantic severity to a brush, so view models never reference UI types.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    private static readonly Brush Normal = Freeze("#1F1F1F");
    private static readonly Brush Muted = Freeze("#6B6B6B");
    private static readonly Brush Good = Freeze("#107C10");
    private static readonly Brush Warning = Freeze("#9D5D00");
    private static readonly Brush Critical = Freeze("#A4262C");

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            DetailSeverity.Good => Good,
            DetailSeverity.Warning => Warning,
            DetailSeverity.Critical => Critical,
            DetailSeverity.Muted => Muted,
            _ => Normal,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}

/// <summary>True to Visible, false to Collapsed. Used for busy and content states.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string to Visible. Used for optional footnotes and messages.</summary>
public sealed class EmptyToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
