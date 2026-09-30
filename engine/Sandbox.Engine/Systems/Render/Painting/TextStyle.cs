using Sandbox.UI;

namespace Sandbox;

/// <summary>
/// Text settings for Painter.
/// </summary>
public readonly partial record struct TextStyle
{
	/// <summary>
	/// White, 16px Roboto with normal weight and top-left alignment.
	/// </summary>
	public static TextStyle Default => new();

	/// <summary>
	/// Font family, with the engine's usual fallback for unavailable fonts.
	/// </summary>
	public string FontName { get; init; } = "Roboto";

	/// <summary>
	/// Font size in pixels, scaled by the panel's ScaleToScreen.
	/// </summary>
	public float FontSize { get; init; } = 16;

	/// <summary>
	/// Font weight from 1 to 1000. Normal is 400, bold is 700.
	/// </summary>
	public int FontWeight { get; init; } = 400;

	/// <summary>
	/// Use an italic face.
	/// </summary>
	public bool Italic { get; init; }

	/// <summary>
	/// Text color. Its alpha combines with drawing and panel opacity.
	/// </summary>
	public Color Color { get; init; } = Color.White;

	/// <summary>
	/// Alignment and layout flags used within the destination rectangle.
	/// </summary>
	public TextFlag Alignment { get; init; } = TextFlag.LeftTop;

	/// <summary>
	/// Line height multiplier, where 1 uses the font's normal line height.
	/// </summary>
	public float LineHeight { get; init; } = 1;

	/// <summary>
	/// Additional spacing between letters, in pixels before ScaleToScreen.
	/// </summary>
	public float LetterSpacing { get; init; }

	/// <summary>
	/// Additional spacing between words, in pixels before ScaleToScreen.
	/// </summary>
	public float WordSpacing { get; init; }

	/// <summary>
	/// One drop shadow, with blur and offset in pixels before ScaleToScreen. Disabled by default.
	/// </summary>
	public TextRendering.Shadow Shadow { get; init; }

	/// <summary>
	/// One outline, with stroke width in pixels before ScaleToScreen. Disabled by default.
	/// </summary>
	public TextRendering.Outline Outline { get; init; }

	/// <summary>
	/// Creates TextStyle.Default: white, 16px Roboto with normal weight and top-left alignment.
	/// Unlike default(TextStyle), this constructor initializes the font and layout settings.
	/// </summary>
	public TextStyle() { }

	/// <summary>
	/// A white style with the given font family and size in pixels.
	/// </summary>
	public TextStyle( string fontName, float fontSize ) : this()
	{
		FontName = fontName;
		FontSize = fontSize;
	}

	/// <summary>
	/// A style with the given font family, size in pixels and color.
	/// </summary>
	public TextStyle( string fontName, float fontSize, Color color ) : this( fontName, fontSize )
	{
		Color = color;
	}

	internal void Validate()
	{
		if ( string.IsNullOrWhiteSpace( FontName ) ) throw new ArgumentException( "A font name is required.", nameof( FontName ) );
		if ( !float.IsFinite( FontSize ) || FontSize <= 0 ) throw new ArgumentOutOfRangeException( nameof( FontSize ) );
		if ( FontWeight < 1 || FontWeight > 1000 ) throw new ArgumentOutOfRangeException( nameof( FontWeight ) );
		if ( !float.IsFinite( LineHeight ) || LineHeight <= 0 ) throw new ArgumentOutOfRangeException( nameof( LineHeight ) );
		if ( !float.IsFinite( LetterSpacing ) ) throw new ArgumentOutOfRangeException( nameof( LetterSpacing ) );
		if ( !float.IsFinite( WordSpacing ) ) throw new ArgumentOutOfRangeException( nameof( WordSpacing ) );
		ValidateColor( Color, nameof( Color ) );
		if ( Shadow.Enabled )
		{
			if ( !float.IsFinite( Shadow.Size ) || Shadow.Size < 0 || !Shadow.Offset.IsFinite )
				throw new ArgumentOutOfRangeException( nameof( Shadow ) );
			ValidateColor( Shadow.Color, nameof( Shadow ) );
		}
		if ( Outline.Enabled )
		{
			if ( !float.IsFinite( Outline.Size ) || Outline.Size < 0 ) throw new ArgumentOutOfRangeException( nameof( Outline ) );
			ValidateColor( Outline.Color, nameof( Outline ) );
		}
	}

	static void ValidateColor( Color color, string parameter )
	{
		if ( !float.IsFinite( color.r ) || !float.IsFinite( color.g ) || !float.IsFinite( color.b ) || !float.IsFinite( color.a ) )
			throw new ArgumentOutOfRangeException( parameter );
	}

	internal TextRendering.Scope CreateScope( string text, float scale )
	{
		var scope = new TextRendering.Scope( text, Color.WithAlpha( 1 ), FontSize * scale, FontName, FontWeight )
		{
			FontItalic = Italic,
			LineHeight = LineHeight,
			LetterSpacing = LetterSpacing * scale,
			WordSpacing = WordSpacing * scale,
		};
		if ( Shadow.Enabled ) scope.Shadow = Shadow with { Size = Shadow.Size * scale, Offset = Shadow.Offset * scale };
		if ( Outline.Enabled ) scope.Outline = Outline with { Size = Outline.Size * scale };
		return scope;
	}
}
