using System.Windows;
using System.Windows.Controls;

namespace Zentrix.Controls;

/// <summary>
/// De miniatuur van een zoekertje. Staat apart omdat zowel de lijst- als de rasterweergave
/// hem gebruikt: de afronding en het bijsnijden wil je maar op één plaats onderhouden.
///
/// Tot 27 september 2026 hing hier een vergroting aan: een vergrootglas op de kaart, en
/// zweven toonde de grote foto ernaast in een popup. Sinds dubbelklikken het detailvenster
/// opent - met álle foto's van de advertentie, en een klik erop schermvullend - was dat een
/// tweede weg naar hetzelfde, en een knopje dat plaats innam op elke kaart.
/// </summary>
public partial class PhotoThumbnail : UserControl
{
    public PhotoThumbnail() => InitializeComponent();

    /// <summary>Breedte van het vaste vak waarin de foto bijgesneden wordt.</summary>
    public static readonly DependencyProperty PhotoWidthProperty =
        DependencyProperty.Register(nameof(PhotoWidth), typeof(double),
            typeof(PhotoThumbnail), new PropertyMetadata(260.0));

    public double PhotoWidth
    {
        get => (double)GetValue(PhotoWidthProperty);
        set => SetValue(PhotoWidthProperty, value);
    }

    /// <summary>Hoogte van datzelfde vak.</summary>
    public static readonly DependencyProperty PhotoHeightProperty =
        DependencyProperty.Register(nameof(PhotoHeight), typeof(double),
            typeof(PhotoThumbnail), new PropertyMetadata(220.0));

    public double PhotoHeight
    {
        get => (double)GetValue(PhotoHeightProperty);
        set => SetValue(PhotoHeightProperty, value);
    }
}
