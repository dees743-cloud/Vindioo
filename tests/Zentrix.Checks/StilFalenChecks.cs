using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix.Checks;

/// <summary>
/// Stil falen: een site die stukgaat maar eruitziet als "niets gevonden", een verdwenen of
/// hernoemde site, een melding die nergens aankomt, en foutmeldingen die niemand begrijpt.
/// </summary>
public static class StilFalenChecks
{
    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Foutmeldingen in gewone taal (FriendlyError)");
        {
            string D(Exception ex) => FriendlyError.Describe(ex);

            Check.Dat(D(new HttpRequestException("x", null, HttpStatusCode.Forbidden)).StartsWith("weigert de app"), "403");
            Check.Dat(D(new HttpRequestException("x", null, HttpStatusCode.TooManyRequests)).Contains("trager"), "429");
            Check.Dat(D(new HttpRequestException("x", null, HttpStatusCode.BadGateway)).Contains("probleem (502)"), "502");
            Check.Dat(D(new HttpRequestException("x", new SocketException())).Contains("niet bereikbaar"), "geen verbinding");
            Check.Dat(D(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"))
                .Contains("binnen de tijd"), "time-out van HttpClient");
            Check.Dat(D(new JsonException("'<' is an invalid start of a value.")).Contains("controlepagina"), "JSON die geen JSON is");
            Check.Dat(D(new TimeoutException("Timeout 45000ms exceeded.")).Contains("binnen de tijd"), "time-out van Playwright");

            const string brug = "Geen antwoord van de browserextensie. Staat Chrome open?";
            Check.Dat(D(new TimeoutException(brug)) == brug, "een Nederlandse melding van de brug blijft staan");
            Check.Dat(D(new InvalidOperationException("kent dit zoekwoord niet.")) == "kent dit zoekwoord niet.",
                "een eigen melding blijft staan");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Een site die ineens niets meer vindt (VerdachtLeeg)");
        {
            var s = new SavedSearch { Query = "marantz" };
            var sites = new[] { "A" };
            Dictionary<string, string> Fout(string m) => new() { ["A"] = m };

            s.RecordRun(sites, new Dictionary<string, string>(), new Dictionary<string, int> { ["A"] = 40 });
            Check.Dat(s.LastCounts.GetValueOrDefault("A") == 40, "een geslaagde beurt wordt het ijkpunt (40)");
            Check.Dat(s.VerdachtLeeg("A", 5) is null, "5 na 40: niet verdacht");

            var melding = s.VerdachtLeeg("A", 0);
            Check.Dat(melding?.StartsWith("vond niets, terwijl de vorige beurt er 40 gaf") == true, "0 na 40: verdacht");

            var r1 = s.RecordRun(sites, Fout(melding!), new Dictionary<string, int> { ["A"] = 0 });
            var r2 = s.RecordRun(sites, Fout(s.VerdachtLeeg("A", 0)!), new Dictionary<string, int> { ["A"] = 0 });
            Check.Dat(r1.Count == 0 && r2.SequenceEqual(sites), "twee verdachte beurten op rij: melding");
            Check.Dat(s.LastCounts["A"] == 40, "een verdachte beurt verzet het ijkpunt niet");

            s.RecordRun(sites, Fout(s.VerdachtLeeg("A", 0)!), new Dictionary<string, int> { ["A"] = 0 });
            Check.Dat(s.VerdachtLeeg("A", 0) is null, $"na {SavedSearch.LeegAanvaardNa} beurten wordt 0 aanvaard");

            s.RecordRun(sites, new Dictionary<string, string>(), new Dictionary<string, int> { ["A"] = 0 });
            Check.Dat(s.LastCounts["A"] == 0 && !s.HasErrors && s.VerdachtLeeg("A", 0) is null,
                "daarna is 0 het nieuwe normaal, zonder waarschuwing");

            var klein = new SavedSearch { LastCounts = { ["B"] = 3 } };
            Check.Dat(klein.VerdachtLeeg("B", 0) is null, $"0 na 3 (minder dan {SavedSearch.VerdachtVanaf}): niet verdacht");
        }

        using var site = new Proefsite();
        var store = new SiteStore();
        store.Load();
        store.Add(site.Site("Proefsite"));
        var history = new HistoryStore();
        var runner = new SearchRunner(store, history);

        SavedSearch Zoekopdracht(params string[] sites)
        {
            var s = new SavedSearch
            {
                Query = "cd",
                SiteSettings = sites.Select(n => new SiteSetting { Site = n, Enabled = true }).ToList(),
                Schedule = new SearchSchedule { Mode = ScheduleMode.Interval, NotifyOnNew = false }
            };
            s.Id = history.Add(s);
            return s;
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Planner: nul na veel, en een verdwenen site");
        {
            site.PerPagina.Clear();
            var z = Zoekopdracht("Proefsite");
            z.LastCounts["Proefsite"] = 40;

            var outcome = await runner.RunAsync(z);
            Check.Dat(outcome.SiteErrors.GetValueOrDefault("Proefsite")?.StartsWith("vond niets") == true,
                $"0 resultaten na 40 staat bij de fouten ('{outcome.SiteErrors.GetValueOrDefault("Proefsite")}')");
            Check.Dat(z.HasErrors && z.LastCounts["Proefsite"] == 40, "en in de zoekopdracht, met het ijkpunt ongemoeid");

            site.PerPagina[1] = 12;
            var weer = await runner.RunAsync(z);
            Check.Dat(weer.SiteErrors.Count == 0 && z.LastCounts["Proefsite"] == 12 && !z.HasErrors,
                "de site vindt weer iets: fout weg, nieuw ijkpunt 12");

            var met = Zoekopdracht("Proefsite", "Verdwenen");
            var o1 = await runner.RunAsync(met);
            Check.Dat(o1.SiteErrors.GetValueOrDefault("Verdwenen")?.Contains("bestaat niet meer") == true && o1.All.Count == 12,
                "een verdwenen site naast een gewone: fout voor die site, de rest zoekt gewoon");

            var alleen = Zoekopdracht("Verdwenen");
            var a1 = await runner.RunAsync(alleen);
            var a2 = await runner.RunAsync(alleen);
            Check.Dat(a1.NotRunReason?.Contains("bestaat niet meer: Verdwenen") == true, $"enkel een verdwenen site: '{a1.NotRunReason}'");
            Check.Dat(a2.JustBroken.SequenceEqual(new[] { "Verdwenen" }), "twee keer op rij: melding");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Zoekmotor: een controlepagina of een ander antwoord is een fout, geen \"niets\"");
        {
            async Task<(int Aantal, string? Fout)> Zoek(SiteDefinition def)
            {
                try { return ((await new GenericSource(def).SearchAsync("cd", 20)).Count, null); }
                catch (Exception ex) { return (0, ex.Message); }
            }

            site.PerPagina.Clear();
            site.VasteInhoud = "<html><head><title>Just a moment...</title></head><body>Checking your browser</body></html>";
            var controle = await Zoek(site.Site("Proefsite"));
            Check.Dat(controle.Fout?.StartsWith("toonde een controlepagina") == true, $"controlepagina: '{controle.Fout}'");

            site.VasteInhoud = "<html><body></body></html>";
            var bijnaLeeg = await Zoek(site.Site("Proefsite"));
            Check.Dat(bijnaLeeg.Fout?.StartsWith("gaf een bijna lege pagina") == true, $"bijna lege pagina: '{bijnaLeeg.Fout}'");

            site.VasteInhoud = null;
            var echtLeeg = await Zoek(site.Site("Proefsite"));
            Check.Dat(echtLeeg is { Aantal: 0, Fout: null }, "een gewone resultatenpagina zonder resultaten: gewoon niets gevonden");

            var json = site.Site("Proefsite");
            json.Kind = SiteKind.Json;
            json.ItemSelector = "data.items";
            json.TitleSelector = "title";

            site.VasteInhoud = """{"errors":[{"message":"PersistedQueryNotFound"}]}""";
            var apiFout = await Zoek(json);
            Check.Dat(apiFout.Fout?.Contains("de site zegt: PersistedQueryNotFound") == true, $"API-fout: '{apiFout.Fout}'");

            site.VasteInhoud = """{"data":{}}""";
            var apiLeeg = await Zoek(json);
            Check.Dat(apiLeeg is { Aantal: 0, Fout: null }, "API die een lege lijst weglaat: gewoon niets gevonden");

            site.VasteInhoud = """{"data":{"items":[{"title":"Plaat"}]}}""";
            var apiGoed = await Zoek(json);
            Check.Dat(apiGoed is { Aantal: 1, Fout: null }, "API met resultaten: werkt");

            site.VasteInhoud = null;
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Een site hernoemen neemt alles mee (RenameSource)");
        {
            var z = Zoekopdracht("Oud");
            z.LastErrors["Oud"] = "kapot";
            z.FailureStreaks["Oud"] = 1;
            z.LastCounts["Oud"] = 30;
            history.Update(z);

            history.MarkSeen(z.Id, new[] { "Oud:https://x/1", "Oud:https://x/2", "Anders:https://x/1" });
            history.AddFavorite(new Listing { Source = "Oud", ExternalId = "https://x/2", Title = "Plaat", Url = "https://x/2" });
            history.SaveOutcome(z.Id, new[] { new Listing { Source = "Oud", ExternalId = "https://x/1", Title = "Plaat" } });

            history.RenameSource("Oud", "Nieuw");

            var gezien = history.GetSeenKeys(z.Id);
            Check.Dat(gezien.SetEquals(new[] { "Nieuw:https://x/1", "Nieuw:https://x/2", "Anders:https://x/1" }),
                "al gezien: meegenomen, een andere site ongemoeid");
            Check.Dat(history.GetFavoriteKeys().Contains("Nieuw:https://x/2") &&
                      history.GetFavorites().Any(f => f.Source == "Nieuw"), "favorieten: sleutel en bron meegenomen");
            Check.Dat(history.GetOutcome(z.Id).All(l => l.Source == "Nieuw"), "bewaarde resultaten: meegenomen");

            var terug = history.GetAll().Single(x => x.Id == z.Id);
            Check.Dat(terug.SiteSettings.Single().Site == "Nieuw" && terug.LastErrors.ContainsKey("Nieuw") &&
                      terug.FailureStreaks.ContainsKey("Nieuw") && terug.LastCounts.GetValueOrDefault("Nieuw") == 30,
                "de zoekopdracht: site, fouten, reeks en ijkpunt meegenomen");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Meldingen: eerlijk zeggen of er iets vertrok");
        {
            var instellingen = AppSettings.Current.Notify;
            instellingen.Tray = true;
            instellingen.Telegram = false;
            instellingen.Email = false;

            var z = new SavedSearch { Query = "melding" };
            var nieuw = new[] { new Listing { Source = "Proefsite", Title = "Plaat", Url = "https://x/1" } };

            Notifier.ShowTrayBalloon = null;
            Check.Dat(!await Notifier.NotifyNewAsync(z, nieuw), "enkel het systeemvak, zonder pictogram: niet verstuurd");

            var getoond = 0;
            Notifier.ShowTrayBalloon = (_, _) => getoond++;
            Check.Dat(await Notifier.NotifyNewAsync(z, nieuw) && getoond == 1, "met pictogram: verstuurd");

            instellingen.Tray = false;
            Check.Dat(await Notifier.NotifyNewAsync(z, nieuw), "geen kanaal ingesteld: geen fout");

            Notifier.ShowTrayBalloon = null;
            instellingen.Tray = true;
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Meldingen: een link met een aanhalingsteken blijft heel");
        {
            // Een " sloot vroeger het href-attribuut af, en dan weigert Telegram het hele
            // bericht. Nagekeken zoals een lezer het doet: de HTML ontleden en de link
            // teruglezen. Met de oude Escape kwam er "https://x/zoek?q=" terug.
            const string url = "https://x/zoek?q=\"12 inch\"&p=2";
            var plaat = new Listing { Source = "Proefsite", Title = "12\" plaat <nieuw>", Url = url };
            var parser = new AngleSharp.Html.Parser.HtmlParser();

            foreach (var (naam, html) in new[]
            {
                ("e-mail", Notifier.MailTekst(new SavedSearch { Query = "plaat" }, new[] { plaat })),
                ("Telegram", Notifier.TelegramTekst(new[] { plaat })),
                ("bijschrift bij een foto", Notifier.Bijschrift(plaat))
            })
            {
                var a = parser.ParseDocument(html).QuerySelector("a");
                Check.Dat(a?.GetAttribute("href") == url && a.Attributes.Length == 1,
                    $"{naam}: de link komt ongeschonden terug, zonder losse stukken als attribuut");
            }

            var titel = parser.ParseDocument(Notifier.TelegramTekst(new[] { plaat })).QuerySelector("a")?.TextContent;
            Check.Dat(titel == plaat.Title, "Telegram: de titel met \" en < erin ook");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Telegram: een lang bericht wordt nooit midden in de HTML afgeknipt");
        {
            // Wat Telegram aanvaardt: enkel <b>, <i> en <a href="...">, elk gesloten, geen losse <,
            // en enkel de vier namen die Telegram kent. Anders weigert het het hele bericht.
            static bool Geldig(string html)
            {
                var tags = System.Text.RegularExpressions.Regex.Matches(html, "<[^>]*>").Select(m => m.Value).ToList();
                if (html.Count(c => c == '<') != tags.Count || html.Count(c => c == '>') != tags.Count) return false;
                if (tags.Any(t => !System.Text.RegularExpressions.Regex.IsMatch(t, "^(</?[bi]>|<a href=\"[^\"]*\">|</a>)$")))
                    return false;

                foreach (var naam in new[] { "b", "i", "a" })
                    if (tags.Count(t => t == $"<{naam}>" || t.StartsWith($"<{naam} ")) != tags.Count(t => t == $"</{naam}>"))
                        return false;

                return !System.Text.RegularExpressions.Regex.IsMatch(html, "&(?!amp;|lt;|gt;|quot;)");
            }

            var parser = new AngleSharp.Html.Parser.HtmlParser();

            // Vijftien zoekertjes met een link van duizend tekens, zoals een lange link van eBay.
            var lang ="https://www.ebay.be/itm/1?_skw=cd&hash=" + new string('x', 1000);
            var lijst = Enumerable.Range(1, 15).Select(i => new Listing
            {
                Source = "eBay", Title = $"Marantz CD6006 & afstandsbediening {i}", Price = 100 + i, Url = lang + i
            }).ToList();

            const string kop = "Zentrix: 15 nieuwe resultaten voor 'Cd speler'";
            var bericht = Notifier.TelegramBericht(kop, Notifier.TelegramTekst(lijst, Notifier.TelegramBerichtMaximum - kop.Length - 2));
            var links = parser.ParseDocument(bericht).QuerySelectorAll("a").Select(a => a.GetAttribute("href")).ToList();

            var oud = bericht.Length > 4000 ? bericht[..4000] + "\n<i>(afgekapt)</i>" : bericht;
            Check.Dat(!Geldig(oud), $"zo knipte het vroeger: op 4000 tekens HTML ({bericht.Length}), en dat weigert Telegram");

            Check.Dat(Geldig(bericht) && links.Count == 15 && links.Select((h, i) => h == lang + (i + 1)).All(x => x),
                $"nu: alle 15, elke link volledig ({Notifier.ZichtbareLengte(bericht)} zichtbare tekens)");
            Check.Dat(!bericht.Contains("en nog") && !bericht.Contains("afgekapt"), "... zonder 'en nog' of 'afgekapt'");

            // Titels van 400 tekens: ingekort, als tekst, voor ze HTML worden.
            var lange = lijst.Select(l => new Listing
            {
                Source = l.Source, Title = new string('é', 399) + "&", Price = l.Price, Url = l.Url
            }).ToList();
            var ingekort = Notifier.TelegramBericht(kop, Notifier.TelegramTekst(lange));
            var titels = parser.ParseDocument(ingekort).QuerySelectorAll("a").Select(a => a.TextContent).ToList();
            Check.Dat(Geldig(ingekort) && titels.Count > 0 && titels.All(t => t.Length <= 200 && t.EndsWith('…')),
                $"titels van 400 tekens: ingekort tot 200, met een beletselteken ({titels.Count} passen)");

            // Weinig ruimte: hele zoekertjes, en eerlijk hoeveel er nog zijn.
            var krap = Notifier.TelegramTekst(lijst, 500);
            var getoond = parser.ParseDocument(krap).QuerySelectorAll("a").Length;
            Check.Dat(Geldig(krap) && getoond is > 0 and < 15 && krap.Contains($"en nog {15 - getoond}...") &&
                      Notifier.ZichtbareLengte(krap) <= 500,
                $"ruimte voor 500 tekens: {getoond} hele zoekertjes en 'en nog {15 - getoond}...'");

            // Het vangnet: honderd regels die elk op zich geldig zijn.
            var regels = string.Join("\n", Enumerable.Range(1, 100).Select(i => $"• <b>regel {i}</b> " + new string('x', 80) + " &amp; meer"));
            var geknipt = Notifier.TelegramBericht("Proef", regels);
            Check.Dat(Geldig(geknipt) && geknipt.EndsWith("<i>(afgekapt)</i>") &&
                      Notifier.ZichtbareLengte(geknipt) <= Notifier.TelegramBerichtMaximum,
                $"het vangnet knipt tussen regels ({Notifier.ZichtbareLengte(geknipt)} zichtbare tekens)");

            var eenRegel = Notifier.TelegramBericht("", "<b>" + new string('a', 5000) + "</b>");
            Check.Dat(Geldig(eenRegel) && Notifier.ZichtbareLengte(eenRegel) <= Notifier.TelegramBerichtMaximum,
                "één regel die op zich al te lang is: als gewone tekst, ingekort");

            // Het bijschrift bij een foto: een titel van 2000 en een link van 1500 tekens.
            var foto = new Listing
            {
                Source = "eBay", Title = new string('t', 2000), Price = 5, Location = "Gent", Url = lang + new string('y', 500)
            };
            var bijschrift = Notifier.Bijschrift(foto);
            Check.Dat(Geldig(bijschrift) && Notifier.ZichtbareLengte(bijschrift) <= Notifier.TelegramBijschriftMaximum &&
                      parser.ParseDocument(bijschrift).QuerySelector("a")?.GetAttribute("href") == foto.Url,
                $"bijschrift: de titel ingekort, de link volledig ({Notifier.ZichtbareLengte(bijschrift)} zichtbare tekens)");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Instellingen: wachtwoord en token beschermd door Windows");
        {
            const string wachtwoord = "proef-wachtwoord-123";
            const string token = "1234567:AAproef-token";
            var pad = AppPaths.SettingsFile;
            string B64(string t) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(t));
            int Beschermd(string inhoud) => System.Text.RegularExpressions.Regex.Matches(inhoud, "\"dpapi:").Count;
            bool Leesbaar(string inhoud) =>
                inhoud.Contains(wachtwoord) || inhoud.Contains(B64(wachtwoord)) ||
                inhoud.Contains(token) || inhoud.Contains(B64(token));

            // Een ander vinkje mag intussen niet veranderen: dat bewijst dat de rest gewoon meegaat.
            AppSettings.Current.PageSize = 150;
            AppSettings.Current.Notify.SmtpPassword = wachtwoord;
            AppSettings.Current.Notify.TelegramToken = token;
            AppSettings.Current.Save();

            var inhoud = File.ReadAllText(pad);
            Check.Dat(!Leesbaar(inhoud), "in het bestand staat geen van beide leesbaar, ook niet als base64");
            Check.Dat(Beschermd(inhoud) == 2, "allebei beschermd door Windows (dpapi:)");
            Check.Dat(AppSettings.Current.Notify.SmtpPassword == wachtwoord && AppSettings.Current.Notify.TelegramToken == token,
                "in het geheugen blijven ze bruikbaar na het bewaren");

            AppSettings.Load();
            Check.Dat(AppSettings.Current.Notify.SmtpPassword == wachtwoord && AppSettings.Current.Notify.TelegramToken == token &&
                      AppSettings.Current.PageSize == 150,
                "weer ingelezen: dezelfde waarden, en de rest van de instellingen ook");

            // Een bestand van voor 22 september 2026: het wachtwoord als base64, het token leesbaar.
            void Zet(string wat, string waarde)
            {
                var boom = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(pad))!;
                boom["Notify"]![wat] = waarde;
                File.WriteAllText(pad, boom.ToJsonString());
            }

            Zet("SmtpPassword", "b64:" + B64(wachtwoord));
            Zet("TelegramToken", token);
            AppSettings.Load();
            inhoud = File.ReadAllText(pad);
            Check.Dat(AppSettings.Current.Notify.SmtpPassword == wachtwoord && AppSettings.Current.Notify.TelegramToken == token,
                "een oud bestand (b64: en leesbaar) wordt gewoon gelezen");
            Check.Dat(!Leesbaar(inhoud) && Beschermd(inhoud) == 2, "... en meteen herschreven, beschermd");

            // Een oudere Zentrix las de beschermde vorm als wachtwoord en pakte ze in als b64:.
            var beschermd = System.Text.Json.Nodes.JsonNode.Parse(inhoud)!["Notify"]!["SmtpPassword"]!.GetValue<string>();
            Zet("SmtpPassword", "b64:" + B64(beschermd));
            AppSettings.Load();
            Check.Dat(AppSettings.Current.Notify.SmtpPassword == wachtwoord && Beschermd(File.ReadAllText(pad)) == 2,
                "door een oudere Zentrix nog eens ingepakt: toch het juiste wachtwoord, en weer beschermd");

            // Beschermd voor een ander account of een andere pc: niet te openen. Hier nagebootst
            // met iets wat DPAPI niet herkent; Windows antwoordt daar op dezelfde manier op.
            var vreemd = "dpapi:" + B64("van een andere pc");
            Zet("SmtpPassword", vreemd);
            AppSettings.Load();
            Check.Dat(AppSettings.Current.Notify.SmtpPassword == "" && AppSettings.Current.Notify.TelegramToken == token,
                "niet te openen: het wachtwoord is leeg, het token werkt nog");

            AppSettings.Current.PageSize = 200;
            AppSettings.Current.Save();
            Check.Dat(File.ReadAllText(pad).Contains(vreemd), "een ander vinkje bewaren wist het onleesbare wachtwoord niet");

            AppSettings.Current.Notify.SmtpPassword = "nieuw-wachtwoord";
            AppSettings.Current.Save();
            inhoud = File.ReadAllText(pad);
            Check.Dat(!inhoud.Contains(vreemd) && Beschermd(inhoud) == 2 && !inhoud.Contains("nieuw-wachtwoord"),
                "een nieuw ingevuld wachtwoord vervangt het, beschermd");

            // Opruimen: de controles hierna verwachten geen kanalen met geheimen.
            AppSettings.Current.Notify.SmtpPassword = "";
            AppSettings.Current.Notify.TelegramToken = "";
            AppSettings.Current.PageSize = 100;
            AppSettings.Current.Save();
        }

        // ---------------------------------------------------------------------------
        Check.Groep("De API-sleutel staat beschermd in het bestand, niet in de omgeving");
        {
            var vorige = AppSettings.Current.ApiKey;
            var vorigeOmgeving = Environment.GetEnvironmentVariable(AppSettings.ApiKeyVariable);

            try
            {
                // Zo ziet een echte sleutel eruit; de waarde zelf doet er niet toe.
                const string sleutel = "sk-ant-api03-proefsleutel-NIET-ECHT";

                AppSettings.Current.ApiKey = sleutel;
                Environment.SetEnvironmentVariable(AppSettings.ApiKeyVariable, null);
                AppSettings.Current.Save();

                var ruw = File.ReadAllText(Path.Combine(AppPaths.Folder, "instellingen.json"));

                Check.Dat(!ruw.Contains(sleutel),
                    "de sleutel staat niet leesbaar in instellingen.json");

                Check.Dat(ruw.Contains("dpapi:"),
                    "hij staat er beschermd door Windows in");

                // En hij komt er ook weer uit.
                AppSettings.Current.ApiKey = "";
                AppSettings.Load();

                Check.Dat(AppSettings.Current.ApiKey == sleutel,
                    "en hij komt er ongeschonden weer uit");

                Check.Dat(AppSettings.ApiKeyInUse == sleutel,
                    "de analyse krijgt die sleutel");

                // De terugval blijft bestaan: wie hem zelf in de omgeving zet, werkt gewoon
                // verder - een wegwerpprojectje bijvoorbeeld.
                AppSettings.Current.ApiKey = "";
                Environment.SetEnvironmentVariable(AppSettings.ApiKeyVariable, "sk-ant-uit-de-omgeving");

                Check.Dat(AppSettings.ApiKeyInUse == "sk-ant-uit-de-omgeving",
                    "zonder bewaarde sleutel telt de omgevingsvariabele nog");

                // Maar de bewaarde wint, want die is de bedoeling.
                AppSettings.Current.ApiKey = sleutel;

                Check.Dat(AppSettings.ApiKeyInUse == sleutel,
                    "staat er een bewaarde, dan wint die");

                // En de verhuis: stond hij enkel in de omgeving, dan neemt Load hem over. Zo
                // hoeft niemand hem opnieuw op te zoeken na het bijwerken.
                AppSettings.Current.ApiKey = "";
                AppSettings.Current.Save();
                Environment.SetEnvironmentVariable(AppSettings.ApiKeyVariable, "sk-ant-verhuisd");
                AppSettings.Load();

                Check.Dat(AppSettings.Current.ApiKey == "sk-ant-verhuisd",
                    "een sleutel die enkel in de omgeving stond, verhuist naar het bestand");

                var na = File.ReadAllText(Path.Combine(AppPaths.Folder, "instellingen.json"));
                Check.Dat(!na.Contains("sk-ant-verhuisd"),
                    "en staat daar meteen beschermd in");
            }
            finally
            {
                AppSettings.Current.ApiKey = vorige;
                Environment.SetEnvironmentVariable(AppSettings.ApiKeyVariable, vorigeOmgeving);
                AppSettings.Current.Save();
            }
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Sites importeren: zeggen welke vervangen werden");
        {
            var map = Path.Combine(AppPaths.Folder, "import-proef");
            Directory.CreateDirectory(map);

            var opties = new JsonSerializerOptions { WriteIndented = true };

            // Een gewoon https-adres, want sinds 1 oktober 2026 weigert het importeren een
            // bestand dat naar een privé-adres wijst - en de proefsite draait op 127.0.0.1.
            // Deze controle gaat over "welke sites werden vervangen" en haalt niets op, dus
            // het adres doet er verder niet toe. Zie ImportChecks voor de regel zelf.
            SiteDefinition Gedeeld(string naam)
            {
                var def = site.Site(naam);
                def.BaseUrl = "https://www.voorbeeld.be";
                def.SearchUrlTemplate = "https://www.voorbeeld.be/q/{query}/";
                return def;
            }

            File.WriteAllText(Path.Combine(map, "proefsite.json"), JsonSerializer.Serialize(Gedeeld("Proefsite"), opties));
            File.WriteAllText(Path.Combine(map, "nieuwe.json"), JsonSerializer.Serialize(Gedeeld("Nieuwe site"), opties));
            File.WriteAllText(Path.Combine(map, "kapot.json"), "{ dit is geen json");

            var (aantal, mislukt, vervangen) = store.ImportFolder(map);

            // "mislukt" draagt nu ook de reden mee, zodat het scherm niet enkel een
            // bestandsnaam toont: "kapot.json (gaf iets anders terug dan ...)".
            Check.Dat(aantal == 2 && mislukt.Count == 1 && mislukt[0].StartsWith("kapot.json")
                      && vervangen.SequenceEqual(new[] { "Proefsite" }),
                $"2 geïmporteerd, Proefsite vervangen, kapot.json mislukt ({aantal}; {string.Join(",", vervangen)}; {string.Join(",", mislukt)})");
        }
    }
}
