using CommunityToolkit.Mvvm.ComponentModel;

namespace Vindioo.Models;

/// <summary>
/// Het chipje achteraan de tabstrip waarmee je kiest welke sites meezoeken.
///
/// Waarom dit een eigen ding is en geen <see cref="SiteTab"/>: de tabstrip toont
/// sinds kort enkel de sites die meedoen, dus het aan- en uitvinken moest ergens
/// anders heen. Het staat in dezelfde rij als de tabs omdat het daar hoort — je
/// kijkt naar de tabs en denkt "waar is de rest" — maar het is geen tabblad: het
/// opent een lijstje in plaats van resultaten. Het als een tab vermommen zou
/// betekenen dat elke plek die over "alle tabs behalve Alles" gaat er ineens een
/// uitzondering bij krijgt, en dat zijn er een stuk of tien.
/// </summary>
public class SiteChooser : ObservableObject
{
    /// <summary>Wat er op het chipje staat, bv. "3 van 8 sites".</summary>
    private string _label = "Sites kiezen";
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    /// <summary>
    /// Zoekt er geen enkele site mee? Dan licht het chipje op. Zonder die nadruk
    /// staat er bij het opstarten alleen een tab "Alles" en is er niets dat zegt
    /// waar de sites gebleven zijn.
    /// </summary>
    private bool _isEmpty = true;
    public bool IsEmpty
    {
        get => _isEmpty;
        set => SetProperty(ref _isEmpty, value);
    }
}
