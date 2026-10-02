using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Zentrix.Controls;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix;

/// <summary>
/// De bewaarde zoekopdrachten en wat de planner ermee doet.
///
/// Onderdeel van <see cref="MainWindow"/>; de velden, de constructor en het
/// opstarten staan in MainWindow.xaml.cs. Opgesplitst op 1 oktober 2026: het
/// bestand was 2824 regels geworden. Zuiver verschoven, geen regel logica
/// gewijzigd - de snijlijnen zijn de sectiemarkeringen die er al in stonden.
/// </summary>
public partial class MainWindow
{
    // ---------- vastgezette zoekopdrachten ----------

    private void LoadSavedSearches()
    {
        _saved.Clear();
        foreach (var search in _history.GetAll()) _saved.Add(search);
    }

    /// <summary>
    /// Zet de zoekterm en de filters van deze zoekopdracht klaar en toont zijn
    /// resultaten. Heeft de planner die net nog opgehaald, dan tonen we díe in
    /// plaats van opnieuw te gaan zoeken — bij sites via de brug scheelt dat al
    /// gauw een halve minuut wachten.
    /// </summary>
    private void SavedList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SavedList.SelectedItem is SavedSearch search) OpenSaved(search, enkelNieuw: false);
    }

    /// <summary>
    /// Enter doet hetzelfde als dubbelklikken; Delete hetzelfde als het vuilbakje. Zo kan je
    /// ook zonder muis verwijderen, nu de knop "Verwijderen" bovenaan weg is.
    /// </summary>
    private void SavedList_KeyDown(object sender, KeyEventArgs e)
    {
        if (SavedList.SelectedItem is not SavedSearch search) return;

        if (e.Key == Key.Enter) OpenSaved(search, enkelNieuw: false);
        else if (e.Key == Key.Delete) DeleteSaved(search);
        else return;

        e.Handled = true;
    }

    /// <summary>
    /// De teller bij een zoekopdracht: opent haar met enkel wat je nog niet bekeek. Met de
    /// schakelaar "Enkel nieuwe" boven de resultaten zie je daarna alles.
    /// </summary>
    private void NewBadge_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SavedSearch search) return;

        SavedList.SelectedItem = search;
        OpenSaved(search, enkelNieuw: true);
    }

    /// <param name="enkelNieuw">Enkel tonen wat je nog niet bekeek (de schakelaar "Enkel nieuwe" aan).</param>
    private void OpenSaved(SavedSearch search, bool enkelNieuw)
    {
        _activeSearch = search;
        QueryBox.Text = search.Query;
        PasToe(search);

        TabSearch.IsChecked = true;

        // Wat de laatste beurt opleverde, uit het geheugen of anders van schijf.
        // Er wordt niet meer op de klok gekeken: een half uur oude lijst is nog
        // altijd beter dan niets, en de statusregel zegt erbij van wanneer ze is.
        // Wie verse resultaten wil, klikt op het vergrootglas om opnieuw te zoeken.
        var bewaard = _lastOutcomes.TryGetValue(search.Id, out var uitGeheugen) && uitGeheugen.Count > 0
            ? uitGeheugen
            : _history.GetOutcome(search.Id);

        if (bewaard.Count == 0)
        {
            _ = RunSearchAsync();
            return;
        }

        // Wat nieuw is, opnieuw bepalen in plaats van de vlag van die beurt te geloven: die
        // zei "nog niet bekeken" op het moment van de beurt, en misschien heb je de lijst
        // intussen al geopend.
        var gezien = _history.GetSeen(search.Id);
        search.LastViewed = _history.GetLastViewed(search.Id);

        foreach (var listing in bewaard)
            listing.IsNew = search.IsUnviewed(gezien.TryGetValue(listing.Key, out var eerst) ? eerst : null);

        _enkelNieuw = enkelNieuw && bewaard.Any(l => l.IsNew);
        ToonBewaard(search, bewaard);

        // Nu heb je ze gezien. Het NIEUW-label blijft staan zolang deze lijst op het scherm
        // staat; pas bij de volgende keer openen tellen ze als bekeken.
        search.LastViewed = DateTimeOffset.Now;
        search.NewCount = 0;
        _history.SetViewed(search.Id, search.LastViewed.Value);
    }

    /// <summary>Zet de resultaten van een eerdere beurt in de lijst.</summary>
    private void ToonBewaard(SavedSearch search, List<Listing> resultaten)
    {
        _heeftGezocht = true;

        _results.Clear();
        _zichtbaar.Clear();
        _pagina = 0;

        // De fouten van die beurt op de tabs, zoals na gewoon zoeken. Vroeger bleven hier de
        // waarschuwingstekens van wat er daarvoor op het scherm stond, en ontbraken die van
        // deze zoekopdracht - net na een melding "kon niet overal zoeken".
        foreach (var tab in _tabs)
        {
            tab.ResultCount = 0;
            tab.ErrorText = tab.IsAll ? "" : search.LastErrors.GetValueOrDefault(tab.Name, "");
        }

        // Nieuwe zoekertjes vooraan, net als bij gewoon zoeken. OrderBy is stabiel:
        // binnen de nieuwe en binnen de rest blijft de volgorde van de sites staan.
        foreach (var listing in resultaten.OrderBy(l => l.IsNew ? 0 : 1))
        {
            listing.IsFavorite = _favoriteKeys.Contains(listing.Key);
            _results.Add(listing);

            var tab = _tabs.FirstOrDefault(t => t.Name == listing.Source);
            if (tab is not null) tab.ResultCount++;
        }

        // De eerste site met resultaten openzetten, anders kijk je naar een
        // lege tab terwijl er wel iets gevonden is. Op "Alles" staat sowieso
        // alles, dus daar hoeft er niets te wisselen.
        if (_active is null || (!_active.IsAll && _active.ResultCount == 0))
        {
            var metResultaat = _tabs.FirstOrDefault(t => t.ResultCount > 0);
            if (metResultaat is not null) SetActiveTab(metResultaat);
        }

        _resultsView.Refresh();
        ToonPagina();
        UpdateEmptyHints();

        var wanneer = search.LastRun is { } run
            ? (DateTime.Now - run) < TimeSpan.FromHours(12)
                ? $"van {run:HH:mm}"
                : $"van {run:d MMM HH:mm}"
            : "van de laatste beurt";

        var nieuw = resultaten.Count(l => l.IsNew);

        StatusText.Text = _enkelNieuw
            ? $"'{search.Name}': de {nieuw} die je nog niet zag, van {resultaten.Count} resultaten {wanneer}. " +
              "Zet 'Enkel nieuwe' uit om alles te zien."
            : $"'{search.Name}': {resultaten.Count} resultaten {wanneer}"
              + (nieuw > 0 ? $", waarvan {nieuw} nieuw" : "")
              + ". Klik op het vergrootglas om opnieuw te zoeken.";
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        var query = QueryBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            StatusText.Text = "Typ eerst een zoekterm.";
            return;
        }

        // Twee keer vastzetten gaf twee dezelfde zoekopdrachten, en dus dubbele meldingen.
        var bestaand = _saved.FirstOrDefault(s => string.Equals(s.Query, query, StringComparison.OrdinalIgnoreCase));
        if (bestaand is not null)
        {
            _activeSearch = bestaand;
            StatusText.Text = $"'{bestaand.Name}' staat al bij Zoekopdrachten. Aanpassen doe je met het tandwiel naast de zoekbalk.";
            return;
        }

        // Elke tab levert zijn eigen instellingen aan: een zoekopdracht bewaart
        // dus de postcode van de ene site naast de provincies van de andere.
        var search = new SavedSearch
        {
            Query = query,
            SiteSettings = _tabs.Where(t => !t.IsAll).Select(SiteSetting.FromTab).ToList()
        };

        search.Id = _history.Add(search);

        // Wat nu op het scherm staat, geldt als gezien én bekeken: pas morgen is er iets
        // nieuw. Bekeken na het markeren, anders telt wat net gemarkeerd werd als nieuwer.
        if (_results.Count > 0)
            _history.MarkSeen(search.Id, _results.Select(r => r.Key));

        search.LastViewed = DateTimeOffset.Now;
        _history.SetViewed(search.Id, search.LastViewed.Value);

        LoadSavedSearches();
        UpdateEmptyHints();
        _activeSearch = search;

        UpdateSchedulerHint();

        StatusText.Text = $"'{search.Name}' vastgezet. Wanneer hij vanzelf draait, stel je in met het tandwiel " +
                          "op zijn kaart bij Zoekopdrachten.";
    }

    private void NewSavedButton_Click(object sender, RoutedEventArgs e)
    {
        var nieuw = new SavedSearch
        {
            Query = QueryBox.Text.Trim(),

            // Met alle sites erin, uitgevinkt: dan staat de keuze er al klaar.
            SiteSettings = _tabs.Where(t => !t.IsAll)
                                .Select(t => new SiteSetting { Site = t.Name }).ToList()
        };

        OpenSearchSettings(nieuw);
    }

    /// <summary>De knop met het tandwiel op de kaart van een zoekopdracht.</summary>
    private void EditSavedButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SavedSearch search) OpenSearchSettings(search);
    }

    /// <summary>De knop met het driehoekje: deze zoekopdracht nu laten draaien.</summary>
    private void RunSavedButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SavedSearch search) return;

        // Zelf gevraagd, dus het scherm mag meekijken (zie Scheduler_Started).
        _meekijkenMet = search.Id;
        _ = _scheduler.RunAsync(search);
    }

    /// <summary>
    /// Opent het instellingenscherm van een zoekopdracht en verwerkt het
    /// resultaat. Een nieuwe zoekopdracht krijgt daar zijn id, dus daarna moet
    /// de lijst opnieuw ingelezen worden.
    /// </summary>
    private void OpenSearchSettings(SavedSearch search)
    {
        var window = new SearchSettingsWindow(search, _store, _history).Boven(this);

        if (window.ShowDialog() != true) return;

        LoadSavedSearches();
        UpdateEmptyHints();
        UpdateSchedulerHint();

        // De bewerkte zoekopdracht opnieuw opzoeken: LoadSavedSearches maakt
        // verse objecten, dus het object dat we meegaven is niet meer het object
        // dat in de lijst staat.
        _activeSearch = _saved.FirstOrDefault(s => s.Id == search.Id);

        if (_activeSearch is not null)
        {
            QueryBox.Text = _activeSearch.Query;
            PasToe(_activeSearch);
        }

        StatusText.Text = $"'{search.Name}' bewaard. {search.Schedule.Describe()}.";
    }

    /// <summary>
    /// De andere richting van <see cref="PasToe"/>: wat je op het scherm aan de sites en hun
    /// filters wijzigde terwijl een bewaarde zoekopdracht openstond, gaat mee in die
    /// zoekopdracht. Zo zoekt de planner straks met dezelfde waarden.
    ///
    /// Tot 22 september 2026 gebeurde dat niet, en dat gaf een stil verschil: zet je de prijs
    /// of de postcode anders en laat je opnieuw zoeken, dan gebruikte die beurt de nieuwe
    /// waarde - met de resultaten, de teller en het tijdstip onder die zoekopdracht - terwijl
    /// de volgende geplande beurt nog met de oude waarden liep. Zo koos de eigenaar het; de
    /// statusregel zegt wat er bewaard is, want stil bewaren is even verwarrend als stil
    /// vergeten. Gevonden bij het vergelijken van de twee zoeklussen (zie Volgende stappen).
    ///
    /// Een site die in de zoekopdracht staat maar geen tab heeft - verwijderd of hernoemd -
    /// blijft staan: de planner meldt die als fout, en dat mag niet stil verdwijnen.
    /// </summary>
    /// <returns>Wat er overgenomen werd, voor de statusregel; leeg als er niets veranderde.</returns>
    private string NeemSchermfiltersOver(SavedSearch search)
    {
        static string Vorm(SiteSetting s) =>
            $"{s.Enabled}|{s.PriceMin}|{s.PriceMax}|{s.Postcode}|{s.RadiusKm}|" +
            string.Join(",", s.Custom.OrderBy(p => p.Key).Select(p => $"{p.Key}={p.Value}"));

        var zonderTab = search.SiteSettings
            .Where(s => !_tabs.Any(t => !t.IsAll && string.Equals(t.Name, s.Site, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        var nieuw = _tabs.Where(t => !t.IsAll).Select(SiteSetting.FromTab).Concat(zonderTab).ToList();

        var sites = false;
        var filters = false;

        foreach (var s in nieuw)
        {
            var oud = search.For(s.Site);

            if (oud is null) { sites = true; continue; }
            if (oud.Enabled != s.Enabled) sites = true;
            if (Vorm(oud) != Vorm(s) && oud.Enabled == s.Enabled) filters = true;
        }

        if (!sites && !filters && nieuw.Count == search.SiteSettings.Count) return "";

        search.SiteSettings = nieuw;

        return sites && filters ? "sites en filters" : sites ? "sites" : "filters";
    }

    /// <summary>
    /// Zet de vinkjes en filters van de tabs gelijk met een bewaarde zoekopdracht,
    /// zodat het zoekscherm toont waarmee die zoekopdracht werkt.
    /// </summary>
    private void PasToe(SavedSearch search)
    {
        foreach (var tab in _tabs)
        {
            if (tab.IsAll) continue;

            var setting = search.For(tab.Name);

            if (setting is null)
            {
                tab.IsEnabled = false;
                continue;
            }

            setting.ApplyTo(tab);
        }

        UpdateFilterAvailability();
    }

    /// <summary>Het vuilbakje op de kaart van een zoekopdracht.</summary>
    private void DeleteSaved_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SavedSearch search) DeleteSaved(search);
    }

    private void DeleteSaved(SavedSearch search)
    {
        // Eerst vragen. Een site verwijderen deed dat al; een zoekopdracht niet, terwijl
        // daar het schema en de hele "al gezien"-geschiedenis mee weggaan. Nu het vuilbakje
        // op elke kaart staat, naast het driehoekje, is die vraag er zeker nodig.
        var bevestig = MessageBox.Show(this,
            $"Zoekopdracht '{search.Name}' verwijderen? Het schema en wat al gezien is, gaan ook weg.",
            "Zoekopdracht verwijderen", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (bevestig != MessageBoxResult.Yes) return;

        _history.Delete(search.Id);
        _lastOutcomes.Remove(search.Id);
        if (_activeSearch?.Id == search.Id) _activeSearch = null;

        LoadSavedSearches();
        UpdateEmptyHints();
        UpdateSchedulerHint();
        StatusText.Text = $"'{search.Name}' verwijderd.";
    }
}
