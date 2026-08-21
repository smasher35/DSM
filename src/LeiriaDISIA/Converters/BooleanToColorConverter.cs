using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LeiriaDISIA.Converters;

public class BooleanToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            var color = isActive ? "#22C55E" : "#EF4444"; // Verde para ativo, vermelho para inativo
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        }
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")); // Cinzento por padrão
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

