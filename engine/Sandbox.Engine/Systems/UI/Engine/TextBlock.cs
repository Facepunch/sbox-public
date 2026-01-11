using Sandbox.Html;
using Sandbox.Rendering;
using SkiaSharp;
using System.Buffers;
using Topten.RichTextKit;

namespace Sandbox.UI;

internal sealed class TextBlock : IDisposable
{
	[ConVar( ConVarFlags.Protected, Help = "Enable rendering text to textures" )]
	public static bool ui_rendertext { get; set; } = true;

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

	Texture Texture;

	// we keep the last texture around incase we can re-use it
	Texture LastTexture;

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
	Topten.RichTextKit.TextGradient Gradient;

	int FontHash;
	//int ParentHash;

	float FontSize;
	int? FontWeight;
	TextAlign TextAlign;
	TextOverflow TextOverflow;
	FilterMode TextFilter;
	TextDecoration TextDecoration;
	FontStyle FontStyle;
	WordBreak WordBreak;
	TextTransform? TextTransform;
	Length? LetterSpacing;
	Length? WordSpacing;
	Length? LineHeight;
	Align AlignItems;
	WhiteSpace? WhiteSpace;
	GradientInfo GradientInfo;
	FontSmooth Smooth;

	Margin EffectMargin;


	Dictionary<int, Vector2> SizeCache = new Dictionary<int, Vector2>();

	public Vector2 Measure( float width, float height )
	{
		if ( !float.IsNaN( width ) ) width = width.CeilToInt();
		if ( !float.IsNaN( height ) ) height = height.CeilToInt();

		var hash = (int)width;
		if ( SizeCache.TryGetValue( hash, out var size ) )
			return size;

		Block.MaxWidth = float.IsNaN( width ) ? null : (width + 1);

		if ( TextOverflow != TextOverflow.None )
		{
			Block.MaxHeight = float.IsNaN( height ) ? null : (height + 1);
		}

		var s = new Vector2( Block.MeasuredWidth.CeilToInt(), Block.MeasuredHeight.CeilToInt() );

		SizeCache[hash] = s;

		return s;
	}

	void WaitTextureReady()
	{
		if ( TextureRebuild == null ) return;
		using var perfScope = Performance.Scope( "TextBlock.WaitRebuild" );
		TextureRebuild.Wait();
		TextureRebuild = null;
	}

	RenderAttributes textAttr = new RenderAttributes();

