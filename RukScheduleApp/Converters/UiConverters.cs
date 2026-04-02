using System.Globalization;

namespace RukScheduleApp.Converters;

public sealed class StringIsNotNullOrEmptyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && !string.IsNullOrWhiteSpace(s);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Для IsVisible / IsEnabled: список преподавателей загружен.</summary>
public sealed class IsNotNullConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string role)
            return Colors.LightGray;
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var isUser = role.Equals("user", StringComparison.OrdinalIgnoreCase);
        if (isUser)
            return dark ? Color.FromArgb("#1E4976") : Color.FromArgb("#DCEBFF");
        return dark ? Color.FromArgb("#2C2C30") : Color.FromArgb("#ECECF0");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToMessageTextColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string role)
            return Colors.Black;
        var dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        return dark ? Colors.White : Color.FromArgb("#1C1C1E");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToSenderLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string role)
            return "AI-ассистент";
        return role.Equals("user", StringComparison.OrdinalIgnoreCase) ? "Вы" : "AI-ассистент";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RoleToTextAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string role)
            return TextAlignment.Start;
        return role.Equals("user", StringComparison.OrdinalIgnoreCase)
            ? TextAlignment.End
            : TextAlignment.Start;
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
