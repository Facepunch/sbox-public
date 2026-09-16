using Sandbox.Html;
using Sandbox.Rendering;
using SkiaSharp;
using Topten.RichTextKit;

namespace Sandbox.UI;

internal sealed partial class TextBlock : IDisposable
{
	[ConVar( ConVarFlags.Protected, Help = "Draw UI text" )]
	public static bool ui_rendertext { get; set; } = true;

	public Action OnChanged { get; set; }

	public string Text { get; internal set; }

	public bool ShouldDrawSelection = false;
	public int SelectionStart { get; internal set; } = 0;
	public int SelectionEnd { get; internal set; } = 0;
	public Color SelectionColor { get; set; } = Color.Cyan.WithAlpha( 0.39f );
	public bool IsTruncated { get; internal set; }
	public bool NoWrap { get; internal set; }
	public bool IsHtml { get; internal set; }

	public Func<INode, Styles> LookupStyles { get; set; }

	public Vector2 BlockSize;

	/// <summary>
	/// The size the text measures to right now, before <see cref="BlockSize"/> is settled by layout, so
	/// layout and scrolling can ask about size at any point.
	/// </summary>
	public Vector2 MeasuredSize => Block is null ? default : new Vector2( Block.MeasuredWidth, Block.MeasuredHeight );

	/// <summary>
	/// The text rendered to a texture, only made when a background-clip: text needs it as a mask
	/// </summary>
	internal Texture Texture;
	bool textureDirty = true;

	internal void SetText( string text )
	{
		Text = text;
		IsHtml = false;
	}

	Sandbox.Html.Node htmlNode;

	internal void SetHtml( string text )
	{
		if ( IsHtml && Text == text ) return;

		Text = text;
		IsHtml = true;

		try
		{
			htmlNode = default;
			htmlNode = Sandbox.Html.Node.Parse( Text );
		}
		catch { }
	}

	public void Dirty()
	{
		FontHash = default;
	}

	Topten.RichTextKit.TextBlock Block;
	Topten.RichTextKit.Style Style;
	bool HasGradient;

	int FontHash;

	float FontSize;
	int? FontWeight;
	TextAlign TextAlign;
	TextOverflow TextOverflow;
	TextDecoration TextDecoration;
	FontStyle FontStyle;
	FontVariantNumeric? FontVariantNumeric;
	WordBreak WordBreak;
	TextTransform? TextTransform;
	Length? LetterSpacing;
	Length? WordSpacing;
	Length? LineHeight;
	WhiteSpace? WhiteSpace;
	TextGradientInfo GradientInfo;
	FontSmooth Smooth;

	/// <summary>
	/// Room shadows and outlines need around the text
	/// </summary>
	Margin EffectMargin;

	/// <summary>
	/// Room around the text in the current texture: effects plus glyph ink overhang. Texture is placed at the
	/// text rect minus this.
	/// </summary>
	Margin TextureMargin;


	Dictionary<int, Vector2> SizeCache = new Dictionary<int, Vector2>();
	Vector2? MinContentSize;

	internal Vector2 MeasureMinContent( float? width = null )
	{
		if ( width is null && MinContentSize is { } cached ) return cached;

		var size = Block.MeasureMinContent( width, WhiteSpace == UI.WhiteSpace.BreakSpaces, EndsWithNewline );
		var result = new Vector2( size.Width.CeilToInt(), size.Height.CeilToInt() );
		if ( width is null ) MinContentSize = result;
		return result;
	}

	public Vector2 Measure( float width, float height )
	{
		if ( !float.IsNaN( width ) ) width = width.CeilToInt();
		if ( !float.IsNaN( height ) ) height = height.CeilToInt();

		// Height only matters when text-overflow clips to it
		var hash = HashCode.Combine( (int)width, TextOverflow != TextOverflow.None ? (int)height : 0 );
		if ( SizeCache.TryGetValue( hash, out var size ) )
			return size;

		Block.MaxWidth = float.IsNaN( width ) ? null : (width + 1);

		if ( TextOverflow != TextOverflow.None )
		{
			Block.MaxHeight = float.IsNaN( height ) ? null : (height + 1);
		}

		var measuredHeight = Block.MeasuredHeight;

		// The paragraph gives a trailing newline's empty line no height, but the caret can sit on it
		if ( EndsWithNewline && Block.Lines.Count > 0 ) measuredHeight += Block.Lines[^1].Height;

		var s = new Vector2( Block.MeasuredWidth.CeilToInt(), measuredHeight.CeilToInt() );

		SizeCache[hash] = s;

		return s;
	}

