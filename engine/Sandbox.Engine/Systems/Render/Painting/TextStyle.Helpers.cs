using Sandbox.UI;

namespace Sandbox;

public readonly partial record struct TextStyle
{
	/// <summary>
	/// Returns a copy with a font family and optional size and weight.
	/// </summary>
	public TextStyle WithFont( string fontName, float? size = null, int? weight = null )
		=> this with { FontName = fontName, FontSize = size ?? FontSize, FontWeight = weight ?? FontWeight };

	/// <summary>
	/// Returns a copy with a font size in pixels before ScaleToScreen.
	/// </summary>
	public TextStyle WithSize( float size ) => this with { FontSize = size };

	/// <summary>
	/// Returns a copy with a font weight from 1 to 1000.
	/// </summary>
	public TextStyle WithWeight( int weight ) => this with { FontWeight = weight };

	/// <summary>
	/// Returns a copy with bold (700) or normal (400) weight.
	/// </summary>
	public TextStyle WithBold( bool bold = true ) => WithWeight( bold ? 700 : 400 );

	/// <summary>
	/// Returns a copy with italic enabled or disabled.
	/// </summary>
	public TextStyle WithItalic( bool italic = true ) => this with { Italic = italic };

	/// <summary>
	/// Returns a copy with a text color. Alpha applies to the text and its effects together.
	/// </summary>
	public TextStyle WithColor( Color color ) => this with { Color = color };

	/// <summary>
	/// Returns a copy with alignment and layout flags for the destination rectangle.
	/// </summary>
	public TextStyle WithAlignment( TextFlag alignment ) => this with { Alignment = alignment };

	/// <summary>
	/// Returns a copy with a line-height multiplier.
	/// </summary>
	public TextStyle WithLineHeight( float height ) => this with { LineHeight = height };

	/// <summary>
	/// Returns a copy with additional spacing between letters, in pixels before ScaleToScreen.
	/// </summary>
	public TextStyle WithLetterSpacing( float spacing ) => this with { LetterSpacing = spacing };

	/// <summary>
	/// Returns a copy with additional spacing between words, in pixels before ScaleToScreen.
	/// </summary>
	public TextStyle WithWordSpacing( float spacing ) => this with { WordSpacing = spacing };

	/// <summary>
	/// Returns a copy with a drop shadow, replacing any existing shadow. Blur and offset are pixels before ScaleToScreen.
	/// Defaults to half-opacity black, 4px blur and a (2, 2) offset. Zero blur produces a crisp shadow.
	/// </summary>
	public TextStyle WithShadow( Color? color = null, float blur = 4, Vector2? offset = null ) => this with
	{
		Shadow = new TextRendering.Shadow { Enabled = true, Color = color ?? Color.Black.WithAlpha( 0.5f ), Size = blur, Offset = offset ?? new Vector2( 2 ) }
	};

	/// <summary>
	/// Returns a copy with a drop shadow using separate X/Y offsets and optional blur, in pixels before ScaleToScreen.
	/// </summary>
	public TextStyle WithShadow( Color color, float offsetX, float offsetY, float blur = 4 )
		=> WithShadow( color, blur, new Vector2( offsetX, offsetY ) );

	/// <summary>
	/// Returns a copy without its drop shadow.
	/// </summary>
	public TextStyle WithoutShadow() => this with { Shadow = default };

	/// <summary>
	/// Returns a copy with an outline, replacing any existing outline. Defaults to black with a 1px stroke width before ScaleToScreen.
	/// </summary>
	public TextStyle WithOutline( Color? color = null, float width = 1 ) => this with
	{
		Outline = new TextRendering.Outline { Enabled = true, Color = color ?? Color.Black, Size = width }
	};

	/// <summary>
	/// Returns a copy without its outline.
	/// </summary>
	public TextStyle WithoutOutline() => this with { Outline = default };
}
