using System.Windows.Markup;
using Wpf.Ui.Controls;
using Wpf.Ui.Extensions;

namespace Retune.App.Controls;

/// <summary>
/// The character for a Fluent icon, for a plain TextBlock (styles <c>Glyph</c> / <c>GlyphFilled</c>). Tiles use this
/// instead of SymbolIcon controls: thousands of tiles scroll past, and a TextBlock is far cheaper to build.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class GlyphExtension : MarkupExtension
{
    public GlyphExtension()
    {
    }

    public GlyphExtension(SymbolRegular symbol) => Symbol = symbol;

    [ConstructorArgument("symbol")]
    public SymbolRegular Symbol { get; set; }

    /// <summary>Use with the <c>GlyphFilled</c> style.</summary>
    public bool Filled { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Filled ? Symbol.Swap().GetString() : Symbol.GetString();
}