	/// <summary>
	/// The block's text ends with a line break. Checked on the collapsed text rather than <see cref="Text"/>,
	/// since white-space collapsing can strip a trailing newline and leave the block with no line for it.
	/// </summary>
	bool EndsWithNewline;

	static bool EndsWithLineBreak( string text ) => text is { Length: > 0 } && text[^1] is '\n' or '\u2029';

	/// <summary>
	/// Number of lines, including the empty one after a trailing newline
	/// </summary>
	public int LineCount => Block is null ? 0 : Block.Lines.Count + (EndsWithNewline ? 1 : 0);

	/// <summary>
	/// The line a caret position is on
	/// </summary>
	public int LineOf( int caretPosition )
	{
		var codepoint = CaretToCodePointIndex( caretPosition );

		if ( EndsWithNewline && codepoint > 0 && codepoint == Block.Length )
			return Block.Lines.Count;

		return Block.GetCaretInfo( new CaretPosition { CodePointIndex = codepoint } ).LineIndex;
	}

	/// <summary>
	/// The caret position nearest an x on a given line
	/// </summary>
	public int GetLetterAtLine( int line, float x )
	{
		if ( Block is null ) return -1;
		if ( line >= Block.Lines.Count ) return Block.LookupCaretIndex( Block.Length );

		return Block.LookupCaretIndex( Block.HitTestLine( line, x ).ClosestCodePointIndex );
	}

	readonly List<GPUBoxInstance> Instances = new();

	/// <summary>
	/// Emit the text through Painter, drawn straight from the glyph outlines every frame.
	/// </summary>
	internal void Draw( Painter painter, BlendMode blendMode, Styles currentStyle, Rect textrect, float opacity )
	{
		if ( !ui_rendertext || Block is null || BlockSize == 0 || Text.Length == 0 ) return;
		if ( opacity <= 0 ) return;

		var options = GetOptions();
		options.Opacity = opacity;

		Instances.Clear();
		GpuFontText.Build( Block, GetBlockOrigin( currentStyle, textrect ), options, Instances );
		painter.Glyphs( Instances, blendMode, HasGradient ? GradientInfo : default );
	}

	GpuFontText.Options GetOptions()
	{
		var options = GpuFontText.Options.Default;
		options.Aliased = Smooth == FontSmooth.Never;
		options.SelectionColor = SelectionColor;
		options.HasGradient = HasGradient;

		if ( ShouldDrawSelection && (SelectionStart > 0 || SelectionEnd > 0) )
		{
			options.SelectionStart = CaretToCodePointIndex( SelectionStart );
			options.SelectionEnd = CaretToCodePointIndex( SelectionEnd );
		}

		return options;
	}

	/// <summary>
	/// The text rect aligned to the block, given the rect the text is laid out in.
	/// </summary>
	Rect GetAlignedRect( Styles currentStyle, Rect textrect )
	{
		if ( currentStyle?.TextAlign == TextAlign.Center )
		{
			textrect.Left += (textrect.Width - BlockSize.x) * 0.5f;
		}
		else if ( currentStyle?.TextAlign == TextAlign.Right )
		{
			textrect.Left = textrect.Right - BlockSize.x;
		}

		if ( currentStyle?.AlignItems == Align.Center )
		{
			textrect.Top += (textrect.Height - BlockSize.y) * 0.5f;
		}
		else if ( currentStyle?.AlignItems == Align.FlexEnd )
		{
			textrect.Top = textrect.Bottom - BlockSize.y;
		}

		return textrect;
	}

	/// <summary>
	/// Where the block's (0,0) lands in layout space
	/// </summary>
	Vector2 GetBlockOrigin( Styles currentStyle, Rect textrect )
	{
		var position = GetAlignedRect( currentStyle, textrect ).Floor().Position;
		return position - new Vector2( Block.MeasuredPadding.Left, 0 );
	}

