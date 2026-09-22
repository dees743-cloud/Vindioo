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
        Check.Groep("Sites importeren: zeggen welke vervangen werden");
        {
            var map = Path.Combine(AppPaths.Folder, "import-proef");
            Directory.CreateDirectory(map);

            var opties = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(map, "proefsite.json"), JsonSerializer.Serialize(site.Site("Proefsite"), opties));
            File.WriteAllText(Path.Combine(map, "nieuwe.json"), JsonSerializer.Serialize(site.Site("Nieuwe site"), opties));
            File.WriteAllText(Path.Combine(map, "kapot.json"), "{ dit is geen json");

            var (aantal, mislukt, vervangen) = store.ImportFolder(map);
            Check.Dat(aantal == 2 && mislukt.SequenceEqual(new[] { "kapot.json" }) && vervangen.SequenceEqual(new[] { "Proefsite" }),
                $"2 geïmporteerd, Proefsite vervangen, kapot.json mislukt ({aantal}; {string.Join(",", vervangen)}; {string.Join(",", mislukt)})");
        }
    }
}
