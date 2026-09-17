using System.Text.Json;
using Zentrix.Models;
using Zentrix.Services;

namespace Zentrix.Checks;

/// <summary>
/// De prijsindicatie (rechtsklik op een foto). De titels en prijzen hieronder zijn wat 2dehands en
/// Marktplaats op 17 september 2026 gaven voor "denon dcd 520": zo meten de controles de regels
/// aan de rommel die echt voorkomt, zonder dat ze het netwerk op gaan.
/// </summary>
public static class PrijsChecks
{
    private static readonly SiteDefinition Tweedehands = new()
    {
        Name = "2dehands", PriceReference = true, SellerSelector = "seller", AuctionSellers = { "Catawiki" }
    };

    private static readonly SiteDefinition Marktplaats = new()
    {
        Name = "Marktplaats", PriceReference = true, SellerSelector = "seller", AuctionSellers = { "Catawiki" }
    };

    private static (Listing, SiteDefinition) Z(SiteDefinition site, string id, string titel, decimal prijs, string verkoper = "Jan") =>
        (new Listing { Source = site.Name, ExternalId = id, Url = "https://x/" + id, Title = titel, Price = prijs, Seller = verkoper }, site);

    /// <summary>De eerste zoektocht, "denon dcd 520", op beide sites.</summary>
    private static List<(Listing Listing, SiteDefinition Def)> Gevonden() => new()
    {
        Z(Tweedehands, "m2436946597", "DENON stereo set + Sony", 250),
        Z(Tweedehands, "a166353354", "Denon - DCD-520 - Cd-speler", 24, "Catawiki"),
        Z(Tweedehands, "m2433048245", "NAD-DENON-ONKYO", 8888888),
        Z(Tweedehands, "a166350982", "Denon - DCD-520 - Lecteur de CD", 24, "Catawiki"),
        Z(Tweedehands, "m2442375971", "Chaibe Hi-Fi Denon (PMA 525R / DCD 520 AE / TU 1500 AE)  🔆", 180),
        Z(Tweedehands, "a166487621", "Denon - DCD-520AE Cd-speler", 39, "Catawiki"),
        Z(Marktplaats, "m2443169282", "Complete hifi-set Onkyo/Denon met KEF Q100", 495),
        Z(Marktplaats, "m2443158474", "Muzikale DENON DCD-520AE met top DAC", 125),
        Z(Marktplaats, "m2442793803", "Denon Audio HiFi Set: Receiver, CD-Speler & Cassettedeck", 349.95m),
        Z(Marktplaats, "a1530962307", "Denon - DCD-520AE Cd-speler", 39, "Catawiki"),
        Z(Marktplaats, "m2437207196", "Denon DCD 520 AE", 110),
        Z(Marktplaats, "m2441465449", "Denon DCD-520AE Hoogwaardige cd-speler", 250),
        Z(Marktplaats, "m2439715135", "Te koop Denon dcd-520ae 32-bit cd speler  ✅️", 80),
        Z(Marktplaats, "a1530828967", "Denon - DCD-520 - Cd-speler", 24, "Catawiki"),
        Z(Marktplaats, "m2441312265", "DENON DCD-520 CD Speler Met Garantie", 80),
        Z(Marktplaats, "m2427986918", "Denon DCD520AE cd-speler (luie laser)", 0),
        Z(Marktplaats, "m2422187126", "Denon DCD-520AE", 0),
        Z(Marktplaats, "m2441165617", "Denon Compact Disc Player DCD-520 - Met Remote", 89.99m),
        Z(Marktplaats, "m2425602245", "Denon Hifi Set: Tuner, CD-speler, Cassettedeck, Versterker", 225),
        Z(Marktplaats, "m2437003538", "DENON stereo set 2 x 120 Watt", 195),
        Z(Marktplaats, "m2408889934", "Denon AVR-1912 AV Receiver en DCD-520AE CD-speler", 170),
        Z(Marktplaats, "m2404642707", "Originele Denon RC-207 Afstandsbediening voor CD-spelers", 20),
        Z(Marktplaats, "m2411583115", "Denon DCD 520AE CD-speler en PMA 520AE versterker set", 0),
        Z(Marktplaats, "m2429613046", "DENON DCD 895 CD SPELER", 0),
        Z(Marktplaats, "m2435436605", "Denon DCD-800NE CD-speler - Zwart", 350),
        Z(Marktplaats, "m2421935041", "Denon cd afspeler  DCD 860", 85),
        Z(Marktplaats, "m2409684455", "Denon DCD-1450AR CD-speler High-end", 150),
        Z(Marktplaats, "m2431889212", "Denon DCD 825", 150),
        Z(Marktplaats, "m2415749590", "Aangeboden : zeldzame DENON DCD - 1800 uit 1983", 250),
        Z(Marktplaats, "m2442166784", "DENON DCD 755AR", 60),
        Z(Marktplaats, "m2441271929", "Denon DCD-1450AR CD-speler", 90),
        Z(Marktplaats, "m2441579678", "Denon DCD-725 CD-Speler | Incl. Afstandsbediening + GARANTIE", 175),
        Z(Marktplaats, "m2410252661", "Denon DCD-1450AR - Cd-player (Laser NEW) Brushed Black.", 250),
        Z(Marktplaats, "m2442272135", "Denon DCD-720AE CD-speler met afstandsbediening", 125),
        Z(Marktplaats, "m2419156662", "Denon cd speler", 80),
        Z(Marktplaats, "m2430277575", "Denon DCD 755 AR met boekje + A.B.", 60),
        Z(Marktplaats, "m2420046493", "Denon DCD 1450 AR CD speler", 225),
        Z(Marktplaats, "m2410905766", "Denon DCD-1460", 229),
        // Uit de live proef met de nieuwe code, dezelfde dag: een toebehoren, en één speler met
        // de code van zijn nieuwe laser in de titel.
        Z(Marktplaats, "m9000000001", "Afstandsbediening Denon RC-253 DCD-520", 20),
        Z(Marktplaats, "m9000000002", "Denon DCD-625 | KSS-240A loopwerk | GARANTIE", 150),
    };

    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Prijsindicatie: een zoekterm uit de titel (SuggestTerm)");
        {
            Check.Dat(PriceIndicator.SuggestTerm("Denon - DCD-520 - Lecteur de CD") == "Denon DCD-520",
                $"merk en model uit een Catawiki-titel ('{PriceIndicator.SuggestTerm("Denon - DCD-520 - Lecteur de CD")}')");
            Check.Dat(PriceIndicator.SuggestTerm("Te koop Denon dcd-520ae 32-bit cd speler") == "Denon dcd-520ae",
                "kleine letters en een achtervoegsel blijven staan, 32-bit is geen model");
            Check.Dat(PriceIndicator.SuggestTerm("Lecteur CD Denon DCD 520") == "Denon DCD 520",
                "een soort toestel vooraan is geen merk");
            var lot = PriceIndicator.SuggestTerm("Lot 63 - muziek cd's en dvd's");
            Check.Dat(!lot.Contains("63") && lot.Length > 0, $"'Lot 63' is geen modelnummer ('{lot}')");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijsindicatie: de zoekterm ontleden (ParseTerm)");
        {
            var t = PriceIndicator.ParseTerm("denon dcd-520");
            Check.Dat(t is { Letters: "dcd", Digits: "520", Suffix: "" } && t.Words.SequenceEqual(new[] { "denon" }),
                "denon dcd-520: reeks dcd, nummer 520, merk als woord");
            Check.Dat(PriceIndicator.ParseTerm("Denon DCD 520 AE").Suffix == "ae", "een los achtervoegsel: DCD 520 AE");
            Check.Dat(PriceIndicator.ParseTerm("Denon DCD 520 cd").Suffix == "", "'cd' na het nummer is geen achtervoegsel");
            var zonder = PriceIndicator.ParseTerm("marantz cd speler");
            Check.Dat(!zonder.HasModel && zonder.Words.Count == 3, "zonder modelnummer: enkel woorden");

            var technics = PriceIndicator.ParseTerm("Technics SL-PJ22");
            Check.Dat(technics is { Letters: "slpj", Digits: "22", Display: "SL-PJ22" } && technics.Words.SequenceEqual(new[] { "technics" }),
                $"twee groepjes letters: SL-PJ22, met Technics als merk ({technics.Display})");
            Check.Dat(PriceIndicator.ParseTerm("Denon DCD-520").Display == "DCD-520", "de weergave volgt de zoekterm: DCD-520");

            var vormen = PriceIndicator.ZoekVormen(technics, "Technics  SL-PJ22");
            Check.Dat(vormen.SequenceEqual(new[] { "Technics SL-PJ22", "Technics sl pj22" }),
                $"naar de sites: zoals getypt en met losse letters, cijfers vast ({string.Join(" | ", vormen)})");
            Check.Dat(PriceIndicator.ZoekVormen(PriceIndicator.ParseTerm("Denon DCD-520"), "Denon DCD-520")
                          .SequenceEqual(new[] { "Denon DCD-520", "Denon dcd 520" }),
                "bij DCD-520 ook 'dcd 520' los");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijsindicatie: een model met twee groepjes letters (Technics SL-PJ, 17 september 2026)");
        {
            Check.Dat(PriceIndicator.SuggestTerm("Technics - SL-PJ22 Cd-speler") == "Technics SL-PJ22",
                $"het voorstel houdt het merk ('{PriceIndicator.SuggestTerm("Technics - SL-PJ22 Cd-speler")}')");

            var gevonden = new List<(Listing Listing, SiteDefinition Def)>
            {
                Z(Tweedehands, "a1", "Technics - SL-PJ22 Cd-speler", 1, "Catawiki"),
                Z(Tweedehands, "a2", "Technics - SL-PJ22 Lecteur de CD", 1, "Catawiki"),
                Z(Marktplaats, "a3", "Technics - SL-PJ22 Cd-speler", 8, "Catawiki"),
                // Zelfgeschreven: een andere schrijfwijze met een komma, die geen set is.
                Z(Marktplaats, "m0", "Technics SL PJ22, met afstandsbediening", 45),
                Z(Marktplaats, "m1", "Technics SL-PJ25 vintage midi Technics CD speler", 25),
                Z(Marktplaats, "m2", "1990 Technics SL-PJ27A cd-speler", 27),
                Z(Marktplaats, "m3", "Technics SL-PJ27A Compact Disc Speler - Vintage CD-speler", 35),
                Z(Marktplaats, "m4", "Technics SL-PJ27A CD Speler - Compact Disc Player", 40),
                Z(Marktplaats, "m5", "vintage cd speler technics type sl-pj25", 110),
                Z(Marktplaats, "m6", "Technics SL-PJ27A Compact Disc Speler", 0),
            };

            var origin = gevonden[1].Listing;
            var model = PriceIndicator.ParseTerm("Technics SL-PJ22");
            var ind = new PriceIndication { Term = "Technics SL-PJ22", Model = model.Display };
            var anders = PriceIndicator.Analyse(ind, model, gevonden, origin);

            Check.Dat(ind.Items.Count(i => i.Kind == ComparableKind.Auction) == 2,
                "de Catawiki-kavel: veilingen, NL op 2dehands en Marktplaats telt apart (ander bod)");
            Check.Dat(ind.Items.SingleOrDefault(i => i.Listing.Title.StartsWith("Technics SL PJ22"))?.Kind == ComparableKind.Counted,
                "'SL PJ22' met een spatie is hetzelfde model, en de komma maakt er geen set van");

            PriceIndicator.AnalyseBroad(ind, model, anders, origin);
            Check.Dat(ind.Broad is { Count: 4, Median: 31, Low: 26.5m, High: 36.25m } &&
                      ind.BroadItems.Any(i => i.Model == "SL-PJ27A") &&
                      ind.BroadItems.Single(i => i.Listing.Price == 110).Kind == ComparableKind.Outlier,
                $"de reeks SL-PJ: 4 prijzen rond € 31, € 110 is een uitschieter ({ind.Broad?.Count}, {ind.Broad?.Median})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijsindicatie: opschonen met de echte titels (Analyse)");
        {
            var gevonden = Gevonden();
            var origin = gevonden.Single(g => g.Listing.ExternalId == "a166350982").Listing;
            var ind = new PriceIndication { Term = "Denon DCD-520" };
            var model = PriceIndicator.ParseTerm("Denon DCD-520");

            var anders = PriceIndicator.Analyse(ind, model, gevonden, origin);

            string[] Titels(ComparableKind soort) =>
                ind.Items.Where(i => i.Kind == soort).Select(i => i.Listing.Title).OrderBy(x => x).ToArray();

            Check.Dat(ind.Items.All(i => i.Listing.ExternalId != "a166350982"), "het zoekertje zelf telt niet mee");
            Check.Dat(Titels(ComparableKind.Counted).SequenceEqual(new[]
                { "Denon Compact Disc Player DCD-520 - Met Remote", "DENON DCD-520 CD Speler Met Garantie" }.OrderBy(x => x)),
                $"meegeteld: enkel de twee DCD-520's met een vraagprijs ({string.Join(" | ", Titels(ComparableKind.Counted))})");
            Check.Dat(ind.Market is { Count: 2, Low: 80, High: 89.99m }, $"marktwaarde uit 2 prijzen, € 80 tot € 89,99");
            Check.Dat(ind.Items.Count(i => i.Kind == ComparableKind.Auction && i.Listing.Title == "Denon - DCD-520 - Cd-speler") == 1,
                "Catawiki op 2dehands en Marktplaats: één veiling, niet twee");
            Check.Dat(ind.Items.Any(i => i.Kind == ComparableKind.Auction && i.Model == "DCD-520AE"),
                "een Catawiki-kavel van de variant is ook een veiling");

            var variant = ind.Variants.GetValueOrDefault("DCD-520AE");
            Check.Dat(variant is { Count: 3, Median: 110, Low: 80, High: 125 },
                $"DCD-520AE apart: 80, 110 en 125 ({variant?.Count} prijzen, mediaan {variant?.Median})");
            Check.Dat(ind.Items.Single(i => i.Listing.Title == "Denon DCD-520AE Hoogwaardige cd-speler").Kind == ComparableKind.Outlier,
                "€ 250 is bij de DCD-520AE een uitschieter");
            Check.Dat(Titels(ComparableKind.Set).Length == 3, $"drie sets ({string.Join(" | ", Titels(ComparableKind.Set))})");
            Check.Dat(Titels(ComparableKind.Defect).SequenceEqual(new[] { "Denon DCD520AE cd-speler (luie laser)" }), "luie laser is defect");
            Check.Dat(Titels(ComparableKind.NoPrice).SequenceEqual(new[] { "Denon DCD-520AE" }), "bieden zonder bedrag: geen prijs");
            Check.Dat(Titels(ComparableKind.Accessory).SequenceEqual(new[] { "Afstandsbediening Denon RC-253 DCD-520" }),
                "een afstandsbediening vóór het model is toebehoren, geen set");
            Check.Dat(anders.Any(a => a.Listing.Title == "NAD-DENON-ONKYO") && anders.Any(a => a.Listing.Title == "Denon DCD-1460") &&
                      ind.Items.All(i => i.Listing.Title != "Originele Denon RC-207 Afstandsbediening voor CD-spelers"),
                "andere toestellen en een afstandsbediening gaan over iets anders");

            // ---------------------------------------------------------------------------
            Check.Groep("Prijsindicatie: verbreden naar dezelfde reeks (AnalyseBroad)");

            PriceIndicator.AnalyseBroad(ind, model, anders, origin);

            Check.Dat(ind.BroadItems.All(i => i.Model.StartsWith("DCD-") && !i.Model.StartsWith("DCD-520")),
                "enkel andere DCD's, niet de 520 of zijn variant");
            Check.Dat(ind.BroadItems.All(i => i.Listing.Title != "Denon cd speler"), "zonder reeksnummer telt een cd-speler niet");
            Check.Dat(ind.BroadItems.Single(i => i.Listing.Title.StartsWith("Denon DCD-625")).Kind == ComparableKind.Related,
                "een onderdeelcode zonder '+' of 'en' maakt er geen set van, en 'loopwerk' na het model geen toebehoren");
            Check.Dat(ind.Broad is { Count: 14, Median: 150, Low: 98.75m, High: 228 },
                $"14 prijzen, mediaan € 150, meestal € 98,75 tot € 228 ({ind.Broad?.Count}, {ind.Broad?.Median}, {ind.Broad?.Low}-{ind.Broad?.High})");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijsindicatie: de prijsvork (Reeks)");
        {
            List<PriceComparable> Groep(params decimal[] prijzen) =>
                prijzen.Select(p => new PriceComparable { Listing = new Listing { Title = "x", Price = p }, Kind = ComparableKind.Counted }).ToList();

            var met = Groep(10, 12, 14, 15, 1000);
            var reeks = PriceIndicator.Reeks(met);
            Check.Dat(reeks is { Count: 4, Median: 13, Low: 11.5m, High: 14.25m } && met[4].Kind == ComparableKind.Outlier,
                $"een onzinprijs valt eruit, de vork is Q1 tot Q3 ({reeks?.Low}-{reeks?.High})");
            Check.Dat(PriceIndicator.Reeks(Groep(80, 89.99m)) is { Count: 2, Low: 80, High: 89.99m },
                "onder de vier prijzen: laagste tot hoogste, zonder uitschieters te zoeken");
            Check.Dat(PriceIndicator.Reeks(Groep(0, 0)) is null, "zonder prijzen geen vork");
            Check.Dat(PriceRange.Euro(84.995m) == "€ 85" && PriceRange.Euro(7.5m) == "€ 7,50" && PriceRange.Euro(1234) == "€ 1.234",
                "bedragen zoals een mens ze leest");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijsindicatie: van begin tot einde, met een lokale site (DetermineAsync)");
        {
            using var site = new Proefsite();

            static string Json(params (string Titel, decimal Prijs, string Verkoper)[] items) => JsonSerializer.Serialize(new
            {
                listings = items.Select((it, i) => new { title = it.Titel, priceCents = (long)(it.Prijs * 100), url = $"/v/{it.Titel.GetHashCode():x}-{i}", seller = it.Verkoper })
            });

            var gevraagd = new List<string>();
            site.Antwoord = adres =>
            {
                adres = adres.Replace('+', ' ');
                lock (gevraagd) gevraagd.Add(adres);
                return adres.Contains("520")
                    ? Json(("Denon DCD-520 cd-speler", 80, "Jan"), ("Denon DCD-520 met afstandsbediening", 90, "Piet"),
                           ("Denon - DCD-520 - Cd-speler", 24, "Veilinghuis"))
                    : Json(("Denon DCD-1450AR", 150, "An"), ("Denon DCD 825", 140, "Bo"), ("Denon DCD-725", 160, "Cas"),
                           ("Denon DCD 755AR", 120, "Dirk"), ("Denon DCD-520 cd-speler", 80, "Jan"));
            };

            SiteDefinition Proef(string naam, bool telt) => new()
            {
                Name = naam, Kind = SiteKind.Json, PriceReference = telt,
                BaseUrl = $"http://127.0.0.1:{site.Poort}",
                SearchUrlTemplate = $"http://127.0.0.1:{site.Poort}/api?q={{query}}",
                ItemSelector = "listings", TitleSelector = "title", PriceSelector = "priceCents", PriceInCents = true,
                UrlSelector = "url", SellerSelector = "seller", AuctionSellers = { "veilinghuis" }
            };

            var proef = Proef("Proef", true);
            var kapot = new SiteDefinition
            {
                Name = "Kapot", Kind = SiteKind.Json, PriceReference = true, ItemSelector = "listings",
                SearchUrlTemplate = "http://127.0.0.1:9/api?q={query}"
            };
            var sites = new List<SiteDefinition> { proef, Proef("Telt niet", false), kapot };

            var origin = new Listing { Source = "Proef", ExternalId = "elders", Title = "Denon - DCD-520 - Lecteur de CD", Price = 24, Seller = "Veilinghuis" };
            var ind = await PriceIndicator.DetermineAsync("Denon DCD-520", origin, sites);

            Check.Dat(ind.SitesSearched.SequenceEqual(new[] { "Proef", "Kapot" }), "enkel de sites met het vinkje");
            Check.Dat(gevraagd.Any(g => g.Contains("q=Denon dcd 520")),
                $"naar de site gaat het model zonder streepje ({string.Join(" | ", gevraagd)})");
            Check.Dat(ind.SiteErrors.ContainsKey("Kapot") && ind.Market is { Count: 2 },
                $"een site die faalt, houdt de rest niet tegen ({ind.SiteErrors.GetValueOrDefault("Kapot")})");
            Check.Dat(ind.OriginIsAuction && ind.Items.All(i => i.Kind != ComparableKind.Auction) &&
                      ind.Items.All(i => !i.Listing.Title.StartsWith("Denon - DCD-520 - Cd-speler")),
                "de advertentie van het veilinghuis is bij het uitlezen al overgeslagen (AuctionSellers)");
            Check.Dat(ind.BroadTerm == "Denon DCD" && ind.Broad is { Count: 4, Median: 145 } &&
                      gevraagd.Count(g => g.Contains("Denon DCD") && !g.Contains("520")) == 1,
                $"te weinig prijzen: één keer verbreed naar 'Denon DCD', 4 andere toestellen ({ind.Broad?.Count})");

            var geen = await PriceIndicator.DetermineAsync("Denon DCD-520", null, new[] { Proef("Uit", false) });
            Check.Dat(geen.NoSites && geen.SitesSearched.Count == 0, "zonder sites met het vinkje: een melding, geen verzoeken");
        }
    }
}
