using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Zentrix.Models;
using Zentrix.Services;
using Zentrix.Sources;

namespace Zentrix.Checks;

/// <summary>
/// De lokale server van de brug, met verzoeken zoals een webpagina en zoals de extensie ze
/// sturen, en een nagebootste extensie die opdrachten aanneemt en pagina's terugstuurt.
/// Heeft poort 8731 nodig: draait Zentrix, dan wordt dit overgeslagen.
/// </summary>
public static class BrugChecks
{
    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Brug: app en extensie tekenen hetzelfde");
        {
            // Sinds 1 oktober 2026 gaat de koppelcode niet meer over de lijn; allebei de kanten
            // bewijzen enkel dát ze hem kennen. Dan moeten die twee implementaties wél precies
            // hetzelfde opleveren, en die staan in verschillende talen: HMACSHA256 in C# en
            // crypto.subtle in de extensie.
            //
            // De waarden hieronder zijn met Node uitgerekend (crypto.createHmac), dus dit meet
            // C# tegen een ONAFHANKELIJKE implementatie en niet tegen zichzelf.
            Check.Dat(BridgeServer.Teken("koppelcode-proef", "abc123\nhallo")
                      == "3a62f4d07ded1eb949895250011b5fef46fc0d4ebfe794457bca7a15cf3c98cd",
                "dezelfde handtekening als JavaScript, met inhoud");

            Check.Dat(BridgeServer.Teken("koppelcode-proef", "abc123\n")
                      == "d3d709fa19efed9eb4a63b5b4758370eaa01b64dc6d99ee5766791f465f617dd",
                "en met een lege body (zoals bij /job en /ping)");

            // Een andere code geeft een andere handtekening - anders bewijst ze niets.
            Check.Dat(BridgeServer.Teken("andere-code", "abc123\nhallo")
                      != BridgeServer.Teken("koppelcode-proef", "abc123\nhallo"),
                "een andere koppelcode geeft een andere handtekening");

            // En een andere nonce ook, anders is een opgevangen handtekening eindeloos te
            // hergebruiken.
            Check.Dat(BridgeServer.Teken("koppelcode-proef", "xyz789\nhallo")
                      != BridgeServer.Teken("koppelcode-proef", "abc123\nhallo"),
                "een andere nonce ook");
        }

        // ---------------------------------------------------------------------------
        // Deze staat bewust vóór de poortcontrole hieronder: ze kijkt naar een bestand in de
        // broncode en niet naar iets dat draait, dus ze hoort ook te werken terwijl Zentrix open
        // staat - en dan zijn alle andere brugcontroles overgeslagen.
        Check.Groep("Brug: het manifest van de extensie vraagt niet om alle sites");
        ManifestControles();

        // ---------------------------------------------------------------------------
        Check.Groep("Brug: enkel de extensie kan een verkeerde koppelcode melden");

        if (!PoortVrij())
        {
            Check.Overgeslagen($"poort {BridgeServer.Port} is bezet (draait Zentrix?): de brugcontroles zijn overgeslagen");
            return;
        }

        var brug = BridgeServer.Instance;

        // Eerst: wat als een ander programma de poort bezet houdt.
        using (var bezetter = new TcpListener(IPAddress.Loopback, BridgeServer.Port))
        {
            bezetter.Start();

            Exception? startFout = null;
            try { brug.Start(); } catch (Exception ex) { startFout = ex; }
            Check.Dat(startFout is null && brug.PortBusy, "poort bezet: geen uitzondering, wel PortBusy");

            var status = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(5));
            Check.Dat(status == BridgeStatus.PortInUse, $"EnsureBridgeAsync zegt het meteen ({status})");

