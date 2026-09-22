using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Zentrix.Models;

namespace Zentrix.Services;

/// <summary>
/// Stuurt een bericht wanneer een geplande zoekopdracht iets nieuws vindt.
///
/// Er zijn drie wegen, en ze sluiten elkaar niet uit:
///   - het systeemvak: een ballon naast de klok. Vraagt niets, maar je ziet hem
///     enkel als je aan de pc zit.
///   - Telegram: komt op je telefoon. Instellen kost één gesprek met @BotFather
///     en er gaat geen wachtwoord over de lijn, enkel een token dat je altijd
///     weer kan intrekken. Dit is de aangewezen weg als je het van ver wil zien.
///   - e-mail: werkt overal, maar vraagt een mailserver met gebruikersnaam en
///     wachtwoord (bij Gmail een app-wachtwoord).
///
/// De app zelf praat met niemand anders: er is geen centrale dienst, geen account.
/// Telegram en e-mail gaan rechtstreeks van deze pc naar de dienst die je zelf koos.
/// </summary>
public static class Notifier
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// Waar de ballon in het systeemvak getoond wordt. Het pictogram leeft in het
    /// venster, niet hier, dus dat hangt zichzelf op aan deze haak.
    /// </summary>
    public static Action<string, string>? ShowTrayBalloon { get; set; }

    /// <summary>
    /// Meldt de nieuwe resultaten van één zoekopdracht via alle kanalen die
    /// aanstaan. Een kanaal dat mislukt houdt de rest niet tegen: de melding is
    /// een extra, geen voorwaarde om te blijven zoeken.
    /// </summary>
    /// <returns>Of de melding ergens aankwam; zie <see cref="Verstuur"/>.</returns>
    public static async Task<bool> NotifyNewAsync(SavedSearch search, IReadOnlyList<Listing> nieuwe)
    {
        if (nieuwe.Count == 0) return true;

        var settings = AppSettings.Current.Notify;

        var titel = nieuwe.Count == 1
            ? $"Zentrix: 1 nieuw resultaat voor '{search.Name}'"
            : $"Zentrix: {nieuwe.Count} nieuwe resultaten voor '{search.Name}'";

        return await Verstuur($"'{search.Name}': {nieuwe.Count} nieuw", settings,
            () => ShowTray(titel, KorteSamenvatting(nieuwe)),
            () => SendTelegramNewAsync(settings, titel, nieuwe),
            () => SendEmailAsync(settings, titel, MailTekst(search, nieuwe)));
    }

    /// <summary>
    /// Stuurt via elk kanaal dat aanstaat, en telt wat lukte. Een kanaal dat mislukt, houdt
    /// de rest niet tegen. Tot september 2026 stond er daarna altijd "melding verstuurd" in
    /// het logboek, ook als elk kanaal faalde - een ingetrokken Telegram-token las dan als
    /// "er was niets nieuws". Geeft false als er kanalen aanstonden en geen enkel lukte.
    /// </summary>
    private static async Task<bool> Verstuur(string wat, NotifySettings settings,
                                             Func<Task> tray, Func<Task> telegram, Func<Task> email)
    {
        var geprobeerd = new List<string>();
        var gelukt = new List<string>();

        async Task Probeer(bool aan, string kanaal, Func<Task> stuur)
        {
            if (!aan) return;
            geprobeerd.Add(kanaal);

            try
            {
                await stuur();
                gelukt.Add(kanaal);
            }
            catch (Exception ex)
            {
                Log.Write($"melding ({kanaal}) mislukte - {ex.Message}");
            }
        }

        await Probeer(settings.Tray, "systeemvak", tray);
        await Probeer(settings.Telegram, "Telegram", telegram);
        await Probeer(settings.Email, "e-mail", email);

        if (geprobeerd.Count == 0)
        {
            Log.Write($"melding voor {wat}: geen kanaal ingesteld");
            return true;
        }

        if (gelukt.Count == 0)
        {
            Log.Write($"melding NIET verstuurd voor {wat}: elk kanaal mislukte ({string.Join(", ", geprobeerd)})");
            return false;
        }

        Log.Write($"melding verstuurd voor {wat} via {string.Join(", ", gelukt)}" +
                  (gelukt.Count < geprobeerd.Count ? $" (mislukt: {string.Join(", ", geprobeerd.Except(gelukt))})" : ""));
        return true;
    }

    /// <summary>De ballon; zonder pictogram in het systeemvak (venster nog niet geladen) telt die als mislukt.</summary>
    private static Task ShowTray(string titel, string tekst)
    {
        if (ShowTrayBalloon is null) throw new InvalidOperationException("er is geen pictogram in het systeemvak");
        ShowTrayBalloon(titel, tekst);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Meldt dat een zoekopdracht op sommige sites niet meer lukt. Wordt enkel
    /// gestuurd na twee mislukte beurten op rij, en daarna niet opnieuw tot de site
    /// weer lukt (zie <see cref="SavedSearch.RecordRun"/>).
    ///
    /// Waarom dit er is: automatisch zoeken is er juist voor wanneer je niet kijkt.
    /// Faalde een site, dan stond dat enkel in het logboek, en een verlopen aanmelding
    /// leek dan wekenlang op "niets nieuws te koop". Stil falen is voor zo'n functie
    /// het ergste wat kan gebeuren.
    /// </summary>
    public static async Task<bool> NotifyProblemAsync(SavedSearch search, IReadOnlyList<string> sites)
    {
        if (sites.Count == 0) return true;

        var settings = AppSettings.Current.Notify;

        var titel = $"Zentrix: '{search.Name}' kon niet overal zoeken";
        var regels = sites
            .Select(site => $"{site}: {search.LastErrors.GetValueOrDefault(site, "mislukt")}")
            .ToList();

        const string slot = "Open Zentrix om het op te lossen.";

        return await Verstuur($"'{search.Name}': {string.Join(", ", sites)} mislukt twee keer op rij", settings,
            () => ShowTray(titel, string.Join(Environment.NewLine, regels.Append(slot))),
            () => SendTelegramAsync(settings, titel,
                string.Join("\n", regels.Select(r => "• " + Escape(r))) + "\n\n" + slot),
            () => SendEmailAsync(settings, titel,
                "<ul>" + string.Concat(regels.Select(r => $"<li>{Escape(r)}</li>")) + $"</ul><p>{slot}</p>"));
    }

    // ---------- opmaak van het bericht ----------

    /// <summary>De eerste titels, voor de ballon in het systeemvak.</summary>
    private static string KorteSamenvatting(IReadOnlyList<Listing> nieuwe)
    {
        var regels = nieuwe.Take(3).Select(l =>
            l.Price is { } p ? $"{l.Title} — €{p:0.##}" : l.Title);

        var tekst = string.Join(Environment.NewLine, regels);
        if (nieuwe.Count > 3) tekst += $"{Environment.NewLine}en nog {nieuwe.Count - 3}...";

        return tekst;
    }

    /// <summary>
    /// Telegram in HTML-modus. Enkel de titel wordt een link: de volledige URL
    /// erbij zetten maakt het bericht op een telefoon onleesbaar lang.
    /// </summary>
    internal static string TelegramTekst(IReadOnlyList<Listing> nieuwe)
    {
        var sb = new StringBuilder();

        foreach (var l in nieuwe.Take(15))
        {
            var prijs = l.Price is { } p ? $"€{p:0.##}" : (l.PriceLabel.Length > 0 ? l.PriceLabel : "prijs onbekend");
            var plaats = l.Location.Length > 0 ? $" · {Escape(l.Location)}" : "";

            sb.AppendLine(l.Url.Length > 0
                ? $"• <a href=\"{Escape(l.Url)}\">{Escape(l.Title)}</a> — <b>{Escape(prijs)}</b>{plaats}"
                : $"• {Escape(l.Title)} — <b>{Escape(prijs)}</b>{plaats}");
        }

        if (nieuwe.Count > 15) sb.AppendLine($"<i>en nog {nieuwe.Count - 15}...</i>");

        return sb.ToString();
    }

    /// <summary>
    /// Zoveel zoekertjes staan er hoogstens in een e-mail. Vroeger stonden ze er allemaal in,
    /// en dat kon: een site gaf er hoogstens 500. Sinds 22 september 2026 halen 2dehands en
    /// Marktplaats er tot 2000 op, en de eerste beurt daarna vond bij "Cd speler" zo'n 1700
    /// die nog nooit gezien waren - een mail van een paar honderd kilobyte om door te scrollen.
    /// De rest staat in Zentrix, achter de teller van de zoekopdracht.
    /// </summary>
    internal const int MailMaximum = 50;

    internal static string MailTekst(SavedSearch search, IReadOnlyList<Listing> nieuwe)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"<p>Nieuwe resultaten voor <b>{Escape(search.Name)}</b>:</p><ul>");

        foreach (var l in nieuwe.Take(MailMaximum))
        {
            var prijs = l.Price is { } p ? $"€{p:0.##}" : (l.PriceLabel.Length > 0 ? l.PriceLabel : "prijs onbekend");
            var plaats = l.Location.Length > 0 ? $" &middot; {Escape(l.Location)}" : "";

            sb.AppendLine($"<li><a href=\"{Escape(l.Url)}\">{Escape(l.Title)}</a> — <b>{Escape(prijs)}</b>" +
                          $"{plaats} <span style=\"color:#888\">({Escape(l.Source)})</span></li>");
        }

        sb.AppendLine("</ul>");

        // Zeggen dat er meer is, en waar: anders lijkt de lijst volledig.
        if (nieuwe.Count > MailMaximum)
            sb.AppendLine($"<p><i>En nog {nieuwe.Count - MailMaximum} meer. Open Zentrix en klik bij " +
                          $"Zoekopdrachten op de teller van '{Escape(search.Name)}' om ze allemaal te zien.</i></p>");

        return sb.ToString();
    }

    /// <summary>
    /// Maakt tekst veilig voor HTML, zowel tussen tags als binnen een attribuut als
    /// <c>href="..."</c>. Het aanhalingsteken kwam er pas op 22 september 2026 bij: zonder
    /// sloot een <c>"</c> in een link het attribuut af, en Telegram weigert dan het héle
    /// bericht ("can't parse entities"), niet enkel die ene link. <c>&amp;quot;</c> is een van
    /// de vier namen die Telegram kent, naast <c>&amp;amp;</c>, <c>&amp;lt;</c> en <c>&amp;gt;</c>.
    /// De <c>&amp;</c> gaat eerst, anders wordt de <c>&amp;</c> van de andere nog eens vervangen.
    /// </summary>
    private static string Escape(string tekst) =>
        tekst.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // ---------- Telegram ----------

    /// <summary>
    /// Stuurt een gewoon tekstbericht via de bot. Telegram kapt berichten af
    /// boven 4096 tekens, dus we snijden zelf op een veilige grens.
    /// </summary>
    public static async Task SendTelegramAsync(NotifySettings settings, string titel, string body)
    {
        var tekst = titel.Length > 0 && body.Length > 0 ? $"<b>{Escape(titel)}</b>\n\n{body}"
                  : titel.Length > 0 ? $"<b>{Escape(titel)}</b>"
                  : body;

        if (tekst.Length > 4000) tekst = tekst[..4000] + "\n<i>(afgekapt)</i>";

        await TelegramAanroepAsync(settings, "sendMessage", new
        {
            chat_id = settings.TelegramChatId,
            text = tekst,
            parse_mode = "HTML",
            disable_web_page_preview = true
        });
    }

    /// <summary>
    /// De melding zoals ze op een telefoon aankomt: eerst de kop, dan per
    /// zoekertje een foto met de titel, de prijs en de link eronder. Een foto
    /// zegt in één oogopslag of iets de moeite is; een regel tekst niet.
    ///
    /// Telegram haalt die foto zelf op bij de URL die wij doorgeven. Dat lukt
    /// niet altijd — sommige sites weigeren een verzoek zonder de juiste
    /// herkomst — dus wat niet lukt, belandt onderaan alsnog in een gewoon
    /// tekstbericht. Zo mis je nooit een resultaat door een weerbarstige foto.
    /// </summary>
    private static async Task SendTelegramNewAsync(NotifySettings settings, string titel,
                                                   IReadOnlyList<Listing> nieuwe)
    {
        // Meer dan dit wordt een stortvloed op je telefoon.
        const int MaxFotos = 6;

        if (!settings.TelegramPhotos)
        {
            await SendTelegramAsync(settings, titel, TelegramTekst(nieuwe));
            return;
        }

        await SendTelegramAsync(settings, titel, "");

        var rest = new List<Listing>();
        var verstuurd = 0;

        foreach (var listing in nieuwe)
        {
            if (verstuurd >= MaxFotos || listing.Thumbnail.Length == 0)
            {
                rest.Add(listing);
                continue;
            }

            try
            {
                await TelegramAanroepAsync(settings, "sendPhoto", new
                {
                    chat_id = settings.TelegramChatId,
                    photo = listing.LargeImage,
                    caption = Bijschrift(listing),
                    parse_mode = "HTML"
                });

                verstuurd++;
            }
            catch (Exception ex)
            {
                Log.Write($"melding (Telegram): foto van '{listing.Title}' ging niet mee - {ex.Message}");
                rest.Add(listing);
            }
        }

        if (rest.Count > 0) await SendTelegramAsync(settings, "", TelegramTekst(rest));
    }

    /// <summary>Het bijschrift onder een foto. Telegram staat 1024 tekens toe.</summary>
    internal static string Bijschrift(Listing listing)
    {
        var prijs = listing.Price is { } p ? $"€{p:0.##}"
                  : listing.PriceLabel.Length > 0 ? listing.PriceLabel
                  : "prijs onbekend";

        var regel = $"<b>{Escape(listing.Title)}</b>\n{Escape(prijs)}";

        if (listing.Location.Length > 0) regel += $" · {Escape(listing.Location)}";

        regel += $" · {Escape(listing.Source)}";

        if (listing.Url.Length > 0)
            regel += $"\n<a href=\"{Escape(listing.Url)}\">bekijken</a>";

        return regel.Length > 1000 ? regel[..1000] : regel;
    }

    /// <summary>Eén aanroep naar de bot-API, met een leesbare fout als het misgaat.</summary>
    private static async Task TelegramAanroepAsync(NotifySettings settings, string methode, object payload)
    {
        if (settings.TelegramToken.Length == 0 || settings.TelegramChatId.Length == 0)
            throw new InvalidOperationException("Telegram is niet volledig ingesteld.");

        var url = $"https://api.telegram.org/bot{settings.TelegramToken}/{methode}";

        using var response = await Http.PostAsJsonAsync(url, payload);

        if (response.IsSuccessStatusCode) return;

        var fout = await response.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Telegram gaf {(int)response.StatusCode}: {fout}");
    }

    /// <summary>
    /// Zoekt het chatnummer op van wie de bot als laatste aanschreef. Zo hoeft
    /// de gebruiker dat nummer niet zelf ergens op te zoeken: één bericht naar
    /// de bot sturen volstaat.
    /// </summary>
    public static async Task<string?> FindTelegramChatIdAsync(string token)
    {
        var url = $"https://api.telegram.org/bot{token}/getUpdates";

        using var response = await Http.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Telegram gaf {(int)response.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("result", out var result)) return null;

        // Achterstevoren: het laatste bericht is het meest waarschijnlijke.
        for (var i = result.GetArrayLength() - 1; i >= 0; i--)
        {
            var update = result[i];

            foreach (var soort in new[] { "message", "channel_post", "edited_message" })
            {
                if (!update.TryGetProperty(soort, out var bericht)) continue;
                if (!bericht.TryGetProperty("chat", out var chat)) continue;
                if (!chat.TryGetProperty("id", out var id)) continue;

                return id.GetRawText();
            }
        }

        return null;
    }

    // ---------- e-mail ----------

    /// <summary>
    /// Verstuurt de melding via een mailserver, met MailKit.
    ///
    /// Niet met <c>System.Net.Mail.SmtpClient</c>, en daar is een reden voor die
    /// je anders lang zoekt: die klasse meldt zich bij sommige servers helemaal
    /// niet aan. In de praktijk gebeurde exact dat — de server biedt na STARTTLS
    /// enkel AUTH LOGIN en PLAIN aan, en .NET stuurt dan geen AUTH. De server
    /// antwoordt op MAIL FROM met "530 5.1.0 must authenticate first", wat eruit
    /// ziet als een fout wachtwoord terwijl er nooit een wachtwoord verstuurd is.
    /// Een écht fout wachtwoord geeft "535 authentication failed"; dat verschil
    /// is de manier om de twee uit elkaar te houden. Microsoft raadt die klasse
    /// zelf al jaren af.
    /// </summary>
    public static async Task SendEmailAsync(NotifySettings settings, string onderwerp, string html)
    {
        if (settings.SmtpHost.Length == 0 || settings.MailTo.Length == 0)
            throw new InvalidOperationException("De e-mailinstellingen zijn niet volledig.");

        // Afzender: wat er als gebruikersnaam staat, tenzij dat geen adres is.
        // Veel servers weigeren een afzender die niet bij het account hoort.
        var van = settings.SmtpUser.Contains('@') ? settings.SmtpUser : settings.MailTo;

        var bericht = new MimeMessage();
        bericht.From.Add(MailboxAddress.Parse(van));
        bericht.To.Add(MailboxAddress.Parse(settings.MailTo));
        bericht.Subject = onderwerp;
        bericht.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

        // Poort 465 is versleuteld vanaf de eerste byte; 587 begint gewoon en
        // schakelt met STARTTLS over. Dat door elkaar halen geeft een verbinding
        // die blijft hangen, dus leiden we het af uit de poort.
        var beveiliging = !settings.SmtpSsl ? SecureSocketOptions.None
                        : settings.SmtpPort == 465 ? SecureSocketOptions.SslOnConnect
                        : SecureSocketOptions.StartTls;

        using var client = new SmtpClient();

        await client.ConnectAsync(settings.SmtpHost, settings.SmtpPort, beveiliging);

        // Zonder gebruikersnaam nemen we aan dat de server open staat (een eigen
        // relay in huis); anders melden we ons aan.
        if (settings.SmtpUser.Length > 0)
            await client.AuthenticateAsync(settings.SmtpUser, settings.SmtpPassword);

        await client.SendAsync(bericht);
        await client.DisconnectAsync(true);
    }

    /// <summary>
    /// Stuurt een proefbericht via één kanaal en geeft terug wat er misging, of
    /// null wanneer het gelukt is. Gebruikt door de knop "Testen" bij de
    /// instellingen: wachten tot er 's nachts iets gevonden wordt om te weten of
    /// je token klopt, is geen manier van werken.
    /// </summary>
    public static async Task<string?> TestAsync(NotifySettings settings, string kanaal)
    {
        try
        {
            const string titel = "Zentrix: proefbericht";
            const string body = "Als je dit ziet, staan de meldingen goed.";

            switch (kanaal)
            {
                case "telegram":
                    await SendTelegramAsync(settings, titel, body);
                    break;

                case "email":
                    await SendEmailAsync(settings, titel, $"<p>{body}</p>");
                    break;

                case "tray":
                    ShowTrayBalloon?.Invoke(titel, body);
                    break;
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
