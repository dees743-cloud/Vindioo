using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Zentrix.Models;

namespace Zentrix.Controls;

/// <summary>
/// De miniatuur van een zoekertje, met de vergroting ernaast. Staat apart omdat zowel
/// de lijst- als de rasterweergave hem gebruikt: de popup, de afronding en het
/// centreren zijn subtiel genoeg om ze maar op een plaats te willen onderhouden.
/// </summary>
public partial class PhotoThumbnail : UserControl
{
    public PhotoThumbnail() => InitializeComponent();

    /// <summary>
    /// Of de grote foto open staat. De kaart koppelt dit aan de muis op het vergrootglas
    /// (<c>ShowPreview="{Binding IsMouseOver, ElementName=ListZoom}"</c>), zodat de
    /// miniatuur zelf niet hoeft te weten waar dat vergrootglas staat.
    /// </summary>
    public static readonly DependencyProperty ShowPreviewProperty =
        DependencyProperty.Register(nameof(ShowPreview), typeof(bool),
            typeof(PhotoThumbnail), new PropertyMetadata(false));

    public bool ShowPreview
    {
        get => (bool)GetValue(ShowPreviewProperty);
        set => SetValue(ShowPreviewProperty, value);
    }

    /// <summary>
    /// De grote foto pas koppelen wanneer de vergroting echt opengaat.
    ///
    /// Stond de Source gewoon in de XAML, dan werd die binding al uitgevoerd zodra de
    /// kaart ontstond: een popup die dicht is, hoort toch bij de boom en erft de
    /// gegevens. En een afbeelding van een webadres begint meteen te downloaden. Bij
    /// Catawiki is de grote foto 365 kB tegenover 66 kB voor de miniatuur, dus een
    /// pagina van vijftig kaarten haalde zo'n 18 MB aan foto's op waar niemand over
    /// zweefde - en die downloads stonden in de weg van de miniaturen.
    /// </summary>
    private void Preview_Opened(object? sender, EventArgs e) =>
        LargePhoto.SetBinding(Image.SourceProperty, new Binding(nameof(Listing.LargeImage)));

    /// <summary>Weer loskoppelen, zodat een hergebruikte kaart geen oude foto toont.</summary>
    private void Preview_Closed(object? sender, EventArgs e) =>
        BindingOperations.ClearBinding(LargePhoto, Image.SourceProperty);

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