	/// <summary>
	/// Where the rendered texture sits, given the rect the text is laid out in.
	/// </summary>
	Rect GetTextureRect( Styles currentStyle, Rect textrect )
	{
		textrect = GetAlignedRect( currentStyle, textrect );
		textrect.Size = Texture.Size;
		textrect.Position -= TextureMargin.Position;

		return textrect.Floor();
	}

	/// <summary>
	/// The rendered text and where it sits, for background-clip: text to use as its mask.
	/// </summary>
	internal bool GetMask( Styles currentStyle, Rect textrect, out Texture texture, out Rect rect )
	{
		texture = null;
		rect = default;

		if ( Block is null || BlockSize == 0 || Text.Length == 0 ) return false;

		// Only background-clip: text needs the texture, so it's rendered here on demand
		if ( textureDirty )
		{
			Texture = RebuildTexture();
			textureDirty = false;
		}

		if ( Texture is null ) return false;

		texture = Texture;
		rect = GetTextureRect( currentStyle, textrect );

		return true;
	}

	public Rect CaretRect( int caretPosition )
	{
		var codepoint = CaretToCodePointIndex( caretPosition );

		// Skias caret includes newlines however for rendering, we don't want this
		// It also appears AltPosition is absolutely fucked and changes nothing
		var cp = new CaretPosition { AltPosition = false, CodePointIndex = codepoint };
		var pos = Block.GetCaretInfo( cp );

		float xPosition = pos.CaretRectangle.Left;
		float yPosition = pos.CaretRectangle.Top;

		if ( codepoint > 0 && codepoint == Block.Length && EndsWithNewline )
		{
			xPosition = 0;
			yPosition += Block.Lines[pos.LineIndex].Height;
		}

		return new Rect( xPosition, yPosition, pos.CaretRectangle.Width, pos.CaretRectangle.Height );
	}

	public int GetLetterAt( Vector2 pos )
	{
		if ( Block == null ) return -1;

		var result = Block.HitTest( pos.x, pos.y );

		return Block.LookupCaretIndex( result.ClosestCodePointIndex );
	}

	public HtmlSpan GetSpanAt( Vector2 pos )
	{
		if ( Block == null ) return default;
		if ( HtmlSpans is null ) return default;

		var result = Block.HitTest( pos.x, pos.y );
		return HtmlSpans.Where( x => x.from <= result.OverCodePointIndex && x.to > result.OverCodePointIndex ).FirstOrDefault();
	}

