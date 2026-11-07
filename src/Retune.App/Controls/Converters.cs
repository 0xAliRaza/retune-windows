using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Retune.App.Controls;

/// <summary>true → Visible. Pass "invert" as the parameter to flip it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Non-null and non-empty → Visible. "invert" flips it.</summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => s.Length > 0,
            int i => i > 0,
            double d => d > 0,
            _ => true,
        };
        return has ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the value's string form equals the parameter (enum states, layouts).</summary>
public sealed class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True when the value's string form equals the parameter (selected layout button).</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Formats large counts with digit grouping; zero shows nothing.</summary>
public sealed class CountConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int n && n > 0 ? n.ToString("N0", culture) : string.Empty;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Lays out its child at a fixed width-to-height ratio (2:3 posters, 16:9 cards, 16:10 logos).</summary>
public sealed class AspectBox : System.Windows.Controls.Decorator
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio), typeof(double), typeof(AspectBox), new FrameworkPropertyMetadata(1.5, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Height divided by width (1.5 for 2:3 posters).</summary>
    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        var width = double.IsInfinity(constraint.Width) ? 150 : constraint.Width;
        var size = new Size(width, width * Ratio);
        Child?.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var size = new Size(arrangeSize.Width, arrangeSize.Width * Ratio);
        Child?.Arrange(new Rect(size));
        return size;
    }
}
