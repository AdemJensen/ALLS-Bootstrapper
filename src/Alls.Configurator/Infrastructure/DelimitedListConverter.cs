using System.Globalization;
using System.Windows.Data;

namespace Alls.Configurator.Infrastructure;

public sealed class DelimitedListConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is IEnumerable<string> values ? string.Join(Environment.NewLine, values) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string ?? string.Empty)
        .Split(["\r\n", "\n", ",", ";"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