	public bool UpdateStyles( Styles style )
	{
		var fontFamily = style.FontFamily ?? "Arial";
		var fontColor = style.FontColor ?? Color.Black;
		var fontSize = style.FontSize ?? Length.Pixels( 13 ).Value;

		FontSize = fontSize.GetPixels( 100 ); // this should probably be screen height for font length?
		FontSize = MathF.Round( FontSize * 32.0f ) / 32.0f; // round the font size so we're not redrawing for no reason on font size lerp
		FontWeight = style.FontWeight;
		TextAlign = style.TextAlign.Value;
		TextDecoration = style.TextDecorationLine.Value;
		FontStyle = style.FontStyle.Value;
		FontVariantNumeric = style.FontVariantNumeric;
		LetterSpacing = style.LetterSpacing;
		WordSpacing = style.WordSpacing;
		LineHeight = style.LineHeight;
		WhiteSpace = style.WhiteSpace;
		TextTransform = style.TextTransform;
		GradientInfo = style.TextGradient;
		TextOverflow = style.TextOverflow.Value;
		WordBreak = style.WordBreak.Value;
		Smooth = style.FontSmooth.Value;

		var hash = HashCode.Combine( FontSize, fontColor, fontFamily, FontWeight, TextAlign, WhiteSpace, TextDecoration, FontStyle );
		hash = HashCode.Combine( hash, LetterSpacing, TextTransform, Text, SelectionStart, SelectionEnd, ShouldDrawSelection, style.TextShadow );
		hash = HashCode.Combine( hash, style.TextStrokeWidth, style.TextStrokeColor, style.TextDecorationColor, style.TextDecorationThickness, style.TextDecorationSkipInk, style.TextDecorationStyle );
		hash = HashCode.Combine( hash, style.TextUnderlineOffset, style.TextOverlineOffset, style.TextLineThroughOffset, style.TextGradient, style.TextOverflow, style.WordBreak, style.LineHeight );
		hash = HashCode.Combine( hash, style.WordSpacing );
		hash = HashCode.Combine( hash, Smooth, FontVariantNumeric, NoWrap, IsHtml );

		if ( FontHash == hash && Block != null )
			return false;

		//
		// Create a hash of things on the font that could change
		//

		FontHash = hash;

		Style ??= new Style();


		Style.FontFamily = fontFamily;
		Style.FontSize = FontSize;
		Style.FontWeight = FontWeight ?? 400;
		Style.FontItalic = FontStyle != FontStyle.None;
		Style.FontVariantNumeric = FontVariantNumeric ?? UI.FontVariantNumeric.Normal;
		Style.TextColor = fontColor.ToSkF();
		Style.Underline = UnderlineStyle.None;
		Style.StrokeInkSkip = style.TextDecorationSkipInk == TextSkipInk.All;
		Style.UnderlineOffset = style.TextUnderlineOffset.Value.GetPixels( 100 );
		Style.OverlineOffset = style.TextOverlineOffset.Value.GetPixels( 100 );
		Style.StrikeThroughOffset = style.TextLineThroughOffset.Value.GetPixels( 100 );

		switch ( style.TextDecorationStyle )
		{
			case TextDecorationStyle.Solid:
				Style.UnderlineStrokeType = UnderlineType.Solid;
				break;
			case TextDecorationStyle.Double:
				Style.UnderlineStrokeType = UnderlineType.Double;
				break;
			case TextDecorationStyle.Dotted:
				Style.UnderlineStrokeType = UnderlineType.Dotted;
				break;
			case TextDecorationStyle.Dashed:
				Style.UnderlineStrokeType = UnderlineType.Dashed;
				break;
			case TextDecorationStyle.Wavy:
				Style.UnderlineStrokeType = UnderlineType.Wavy;
				break;
			default:
				Style.UnderlineStrokeType = UnderlineType.Solid;
				break;
		}

		var decorationColor = style.TextDecorationColor ?? fontColor;
		Style.UnderlineColor = decorationColor.ToSkF();
		Style.StrokeThickness = style.TextDecorationThickness?.GetPixels( 100.0f );
		Style.Underline |= (TextDecoration & UI.TextDecoration.Underline) != 0 ? UnderlineStyle.Gapped : UnderlineStyle.None;
		Style.Underline |= (TextDecoration & UI.TextDecoration.Overline) != 0 ? UnderlineStyle.Overline : UnderlineStyle.None;
		Style.StrikeThrough = (TextDecoration & UI.TextDecoration.LineThrough) != 0 ? StrikeThroughStyle.Solid : StrikeThroughStyle.None;
		Style.LetterSpacing = LetterSpacing.Value.GetPixels( 1000.0f );
		Style.WordSpacing = WordSpacing.Value.GetPixels( 1000.0f );
		Style.LineHeight = GetLineHeightMultiplier();

		Style.ClearEffects();

		EffectMargin = default;

		HasGradient = !style.TextGradient.ColorOffsets.IsDefaultOrEmpty && style.TextGradient.GradientType != Sandbox.UI.GradientInfo.GradientTypes.Conic;

		if ( style.TextShadow != null && !style.TextShadow.IsNone )
		{
			foreach ( var shadow in style.TextShadow )
			{
				var effect = TextEffect.DropShadow( shadow.Color.ToSkF(), shadow.OffsetX, shadow.OffsetY, shadow.Blur );
				effect.BlurSize = MathF.Max( effect.BlurSize, 0.01f );
				Style.AddEffect( effect );

				var shadowSize = shadow.Blur * 2.0f;

				EffectMargin.Left = MathF.Max( EffectMargin.Left, shadowSize + -shadow.OffsetX ).CeilToInt();
				EffectMargin.Right = MathF.Max( EffectMargin.Right, shadowSize + shadow.OffsetX ).CeilToInt();
				EffectMargin.Top = MathF.Max( EffectMargin.Top, shadowSize + -shadow.OffsetY ).CeilToInt();
				EffectMargin.Bottom = MathF.Max( EffectMargin.Bottom, shadowSize + shadow.OffsetY ).CeilToInt();
			}
		}

		if ( style.TextStrokeWidth.Value.Value > 0.0f )
		{
			var color = style.TextStrokeColor ?? style.FontColor ?? Color.Black;

			var size = style.TextStrokeWidth.Value.GetPixels( 1.0f );
			Style.AddEffect( TextEffect.Outline( color.ToSkF(), size ) );

			EffectMargin.Left = MathF.Max( EffectMargin.Left, size ).CeilToInt();
			EffectMargin.Right = MathF.Max( EffectMargin.Right, size ).CeilToInt();
			EffectMargin.Top = MathF.Max( EffectMargin.Top, size ).CeilToInt();
			EffectMargin.Bottom = MathF.Max( EffectMargin.Bottom, size ).CeilToInt();
		}

		if ( Block == null )
		{
			Block = new Topten.RichTextKit.TextBlock();
			Block.FontMapper = FontManager.Instance;
		}

		Block.Clear();
		EndsWithNewline = false;
		Block.Alignment = (Topten.RichTextKit.TextAlignment)TextAlign;
		Block.Overflow = (Topten.RichTextKit.TextOverflow)TextOverflow;
		Block.WordBreak = (Topten.RichTextKit.WordBreakMode)WordBreak;
		Block.NoWrap = NoWrap || WhiteSpace is UI.WhiteSpace.NoWrap or UI.WhiteSpace.Pre;

		if ( IsHtml && !string.IsNullOrWhiteSpace( Text ) )
		{
			try
			{
				HtmlSpans = new List<HtmlSpan>();

				var html = htmlNode;
				if ( html is not null )
				{
					BuildBlockFromHtml( Block, html, Style );
				}

				if ( LookupStyles is not null )
				{
					foreach ( var span in HtmlSpans )
					{
						var s = LookupStyles( span.node );
						if ( s is null ) continue;

						var sty = Style.Copy();

						sty.FontSize = (s.FontSize ?? style.FontSize ?? Length.Pixels( 13 ).Value).GetPixels( 100 );
						sty.FontSize = MathF.Round( sty.FontSize * 32.0f ) / 32.0f;
						sty.FontFamily = s.FontFamily ?? sty.FontFamily;
						sty.TextColor = s.FontColor?.ToSkF() ?? sty.TextColor;
						sty.BackgroundColor = s.BackgroundColor?.ToSkF() ?? sty.BackgroundColor;
						sty.FontWeight = s.FontWeight ?? sty.FontWeight;
						sty.FontItalic = s.FontStyle == FontStyle.Italic;
						sty.FontVariantNumeric = s.FontVariantNumeric ?? sty.FontVariantNumeric;
						sty.Underline = s.TextDecorationLine == UI.TextDecoration.Underline ? UnderlineStyle.Solid : UnderlineStyle.None;
						sty.UnderlineColor = sty.TextColor;
						sty.LetterSpacing = s.LetterSpacing?.GetPixels( 1000.0f ) ?? sty.LetterSpacing;
						sty.WordSpacing = s.WordSpacing?.GetPixels( 1000.0f ) ?? sty.WordSpacing;
						sty.StrikeThrough = (s.TextDecorationLine?.Contains( UI.TextDecoration.LineThrough ) ?? false) ? StrikeThroughStyle.Solid : sty.StrikeThrough;

						Block.ApplyStyle( span.from, span.to - span.from, sty );
					}
				}
			}
			catch ( System.Exception e )
			{
				Log.Warning( e );
			}
		}
		else if ( !IsInlineParagraph )
		{
			var text = FixedText( Text );
			Block.AddText( text, Style );
			EndsWithNewline = EndsWithLineBreak( text );
		}


		SizeCache.Clear();
		MinContentSize = null;
		Invalidate();

		return true;
	}

