namespace Zentrix.Models;

/// <summary>
/// Een zoekterm die eerder gebruikt is. Anders dan een <see cref="SavedSearch"/>
/// wordt hier niets vastgezet: het is enkel een spoor van wat je onlangs zocht,
/// zodat je het met één klik kan herhalen.
/// </summary>
public class RecentSearch
{
    public string Query { get; set; } = "";

    public DateTime RanAt { get; set; }

    /// <summary>Leesbare tijd voor in de lijst, bv. "vandaag 14:32".</summary>
    public string WhenLabel
    {
        get
        {
            var days = (DateTime.Now.Date - RanAt.Date).Days;

            return days switch
            {
                0 => "vandaag " + RanAt.ToString("HH:mm"),
                1 => "gisteren " + RanAt.ToString("HH:mm"),
                < 7 => RanAt.ToString("dddd HH:mm"),
                _ => RanAt.ToString("d MMMM HH:mm")
            };
        }
    }
}
