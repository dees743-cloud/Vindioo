using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Vindioo.Models;

namespace Vindioo.Controls;

/// <summary>
/// Bouwt de invoer voor de filters die maar op één site bestaan, uit hun
/// beschrijving in het sitebestand. Gedeeld door het zoekscherm (de popups) en het
/// instellingenvenster van een zoekopdracht, zodat een filter er op beide plaatsen
/// hetzelfde uitziet en zich hetzelfde gedraagt.
///
/// Het werkt op een gewoon woordenboek met waarden: in het zoekscherm is dat
/// SearchFilters.Custom van het open tabblad, in het instellingenvenster
/// SiteEditor.Custom. Deze klasse weet dus niets van tabbladen of zoekopdrachten.
///
/// Waarom dit uit MainWindow kwam: de provincies en veilinghuizen van AlleVeilingen
/// stonden als vaste lijsten in de code, met eigen vinkjes op drie plaatsen. Sinds
/// ze gewone filters in het sitebestand zijn, moet ook het instellingenvenster
/// gewone filters kunnen tonen - en dat kon het nog niet.
/// </summary>
public static class CustomFilterControls
{
    /// <summary>Eén blok: het label, de invoer en eventueel een uitleg eronder.</summary>
    /// <param name="values">Waar de gekozen waarde in komt; ontbreekt de sleutel, dan staat het filter uit.</param>
    /// <param name="changed">Na elke wijziging, bv. om een samenvatting bij te werken.</param>
    /// <param name="labelStyle">De tekststijl van het label; die verschilt per venster.</param>
    /// <param name="subtle">De kleur van de uitleg.</param>
    /// <param name="maxColumns">Hoeveel kolommen vinkjes hoogstens; een smalle popup kan er maar één aan.</param>
    public static FrameworkElement Build(CustomFilter filter, IDictionary<string, string> values,
        Action? changed, Style? labelStyle, Brush? subtle, int maxColumns = 2)
    {
        var blok = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        blok.Children.Add(new TextBlock { Text = filter.Label, Style = labelStyle });

        blok.Children.Add(filter.Kind switch
        {
            CustomFilterKind.Number => Number(filter, values, changed),
            CustomFilterKind.Toggle => Toggle(filter, values, changed),
            _ => filter.Multiple
                ? Checkboxes(filter, values, changed, maxColumns)
                : ChoiceList(filter, values, changed)
        });

        if (filter.Hint.Length > 0)
        {
            var uitleg = new TextBlock
            {
                Text = filter.Hint,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0)
            };

            // Niet op null zetten: dan tekent de tekst in geen enkele kleur.
            if (subtle is not null) uitleg.Foreground = subtle;

            blok.Children.Add(uitleg);
        }

        return blok;
    }

    private static string Current(CustomFilter filter, IDictionary<string, string> values) =>
        values.TryGetValue(filter.Key, out var waarde) ? waarde : "";

    /// <summary>Zet een waarde; leeg betekent dat het filter uit gaat.</summary>
    private static void Set(CustomFilter filter, IDictionary<string, string> values, string waarde, Action? changed)
    {
        if (string.IsNullOrWhiteSpace(waarde)) values.Remove(filter.Key);
        else values[filter.Key] = waarde;

        changed?.Invoke();
    }

    private static UIElement Number(CustomFilter filter, IDictionary<string, string> values, Action? changed)
    {
        var vak = new TextBox
        {
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(8, 6, 8, 6),
            Text = Current(filter, values)
        };

        // Alleen cijfers doorlaten: een bouwjaar met letters erin levert enkel
        // een zoekopdracht op die niets teruggeeft.
        vak.TextChanged += (_, _) =>
        {
            var schoon = new string(vak.Text.Where(char.IsDigit).ToArray());

            if (schoon != vak.Text)
            {
                var plek = vak.CaretIndex;
                vak.Text = schoon;
                vak.CaretIndex = Math.Min(plek, schoon.Length);
                return;
            }

            Set(filter, values, schoon, changed);
        };

        return vak;
    }

    private static UIElement Toggle(CustomFilter filter, IDictionary<string, string> values, Action? changed)
    {
        var vinkje = new CheckBox
        {
            Content = "Aan",
            IsChecked = Current(filter, values).Length > 0
        };

        vinkje.Checked += (_, _) => Set(filter, values, "1", changed);
        vinkje.Unchecked += (_, _) => Set(filter, values, "", changed);

        return vinkje;
    }

    private static UIElement ChoiceList(CustomFilter filter, IDictionary<string, string> values, Action? changed)
    {
        var lijst = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };

        lijst.Items.Add(new ComboBoxItem { Content = "Alle", Tag = "" });

        foreach (var optie in filter.Options)
            lijst.Items.Add(new ComboBoxItem { Content = optie.Label, Tag = optie.Value });

        var huidig = Current(filter, values);
        lijst.SelectedIndex = Math.Max(0, filter.Options.FindIndex(o => o.Value == huidig) + 1);

        // Pas na het kiezen van de beginwaarde inhaken, anders telt die als wijziging.
        lijst.SelectionChanged += (_, _) =>
            Set(filter, values, (lijst.SelectedItem as ComboBoxItem)?.Tag as string ?? "", changed);

        return lijst;
    }

    private static UIElement Checkboxes(CustomFilter filter, IDictionary<string, string> values,
        Action? changed, int maxColumns)
    {
        // Meerdere kolommen vanaf een handvol keuzes: met veertien kleuren of twintig
        // veilinghuizen wordt één kolom een eindeloze rol.
        var raster = new UniformGrid { Columns = filter.Options.Count > 6 ? Math.Max(1, maxColumns) : 1 };

        var gekozen = Current(filter, values)
            .Split(filter.Separator, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        var vinkjes = new List<(CheckBox Vak, string Waarde)>();

        void Bijwerken()
        {
            var waarden = vinkjes.Where(v => v.Vak.IsChecked == true).Select(v => v.Waarde);
            Set(filter, values, string.Join(filter.Separator, waarden), changed);
        }

        foreach (var optie in filter.Options)
        {
            var vak = new CheckBox
            {
                Content = optie.Label,
                FontSize = 12,
                Margin = new Thickness(0, 3, 8, 3),
                IsChecked = gekozen.Contains(optie.Value)
            };

            vak.Checked += (_, _) => Bijwerken();
            vak.Unchecked += (_, _) => Bijwerken();

            vinkjes.Add((vak, optie.Value));
            raster.Children.Add(vak);
        }

        return raster;
    }
}