	public record class HtmlSpan( INode node, int from, int to );
	public List<HtmlSpan> HtmlSpans;

	private void BuildBlockFromHtml( Topten.RichTextKit.TextBlock block, Node node, Style style )
	{
		if ( node.IsComment )
			return;

		if ( node.IsText )
		{
			var startText = block.Length;
			block.AddText( node.InnerHtml, style );
			EndsWithNewline = EndsWithLineBreak( node.InnerHtml );
			var endText = block.Length;

			var span = new HtmlSpan( node?.ParentNode, startText, endText );
			HtmlSpans.Add( span );
		}

		if ( node.Name == "br" )
		{
			block.AddText( "\n", style );
			EndsWithNewline = true;
			return;
		}

		foreach ( var c in node.ChildNodes )
		{
			BuildBlockFromHtml( block, c, style );
		}
	}

	/// <summary>The text is about to change shape, so whoever draws it has to rebuild.</summary>
	void Invalidate()
	{
		needsLayout = true;
		textureDirty = true;

		OnChanged?.Invoke();
	}

	int lastSizeHash = 0;
	bool needsLayout = true;
	Vector2 textureSize;

	/// <summary>
	/// Called on layout. We should decide here if we actually need to re-lay the text out
	/// </summary>
	public void SizeFinalized( float width, float height )
	{
		width = width.CeilToInt();
		height = height.CeilToInt();

		int sizeHash = new Vector2( width, height ).GetHashCode();

		if ( lastSizeHash != sizeHash )
		{
			Invalidate();
			lastSizeHash = sizeHash;

			if ( Text.Length == 0 )
			{
				BlockSize = new Vector2( Block.MeasuredWidth.CeilToInt().Clamp( 2, 4096 ), Block.MeasuredHeight.CeilToInt().Clamp( 2, 4096 ) );
			}
		}

		if ( Text.Length == 0 )
			return;

		if ( needsLayout )
		{
			Relayout( width, height );
			needsLayout = false;
		}
	}

