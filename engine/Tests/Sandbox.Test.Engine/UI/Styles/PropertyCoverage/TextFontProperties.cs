using Sandbox.UI;

namespace UITests.PropertyCoverage;

/// <summary>
/// Exhaustive property coverage for the Text &amp; Font CSS property group.
/// Each test asserts the actual parsed value.
/// </summary>
[TestClass]
public class TextFontPropertiesTest
{
	// ---------------------------------------------------------------------
	// font-size -> FontSize (Length?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void FontSize_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-size", "20px" ) );
		Assert.IsTrue( s.FontSize.HasValue );
		Assert.AreEqual( 20, s.FontSize.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FontSize.Value.Unit );
	}

	[TestMethod]
	public void FontSize_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-size", "150%" ) );
		Assert.IsTrue( s.FontSize.HasValue );
		Assert.AreEqual( 150, s.FontSize.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.FontSize.Value.Unit );
	}

	[TestMethod]
	public void FontSize_Em()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-size", "1.5em" ) );
		Assert.IsTrue( s.FontSize.HasValue );
		Assert.AreEqual( 1.5f, s.FontSize.Value.Value );
		Assert.AreEqual( LengthUnit.Em, s.FontSize.Value.Unit );
	}

	[TestMethod]
	public void FontSize_Rem()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-size", "2rem" ) );
		Assert.IsTrue( s.FontSize.HasValue );
		Assert.AreEqual( 2, s.FontSize.Value.Value );
		Assert.AreEqual( LengthUnit.RootEm, s.FontSize.Value.Unit );
	}

	[TestMethod]
	public void FontSize_Unitless_TreatedAsPixels()
	{
		// s&box treats a bare number as pixels.
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-size", "16" ) );
		Assert.IsTrue( s.FontSize.HasValue );
		Assert.AreEqual( 16, s.FontSize.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FontSize.Value.Unit );
	}

	[TestMethod]
	public void FontSize_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "font-size", "bullshit" ) );
		Assert.IsFalse( s.FontSize.HasValue );
	}

	// ---------------------------------------------------------------------
	// color / font-color -> FontColor (Color?)  ('color' is an alias)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void FontColor_NamedRed_ViaFontColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-color", "red" ) );
		Assert.IsTrue( s.FontColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.FontColor.Value );
	}

	[TestMethod]
	public void FontColor_NamedRed_ViaColorAlias()
	{
		// 'color' is an alias for 'font-color'
		var s = new Styles();
		Assert.IsTrue( s.Set( "color", "red" ) );
		Assert.IsTrue( s.FontColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.FontColor.Value );
	}

	[TestMethod]
	public void FontColor_Hex()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "color", "#f0f0" ) );
		Assert.IsTrue( s.FontColor.HasValue );
		Assert.AreEqual( 1.0f, s.FontColor.Value.r );
		Assert.AreEqual( 0.0f, s.FontColor.Value.g );
		Assert.AreEqual( 1.0f, s.FontColor.Value.b );
		Assert.AreEqual( 0.0f, s.FontColor.Value.a );
	}

	[TestMethod]
	public void FontColor_Rgba()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "color", "rgba( 0, 0, 0, 0.5 )" ) );
		Assert.IsTrue( s.FontColor.HasValue );
		Assert.AreEqual( new Color( 0, 0, 0, 0.5f ), s.FontColor.Value );
	}

	[TestMethod]
	public void FontColor_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "color", "notacolor" ) );
		Assert.IsFalse( s.FontColor.HasValue );
	}

	// ---------------------------------------------------------------------
	// font-weight -> FontWeight (int?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void FontWeight_Numeric()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "500" ) );
		Assert.IsTrue( s.FontWeight.HasValue );
		Assert.AreEqual( 500, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordNormal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "normal" ) );
		Assert.AreEqual( 400, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordBold()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "bold" ) );
		Assert.AreEqual( 700, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordThin()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "thin" ) );
		Assert.AreEqual( 100, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordExtraLight()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "extralight" ) );
		Assert.AreEqual( 200, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordLight()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "light" ) );
		Assert.AreEqual( 300, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordMedium()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "medium" ) );
		Assert.AreEqual( 500, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordSemiBold()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "semibold" ) );
		Assert.AreEqual( 600, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordExtraBold()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "extrabold" ) );
		Assert.AreEqual( 800, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordBlack()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "black" ) );
		Assert.AreEqual( 900, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_KeywordExtraBlack()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-weight", "extrablack" ) );
		Assert.AreEqual( 950, s.FontWeight.Value );
	}

	[TestMethod]
	public void FontWeight_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "font-weight", "extra-fat" ) );
		Assert.IsFalse( s.FontWeight.HasValue );
	}

	// ---------------------------------------------------------------------
	// font-family -> FontFamily (string)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void FontFamily_Single()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-family", "Arial" ) );
		Assert.AreEqual( "Arial", s.FontFamily );
	}

	[TestMethod]
	public void FontFamily_Quoted()
	{
		// TrimQuoted strips the surrounding quotes
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-family", "\"Segoe UI\"" ) );
		Assert.AreEqual( "Segoe UI", s.FontFamily );
	}

	/// <summary>
	/// A font stack resolves to the first family (we don't resolve fallbacks).
	/// </summary>
	[TestMethod]
	public void FontFamily_StackResolvesFirst()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-family", "\"Segoe UI\", Arial, sans-serif" ) );
		Assert.AreEqual( "Segoe UI", s.FontFamily );
	}

	// ---------------------------------------------------------------------
	// font-style -> FontStyle (flags: Italic / Oblique)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void FontStyle_Italic()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-style", "italic" ) );
		Assert.AreEqual( FontStyle.Italic, s.FontStyle );
	}

	[TestMethod]
	public void FontStyle_Oblique()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-style", "oblique" ) );
		Assert.AreEqual( FontStyle.Oblique, s.FontStyle );
	}

	[TestMethod]
	public void FontStyle_NormalIsNone()
	{
		// SetFontStyle always succeeds; 'normal' contains neither keyword => None
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-style", "normal" ) );
		Assert.AreEqual( FontStyle.None, s.FontStyle );
	}

	// ---------------------------------------------------------------------
	// font-variant-numeric -> FontVariantNumeric
	// ---------------------------------------------------------------------

	[TestMethod]
	public void FontVariantNumeric_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-variant-numeric", "normal" ) );
		Assert.AreEqual( FontVariantNumeric.Normal, s.FontVariantNumeric );
	}

	[TestMethod]
	public void FontVariantNumeric_TabularNums()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-variant-numeric", "tabular-nums" ) );
		Assert.AreEqual( FontVariantNumeric.TabularNums, s.FontVariantNumeric );
	}

	[TestMethod]
	public void FontVariantNumeric_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "font-variant-numeric", "oldstyle-nums" ) );
	}

	// ---------------------------------------------------------------------
	// font-smooth -> FontSmooth (Auto / Never / Always)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void FontSmooth_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-smooth", "auto" ) );
		Assert.AreEqual( FontSmooth.Auto, s.FontSmooth );
	}

	[TestMethod]
	public void FontSmooth_Never()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-smooth", "never" ) );
		Assert.AreEqual( FontSmooth.Never, s.FontSmooth );
	}

	[TestMethod]
	public void FontSmooth_Always()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-smooth", "always" ) );
		Assert.AreEqual( FontSmooth.Always, s.FontSmooth );
	}

	[TestMethod]
	public void FontSmooth_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "font-smooth", "subpixel-antialiased" ) );
	}

	// ---------------------------------------------------------------------
	// line-height -> LineHeight (Length?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void LineHeight_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "line-height", "24px" ) );
		Assert.IsTrue( s.LineHeight.HasValue );
		Assert.AreEqual( 24, s.LineHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.LineHeight.Value.Unit );
	}

	[TestMethod]
	public void LineHeight_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "line-height", "120%" ) );
		Assert.IsTrue( s.LineHeight.HasValue );
		Assert.AreEqual( 120, s.LineHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.LineHeight.Value.Unit );
	}

	/// <summary>
	/// A unitless line-height is a multiple of the font size (1.5 == 150%).
	/// </summary>
	[TestMethod]
	public void LineHeight_Unitless_IsMultiplier()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "line-height", "1.5" ) );
		Assert.IsTrue( s.LineHeight.HasValue );
		Assert.AreEqual( 150, s.LineHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.LineHeight.Value.Unit );
	}

	/// <summary>
	/// 'line-height: normal' uses the default line height (100% of the font size).
	/// </summary>
	[TestMethod]
	public void LineHeight_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "line-height", "normal" ) );
		Assert.AreEqual( 100, s.LineHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.LineHeight.Value.Unit );
	}

	// ---------------------------------------------------------------------
	// letter-spacing -> LetterSpacing (Length?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void LetterSpacing_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "letter-spacing", "2px" ) );
		Assert.IsTrue( s.LetterSpacing.HasValue );
		Assert.AreEqual( 2, s.LetterSpacing.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.LetterSpacing.Value.Unit );
	}

	[TestMethod]
	public void LetterSpacing_Em()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "letter-spacing", "0.1em" ) );
		Assert.IsTrue( s.LetterSpacing.HasValue );
		Assert.AreEqual( 0.1f, s.LetterSpacing.Value.Value );
		Assert.AreEqual( LengthUnit.Em, s.LetterSpacing.Value.Unit );
	}

	[TestMethod]
	public void LetterSpacing_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "letter-spacing", "wide" ) );
		Assert.IsFalse( s.LetterSpacing.HasValue );
	}

	// ---------------------------------------------------------------------
	// word-spacing -> WordSpacing (Length?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void WordSpacing_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "word-spacing", "5px" ) );
		Assert.IsTrue( s.WordSpacing.HasValue );
		Assert.AreEqual( 5, s.WordSpacing.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.WordSpacing.Value.Unit );
	}

	[TestMethod]
	public void WordSpacing_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "word-spacing", "50%" ) );
		Assert.IsTrue( s.WordSpacing.HasValue );
		Assert.AreEqual( 50, s.WordSpacing.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.WordSpacing.Value.Unit );
	}

	[TestMethod]
	public void WordSpacing_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "word-spacing", "spaced" ) );
		Assert.IsFalse( s.WordSpacing.HasValue );
	}

	// ---------------------------------------------------------------------
	// word-break -> WordBreak (Normal / BreakAll)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void WordBreak_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "word-break", "normal" ) );
		Assert.AreEqual( WordBreak.Normal, s.WordBreak );
	}

	[TestMethod]
	public void WordBreak_BreakAll()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "word-break", "break-all" ) );
		Assert.AreEqual( WordBreak.BreakAll, s.WordBreak );
	}

	[TestMethod]
	public void WordBreak_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "word-break", "nonsense" ) );
	}

	/// <summary>
	/// break-word == normal here: word breaking already character-breaks an over-long word on overflow.
	/// </summary>
	[TestMethod]
	public void WordBreak_BreakWord()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "word-break", "break-word" ) );
		Assert.AreEqual( WordBreak.Normal, s.WordBreak );
	}

	/// <summary>
	/// keep-all is accepted but currently does nothing (no suppress-breaking mode); behaves like normal.
	/// </summary>
	[TestMethod]
	public void WordBreak_KeepAll()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "word-break", "keep-all" ) );
		Assert.AreEqual( WordBreak.Normal, s.WordBreak );
	}

	// ---------------------------------------------------------------------
	// white-space -> WhiteSpace (Normal / NoWrap / PreLine / Pre)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void WhiteSpace_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "white-space", "normal" ) );
		Assert.AreEqual( WhiteSpace.Normal, s.WhiteSpace );
	}

	[TestMethod]
	public void WhiteSpace_NoWrap()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "white-space", "nowrap" ) );
		Assert.AreEqual( WhiteSpace.NoWrap, s.WhiteSpace );
	}

	[TestMethod]
	public void WhiteSpace_PreLine()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "white-space", "pre-line" ) );
		Assert.AreEqual( WhiteSpace.PreLine, s.WhiteSpace );
	}

	[TestMethod]
	public void WhiteSpace_Pre()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "white-space", "pre" ) );
		Assert.AreEqual( WhiteSpace.Pre, s.WhiteSpace );
	}

	[TestMethod]
	public void WhiteSpace_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "white-space", "collapse" ) );
	}

	/// <summary>
	/// pre-wrap preserves whitespace and wraps.
	/// </summary>
	[TestMethod]
	public void WhiteSpace_PreWrap()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "white-space", "pre-wrap" ) );
		Assert.AreEqual( WhiteSpace.PreWrap, s.WhiteSpace );
	}

	[TestMethod]
	public void WhiteSpace_BreakSpaces()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "white-space", "break-spaces" ) );
		Assert.AreEqual( WhiteSpace.BreakSpaces, s.WhiteSpace );
	}

	// ---------------------------------------------------------------------
	// text-align -> TextAlign (Left / Right / Center)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextAlign_Left()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-align", "left" ) );
		Assert.AreEqual( TextAlign.Left, s.TextAlign );
	}

	[TestMethod]
	public void TextAlign_Right()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-align", "right" ) );
		Assert.AreEqual( TextAlign.Right, s.TextAlign );
	}

	[TestMethod]
	public void TextAlign_Center()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-align", "center" ) );
		Assert.AreEqual( TextAlign.Center, s.TextAlign );
	}

	[TestMethod]
	public void TextAlign_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-align", "middle" ) );
	}

	/// <summary>
	/// 'justify' is accepted and maps to the Justify alignment.
	/// </summary>
	[TestMethod]
	public void TextAlign_Justify()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-align", "justify" ) );
		Assert.AreEqual( Sandbox.UI.TextAlign.Justify, s.TextAlign );
	}

	/// <summary>
	/// start/end map to left/right (logical direction not tracked).
	/// </summary>
	[TestMethod]
	public void TextAlign_Start()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-align", "start" ) );
		Assert.AreEqual( TextAlign.Left, s.TextAlign );
	}

	[TestMethod]
	public void TextAlign_End()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-align", "end" ) );
		Assert.AreEqual( TextAlign.Right, s.TextAlign );
	}

	// ---------------------------------------------------------------------
	// text-overflow -> TextOverflow (Ellipsis / Clip)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextOverflow_Ellipsis()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-overflow", "ellipsis" ) );
		Assert.AreEqual( TextOverflow.Ellipsis, s.TextOverflow );
	}

	[TestMethod]
	public void TextOverflow_Clip()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-overflow", "clip" ) );
		Assert.AreEqual( TextOverflow.Clip, s.TextOverflow );
	}

	[TestMethod]
	public void TextOverflow_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-overflow", "fade" ) );
	}

	// ---------------------------------------------------------------------
	// text-transform -> TextTransform (Capitalize / Uppercase / Lowercase / None)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextTransform_Capitalize()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-transform", "capitalize" ) );
		Assert.AreEqual( TextTransform.Capitalize, s.TextTransform );
	}

	[TestMethod]
	public void TextTransform_Uppercase()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-transform", "uppercase" ) );
		Assert.AreEqual( TextTransform.Uppercase, s.TextTransform );
	}

	[TestMethod]
	public void TextTransform_Lowercase()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-transform", "lowercase" ) );
		Assert.AreEqual( TextTransform.Lowercase, s.TextTransform );
	}

	[TestMethod]
	public void TextTransform_None()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-transform", "none" ) );
		Assert.AreEqual( TextTransform.None, s.TextTransform );
	}

	[TestMethod]
	public void TextTransform_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-transform", "smallcaps" ) );
	}

	// ---------------------------------------------------------------------
	// text-decoration (shorthand) -> TextDecorationLine (+color/style/thickness)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextDecoration_LineOnly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration", "underline" ) );
		Assert.AreEqual( TextDecoration.Underline, s.TextDecorationLine );
	}

	[TestMethod]
	public void TextDecoration_MultipleLines()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration", "underline overline" ) );
		Assert.AreEqual( TextDecoration.Underline | TextDecoration.Overline, s.TextDecorationLine );
	}

	[TestMethod]
	public void TextDecoration_LineColorStyle()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration", "underline red wavy" ) );
		Assert.AreEqual( TextDecoration.Underline, s.TextDecorationLine );
		Assert.IsTrue( s.TextDecorationColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.TextDecorationColor.Value );
		Assert.AreEqual( TextDecorationStyle.Wavy, s.TextDecorationStyle );
	}

	[TestMethod]
	public void TextDecoration_WithThickness()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration", "line-through 3px" ) );
		Assert.AreEqual( TextDecoration.LineThrough, s.TextDecorationLine );
		Assert.IsTrue( s.TextDecorationThickness.HasValue );
		Assert.AreEqual( 3, s.TextDecorationThickness.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextDecorationThickness.Value.Unit );
	}

	[TestMethod]
	public void TextDecoration_Invalid()
	{
		// An unrecognised token that is neither a line/color/style/length fails.
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-decoration", "underline blink" ) );
	}

	// ---------------------------------------------------------------------
	// text-decoration-line -> TextDecorationLine
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextDecorationLine_Underline()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-line", "underline" ) );
		Assert.AreEqual( TextDecoration.Underline, s.TextDecorationLine );
	}

	[TestMethod]
	public void TextDecorationLine_LineThrough()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-line", "line-through" ) );
		Assert.AreEqual( TextDecoration.LineThrough, s.TextDecorationLine );
	}

	[TestMethod]
	public void TextDecorationLine_Overline()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-line", "overline" ) );
		Assert.AreEqual( TextDecoration.Overline, s.TextDecorationLine );
	}

	[TestMethod]
	public void TextDecorationLine_None()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-line", "none" ) );
		Assert.AreEqual( TextDecoration.None, s.TextDecorationLine );
	}

	[TestMethod]
	public void TextDecorationLine_Combined()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-line", "underline line-through" ) );
		Assert.AreEqual( TextDecoration.Underline | TextDecoration.LineThrough, s.TextDecorationLine );
	}

	// ---------------------------------------------------------------------
	// text-decoration-color -> TextDecorationColor (Color?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextDecorationColor_Named()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-color", "red" ) );
		Assert.IsTrue( s.TextDecorationColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.TextDecorationColor.Value );
	}

	[TestMethod]
	public void TextDecorationColor_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-decoration-color", "notacolor" ) );
		Assert.IsFalse( s.TextDecorationColor.HasValue );
	}

	// ---------------------------------------------------------------------
	// text-decoration-style -> TextDecorationStyle
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextDecorationStyle_Solid()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-style", "solid" ) );
		Assert.AreEqual( TextDecorationStyle.Solid, s.TextDecorationStyle );
	}

	[TestMethod]
	public void TextDecorationStyle_Double()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-style", "double" ) );
		Assert.AreEqual( TextDecorationStyle.Double, s.TextDecorationStyle );
	}

	[TestMethod]
	public void TextDecorationStyle_Dotted()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-style", "dotted" ) );
		Assert.AreEqual( TextDecorationStyle.Dotted, s.TextDecorationStyle );
	}

	[TestMethod]
	public void TextDecorationStyle_Dashed()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-style", "dashed" ) );
		Assert.AreEqual( TextDecorationStyle.Dashed, s.TextDecorationStyle );
	}

	[TestMethod]
	public void TextDecorationStyle_Wavy()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-style", "wavy" ) );
		Assert.AreEqual( TextDecorationStyle.Wavy, s.TextDecorationStyle );
	}

	[TestMethod]
	public void TextDecorationStyle_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-decoration-style", "zigzag" ) );
	}

	// ---------------------------------------------------------------------
	// text-decoration-thickness -> TextDecorationThickness (Length?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextDecorationThickness_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-thickness", "4px" ) );
		Assert.IsTrue( s.TextDecorationThickness.HasValue );
		Assert.AreEqual( 4, s.TextDecorationThickness.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextDecorationThickness.Value.Unit );
	}

	[TestMethod]
	public void TextDecorationThickness_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-decoration-thickness", "thick" ) );
		Assert.IsFalse( s.TextDecorationThickness.HasValue );
	}

	// ---------------------------------------------------------------------
	// text-decoration-skip-ink -> TextDecorationSkipInk (All / None)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextDecorationSkipInk_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-skip-ink", "auto" ) );
		Assert.AreEqual( TextSkipInk.All, s.TextDecorationSkipInk );
	}

	[TestMethod]
	public void TextDecorationSkipInk_All()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-skip-ink", "all" ) );
		Assert.AreEqual( TextSkipInk.All, s.TextDecorationSkipInk );
	}

	[TestMethod]
	public void TextDecorationSkipInk_None()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-decoration-skip-ink", "none" ) );
		Assert.AreEqual( TextSkipInk.None, s.TextDecorationSkipInk );
	}

	[TestMethod]
	public void TextDecorationSkipInk_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-decoration-skip-ink", "maybe" ) );
	}

	// ---------------------------------------------------------------------
	// text-underline-offset / text-overline-offset / text-line-through-offset
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextUnderlineOffset_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-underline-offset", "2px" ) );
		Assert.IsTrue( s.TextUnderlineOffset.HasValue );
		Assert.AreEqual( 2, s.TextUnderlineOffset.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextUnderlineOffset.Value.Unit );
	}

	[TestMethod]
	public void TextUnderlineOffset_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-underline-offset", "low" ) );
		Assert.IsFalse( s.TextUnderlineOffset.HasValue );
	}

	[TestMethod]
	public void TextOverlineOffset_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-overline-offset", "3px" ) );
		Assert.IsTrue( s.TextOverlineOffset.HasValue );
		Assert.AreEqual( 3, s.TextOverlineOffset.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextOverlineOffset.Value.Unit );
	}

	[TestMethod]
	public void TextLineThroughOffset_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-line-through-offset", "1px" ) );
		Assert.IsTrue( s.TextLineThroughOffset.HasValue );
		Assert.AreEqual( 1, s.TextLineThroughOffset.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextLineThroughOffset.Value.Unit );
	}

	// ---------------------------------------------------------------------
	// text-stroke (shorthand) -> TextStrokeWidth + TextStrokeColor
	// ---------------------------------------------------------------------

	[TestMethod]
	public void TextStroke_Shorthand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-stroke", "2px red" ) );
		Assert.IsTrue( s.TextStrokeWidth.HasValue );
		Assert.AreEqual( 2, s.TextStrokeWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextStrokeWidth.Value.Unit );
		Assert.IsTrue( s.TextStrokeColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.TextStrokeColor.Value );
	}

	[TestMethod]
	public void TextStroke_MissingColor()
	{
		// SetTextStroke requires both a width and a color.
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-stroke", "2px" ) );
	}

	[TestMethod]
	public void TextStrokeWidth_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-stroke-width", "5px" ) );
		Assert.IsTrue( s.TextStrokeWidth.HasValue );
		Assert.AreEqual( 5, s.TextStrokeWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.TextStrokeWidth.Value.Unit );
	}

	[TestMethod]
	public void TextStrokeColor_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "text-stroke-color", "red" ) );
		Assert.IsTrue( s.TextStrokeColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.TextStrokeColor.Value );
	}

	[TestMethod]
	public void TextStrokeColor_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "text-stroke-color", "notacolor" ) );
		Assert.IsFalse( s.TextStrokeColor.HasValue );
	}

	// ---------------------------------------------------------------------
	// caret-color -> CaretColor (Color?)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void CaretColor_Named()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "caret-color", "red" ) );
		Assert.IsTrue( s.CaretColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.CaretColor.Value );
	}

	[TestMethod]
	public void CaretColor_Rgba()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "caret-color", "rgba( 0, 0, 0, 0.25 )" ) );
		Assert.IsTrue( s.CaretColor.HasValue );
		Assert.AreEqual( new Color( 0, 0, 0, 0.25f ), s.CaretColor.Value );
	}

	[TestMethod]
	public void CaretColor_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "caret-color", "notacolor" ) );
		Assert.IsFalse( s.CaretColor.HasValue );
	}

	// ---------------------------------------------------------------------
	// content -> Content (string)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void Content_Quoted()
	{
		// TrimQuoted strips surrounding quotes.
		var s = new Styles();
		Assert.IsTrue( s.Set( "content", "\"hello\"" ) );
		Assert.AreEqual( "hello", s.Content );
	}

	[TestMethod]
	public void Content_Empty()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "content", "\"\"" ) );
		Assert.AreEqual( "", s.Content );
	}

	[TestMethod]
	public void Content_Unquoted()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "content", "foo" ) );
		Assert.AreEqual( "foo", s.Content );
	}

	// ---------------------------------------------------------------------
	// cursor -> Cursor (string)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void Cursor_Pointer()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "cursor", "pointer" ) );
		Assert.AreEqual( "pointer", s.Cursor );
	}

	[TestMethod]
	public void Cursor_None()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "cursor", "none" ) );
		Assert.AreEqual( "none", s.Cursor );
	}

	// ---------------------------------------------------------------------
	// mix-blend-mode -> MixBlendMode (string)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void MixBlendMode_Multiply()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mix-blend-mode", "multiply" ) );
		Assert.AreEqual( "multiply", s.MixBlendMode );
	}

	[TestMethod]
	public void MixBlendMode_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "mix-blend-mode", "normal" ) );
		Assert.AreEqual( "normal", s.MixBlendMode );
	}

	/// <summary>
	/// The font shorthand dispatches to style/weight/size/line-height/family longhands.
	/// </summary>
	[TestMethod]
	public void FontShorthand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font", "italic bold 16px/1.5 Arial" ) );

		Assert.IsTrue( s.FontStyle.Value.HasFlag( Sandbox.UI.FontStyle.Italic ) );
		Assert.AreEqual( 700, s.FontWeight.Value );
		Assert.AreEqual( 16, s.FontSize.Value.Value );
		Assert.AreEqual( 150, s.LineHeight.Value.Value ); // 1.5 -> 150%
		Assert.AreEqual( "Arial", s.FontFamily );
	}

	/// <summary>
	/// The minimal font shorthand (just size and family) works.
	/// </summary>
	[TestMethod]
	public void FontShorthandMinimal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font", "14px Tahoma" ) );
		Assert.AreEqual( 14, s.FontSize.Value.Value );
		Assert.AreEqual( "Tahoma", s.FontFamily );
	}

	/// <summary>
	/// font-size accepts the CSS absolute-size keywords as well as lengths.
	/// </summary>
	[TestMethod]
	public void FontSizeKeywords()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-size", "large" ) );
		Assert.AreEqual( 18, s.FontSize.Value.Value );

		Assert.IsTrue( s.Set( "font-size", "xx-large" ) );
		Assert.AreEqual( 32, s.FontSize.Value.Value );

		Assert.IsTrue( s.Set( "font-size", "13px" ) );
		Assert.AreEqual( 13, s.FontSize.Value.Value );
	}

	/// <summary>
	/// letter-spacing and word-spacing accept 'normal' (no extra spacing).
	/// </summary>
	[TestMethod]
	public void LetterAndWordSpacingNormal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "letter-spacing", "normal" ) );
		Assert.AreEqual( 0, s.LetterSpacing.Value.Value );

		Assert.IsTrue( s.Set( "word-spacing", "normal" ) );
		Assert.AreEqual( 0, s.WordSpacing.Value.Value );

		Assert.IsTrue( s.Set( "letter-spacing", "2px" ) );
		Assert.AreEqual( 2, s.LetterSpacing.Value.Value );
	}

	/// <summary>
	/// font-smooth: none maps to no smoothing.
	/// </summary>
	[TestMethod]
	public void FontSmoothNone()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-smooth", "none" ) );
		Assert.AreEqual( Sandbox.UI.FontSmooth.Never, s.FontSmooth.Value );
	}

	/// <summary>
	/// Generic font families map to a concrete font; real family names pass through unchanged.
	/// </summary>
	[TestMethod]
	public void FontFamilyGenerics()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "font-family", "sans-serif" ) );
		Assert.AreEqual( "Arial", s.FontFamily );

		Assert.IsTrue( s.Set( "font-family", "serif" ) );
		Assert.AreEqual( "Times New Roman", s.FontFamily );

		Assert.IsTrue( s.Set( "font-family", "monospace" ) );
		Assert.AreEqual( "Consolas", s.FontFamily );

		// A real family name is left alone.
		Assert.IsTrue( s.Set( "font-family", "Poppins" ) );
		Assert.AreEqual( "Poppins", s.FontFamily );

		// We still take the first family from a stack (no fallback resolution).
		Assert.IsTrue( s.Set( "font-family", "Inter, sans-serif" ) );
		Assert.AreEqual( "Inter", s.FontFamily );
	}
}
