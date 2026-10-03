using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Vindioo.Controls;
using Vindioo.Models;
using Vindioo.Services;

namespace Vindioo;

/// <summary>
/// Alles van één zoekopdracht op één scherm: het zoekwoord, welke sites
/// meezoeken en met welke filters, en wanneer hij vanzelf draait.
///
/// Dit is het scherm achter het tandwieltje naast de zoekbalk. Wat je wijzigt, staat
/// enkel op het scherm; pas bij "Bewaren" komt het in de zoekopdracht, zodat annuleren
/// echt annuleert. Dat laatste klopte tot september 2026 niet: "Nu uitvoeren" en een
/// geweigerde Bewaren schreven al in de echte zoekopdracht - hetzelfde object dat de
/// planner gebruikt en bij zijn volgende beurt wegschrijft.
/// </summary>
public partial class SearchSettingsWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly SavedSearch _search;
    private readonly SiteStore _store;
    private readonly HistoryStore _history;

    private readonly ObservableCollection<SiteEditor> _sites = new();

    /// <summary>De bewaarde zoekopdracht, of null wanneer er geannuleerd is.</summary>
    public SavedSearch? Result { get; private set; }

    public SearchSettingsWindow(SavedSearch search, SiteStore store, HistoryStore history)
    {
        InitializeComponent();

        _search = search;
        _store = store;
        _history = history;

        SitesList.ItemsSource = _sites;

        Vul();

        // De hint onderaan meteen laten meelopen met wat er aangevinkt staat.
        foreach (var knop in new[] { ModeOff, ModeInterval, ModeDaily })
            knop.Checked += (_, _) => UpdateHint();

        WindowBox.Checked += (_, _) => UpdateHint();
        WindowBox.Unchecked += (_, _) => UpdateHint();
        StartupBox.Checked += (_, _) => UpdateHint();
        StartupBox.Unchecked += (_, _) => UpdateHint();
        IntervalBox.TextChanged += (_, _) => UpdateHint();
        DailyHourBox.TextChanged += (_, _) => UpdateHint();
        DailyMinuteBox.TextChanged += (_, _) => UpdateHint();
        FromHourBox.TextChanged += (_, _) => UpdateHint();
        ToHourBox.TextChanged += (_, _) => UpdateHint();

        NotifyBox.Checked += (_, _) => UpdateNotifyWarning();
        NotifyBox.Unchecked += (_, _) => UpdateNotifyWarning();

        UpdateHint();
        UpdateNotifyWarning();
    }

    /// <summary>Waarschuwt wanneer er een melding gevraagd wordt die nergens heen kan.</summary>
    private void UpdateNotifyWarning() =>
        NotifyWarning.Visibility = NotifyBox.IsChecked == true && !AppSettings.Current.Notify.AnyConfigured
            ? Visibility.Visible
            : Visibility.Collapsed;

    // ---------- het scherm vullen ----------

    private void Vul()
    {
        QueryBox.Text = _search.Query;
        PhotosOnlyBox.IsChecked = _search.PhotosOnly;
        TitleOnlyBox.IsChecked = _search.TitleOnly;

        // Eén regel per site die de app kent, met de bewaarde waarden ingevuld.
        // Sites die er later bij komen verschijnen dus vanzelf, uitgevinkt.
        foreach (var def in _store.Sites)
        {
            var editor = new SiteEditor(def, _search.For(def.Name));

            // De samenvatting achter de naam moet meelopen met wat er getypt wordt.
            editor.PropertyChanged += Site_PropertyChanged;

            _sites.Add(editor);
        }

        var schema = _search.Schedule;

        ModeOff.IsChecked = schema.Mode == ScheduleMode.Off;
        ModeInterval.IsChecked = schema.Mode == ScheduleMode.Interval;
        ModeDaily.IsChecked = schema.Mode == ScheduleMode.Daily;

        IntervalBox.Text = schema.IntervalMinutes.ToString(CultureInfo.InvariantCulture);
        DailyHourBox.Text = schema.DailyHour.ToString("00");
        DailyMinuteBox.Text = schema.DailyMinute.ToString("00");

        WindowBox.IsChecked = schema.OnlyBetween;
        FromHourBox.Text = schema.FromHour.ToString(CultureInfo.InvariantCulture);
        ToHourBox.Text = schema.ToHour.ToString(CultureInfo.InvariantCulture);

        StartupBox.IsChecked = schema.RunOnStartup;
        NotifyBox.IsChecked = schema.NotifyOnNew;
    }

    /// <summary>
    /// De samenvatting achter de sitenaam herberekenen zodra er iets verandert.
    /// Niet op elke eigenschap: de samenvatting zelf veranderen zou een lus geven.
    /// </summary>
    private static void Site_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not SiteEditor editor) return;
        if (e.PropertyName == nameof(SiteEditor.Samenvatting)) return;

        editor.RefreshSamenvatting();
    }

    /// <summary>
    /// Vult het blok met de sitegebonden filters van één site, zodra dat in beeld
    /// komt. Die komen uit het sitebestand, net als in de popups van het zoekscherm,
    /// en met dezelfde bouwstenen - zodat een filter er op beide plaatsen hetzelfde
    /// uitziet. Elke wijziging werkt de samenvatting achter de sitenaam bij.
    /// </summary>
    private void CustomFilters_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not StackPanel blok || blok.DataContext is not SiteEditor editor) return;

        // Loaded kan meer dan eens komen, bv. wanneer het venster opnieuw opgebouwd wordt.
        if (blok.Children.Count > 0) return;

        var label = (Style)FindResource("FieldLabel");
        var uitleg = (Brush)FindResource("TextSubtleBrush");

        foreach (var filter in editor.Def.CustomFilters)
            blok.Children.Add(CustomFilterControls.Build(filter, editor.Custom,
                editor.RefreshSamenvatting, label, uitleg, maxColumns: 3));
    }

    // ---------- het scherm uitlezen ----------

    /// <summary>
    /// Zet alles wat op het scherm staat in <paramref name="doel"/>: de echte zoekopdracht
    /// bij Bewaren, of een kopie om mee proef te draaien.
    /// </summary>
    private void Neem(SavedSearch doel)
    {
        doel.Query = QueryBox.Text.Trim();
        doel.PhotosOnly = PhotosOnlyBox.IsChecked == true;
        doel.TitleOnly = TitleOnlyBox.IsChecked == true;

        doel.SiteSettings = _sites.Select(s => s.ToSetting()).ToList();

        var schema = doel.Schedule;

        schema.Mode = ModeInterval.IsChecked == true ? ScheduleMode.Interval
                    : ModeDaily.IsChecked == true ? ScheduleMode.Daily
                    : ScheduleMode.Off;

        schema.IntervalMinutes = Getal(IntervalBox.Text, 60, 1, 60 * 24 * 7);
        schema.DailyHour = Getal(DailyHourBox.Text, 8, 0, 23);
        schema.DailyMinute = Getal(DailyMinuteBox.Text, 0, 0, 59);

        schema.OnlyBetween = WindowBox.IsChecked == true;
        schema.FromHour = Getal(FromHourBox.Text, 8, 0, 23);
        schema.ToHour = Getal(ToHourBox.Text, 22, 0, 23);

        schema.RunOnStartup = StartupBox.IsChecked == true;
        schema.NotifyOnNew = NotifyBox.IsChecked == true;
    }

    /// <summary>Een getal uit een tekstvak, binnen de grenzen gehouden.</summary>
    private static int Getal(string tekst, int standaard, int min, int max)
    {
        if (!int.TryParse(tekst.Trim(), out var waarde)) return standaard;
        return Math.Clamp(waarde, min, max);
    }

    /// <summary>De regel onderaan die in gewone taal zegt wat er zal gebeuren.</summary>
    private void UpdateHint()
    {
        // Niet via Neem(_search): dat zou het echte object aanpassen terwijl de
        // gebruiker nog kan annuleren.
        var proef = new SearchSchedule
        {
            Mode = ModeInterval.IsChecked == true ? ScheduleMode.Interval
                 : ModeDaily.IsChecked == true ? ScheduleMode.Daily
                 : ScheduleMode.Off,
            IntervalMinutes = Getal(IntervalBox.Text, 60, 1, 60 * 24 * 7),
            DailyHour = Getal(DailyHourBox.Text, 8, 0, 23),
            DailyMinute = Getal(DailyMinuteBox.Text, 0, 0, 59),
            OnlyBetween = WindowBox.IsChecked == true,
            FromHour = Getal(FromHourBox.Text, 8, 0, 23),
            ToHour = Getal(ToHourBox.Text, 22, 0, 23),
            RunOnStartup = StartupBox.IsChecked == true
        };

        if (proef.Mode == ScheduleMode.Off)
        {
            ScheduleHint.Text = proef.RunOnStartup
                ? "Deze zoekopdracht draait enkel bij het opstarten van de app en wanneer je er zelf op klikt."
                : "Deze zoekopdracht draait enkel wanneer je er zelf op klikt.";
            return;
        }

        var volgende = proef.NextRun(_search.LastRun);

        var wanneer = volgende is null ? "onbepaald"
            : volgende.Value <= DateTime.Now ? "meteen"
            : volgende.Value.Date == DateTime.Today ? $"vandaag om {volgende:HH:mm}"
            : $"{volgende:dddd d MMMM} om {volgende:HH:mm}";

        ScheduleHint.Text = $"Draait {proef.Describe()}. Eerstvolgende beurt: {wanneer}. " +
                            "De app moet daarvoor draaien — geminimaliseerd of in het systeemvak is genoeg.";
    }

    // ---------- knoppen ----------

    private void AllOn_Click(object sender, RoutedEventArgs e)
    {
        foreach (var site in _sites) site.Enabled = true;
    }

    private void AllOff_Click(object sender, RoutedEventArgs e)
    {
        foreach (var site in _sites) site.Enabled = false;
    }

    /// <summary>
    /// Draait de zoekopdracht één keer om te zien of de instellingen kloppen,
    /// zonder de resultaten als "gezien" weg te schrijven. Dat laatste is
    /// belangrijk: wie hier proefdraait, wil niet dat alles daarna plots niet
    /// meer nieuw is.
    /// </summary>
    private async void RunNow_Click(object sender, RoutedEventArgs e)
    {
        // Proefdraaien op een kopie. Met hetzelfde Id, zodat "nieuw" klopt met wat deze
        // zoekopdracht al zag; markSeen staat uit, dus er wordt niets weggeschreven.
        var proef = new SavedSearch { Id = _search.Id };
        Neem(proef);

        if (proef.Query.Length == 0)
        {
            RunResult.Text = "Vul eerst een zoekwoord in.";
            return;
        }

        if (proef.SiteSettings.All(s => !s.Enabled))
        {
            RunResult.Text = "Vink eerst minstens één site aan.";
            return;
        }

        RunNowButton.IsEnabled = false;
        RunResult.Text = "Bezig...";

        try
        {
            var runner = new SearchRunner(_store, _history);
            var melder = new Progress<string>(tekst => RunResult.Text = tekst);

            var outcome = await runner.RunAsync(proef, markSeen: false, status: melder);

            RunResult.Text = $"{outcome.All.Count} resultaten, {outcome.New.Count} daarvan nieuw." +
                             (outcome.Errors.Count > 0 ? $" Fout bij: {string.Join(", ", outcome.Errors)}" : "");
        }
        catch (Exception ex)
        {
            Log.Write($"proefbeurt van '{proef.Name}' mislukt - {ex.Message}");
            RunResult.Text = "Mislukt: " + FriendlyError.Describe(ex);
        }
        finally
        {
            RunNowButton.IsEnabled = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Eerst nakijken, dan pas overnemen: een geweigerde Bewaren mag de zoekopdracht
        // niet half aangepast achterlaten.
        if (QueryBox.Text.Trim().Length == 0)
        {
            RunResult.Text = "Vul eerst een zoekwoord in.";
            return;
        }

        Neem(_search);

        // Nog niet bewaard? Dan krijgt hij hier zijn plaats in de databank.
        if (_search.Id == 0) _search.Id = _history.Add(_search);
        else _history.Update(_search);

        _search.RefreshStatus();

        Result = _search;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
