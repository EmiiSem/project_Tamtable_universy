using System.Globalization;

namespace RukScheduleApp.Converters;

public sealed class StringIsNotNullOrEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string role)
            return Colors.LightGray;
        return role.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb("#E3F2FD")
            : Color.FromArgb("#F5F5F5");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToLayoutOptionsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string role)
            return LayoutOptions.Start;
        return role.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? LayoutOptions.End
            : LayoutOptions.Start;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