	/// <summary>
	/// Proper Rendering
	/// </summary>
	internal void Render( PanelRenderer renderer, ref RenderState state, Styles currentStyle, Rect textrect, float opacity )
	{
		WaitTextureReady();

		if ( Texture is null ) return;
		if ( BlockSize == 0 ) return;
		if ( !PanelRenderer.ui_drawtext ) return;

		if ( currentStyle.TextAlign == TextAlign.Center )
		{
			textrect.Left += (textrect.Width - BlockSize.x) * 0.5f;
		}
		else if ( currentStyle.TextAlign == TextAlign.Right )
		{
			textrect.Left = textrect.Right - BlockSize.x;
		}

		if ( currentStyle.AlignItems == Align.Center )
		{
			textrect.Top += (textrect.Height - BlockSize.y) * 0.5f;
		}
		else if ( currentStyle.AlignItems == Align.FlexEnd )
		{
			textrect.Top = textrect.Bottom - BlockSize.y;
		}

		textrect.Size = Texture.Size;
		textrect.Position -= EffectMargin.Position;

		var color = Color.White;
		color.a *= opacity;

		if ( color.a <= 0 ) return;

		var bm = renderer.OverrideBlendMode;

		if ( bm == BlendMode.Normal && Texture.Flags.Contains( TextureFlags.PremultipliedAlpha ) )
			bm = BlendMode.PremultipliedAlpha;

		textAttr.Set( "BoxPosition", textrect.Position );
		textAttr.Set( "BoxSize", textrect.Size );

		textAttr.Set( "TextureIndex", Texture.Index );
		textAttr.Set( "SamplerIndex", SamplerState.GetBindlessIndex( new SamplerState() { Filter = TextFilter } ) );
		textAttr.SetComboEnum( "D_BLENDMODE", bm );

		Graphics.DrawQuad( textrect.Floor(), Material.UI.Text, color, textAttr );
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

		if ( codepoint > 0 && codepoint == Block.Length && Text.Length > 0 && Text[^1] == '\n' )
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
		AlignItems = style.AlignItems.Value;
		LetterSpacing = style.LetterSpacing;
		WordSpacing = style.WordSpacing;
		LineHeight = style.LineHeight;
		WhiteSpace = style.WhiteSpace;
		TextTransform = style.TextTransform;
		GradientInfo = style.TextGradient;
		TextOverflow = style.TextOverflow.Value;
		TextFilter = style.TextFilter.Value;
		WordBreak = style.WordBreak.Value;
		Smooth = style.FontSmooth.Value;

		var hash = HashCode.Combine( FontSize, fontColor, fontFamily, FontWeight, TextAlign, WhiteSpace, TextDecoration, FontStyle );
		hash = HashCode.Combine( hash, LetterSpacing, TextTransform, Text, SelectionStart, SelectionEnd, ShouldDrawSelection, style.TextShadow );
		hash = HashCode.Combine( hash, style.TextStrokeWidth, style.TextStrokeColor, style.TextDecorationColor, style.TextDecorationThickness, style.TextDecorationSkipInk, style.TextDecorationStyle );
		hash = HashCode.Combine( hash, style.TextUnderlineOffset, style.TextOverlineOffset, style.TextLineThroughOffset, style.TextGradient, style.TextOverflow, style.WordBreak, style.LineHeight );
		hash = HashCode.Combine( hash, style.WordSpacing );
		hash = HashCode.Combine( hash, Smooth );

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
		Style.TextColor = fontColor.ToSk();
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

		Style.UnderlineColor = (style.TextDecorationColor ?? fontColor).ToSk();
		Style.StrokeThickness = style.TextDecorationThickness?.GetPixels( 100.0f );
		Style.Underline |= (TextDecoration & UI.TextDecoration.Underline) != 0 ? UnderlineStyle.Gapped : UnderlineStyle.None;
		Style.Underline |= (TextDecoration & UI.TextDecoration.Overline) != 0 ? UnderlineStyle.Overline : UnderlineStyle.None;
		Style.StrikeThrough = (TextDecoration & UI.TextDecoration.LineThrough) != 0 ? StrikeThroughStyle.Solid : StrikeThroughStyle.None;
		Style.LetterSpacing = LetterSpacing.Value.GetPixels( 1000.0f );
		Style.WordSpacing = WordSpacing.Value.GetPixels( 1000.0f );
		Style.LineHeight = GetLineHeightMultiplier();

		Style.ClearEffects();
		Gradient = null;

		EffectMargin = default;

		if ( !style.TextGradient.ColorOffsets.IsDefaultOrEmpty )
		{
			var colors = style.TextGradient.ColorOffsets.Select( x => SkiaCompat.ToSk( x.color ) ).ToArray();
			var stops = style.TextGradient.ColorOffsets.Select( x => x.offset.Value ).ToArray();

			if ( style.TextGradient.GradientType == GradientInfo.GradientTypes.Linear )
			{
				Gradient = TextGradient.Linear( colors, stops, style.TextGradient.Angle );
			}

			if ( style.TextGradient.GradientType == GradientInfo.GradientTypes.Radial )
			{
				Gradient = TextGradient.Radial( colors, stops, 0, new SKPoint( 0.5f, 0.5f ), (RadialSizeMode)style.TextGradient.SizeMode );
			}
		}

		if ( style.TextShadow != null && !style.TextShadow.IsNone )
		{
			foreach ( var shadow in style.TextShadow )
			{
				var effect = TextEffect.DropShadow( shadow.Color.ToSk(), shadow.OffsetX, shadow.OffsetY, shadow.Blur );
				effect.Width = 0;
				effect.BlurSize = MathF.Max( effect.BlurSize, 0.01f );
				Style.AddEffect( effect );

				var shadowSize = (effect.Width + shadow.Blur) * 2.0f;

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
			var effect = TextEffect.Outline( color.ToSk(), size );
			effect.StrokeMiter = 2.0f;
			effect.StrokeJoin = SKStrokeJoin.Round;
			Style.AddEffect( effect );

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
		Block.Alignment = (Topten.RichTextKit.TextAlignment)TextAlign;
		Block.Overflow = (Topten.RichTextKit.TextOverflow)TextOverflow;
		Block.WordBreak = (Topten.RichTextKit.WordBreakMode)WordBreak;
		Block.NoWrap = NoWrap || WhiteSpace == UI.WhiteSpace.NoWrap;

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

						sty.FontSize = s.FontSize?.GetPixels( 100 ) ?? sty.FontSize;
						sty.FontSize = MathF.Round( sty.FontSize * 32.0f ) / 32.0f;
						sty.FontFamily = s.FontFamily;
						sty.TextColor = s.FontColor?.ToSk() ?? sty.TextColor;
						sty.BackgroundColor = s.BackgroundColor?.ToSk() ?? sty.BackgroundColor;
						sty.FontWeight = s.FontWeight ?? sty.FontWeight;
						sty.FontItalic = s.FontStyle == FontStyle.Italic;
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
		else
		{
			Block.AddText( FixedText( Text ), Style );
		}


		SizeCache.Clear();
		ReleaseTexture();

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
			var endText = block.Length;

			var span = new HtmlSpan( node?.ParentNode, startText, endText );
			HtmlSpans.Add( span );
		}

		if ( node.Name == "br" )
		{
			block.AddText( "\n", style );
			return;
		}

		foreach ( var c in node.ChildNodes )
		{
			BuildBlockFromHtml( block, c, style );
		}
	}

	void ReleaseTexture()
	{
		WaitTextureReady();

		if ( Texture == null )
			return;

		LastTexture?.Dispose();
		LastTexture = Texture;

		Texture = null;
	}

	int lastSizeHash = 0;

	/// <summary>
	/// Called on layout. We should decide here if we actually need to rebuild
	/// </summary>
	public void SizeFinalized( float width, float height )
	{
		WaitTextureReady();

		width = width.CeilToInt();
		height = height.CeilToInt();

		int sizeHash = new Vector2( width, height ).GetHashCode();

		if ( lastSizeHash != sizeHash )
		{
			ReleaseTexture();
			lastSizeHash = sizeHash;

			if ( Text.Length == 0 )
			{
				BlockSize = new Vector2( Block.MeasuredWidth.CeilToInt().Clamp( 2, 4096 ), Block.MeasuredHeight.CeilToInt().Clamp( 2, 4096 ) );
			}
		}

		if ( Text.Length == 0 )
			return;

		if ( Texture == null )
		{
			// threaded
			// TextureRebuild = Task.Run( () => RebuildTexture( width, height ) );

			// blocking
			RebuildTexture( width, height );
		}
	}

	Task TextureRebuild;

	/// <summary>
	/// Actually recreate the texture
	/// </summary>
	unsafe void RebuildTexture( float maxwidth, float maxheight )
	{
		if ( !ui_rendertext )
			return;

		//Log.Info( $"RenderText: {Text}" );

		bool isEmpty = Text.Length == 0;

		if ( TextOverflow != TextOverflow.None )
		{
			Block.MaxWidth = maxwidth;
			Block.MaxHeight = maxheight;
		}
		else
		{
			Block.MaxWidth = WhiteSpace == UI.WhiteSpace.NoWrap ? null : (maxwidth.CeilToInt() + 1);
		}

		int width = Block.MeasuredWidth.CeilToInt().Clamp( 2, 4096 );
		int height = Block.MeasuredHeight.CeilToInt().Clamp( 2, 4096 );

		if ( Style.LetterSpacing < 0 )
			width += Math.Abs( (int)MathF.Floor( Style.LetterSpacing ) );

		BlockSize = new Vector2( width, height );
		IsTruncated = Block.Truncated;

		var marginEdge = EffectMargin.EdgeSize;
		width += marginEdge.x.CeilToInt();
		height += marginEdge.y.CeilToInt();

		if ( isEmpty )
			return;

		if ( Gradient != null && Gradient.GradientType == Topten.RichTextKit.GradientType.Radial )
		{
			var centerX = GradientInfo.OffsetX.GetPixels( width ) / width;
			var centerY = GradientInfo.OffsetY.GetPixels( height ) / height;
			Gradient.Center = new SKPoint( centerX, centerY );
		}

		using var perfScope = Performance.Scope( "TextBlock.RebuildTexture" );

		using ( var bitmap = new SKBitmap( width, height, SKColorType.Bgra8888, SKAlphaType.Premul ) )
		using ( var canvas = new SKCanvas( bitmap ) )
		{
			var o = new Topten.RichTextKit.TextPaintOptions
			{
				Edging = Smooth switch
				{
					FontSmooth.Never => SKFontEdging.Alias,
					_ => SKFontEdging.Antialias,
				},

				Hinting = SKFontHinting.Full,
				TextGradient = Gradient
			};

			canvas.Clear( Style.TextColor.WithAlpha( 0 ) );

			if ( ShouldDrawSelection && (SelectionStart > 0 || SelectionEnd > 0) )
			{
				o.Selection = new TextRange( CaretToCodePointIndex( SelectionStart ), CaretToCodePointIndex( SelectionEnd ) );
				o.SelectionColor = SelectionColor.ToSk();
			}

			Block.Paint( canvas, new SKPoint( EffectMargin.Left - Block.MeasuredPadding.Left, EffectMargin.Top ), o );

			var debugName = Text;
			if ( debugName.Length > 10 ) debugName = $"{debugName.Substring( 0, 8 )}..";
			if ( debugName.Contains( ':' ) ) debugName = debugName.Replace( ':', '-' );

			//
			// Make a texture that big
			//
			int numMips = (int)Math.Log2( Math.Min( width, height ) ) + 1;

			if ( LastTexture != null )
			{
				// we already have a texture that is the right size, lets just use that
				if ( LastTexture.Size == new Vector2( width, height ) )
				{
					var span = new Span<byte>( bitmap.GetPixels().ToPointer(), width * height * bitmap.BytesPerPixel );
					LastTexture.Update( span, 0, 0, width, height );
					Texture = LastTexture;
					LastTexture = null;
					return;
				}

				LastTexture?.Dispose();
				LastTexture = null;
			}

			Texture = Texture.Create( width, height, ImageFormat.BGRA8888 )
									.WithName( $"skiatextblock[{debugName}]" )
									.WithMips( numMips )
									.WithData( bitmap.GetPixels(), width * height * bitmap.BytesPerPixel )
									.WithDynamicUsage()
									.Finish();

			Texture.Flags |= TextureFlags.PremultipliedAlpha;
		}
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
			UI.WhiteSpace.Pre => text,
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

	internal void ScrollToCaret( int caretPosition, ref Vector2 scroll, Vector2 visibleBounds )
	{
		Rect caretRect = CaretRect( caretPosition - 1 );

		if ( caretRect.Left < scroll.x )
		{
			scroll.x = caretRect.Left;
		}
		else if ( caretRect.Right > scroll.x + visibleBounds.x )
		{
			scroll.x = caretRect.Right - visibleBounds.x + caretRect.Width;
		}

		if ( caretRect.Top < scroll.y )
		{
			scroll.y = caretRect.Top;
		}
		else if ( caretRect.Bottom > scroll.y + visibleBounds.y )
		{
			scroll.y = caretRect.Bottom - visibleBounds.y + caretRect.Height;
		}
	}

	public void Dispose()
	{
		ReleaseTexture();

		LastTexture?.Dispose();
		LastTexture = null;

		Block = null;
		Style = null;
		SizeCache = null;
	}
}
