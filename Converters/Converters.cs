using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Zentrix.Converters;

/// <summary>
/// Verticale verschuiving om een popup gelijk te centreren met het element waaraan
/// hij hangt. WPF lijnt een popup standaard uit op de bovenkant van dat element;
/// deze verschuiving zet de middens op één lijn.
/// </summary>
public class CenterOffsetConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double targetSize || values[1] is not double popupSize)
            return 0d;

        // Nog geen hoogte bekend (vóór de eerste lay-out): niet verschuiven,
        // anders springt de popup ver naar beneden en corrigeert hij pas daarna.
        if (popupSize <= 0) return 0d;

        return (targetSize - popupSize) / 2;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Maakt een afgeronde rechthoek ter grootte van het element zelf, om er een foto
/// mee bij te knippen. Zo ronden de hoeken van de foto mee af, welke maat de foto
/// ook heeft. De parameter is de straal van de hoeken.
/// </summary>
public class RoundedClipConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double width || values[1] is not double height ||
            width <= 0 || height <= 0)
        {
            // Nog geen maat bekend (vóór de eerste lay-out): dan nog niet bijknippen.
            return DependencyProperty.UnsetValue;
        }

        var radius = parameter is string text &&
                     double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 10;

        return new RectangleGeometry(new Rect(0, 0, width, height), radius, radius);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Toont een element alleen wanneer de waarde true is.</summary>
/// <summary>
/// De prijs als tekst: met centen wanneer die er zijn, anders zonder.
///
/// Vaste opmaak voldeed niet meer. Twee decimalen geeft "EUR 2.999,00" bij een
/// aanhangwagen, en nul decimalen maakte van een plaat van EUR 0,76 doodleuk
/// "EUR 1" - precies bij Discogs, waar veel onder de vijf euro ligt. De prijs
/// zelf bepaalt nu wat er nodig is.
/// </summary>
public class PriceTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not decimal prijs) return "\u2014";

        var heeftCenten = prijs != decimal.Truncate(prijs);
        return prijs.ToString(heeftCenten ? "C2" : "C0", new CultureInfo("nl-BE"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Toont een teller alleen wanneer die groter is dan nul.</summary>
public class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int count && count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}