            bezetter.Stop();
        }

        brug.Start();
        Check.Dat(!brug.PortBusy, "poort weer vrij: de brug start alsnog");

        // Draait Chrome met de brug-extensie van de gebruiker, dan klopt die elke 250 ms aan met
        // de échte koppelcode. Dit controleproject heeft een eigen gegevensmap en dus een andere
        // code, dus voor deze brug is dat een verkeerde - en dan staat WrongCodeRecently altijd
        // aan, los van wat wij sturen. Die twee controles vallen dan weg, zoals de hele groep
        // wegvalt wanneer Zentrix de poort bezet houdt.
        await Task.Delay(1200);
        var vreemdeExtensie = brug.WrongCodeRecently;

        var web = await StuurAsync("GET", "/job?token=verzonnen", "Origin: https://kwaad.example");
        Check.Dat(!web.Contains("Access-Control-Allow-Origin", StringComparison.OrdinalIgnoreCase),
            "webpagina krijgt geen toestemming om het antwoord te lezen");

        if (vreemdeExtensie)
        {
            Check.Overgeslagen("Chrome draait met de brug-extensie en meldt zich met een andere code: " +
                               "de twee controles op 'een webpagina telt niet' zijn overgeslagen");
        }
        else
        {
            Check.Dat(!brug.WrongCodeRecently, "webpagina (fetch) met een verzonnen code: telt niet");

            await StuurAsync("GET", "/job?token=verzonnen");
            Check.Dat(!brug.WrongCodeRecently, "webpagina (img, zonder Origin) met een verzonnen code: telt niet");
        }

        var voorvraagWeb = await StuurAsync("OPTIONS", "/job?token=verzonnen", "Origin: https://kwaad.example",
            "Access-Control-Request-Headers: x-zentrix-brug");
        Check.Dat(!voorvraagWeb.Contains("Access-Control-Allow", StringComparison.OrdinalIgnoreCase),
            "voorvraag van een webpagina voor de kopregel: geweigerd");

        var voorvraagExt = await StuurAsync("OPTIONS", "/job?token=x", "Origin: chrome-extension://abcdefgh",
            "Access-Control-Request-Headers: x-zentrix-brug");
        Check.Dat(voorvraagExt.Contains("Access-Control-Allow-Origin: chrome-extension://abcdefgh") &&
                  voorvraagExt.Contains(BridgeServer.ExtensionHeader),
            "voorvraag van de extensie: toegestaan");

        var n1 = Guid.NewGuid().ToString("N");
        var fout = await StuurAsync("GET", $"/job?{BridgeServer.NonceParam}={n1}", Getekend("verzonnen", n1));
        Check.Dat(brug.WrongCodeRecently && fout.Contains("verkeerde koppelcode"), "extensie met een verkeerde code: gemeld");

        var n2 = Guid.NewGuid().ToString("N");
        var ping = await StuurAsync("GET", $"/ping?{BridgeServer.NonceParam}={n2}", Getekend(brug.Token, n2));
        Check.Dat(ping.Contains("\"ok\":true") && brug.ExtensionAlive, "extensie met de juiste code: /ping klopt");

        // ---------------------------------------------------------------------------
        Check.Groep("Brug: grenzen aan wat ze aanneemt");
        {
            // Telkens enkel de kop, zonder de aangekondigde body. De oude brug reserveerde dan
            // die maat en bleef op de body wachten, en deze verzoeken liepen af zonder antwoord.
            // Met een geldige voorcontrole: die kan de brug nakijken voor ze een byte van de body
            // leest, dus de 413 komt meteen. Dat is precies waarvoor die tweede handtekening er is.
            string Voor(string nonce) =>
                $"{BridgeServer.ExtensionHeader}: 1\r\n" +
                $"{BridgeServer.PreHeader}: {BridgeServer.Teken(brug.Token, nonce)}\r\n";

            var nG = Guid.NewGuid().ToString("N");
            var groot = await RauwAsync($"POST /result?{BridgeServer.NonceParam}={nG} HTTP/1.1\r\n" +
                                        Gastheer + Voor(nG) + "Content-Length: 1500000000\r\n\r\n");
            Check.Dat(groot.Antwoord.StartsWith("HTTP/1.1 413"),
                $"juiste code, 1,5 GB aangekondigd: meteen geweigerd ({Eerste(groot.Antwoord)})");

            var nR = Guid.NewGuid().ToString("N");
            var ruim = await RauwAsync($"POST /result?{BridgeServer.NonceParam}={nR} HTTP/1.1\r\n" +
                                       Gastheer + Voor(nR) + "Content-Length: 99999999999\r\n\r\n");
            Check.Dat(ruim.Antwoord.StartsWith("HTTP/1.1 413"), "een maat voorbij de 2 GB: ook geweigerd, niet als 0 gelezen");

            // En zonder de code: dan wordt die body sowieso niet gelezen, dus geen 413 maar een
            // gewone weigering. Het punt is dat er niets gereserveerd wordt.
            var nZ = Guid.NewGuid().ToString("N");
            var zonder = await RauwAsync($"POST /result?{BridgeServer.NonceParam}={nZ} HTTP/1.1\r\n" +
                                         Gastheer + $"{BridgeServer.ExtensionHeader}: 1\r\nContent-Length: 1500000000\r\n\r\n");
            Check.Dat(zonder.Antwoord.Contains("verkeerde koppelcode"),
                $"zonder de code wordt die body niet eens gelezen ({Eerste(zonder.Antwoord)})");

            var vreemd = await RauwAsync("POST /result?token=verzonnen HTTP/1.1\r\n" + Gastheer +
                                         "Content-Length: 50000000\r\n\r\n");
            Check.Dat(vreemd.Antwoord.Contains("verkeerde koppelcode"),
                "verzonnen code, 50 MB aangekondigd: meteen geweigerd, de body wordt niet gelezen");

            // Een kop zonder einde. Het antwoord kan verloren gaan als de brug sluit terwijl er nog
            // iets onderweg is (dan komt er een reset); wat telt, is dat ze sluit.
            var lang = await RauwAsync("GET /job?token=" + new string('x', 100_000) + " HTTP/1.1\r\n" + Gastheer);
            Check.Dat(lang.Gesloten, $"een kop van 100 kB zonder einde: afgebroken ({Eerste(lang.Antwoord)})");

            var nGewoon = Guid.NewGuid().ToString("N");
            var gewoon = await RauwAsync($"POST /result?{BridgeServer.NonceParam}={nGewoon} HTTP/1.1\r\n" +
                                         Gastheer + Voor(nGewoon) + "Content-Length: 2\r\n\r\n{{}}");
            Check.Dat(gewoon.Antwoord.StartsWith("HTTP/1.1 200"), "een gewone levering gaat nog door");

            var vroeger = BridgeServer.ReadTimeout;
            try
            {
                BridgeServer.ReadTimeout = TimeSpan.FromSeconds(1);
                var stil = await RauwAsync("", TimeSpan.FromSeconds(5));
                Check.Dat(stil.Gesloten && stil.Duur < TimeSpan.FromSeconds(4),
                    $"een verbinding die zwijgt: na de wachttijd gesloten ({stil.Duur.TotalSeconds:F1} s)");

                // Meer zwijgers dan er plaatsen zijn: wat er niet meer bij past, sluit de brug
                // meteen, en zo staan de plaatsen vol hoeveel er ook naast grijpen. Ruimer dan
                // precies 32, want draait Chrome met de brug-extensie, dan neemt die er af en
                // toe zelf een - en dan zou "precies 32" er 31 zijn.
                BridgeServer.ReadTimeout = TimeSpan.FromSeconds(2);
                var zwijgers = new List<TcpClient>();
                try
                {
                    for (var i = 0; i < BridgeServer.MaxConnections + 8; i++)
                    {
                        var zwijger = new TcpClient();
                        await zwijger.ConnectAsync("127.0.0.1", BridgeServer.Port);
                        zwijgers.Add(zwijger);
                    }

                    await Task.Delay(300);
                    var nV = Guid.NewGuid().ToString("N");
                    var teVeel = await RauwAsync($"GET /ping?{BridgeServer.NonceParam}={nV} HTTP/1.1\r\n" + Gastheer +
                                                 string.Join("\r\n", Getekend(brug.Token, nV)) + "\r\n\r\n");
                    Check.Dat(teVeel.Gesloten && teVeel.Antwoord.Length == 0,
                        $"de plaatsen vol ({BridgeServer.MaxConnections}): een volgende wordt meteen gesloten");

                    await Task.Delay(2500);
                    var nW = Guid.NewGuid().ToString("N");
                    var weer = await RauwAsync($"GET /ping?{BridgeServer.NonceParam}={nW} HTTP/1.1\r\n" + Gastheer +
                                               string.Join("\r\n", Getekend(brug.Token, nW)) + "\r\n\r\n");
                    Check.Dat(weer.Antwoord.Contains("\"ok\":true"), "na hun wachttijd is er weer plaats");
                }
                finally
                {
                    foreach (var zwijger in zwijgers) zwijger.Dispose();
                }
            }
            finally
            {
                BridgeServer.ReadTimeout = vroeger;
            }
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Brug: enkel verzoeken die echt voor 127.0.0.1 bedoeld zijn");
        {
            var n = Guid.NewGuid().ToString("N");

            // DNS-rebinding: een webpagina laat haar eigen naam naar 127.0.0.1 wijzen en praat
            // dan met ons. De browser stuurt die naam mee als Host, en daaraan is het te zien.
            var vreemdeNaam = await RauwAsync($"GET /ping?{BridgeServer.NonceParam}={n} HTTP/1.1\r\n" +
                                              "Host: kwaadaardig.be\r\n" +
                                              string.Join("\r\n", Getekend(brug.Token, n)) + "\r\n\r\n");

            Check.Dat(vreemdeNaam.Antwoord.StartsWith("HTTP/1.1 400"),
                $"een vreemde Host-kopregel wordt geweigerd, ook mét een geldige handtekening ({Eerste(vreemdeNaam.Antwoord)})");

            var zonderHost = await RauwAsync($"GET /ping?{BridgeServer.NonceParam}={n} HTTP/1.1\r\n" +
                                             string.Join("\r\n", Getekend(brug.Token, n)) + "\r\n\r\n");

            Check.Dat(zonderHost.Antwoord.StartsWith("HTTP/1.1 400"),
                $"zonder Host-kopregel ook ({Eerste(zonderHost.Antwoord)})");

            var localhost = await RauwAsync($"GET /ping?{BridgeServer.NonceParam}={n} HTTP/1.1\r\n" +
                                            $"Host: localhost:{BridgeServer.Port}\r\n" +
                                            string.Join("\r\n", Getekend(brug.Token, n)) + "\r\n\r\n");

            Check.Dat(localhost.Antwoord.Contains("\"ok\":true"), "\"localhost\" mag wel");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Brug: de extensie vraagt toegang per site");
        {
            // Sinds 1 oktober 2026 vraagt de extensie bij het installeren geen toegang tot álle
            // sites meer. Dat was het zwakke punt: het adres van een opdracht komt uit een
            // sitebestand dat je van iemand anders krijgt, en met <all_urls> had een verzonnen
            // bestand jouw aangemelde browser naar eender welke site kunnen sturen.
            //
            // Toestemming vragen kan enkel voor een site die de extensie kent, dus geeft de app
            // haar de hosts van de brugsites. Zonder dat zou elke nieuwe site één mislukte
            // zoekopdracht kosten voor je hem kan aanvinken.
            var eerder = brug.BridgeHosts;
            brug.BridgeHosts = () => new List<string> { "api.voorbeeld.be", "www.voorbeeld.be" };

            var nH = Guid.NewGuid().ToString("N");
            var lijst = await StuurAsync("GET", $"/hosts?{BridgeServer.NonceParam}={nH}",
                                         Getekend(brug.Token, nH));

            Check.Dat(lijst.Contains("www.voorbeeld.be") && lijst.Contains("api.voorbeeld.be"),
                "de brug geeft door welke sites de extensie zal moeten openen");

            // En dat hoort niemand anders te weten: welke sites er op deze pc gezocht worden,
            // is op zichzelf al iets over de gebruiker.
            var vreemd = await StuurAsync("GET", $"/hosts?{BridgeServer.NonceParam}={nH}",
                                          Getekend("verzonnen", nH));

            // Niet enkel "de hosts staan er niet in": dat slaagt ook bij een leeg antwoord, en
            // dan meet je niets. Er hoort te staan waaróm er niets komt.
            Check.Dat(!vreemd.Contains("voorbeeld.be") && vreemd.Contains("verkeerde koppelcode"),
                $"wie de koppelcode niet kent, krijgt die lijst niet maar een weigering ({Eerste(vreemd)})");

            // De lijst komt uit de sites, en die kunnen veranderen terwijl de brug ze opvraagt
            // (het instellingenvenster voegt er een toe). Dan hoort het antwoord leeg te zijn en
            // niet de verbinding stuk.
            brug.BridgeHosts = () => throw new InvalidOperationException("de sites veranderen net");

            var nS = Guid.NewGuid().ToString("N");
            var stuk = await StuurAsync("GET", $"/hosts?{BridgeServer.NonceParam}={nS}",
                                        Getekend(brug.Token, nS));

            Check.Dat(stuk.Contains("\"hosts\":[]"),
                $"een lijst die onderweg omvalt geeft een leeg antwoord ({Eerste(stuk)})");

            brug.BridgeHosts = eerder;
        }


        // ---------------------------------------------------------------------------
        Check.Groep("Brug: de koppelcode gaat niet meer over de lijn");
        {
            var nonce = Guid.NewGuid().ToString("N");

            string Teken(string code, string inhoud) => BridgeServer.Teken(code, nonce + "\n" + inhoud);

            // Zonder handtekening komt er geen opdracht uit, hoe goed het verzoek er ook uitziet.
            var zonder = await StuurAsync("GET", $"/job?{BridgeServer.NonceParam}={nonce}",
                $"{BridgeServer.ExtensionHeader}: 1");

            Check.Dat(zonder.Contains("verkeerde koppelcode"),
                "zonder handtekening: geweigerd");

            // En met een handtekening van een ANDERE code ook niet. Dit is het geval dat ertoe
            // doet: een programma dat poort 8731 eerst bezet kende vroeger de code uit het adres,
            // en kon daarmee de extensie pagina's laten ophalen met jouw cookies. Nu komt die
            // code nergens meer, dus valt er niets af te kijken.
            var verkeerd = await StuurAsync("GET", $"/job?{BridgeServer.NonceParam}={nonce}",
                $"{BridgeServer.ExtensionHeader}: 1",
                $"{BridgeServer.SignatureHeader}: {Teken("een-andere-code", "")}");

            Check.Dat(verkeerd.Contains("verkeerde koppelcode"),
                "met de handtekening van een andere code: geweigerd");

            // Met de juiste wél.
            var goed = await StuurAsync("GET", $"/ping?{BridgeServer.NonceParam}={nonce}",
                $"{BridgeServer.ExtensionHeader}: 1",
                $"{BridgeServer.SignatureHeader}: {Teken(brug.Token, "")}");

            Check.Dat(goed.Contains("\"ok\":true"), "met de juiste handtekening: binnen");

            // En het ANTWOORD is ook getekend. Dat is de helft die de extensie beschermt: zij
            // voert uit wat eruit komt, dus zij moet weten dat ze met de echte app praat.
            var kop = goed.Split("\r\n\r\n")[0];
            var lijf = goed.Split("\r\n\r\n").Length > 1 ? goed.Split("\r\n\r\n")[1] : "";

            var meegestuurd = kop.Split("\r\n")
                .FirstOrDefault(r => r.StartsWith(BridgeServer.SignatureHeader + ":",
                                                  StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1].Trim() ?? "";

            Check.Dat(meegestuurd == Teken(brug.Token, lijf),
                "het antwoord van de app is getekend met de koppelcode");

            Check.Dat(meegestuurd != Teken("een-andere-code", lijf),
                "en met een andere code zou die handtekening niet kloppen");

            // Een extensie van voor deze wijziging stuurt de code nog in het adres. Die werkt
            // niet meer - en de app hoort dat te zeggen, want aan de code zelf is niets mis.
            var oud = await StuurAsync("GET", $"/job?token={brug.Token}",
                $"{BridgeServer.ExtensionHeader}: 1");

            Check.Dat(oud.Contains("verouderde extensie"),
                "een oude extensie krijgt te horen dat ze verouderd is");

            Check.Dat(brug.OudeExtensieRecent, "en de app onthoudt dat");

            // En de melding wijst dan naar chrome://extensions in plaats van naar de koppelcode -
            // aan de code zelf is immers niets mis.
            Check.Dat(ChromeLauncher.Describe(BridgeStatus.OldExtension).Contains("chrome://extensions") &&
                      ChromeLauncher.Describe(BridgeStatus.OldExtension).Contains("Herlaad"),
                "de melding stuurt je naar het juiste scherm");

            // EnsureBridgeAsync zegt hier Ready, en dat is juist: er meldde zich net ook een
            // werkende extensie (de geldige /ping hierboven). Een brug die wérkt weegt zwaarder
            // dan een oude die ook aanklopte; de melding over herladen is voor het geval er
            // niets werkends is.
            var status = await ChromeLauncher.EnsureBridgeAsync(TimeSpan.FromSeconds(2));
            Check.Dat(status == BridgeStatus.Ready,
                $"een werkende extensie weegt zwaarder dan een oude die ook aanklopte ({status})");

            // En een webpagina die het hele verhaal nabootst - code én nonce - komt er nog
            // steeds niet in: zij kan de handtekening niet maken, want ze kent de code niet.
            var webpagina = await StuurAsync("GET", $"/job?{BridgeServer.NonceParam}={nonce}",
                "Origin: https://kwaadaardig.be",
                $"{BridgeServer.SignatureHeader}: {Teken("geraden", "")}");

            Check.Dat(webpagina.Contains("verkeerde koppelcode") && !webpagina.Contains("\"url\""),
                "een webpagina met een verzonnen handtekening krijgt geen opdracht");

            // En een ANDERE extensie in jouw Chrome? Die kan wel tegen de poort praten - dat
            // kan elk programma op deze pc - maar ze krijgt het antwoord niet te LEZEN. De
            // kopregel Access-Control-Allow-Origin komt er enkel bij een verzoek dat de
            // koppelcode kende, en zonder die kopregel houdt de browser het antwoord bij haar
            // weg. Vroeger kreeg elke chrome-extension://-herkomst die kopregels.
            var nV = Guid.NewGuid().ToString("N");

            var andereExtensie = await StuurAsync("GET", $"/ping?{BridgeServer.NonceParam}={nV}",
                "Origin: chrome-extension://eenanderid",
                BridgeServer.ExtensionHeader + ": 1",
                $"{BridgeServer.SignatureHeader}: {BridgeServer.Teken("geraden", nV + "\n")}",
                $"{BridgeServer.PreHeader}: {BridgeServer.Teken("geraden", nV)}");

            Check.Dat(!andereExtensie.Contains("Access-Control-Allow-Origin"),
                "een andere extensie zonder de code mag het antwoord niet lezen");

            var nE = Guid.NewGuid().ToString("N");

            var onze = await StuurAsync("GET", $"/ping?{BridgeServer.NonceParam}={nE}",
                "Origin: chrome-extension://onzeeigenid",
                BridgeServer.ExtensionHeader + ": 1",
                $"{BridgeServer.SignatureHeader}: {BridgeServer.Teken(brug.Token, nE + "\n")}",
                $"{BridgeServer.PreHeader}: {BridgeServer.Teken(brug.Token, nE)}");

            Check.Dat(onze.Contains("Access-Control-Allow-Origin: chrome-extension://onzeeigenid"),
                "met de code wel");
        }

        Check.Groep("Brug: stoppen is geen time-out");
        {
            using var stop = new CancellationTokenSource();
            stop.CancelAfter(TimeSpan.FromMilliseconds(300));

            Exception? gestopt = null;

            try
            {
                // Niemand haalt deze opdracht op (geen nepextensie), dus ze blijft wachten -
                // net als bij een trage pagina. Intussen drukken we op de stopknop.
                await brug.FetchAsync("https://www.voorbeeld.be/q/cd/", stop.Token);
            }
            catch (Exception ex)
            {
                gestopt = ex;
            }

            Check.Dat(gestopt is OperationCanceledException,
                $"stoppen geeft een annulering en geen TimeoutException ({gestopt?.GetType().Name})");

            Check.Dat(gestopt is not TimeoutException,
                "dus de site krijgt geen 'gaf geen antwoord binnen de tijd' aan haar tab");
        }

        Check.Groep("Brug: streamen enkel als iemand meeleest, vervolgpagina's in golven van drie");

        using var site = new Proefsite();
        using var extensie = new NepExtensie(brug.Token);

        var def = site.Site("Brugsite", paginering: true);
        def.UseBridge = true;

        // Planner: geen melder, dus geen stream.
        site.PerPagina.Clear();
        site.PerPagina[1] = 30;
        extensie.Opdrachten.Clear();
        await new GenericSource(def).SearchAsync("cd", 20);
        Check.Dat(extensie.Opdrachten.Count >= 1 && extensie.Opdrachten.All(o => !o.Stream),
            "zonder melder (zoals de planner): stream staat uit");

        // Scherm: met melder, dus pagina 1 streamt, de vervolgpagina's niet.
        site.PerPagina[2] = 30;
        extensie.Opdrachten.Clear();
        await new GenericSource(def).SearchAsync("cd", 40, null, new SynchroneMelder<List<Listing>>(_ => { }));
        Check.Dat(extensie.Opdrachten.FirstOrDefault()?.Stream == true && extensie.Opdrachten.Skip(1).All(o => !o.Stream),
            $"met melder: enkel pagina 1 streamt ({string.Join(",", extensie.Opdrachten.Select(o => o.Stream))})");

        async Task<(List<int> Paginas, List<int> Golven)> Golven(Dictionary<int, int> perPagina)
        {
            site.PerPagina.Clear();
            foreach (var (p, n) in perPagina) site.PerPagina[p] = n;
            lock (site.Gevraagd) site.Gevraagd.Clear();
            extensie.Opdrachten.Clear();
            extensie.Golven.Clear();

            await new GenericSource(def).SearchAsync("cd", 500);

            lock (site.Gevraagd) return (site.Gevraagd.OrderBy(x => x).ToList(), extensie.Golven.ToList());
        }

        var vol = await Golven(Enumerable.Range(1, 12).ToDictionary(p => p, _ => 24));
        Check.Dat(vol.Paginas.Count == 10 && vol.Golven.SequenceEqual(new[] { 1, 3, 3, 3 }),
            $"volle pagina's: 1, dan golven van drie tot de rem ({vol.Paginas.Count} pagina's, golven {string.Join("+", vol.Golven)})");

        var kort = await Golven(new() { [1] = 24, [2] = 24, [3] = 24, [4] = 5 });
        Check.Dat(string.Join(",", kort.Paginas) == "1,2,3,4",
            $"24, 24, 24, 5: na de golf met de korte pagina stopt het ({string.Join(",", kort.Paginas)})");

        var weinig = await Golven(new() { [1] = 2 });
        Check.Dat(string.Join(",", weinig.Paginas) == "1",
            $"2 op pagina 1: geen vervolgpagina's (vroeger 9 samen) ({string.Join(",", weinig.Paginas)})");
    }

    /// <summary>
    /// Wat het manifest van de extensie vraagt, en of de extensie het ook nakijkt.
    ///
    /// Hier wordt het vastgelegd, want <c>"&lt;all_urls&gt;"</c> terugzetten is één woord typen -
    /// en dan heeft de extensie weer toegang tot elke site in je browser zonder dat iemand het
    /// merkt. Het gedrag zelf staat in <c>tools/meet-extensie-toegang.mjs</c>: die draait de
    /// échte handleJob() tegen een nagebootste Chrome.
    /// </summary>
    private static void ManifestControles()
    {
        var wortel = Check.Projectmap();

        if (wortel is null)
        {
            Check.Overgeslagen("de projectmap is niet gevonden: het manifest is niet nagekeken");
            return;
        }

        var manifest = Path.Combine(wortel, "extension", "manifest.json");

        if (!File.Exists(manifest))
        {
            Check.Dat(false, "extension/manifest.json bestaat");
            return;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));

        var vast = Lijst(doc.RootElement, "host_permissions");
        var optioneel = Lijst(doc.RootElement, "optional_host_permissions");

        Check.Dat(!vast.Contains("<all_urls>") && !vast.Any(p => p.StartsWith("https://*")),
            $"host_permissions vraagt niet om alle sites ({string.Join(" ", vast)})");

        Check.Dat(vast.Count == 1 && vast[0] == "http://127.0.0.1/*",
            "enkel de app op deze pc staat er vast in");

        Check.Dat(optioneel.Contains("https://*/*"),
            $"de sites staan bij optional_host_permissions, dus ze worden per stuk gevraagd ({string.Join(" ", optioneel)})");

        // Een manifest houdt op zichzelf niets tegen: de extensie moet het nakijken, en wel
        // VOOR ze een tabblad opent. Een tabblad openen stuurt al een verzoek met jouw cookies;
        // of we de pagina daarna mogen uitlezen, is dan te laat.
        var script = File.ReadAllText(Path.Combine(wortel, "extension", "background.js"));

        Check.Dat(script.Contains("chrome.permissions.contains"),
            "background.js kijkt de toestemming zelf na");

        // Op de ronde haakjes zoeken, want de commentaren hierboven noemen chrome.tabs.create
        // ook - en daar struikelde deze controle de eerste keer over. Dit legt enkel de orde in
        // de brontekst vast; dát er niets opengaat, staat in meet-extensie-toegang.mjs.
        var magPlek = script.IndexOf("await mag(job.url)", StringComparison.Ordinal);
        var tabPlek = script.IndexOf("chrome.tabs.create(", StringComparison.Ordinal);

        Check.Dat(magPlek > 0 && tabPlek > magPlek,
            $"en dat staat vóór de eerste chrome.tabs.create() (teken {magPlek} tegen {tabPlek})");
    }

    /// <summary>Een lijst met tekst uit het manifest, of een lege lijst als de sleutel ontbreekt.</summary>
    private static List<string> Lijst(JsonElement wortel, string naam) =>
        wortel.TryGetProperty(naam, out var waarde) && waarde.ValueKind == JsonValueKind.Array
            ? waarde.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
            : new List<string>();

    private static bool PoortVrij()
    {
        try
        {
            var proef = new TcpListener(IPAddress.Loopback, BridgeServer.Port);
            proef.Start();
            proef.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// Stuurt ruwe tekst naar de brug en leest tot ze de verbinding sluit, of tot de wachttijd om
    /// is. Sluit de brug terwijl er nog iets onderweg was, dan eindigt het met een reset: ook dat
    /// telt als gesloten.
    /// </summary>
    private static async Task<(string Antwoord, bool Gesloten, TimeSpan Duur)> RauwAsync(string verzoek, TimeSpan? wacht = null)
    {
        var klok = System.Diagnostics.Stopwatch.StartNew();
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", BridgeServer.Port);
        var stream = client.GetStream();

        using var tijd = new CancellationTokenSource(wacht ?? TimeSpan.FromSeconds(3));
        using var ontvangen = new MemoryStream();

        try
        {
            if (verzoek.Length > 0) await stream.WriteAsync(Encoding.ASCII.GetBytes(verzoek), tijd.Token);

            var buffer = new byte[8192];
            while (true)
            {
                var n = await stream.ReadAsync(buffer, tijd.Token);
                if (n <= 0) return (Tekst(), true, klok.Elapsed);
                ontvangen.Write(buffer, 0, n);
            }
        }
        catch (OperationCanceledException)
        {
            return (Tekst(), false, klok.Elapsed);
        }
        catch (IOException)
        {
            return (Tekst(), true, klok.Elapsed);
        }

        string Tekst() => Encoding.UTF8.GetString(ontvangen.ToArray());
    }

    /// <summary>
    /// De Host-kopregel die een echte client altijd meestuurt. HTTP/1.1 vereist hem, en de brug
    /// kijkt hem na: een webpagina die een naam naar 127.0.0.1 laat wijzen ("DNS-rebinding")
    /// draagt daar de naam van die pagina in plaats van het adres.
    /// </summary>
    private static string Gastheer => $"Host: 127.0.0.1:{BridgeServer.Port}\r\n";

    /// <summary>
    /// De kopregels die een verzoek sinds 1 oktober 2026 nodig heeft: de kopregel van de
    /// extensie, de handtekening over nonce + body, en de voorcontrole over enkel de nonce.
    /// </summary>
    private static string[] Getekend(string code, string nonce, string body = "") => new[]
    {
        BridgeServer.ExtensionHeader + ": 1",
        $"{BridgeServer.SignatureHeader}: {BridgeServer.Teken(code, nonce + "\n" + body)}",
        $"{BridgeServer.PreHeader}: {BridgeServer.Teken(code, nonce)}"
    };

    /// <summary>De eerste regel van een antwoord, of "geen antwoord".</summary>
    private static string Eerste(string antwoord) =>
        antwoord.Length == 0 ? "geen antwoord" : antwoord.Split("\r\n")[0];

    private static async Task<string> StuurAsync(string methode, string pad, params string[] kopregels)
    {
        using var client = new TcpClient();
        await client.ConnectAsync("127.0.0.1", BridgeServer.Port);
        var stream = client.GetStream();

        var verzoek = new StringBuilder($"{methode} {pad} HTTP/1.1\r\nHost: 127.0.0.1:{BridgeServer.Port}\r\n");
        foreach (var kop in kopregels) verzoek.Append(kop).Append("\r\n");
        verzoek.Append("Content-Length: 0\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(verzoek.ToString()));
        using var lezer = new StreamReader(stream);
        return await lezer.ReadToEndAsync();
    }
}

/// <summary>
/// Doet wat de extensie doet: om werk vragen, de pagina ophalen (hier gewoon met HttpClient)
/// en terugsturen, met eerst een tussentijdse versie als de opdracht dat vraagt. Houdt bij
/// hoeveel opdrachten er telkens samen klaarstonden: dat zijn de golven.
/// </summary>
public sealed class NepExtensie : IDisposable
{
    public sealed record Opdracht(string Url, bool Stream);

    private readonly string _token;
    private readonly CancellationTokenSource _stop = new();
    private static readonly HttpClient Http = new();

    public List<Opdracht> Opdrachten { get; } = new();
    public List<int> Golven { get; } = new();

    public NepExtensie(string token)
    {
        _token = token;
        _ = Task.Run(LusAsync);
    }

    private async Task LusAsync()
    {
        var golf = new List<(string Id, string Url, bool Stream)>();

        while (!_stop.IsCancellationRequested)
        {
            try
            {
                var job = await VraagAsync();

                if (job is not null)
                {
                    golf.Add(job.Value);

                    // Even wachten: de app zet een golf in één keer klaar.
                    await Task.Delay(30);
                    continue;
                }

                if (golf.Count > 0)
                {
                    lock (Golven) Golven.Add(golf.Count);
                    await Task.WhenAll(golf.Select(VoerUitAsync));
                    golf.Clear();
                }

                await Task.Delay(50);
            }
            catch when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                await Task.Delay(100);
            }
        }
    }

    /// <summary>
    /// Een getekend verzoek, zoals de echte extensie het sinds 1 oktober 2026 stuurt: de
    /// koppelcode gaat niet mee, enkel een bewijs dat we hem kennen. En het antwoord van de app
    /// wordt óók nagekeken - dat is de helft die de extensie beschermt.
    /// </summary>
    private async Task<string> VraagAppAsync(string pad, object? inhoud = null)
    {
        var nonce = Guid.NewGuid().ToString("N");
        var body = inhoud is null ? "" : JsonSerializer.Serialize(inhoud);

        using var verzoek = new HttpRequestMessage(
            inhoud is null ? HttpMethod.Get : HttpMethod.Post,
            $"http://127.0.0.1:{BridgeServer.Port}{pad}?{BridgeServer.NonceParam}={nonce}");

        verzoek.Headers.Add(BridgeServer.ExtensionHeader, "1");
        verzoek.Headers.Add(BridgeServer.SignatureHeader, BridgeServer.Teken(_token, nonce + "\n" + body));
        verzoek.Headers.Add(BridgeServer.PreHeader, BridgeServer.Teken(_token, nonce));

        if (inhoud is not null)
            verzoek.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var antwoord = await Http.SendAsync(verzoek, _stop.Token);
        var tekst = await antwoord.Content.ReadAsStringAsync(_stop.Token);

        var getekend = antwoord.Headers.TryGetValues(BridgeServer.SignatureHeader, out var waarden)
                       ? waarden.FirstOrDefault()
                       : null;

        if (getekend != BridgeServer.Teken(_token, nonce + "\n" + tekst))
            throw new InvalidOperationException("het antwoord is niet van de app getekend");

        return tekst;
    }

    private async Task<(string Id, string Url, bool Stream)?> VraagAsync()
    {
        using var doc = JsonDocument.Parse(await VraagAppAsync("/job"));

        if (!doc.RootElement.TryGetProperty("url", out var url)) return null;

        var opdracht = (doc.RootElement.GetProperty("id").GetString()!, url.GetString()!,
                        doc.RootElement.TryGetProperty("stream", out var s) && s.GetBoolean());

        lock (Opdrachten) Opdrachten.Add(new Opdracht(opdracht.Item2, opdracht.Item3));
        return opdracht;
    }

    private async Task VoerUitAsync((string Id, string Url, bool Stream) opdracht)
    {
        var html = await Http.GetStringAsync(opdracht.Url, _stop.Token);

        if (opdracht.Stream)
            await StuurAsync(new { id = opdracht.Id, html, partial = true });

        await StuurAsync(new { id = opdracht.Id, html });
    }

    private async Task StuurAsync(object inhoud) => await VraagAppAsync("/result", inhoud);

    public void Dispose() => _stop.Cancel();
}