	/// <summary>
	/// Lay the block out at this size and work out how big the text is, effects and overhang included
	/// </summary>
	void Relayout( float maxwidth, float maxheight )
	{
		if ( TextOverflow != TextOverflow.None )
		{
			Block.MaxWidth = maxwidth;
			Block.MaxHeight = maxheight;
		}
		else
		{
			Block.MaxWidth = IsInlineParagraph ? _inlineWidth : WhiteSpace == UI.WhiteSpace.NoWrap ? null : (maxwidth.CeilToInt() + 1);
		}

		int width = Block.MeasuredWidth.CeilToInt().Clamp( 2, 4096 );
		int height = Block.MeasuredHeight.CeilToInt().Clamp( 2, 4096 );

		if ( Style.LetterSpacing < 0 )
			width += Math.Abs( (int)MathF.Floor( Style.LetterSpacing ) );

		BlockSize = new Vector2( width, height );
		IsTruncated = Block.Truncated;

		// Ink that reaches past the measured rect (italic tails, accents, tight bearings) needs room too
		var overhang = Block.MeasuredOverhang;
		TextureMargin = EffectMargin + new Margin( MathF.Ceiling( overhang.Left ), MathF.Ceiling( overhang.Top ), MathF.Ceiling( overhang.Right ), MathF.Ceiling( overhang.Bottom ) );

		var marginEdge = TextureMargin.EdgeSize;
		textureSize = new Vector2( width + marginEdge.x.CeilToInt(), height + marginEdge.y.CeilToInt() );
	}

	/// <summary>
	/// Render the laid out text into a texture, for background-clip: text
	/// </summary>
	Texture RebuildTexture()
	{
		using var perfScope = Performance.Scope( "TextBlock.RebuildTexture" );

		int width = (int)textureSize.x, height = (int)textureSize.y;
		if ( width < 2 || height < 2 ) return null;

		// Only the alpha is used, so colour and gradient don't matter here
		var options = GetOptions();
		options.HasGradient = false;

		var origin = new Vector2( TextureMargin.Left - Block.MeasuredPadding.Left, TextureMargin.Top );
		return GpuFontText.Render( Block, origin, width, height, false, int.MaxValue, options, Texture );
	}

