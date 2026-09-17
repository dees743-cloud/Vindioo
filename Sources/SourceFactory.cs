using Zentrix.Models;

namespace Zentrix.Sources;

/// <summary>
/// Maakt van een sitebeschrijving de juiste bron. Elke site is dus data; enkel
/// welke motor die data uitleest, verschilt.
/// </summary>
public static class SourceFactory
{
    public static ISearchSource Create(SiteDefinition site) => site.Engine switch
    {
        SiteEngine.LinkText => new LinkTextSource(site),
        _ => new GenericSource(site)
    };
}
