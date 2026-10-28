using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ThreeMfCompare.App;

/// <summary>True when the bound enum value is the one named by the parameter; lets radio buttons pick an enum value.</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Enum.Parse(targetType, (string)parameter) : Binding.DoNothing;
}

/// <summary>Visible when the bound enum value is one of those the parameter names, separated by commas.</summary>
public sealed class EnumVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value?.ToString() is { } name && ((string)parameter).Split(',', StringSplitOptions.TrimEntries).Contains(name, StringComparer.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible for a non-empty string, for true, for a positive count, or for anything else that is not null; "Not" as the parameter turns it round.</summary>
public sealed class ShownConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var shown = value switch
        {
            null => false,
            bool flag => flag,
            string text => text.Length > 0,
            int count => count > 0,
            _ => true,
        };
        if (parameter as string == "Not") shown = !shown;
        return shown ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>A fraction 0..1 of the parameter's width in pixels, for bars.</summary>
public sealed class FractionWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double fraction ? Math.Max(0, fraction) * double.Parse((string)parameter, CultureInfo.InvariantCulture) : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
