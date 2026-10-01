using Zentrix.Models;
using Zentrix.Sources;

namespace Zentrix.Checks;

/// <summary>
/// De prijs uit de tekst van een site lezen (<see cref="PriceParser"/>).
///
/// Dit stond tot 1 oktober 2026 twee keer in de code, één keer per motor, en die twee waren uit
/// elkaar gegroeid. Een codeanalyse van 30 september wees twee echte fouten aan, en die staan
/// hieronder als controle - met een <b>tegenproef</b>: de regel die ernaast zit, moet nog altijd
/// kloppen, anders los je het ene op door het andere stuk te maken.
///
/// Waarom het ernstig is: een prijs die null wordt, verdwijnt niet uit de lijst. Het zoekertje
/// <b>glipt door de prijsgrens heen</b> (die filtert op een prijs die er niet is) en valt uit de
/// prijsindicatie. Er komt geen enkele fout van, dus zonder controle merk je het nooit.
/// </summary>
public static class PrijslezerChecks
{
    /// <summary>De gewone motor leest een kaart; zo meten we hem met echte HTML.</summary>
    private static async Task<decimal?> ViaDeMotor(string prijstekst)
    {
        var def = new SiteDefinition
        {
            Name = "Proef",
            BaseUrl = "https://voorbeeld.be",
            ItemSelector = "div.item",
            TitleSelector = "a.t",
            UrlSelector = "a.t@href",
            PriceSelector = "span.p"
        };

        var html = "<html><body><div class='item'><a class='t' href='/1'>Zoekertje</a>" +
                   $"<span class='p'>{prijstekst}</span></div></body></html>";

        var gelezen = await new GenericSource(def).ReadPageAsync(html);
        return gelezen.Count == 1 ? gelezen[0].Price : null;
    }

    public static async Task RunAsync()
    {
        // ---------------------------------------------------------------------------
        Check.Groep("Prijs lezen: de harde spatie (\"1 499 €\" van een Franse of Spaanse site)");
        {
            // U+00A0 en U+202F zien er op het scherm uit als een gewone spatie. De regex pikte ze
            // op (\s van .NET dekt heel \p{Z}), maar het opschonen deed Replace(" ", "") en dat
            // raakt enkel U+0020. Wat overbleef kon TryParse niet lezen: de prijs werd null.
            Check.Dat(PriceParser.Parse("1 499 €") == 1499m,
                $"een harde spatie als duizendtal: 1 499 euro ({PriceParser.Parse("1 499 €")})");

            Check.Dat(PriceParser.Parse("12 345,67 €") == 12345.67m,
                $"een smalle harde spatie, met centen ({PriceParser.Parse("12 345,67 €")})");

            Check.Dat(PriceParser.Parse("1 499 €") == 1499m, "en een gewone spatie, zoals vroeger");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijs lezen: wanneer een punt een duizendtal is en wanneer een komma");
        {
            // De linkmotor haalde de punt ALTIJD weg, en maakte van "12.50" dus 1250 - honderd
            // keer te veel. De regel: drie cijfers achter de punt is een duizendtal, anders is
            // het een decimaalteken.
            Check.Dat(PriceParser.Parse("12.50") == 12.50m, $"\"12.50\" is twaalf euro vijftig ({PriceParser.Parse("12.50")})");
            Check.Dat(PriceParser.Parse("€ 1.499") == 1499m, $"\"1.499\" is duizend vierhonderd negenennegentig ({PriceParser.Parse("€ 1.499")})");
            Check.Dat(PriceParser.Parse("€ 1.499,95") == 1499.95m, "punt en komma samen, Nederlandse notatie");
            Check.Dat(PriceParser.Parse("1.234.567") == 1234567m, $"meerdere punten zijn allemaal duizendtallen ({PriceParser.Parse("1.234.567")})");
            Check.Dat(PriceParser.Parse("€ 0,76") == 0.76m, "een plaat van 76 cent (Discogs)");
            Check.Dat(PriceParser.Parse("€ 175") == 175m, "een kaal bedrag");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijs lezen: wat géén prijs is");
        {
            Check.Dat(PriceParser.Parse("Gratis") is null, "\"Gratis\" geeft geen bedrag");
            Check.Dat(PriceParser.Parse("Bieden") is null, "\"Bieden\" ook niet");
            Check.Dat(PriceParser.Parse("") is null, "lege tekst");
            Check.Dat(PriceParser.Parse(null) is null, "null");
            Check.Dat(PriceParser.Parse("   ") is null, "enkel witruimte");
        }

        // ---------------------------------------------------------------------------
        Check.Groep("Prijs lezen: allebei de motoren lezen hetzelfde");
        {
            // De gewone motor met echte HTML erdoor, zodat het niet enkel de losse lezer is die
            // nagemeten wordt maar ook de weg ernaartoe.
            var hard = await ViaDeMotor("1 499 €");
            Check.Dat(hard == 1499m, $"de gewone motor leest een harde spatie ({hard})");

            var centen = await ViaDeMotor("12.50");
            Check.Dat(centen == 12.50m, $"en \"12.50\" blijft twaalf vijftig ({centen})");

            // De linkmotor leest de tekstregels van een kaart. Dezelfde kaart, dezelfde prijs.
            var kaart = "<html><body><a href='/marketplace/item/7/'>" +
                        "<span>1 499 €</span><span>Commodore 64</span><span>Aalter</span>" +
                        "</a></body></html>";

            var def = new SiteDefinition
            {
                Name = "Proef",
                BaseUrl = "https://voorbeeld.be",
                Engine = SiteEngine.LinkText,
                ItemSelector = "a[href*='/marketplace/item/']",
                LinkText = new LinkTextOptions { PriceMarkers = { "€" } }
            };

            var zoekertjes = new LinkTextSource(def).ReadPage(kaart);
            Check.Dat(zoekertjes.Count == 1 && zoekertjes[0].Price == 1499m,
                $"de linkmotor leest dezelfde prijs ({(zoekertjes.Count == 1 ? zoekertjes[0].Price : null)})");
        }
    }
}
