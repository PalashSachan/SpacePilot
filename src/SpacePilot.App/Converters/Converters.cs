using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SpacePilot.Core.Enums;
using SpacePilot.Core.Utilities;

namespace SpacePilot.App.Converters;

public class ByteSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long l) return ByteSizeFormatter.Format(l);
        if (value is int i) return ByteSizeFormatter.Format(i);
        if (value is double d) return ByteSizeFormatter.Format((long)d);
        return "0 B";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class SavingsPercentageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double d) return $"{d:0.0}%";
        if (value is float f) return $"{f:0.0}%";
        return "0.0%";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isNullOrEmpty = value == null || (value is string s && string.IsNullOrWhiteSpace(s));
        if (Invert) isNullOrEmpty = !isNullOrEmpty;
        return isNullOrEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class StatusToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new((Color)ColorConverter.ConvertFromString("#10B981"));
    private static readonly SolidColorBrush Blue = new((Color)ColorConverter.ConvertFromString("#3B82F6"));
    private static readonly SolidColorBrush Orange = new((Color)ColorConverter.ConvertFromString("#F59E0B"));
    private static readonly SolidColorBrush Red = new((Color)ColorConverter.ConvertFromString("#EF4444"));
    private static readonly SolidColorBrush Slate = new((Color)ColorConverter.ConvertFromString("#64748B"));
    private static readonly SolidColorBrush Purple = new((Color)ColorConverter.ConvertFromString("#8B5CF6"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is JobItemStatus itemStatus)
        {
            return itemStatus switch
            {
                JobItemStatus.Compressed or JobItemStatus.UploadedVerified => Green,
                JobItemStatus.Processing => Blue,
                JobItemStatus.SkippedBelowThreshold or JobItemStatus.SkippedAlreadyProcessed or JobItemStatus.CheckedNoSavings => Orange,
                JobItemStatus.Failed => Red,
                JobItemStatus.PreviewReady => Purple,
                _ => Slate
            };
        }

        if (value is JobStatus jobStatus)
        {
            return jobStatus switch
            {
                JobStatus.Completed => Green,
                JobStatus.Running => Blue,
                JobStatus.Paused => Orange,
                JobStatus.Failed => Red,
                _ => Slate
            };
        }

        return Slate;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null) return Visibility.Visible;
        if (value != null && parameter != null)
        {
            return value.ToString()!.Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class EqualsToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null) return true;
        if (value != null && parameter != null)
        {
            return value.ToString()!.Equals(parameter.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