	int CaretToCodePointIndex( int caretPos )
	{
		if ( caretPos < 0 || caretPos > Block.CaretIndicies.Count - 1 )
			return caretPos;

		return Block.CaretIndicies[caretPos];
	}

	string FixedText( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return ".";

		// TODO - if starts with #, look up localized string

		//text = text.Replace( "\r", "" );
		text = text.Replace( "\r\n", new string( (char)0x2029, 1 ) );
		text = text.Replace( '\n', (char)0x2029 ); // replace newlines with paragraph, makes them not render as a square with a cross in it

		if ( TextTransform.HasValue )
		{
			switch ( TextTransform.Value )
			{
				case UI.TextTransform.Uppercase:
					text = text.ToUpperInvariant();
					break;

				case UI.TextTransform.Lowercase:
					text = text.ToLowerInvariant();
					break;

				case UI.TextTransform.Capitalize:
					{
						text = System.Threading.Thread.CurrentThread.CurrentCulture.TextInfo.ToTitleCase( text );
						break;
					}
			}
		}

		text = WhiteSpace switch
		{
			UI.WhiteSpace.Normal or UI.WhiteSpace.NoWrap => text.CollapseWhiteSpace(),
			UI.WhiteSpace.PreLine => text.CollapseSpacesAndPreserveLines(),
			UI.WhiteSpace.Pre or UI.WhiteSpace.PreWrap or UI.WhiteSpace.BreakSpaces => text,
			_ => throw new Exception( $"Unknown white-space value {WhiteSpace}" ),
		};

		return text;
	}

	float GetLineHeightMultiplier()
	{
		var lineHeight = LineHeight.Value;

		if ( lineHeight.Unit == LengthUnit.Percentage )
			return lineHeight.GetFraction();

		if ( lineHeight.Unit == LengthUnit.Pixels )
			return lineHeight.Value / Math.Max( FontSize, 1.0f );

		return 1.0f;
	}

	/// <summary>
	/// How wide the drawn caret is, which has to fit on screen along with the glyph it sits against.
	/// </summary>
	const float CaretWidth = 2.0f;

	/// <summary>
	/// Move the scroll offset so the caret is inside the visible bounds, and never past the
	/// ends of the text.
	/// </summary>
	internal void ScrollToCaret( int caretPosition, ref Vector2 scroll, Vector2 visibleBounds )
	{
		if ( visibleBounds.x <= 0 || visibleBounds.y <= 0 )
			return;

		Rect caretRect = CaretRect( caretPosition );

		if ( caretRect.Left < scroll.x )
		{
			scroll.x = caretRect.Left;
		}
		else if ( caretRect.Left + CaretWidth > scroll.x + visibleBounds.x )
		{
			scroll.x = caretRect.Left + CaretWidth - visibleBounds.x;
		}

		if ( caretRect.Top < scroll.y )
		{
			scroll.y = caretRect.Top;
		}
		else if ( caretRect.Bottom > scroll.y + visibleBounds.y )
		{
			scroll.y = caretRect.Bottom - visibleBounds.y;
		}

		ClampScroll( ref scroll, visibleBounds );
	}

	/// <summary>
	/// Keep the scroll offset inside the text. Editing can leave it pointing past the end -
	/// deleting the second half of a line the entry was scrolled into, say.
	/// </summary>
	internal void ClampScroll( ref Vector2 scroll, Vector2 visibleBounds )
	{
		if ( visibleBounds.x <= 0 || visibleBounds.y <= 0 )
			return;

		if ( Block is null )
			return;

		// The measured size, not BlockSize - BlockSize is clamped to 4096 and rounded up, and scrolling
		// wants the real extent
		scroll.x = Math.Clamp( scroll.x, 0, Math.Max( 0, Block.MeasuredWidth + CaretWidth - visibleBounds.x ) );
		scroll.y = Math.Clamp( scroll.y, 0, Math.Max( 0, Block.MeasuredHeight - visibleBounds.y ) );
	}

	public void Dispose()
	{
		Texture?.Dispose();
		Texture = null;

		Block = null;
		Style = null;
		SizeCache = null;
		_inlineLayout = null;
	}
}
