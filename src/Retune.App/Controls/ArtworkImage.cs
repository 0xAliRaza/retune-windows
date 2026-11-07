using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Retune.App.Composition;

namespace Retune.App.Controls;

/// <summary>
/// An image that loads provider artwork asynchronously at the size it's displayed, shows a stable gradient
/// placeholder with initials until (or unless) it arrives, and fades in. Recycled containers cancel stale loads.
/// </summary>
public sealed class ArtworkImage : Control
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Uri), typeof(ArtworkImage), new PropertyMetadata(null, (d, _) => ((ArtworkImage)d).Reload()));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ArtworkImage), new PropertyMetadata(string.Empty, (d, _) => ((ArtworkImage)d).UpdatePlaceholder()));

    public static readonly DependencyProperty InitialsProperty = DependencyProperty.Register(
        nameof(Initials), typeof(string), typeof(ArtworkImage), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize), typeof(int), typeof(ArtworkImage), new PropertyMetadata(420));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(Stretch), typeof(ArtworkImage), new PropertyMetadata(Stretch.UniformToFill));

    public static readonly DependencyProperty ShowPlaceholderProperty = DependencyProperty.Register(
        nameof(ShowPlaceholder), typeof(bool), typeof(ArtworkImage), new PropertyMetadata(true));

    private static readonly DependencyPropertyKey ImageKey = DependencyProperty.RegisterReadOnly(
        nameof(Image), typeof(ImageSource), typeof(ArtworkImage), new PropertyMetadata(null));

    private static readonly DependencyPropertyKey PlaceholderKey = DependencyProperty.RegisterReadOnly(
        nameof(Placeholder), typeof(Brush), typeof(ArtworkImage), new PropertyMetadata(null));

    public static readonly DependencyProperty ImageProperty = ImageKey.DependencyProperty;
    public static readonly DependencyProperty PlaceholderProperty = PlaceholderKey.DependencyProperty;

    private static readonly Duration Fade = new(TimeSpan.FromMilliseconds(200));

    private CancellationTokenSource? _loading;

    static ArtworkImage()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ArtworkImage), new FrameworkPropertyMetadata(typeof(ArtworkImage)));
    }

    public ArtworkImage()
    {
        Unloaded += (_, _) => _loading?.Cancel();
    }

    public Uri? Source
    {
        get => (Uri?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Initials
    {
        get => (string)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    /// <summary>Longest side to decode to, in pixels (the spec's per-use sizes).</summary>
    public int PixelSize
    {
        get => (int)GetValue(PixelSizeProperty);
        set => SetValue(PixelSizeProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public bool ShowPlaceholder
    {
        get => (bool)GetValue(ShowPlaceholderProperty);
        set => SetValue(ShowPlaceholderProperty, value);
    }

    public ImageSource? Image => (ImageSource?)GetValue(ImageProperty);

    public Brush? Placeholder => (Brush?)GetValue(PlaceholderProperty);

    private void UpdatePlaceholder() => SetValue(PlaceholderKey, Services.PlaceholderBrushes.For(Title ?? string.Empty));

    private async void Reload()
    {
        _loading?.Cancel();
        _loading = null;
        SetValue(ImageKey, null);
        if (Source is not { } url || Application.Current is not App)
        {
            return;
        }

        var loader = AppContainer.Current.Artwork;
        if (loader.Peek(url, PixelSize) is { } cached)
        {
            SetValue(ImageKey, cached);
            return;
        }

        var cts = new CancellationTokenSource();
        _loading = cts;
        try
        {
            var image = await loader.LoadAsync(url, PixelSize, cts.Token).ConfigureAwait(true);
            if (!cts.IsCancellationRequested && image is not null)
            {
                SetValue(ImageKey, image);
                (GetTemplateChild("PART_Image") as UIElement)?.BeginAnimation(OpacityProperty, new DoubleAnimation(0.0, 1.0, Fade) { FillBehavior = FillBehavior.Stop });
            }
        }
        catch (OperationCanceledException)
        {
            // A recycled container asked for a different image.
        }
    }
}
