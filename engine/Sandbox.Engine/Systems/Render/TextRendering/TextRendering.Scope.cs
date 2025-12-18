using System.Text.Json.Serialization;

namespace Sandbox;

public static partial class TextRendering
{
	// note: the is like this because eventally TextBlock will take multiple scopes, which would allow us
	// to parse html or something and change the styles of text inside a single textblock

	/// <summary>
	/// Defines a scope of text, all using the same style.
	/// </summary>
	[Expose]
	public struct Scope
	{
		[KeyProperty]
		[JsonInclude, TextArea] public string Text;

		[JsonInclude] public Color TextColor;
		[JsonInclude, FontName] public string FontName;

		[Range( 6, 256 )]
		[JsonInclude] public float FontSize;

		[Range( 0, 800 )]
		[JsonInclude] public int FontWeight;

		[JsonInclude] public bool FontItalic;

		[Range( 0, 5 )]
		[JsonInclude] public float LineHeight;

		[JsonInclude] public float LetterSpacing;
		[JsonInclude] public float WordSpacing;

		[JsonInclude] public Rendering.FilterMode FilterMode;
		[JsonInclude] public UI.FontSmooth FontSmooth;

		// this seems stupid, but it's like this because a list would be a pain
		// and also would be a bunch of bullshit to hashcode. This is fast.

		[Group( "Effects" )]
		[JsonInclude] public Outline Outline;

		[Group( "Effects" )]
		[JsonInclude] public Shadow Shadow;

		[Group( "Effects" )]
		[JsonInclude] public Outline OutlineUnder;

		[Group( "Effects" )]
		[JsonInclude] public Shadow ShadowUnder;

		public override int GetHashCode()
		{
			var hc = new HashCode();
			hc.Add( Text );
			hc.Add( TextColor );
			hc.Add( FontName );
			hc.Add( FontSize );
			hc.Add( FontWeight );
			hc.Add( FontItalic );
			hc.Add( LineHeight );
			hc.Add( LetterSpacing );
			hc.Add( WordSpacing );
			hc.Add( Outline );
			hc.Add( OutlineUnder );
			hc.Add( Shadow );
			hc.Add( ShadowUnder );
			hc.Add( FilterMode );
			hc.Add( FontSmooth );

			return hc.ToHashCode();
		}

		// outlines, shadows, underlines, italic?
		// Topten.RichTextKit.Style

		public static Scope Default => new Scope
		{
			Text = "Text",
			TextColor = Color.White,
			FontName = "Roboto",
			FontSize = 64,
			FontWeight = 400,
			LineHeight = 1,
			Shadow = new Shadow() { Color = Color.Black },
			ShadowUnder = new Shadow() { Color = Color.Black },
			Outline = new Outline() { Color = Color.Blue },
			OutlineUnder = new Outline() { Color = Color.Green },
			FilterMode = Rendering.FilterMode.Bilinear,
		};


		public Scope( string text, in Color color, float size, string font = "Roboto", int weight = 400 )
		{
			Text = text;
			TextColor = color;
			FontSize = size;
			FontName = font;
			FontWeight = weight;
			LineHeight = 1;
			Shadow = new Shadow() { Color = Color.Black };
			ShadowUnder = new Shadow() { Color = Color.Black };
			Outline = new Outline() { Color = Color.Blue };
			OutlineUnder = new Outline() { Color = Color.Green };
			FilterMode = Rendering.FilterMode.Bilinear;
		}

		/// <summary>
		/// Measures the rendered size of the text in this <see cref="Scope"/> using its current style settings. This is non trivial
		/// but the underlying style is cached, so if you end up drawing it, it'll re-use the cached data anyway.
		/// </summary>
		/// <returns>
		/// A <see cref="Vector2"/> representing the width and height, in pixels, of the rendered text.
		/// </returns>
		public Vector2 Measure()
		{
			var block = TextRendering.GetOrCreateTexture( this );
			return block.Size;
		}

		internal void ToStyle( Topten.RichTextKit.Style style )
		{
			style.FontFamily = FontName;
			style.FontSize = FontSize;
			style.FontWeight = FontWeight;
			style.FontItalic = FontItalic;
			style.TextColor = TextColor.ToSk();
			style.Underline = Topten.RichTextKit.UnderlineStyle.None;
			style.StrikeThrough = Topten.RichTextKit.StrikeThroughStyle.None;
			style.LetterSpacing = LetterSpacing;
			style.WordSpacing = WordSpacing;
			style.LineHeight = LineHeight;

			if ( ShadowUnder.Enabled )
			{
				style.AddEffect( Topten.RichTextKit.TextEffect.DropShadow( ShadowUnder.Color.ToSk(), ShadowUnder.Offset.x, ShadowUnder.Offset.y, ShadowUnder.Size ) );
			}

			if ( OutlineUnder.Enabled && OutlineUnder.Size > 0 )
			{
				style.AddEffect( Topten.RichTextKit.TextEffect.Outline( OutlineUnder.Color.ToSk(), OutlineUnder.Size ) );
			}

			if ( Shadow.Enabled )
			{
				style.AddEffect( Topten.RichTextKit.TextEffect.DropShadow( Shadow.Color.ToSk(), Shadow.Offset.x, Shadow.Offset.y, Shadow.Size ) );
			}

			if ( Outline.Enabled && Outline.Size > 0 )
			{
				style.AddEffect( Topten.RichTextKit.TextEffect.Outline( Outline.Color.ToSk(), Outline.Size ) );
			}

		}
	}

	[Expose]
	public struct Outline
	{
		public Outline()
		{
		}

		[JsonInclude][KeyProperty] public bool Enabled;
		[JsonInclude] public float Size = 4;
		[JsonInclude][KeyProperty] public Color Color = Color.White;

		public override int GetHashCode() => HashCode.Combine( Enabled, Size, Color );
	}

	[Expose]
	public struct Shadow
	{
		public Shadow()
		{
		}

		[JsonInclude][KeyProperty] public bool Enabled;
		[JsonInclude][KeyProperty] public float Size = 4;
		[JsonInclude][KeyProperty] public Color Color = Color.White;
		[JsonInclude] public Vector2 Offset = 4;

		public override int GetHashCode() => HashCode.Combine( Enabled, Size, Color, Offset );
	}
}
