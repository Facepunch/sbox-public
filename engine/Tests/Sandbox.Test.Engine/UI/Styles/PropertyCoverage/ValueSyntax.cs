using Sandbox.UI;
namespace UITests.PropertyCoverage;

/// <summary>
/// Cross-cutting CSS *value syntax* coverage: color forms, length forms, CSS-wide
/// keywords (inherit/initial/unset) and !important. Exercised mostly through
/// 'background-color', 'color' (-> FontColor) and 'width'.
/// </summary>
[TestClass]
public class ValueSyntaxTest
{
	// ----------------------------------------------------------------------------
	// COLOR - hex forms
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Color_Hex_Rgb()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "#f00" );
		Assert.IsTrue( ok );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Hex_Rgba()
	{
		// #RGBA -> each nibble doubled. #0f08 -> r=00 g=ff b=00 a=88
		var s = new Styles();
		bool ok = s.Set( "background-color", "#0f08" );
		Assert.IsTrue( ok );
		Assert.IsTrue( s.BackgroundColor.HasValue );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.r );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.g );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.b );
		Assert.AreEqual( 0x88 / 255.0f, s.BackgroundColor.Value.a );
	}

	[TestMethod]
	public void Color_Hex_RrGgBb()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "#00ff00" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Hex_RrGgBbAa()
	{
		// #RRGGBBAA - alpha 0x80
		var s = new Styles();
		bool ok = s.Set( "background-color", "#ff000080" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.r );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.g );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.b );
		Assert.AreEqual( 0x80 / 255.0f, s.BackgroundColor.Value.a );
	}

	// ----------------------------------------------------------------------------
	// COLOR - rgb()/rgba() forms
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Color_Rgb_LegacyComma()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "rgb(255,0,0)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Rgb_ModernSpace()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "rgb(255 0 0)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Rgb_ModernSpaceAlpha()
	{
		// rgb( r g b / a )
		var s = new Styles();
		bool ok = s.Set( "background-color", "rgb(255 0 0 / 0.5)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.r );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.g );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.b );
		Assert.AreEqual( 0.5f, s.BackgroundColor.Value.a );
	}

	[TestMethod]
	public void Color_Rgba_LegacyComma()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "rgba(0,0,0,0.1)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Rgb_Percentages()
	{
		// rgb(100% 0% 0%) -> pure red
		var s = new Styles();
		bool ok = s.Set( "background-color", "rgb(100% 0% 0%)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.r );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.g );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.b );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.a );
	}

	// ----------------------------------------------------------------------------
	// COLOR - hsl()/hsla() forms
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Color_Hsl_LegacyComma()
	{
		// hsl(0,100%,50%) -> red
		var s = new Styles();
		bool ok = s.Set( "background-color", "hsl(0,100%,50%)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.r, 0.001f );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.g, 0.001f );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.b, 0.001f );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.a, 0.001f );
	}

	[TestMethod]
	public void Color_Hsl_ModernSpace()
	{
		// hsl(0 100% 50%) -> red
		var s = new Styles();
		bool ok = s.Set( "background-color", "hsl(0 100% 50%)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.r, 0.001f );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.g, 0.001f );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.b, 0.001f );
	}

	[TestMethod]
	public void Color_Hsla_Alpha()
	{
		// hsla(0,100%,50%,0.5) -> red with half alpha
		var s = new Styles();
		bool ok = s.Set( "background-color", "hsla(0,100%,50%,0.5)" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.r, 0.001f );
		Assert.AreEqual( 0.5f, s.BackgroundColor.Value.a, 0.001f );
	}

	// ----------------------------------------------------------------------------
	// COLOR - named colors
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Color_Named_Red()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "red" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Named_White()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "white" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 1, 1, 1, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Named_Black()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "black" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Named_RebeccaPurple()
	{
		// rebeccapurple = #663399
		var s = new Styles();
		bool ok = s.Set( "background-color", "rebeccapurple" );
		Assert.IsTrue( ok );
		Assert.AreEqual( Color.FromBytes( 0x66, 0x33, 0x99 ), s.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Named_Transparent()
	{
		// s&box maps "transparent" to "#AAAAAA00" (grey rgb, zero alpha).
		// The defining feature is alpha == 0.
		var s = new Styles();
		bool ok = s.Set( "background-color", "transparent" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.a );
		Assert.AreEqual( 0xAA / 255.0f, s.BackgroundColor.Value.r );
	}

	// ----------------------------------------------------------------------------
	// COLOR - via 'color' shorthand (-> FontColor)
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Color_ColorAliasesToFontColor()
	{
		var s = new Styles();
		bool ok = s.Set( "color", "red" );
		Assert.IsTrue( ok );
		Assert.IsTrue( s.FontColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.FontColor.Value );
	}

	[TestMethod]
	public void Color_FontColorHex()
	{
		var s = new Styles();
		bool ok = s.Set( "color", "#00ff00ff" );
		Assert.IsTrue( ok );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.FontColor.Value );
	}

	// ----------------------------------------------------------------------------
	// COLOR - invalid
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Color_Invalid()
	{
		var s = new Styles();
		bool ok = s.Set( "background-color", "notacolor" );
		Assert.IsFalse( ok );
		Assert.IsFalse( s.BackgroundColor.HasValue );
	}

	// ----------------------------------------------------------------------------
	// LENGTH - units (via 'width')
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Length_Pixels()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "140px" );
		Assert.IsTrue( ok );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 140, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Percentage()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "50%" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 50, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Em()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "2em" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 2, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Em, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Rem()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "3rem" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 3, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.RootEm, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_ViewWidth()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "80vw" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 80, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewWidth, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_ViewHeight()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "75vh" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 75, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_ViewMin()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "10vmin" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 10, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewMin, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_ViewMax()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "20vmax" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 20, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewMax, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_UnitlessNumber()
	{
		// A bare number is treated as pixels.
		var s = new Styles();
		bool ok = s.Set( "width", "100" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 100, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Zero()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "0" );
		Assert.IsTrue( ok );
		Assert.AreEqual( 0, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Auto()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "auto" );
		Assert.IsTrue( ok );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Calc()
	{
		// calc() is stored as a deferred expression.
		var s = new Styles();
		bool ok = s.Set( "width", "calc(50% - 10px)" );
		Assert.IsTrue( ok );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( LengthUnit.Expression, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_NegativePixels()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "-25px" );
		Assert.IsTrue( ok );
		Assert.AreEqual( -25, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Invalid()
	{
		var s = new Styles();
		bool ok = s.Set( "width", "blah" );
		Assert.IsFalse( ok );
		Assert.IsFalse( s.Width.HasValue );
	}

	// ----------------------------------------------------------------------------
	// COLOR - currentColor and extended color spaces (oklch/hwb/lab)
	// ----------------------------------------------------------------------------

	/// <summary>
	/// 'currentColor' is valid CSS on any colour property (it resolves to the element's 'color'), so
	/// Set should accept it.
	/// </summary>
	[TestMethod]
	public void Color_CurrentColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "color", "currentColor" ) );
		Assert.IsTrue( s.Set( "background-color", "currentColor" ) );
		Assert.IsTrue( s.Set( "border-color", "currentcolor" ) ); // case-insensitive
	}

	/// <summary>
	/// 'currentColor' resolves to the element's own computed font colour - here border-color picks up
	/// the red set by 'color' on the same element.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void CurrentColor_ResolvesToFontColor()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( ".child { color: red; border-color: currentColor; }" );

		panel.Layout();

		Assert.AreEqual( Color.Red, child.ComputedStyle.BorderLeftColor.Value );
		Assert.AreEqual( Color.Red, child.ComputedStyle.BorderTopColor.Value );
		Assert.AreEqual( Color.Red, child.ComputedStyle.BorderRightColor.Value );
		Assert.AreEqual( Color.Red, child.ComputedStyle.BorderBottomColor.Value );
	}

	/// <summary>
	/// 'currentColor' nested inside the 'border' shorthand resolves too, since it's parsed through the
	/// shared colour parser.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void CurrentColor_InBorderShorthand()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( ".child { color: green; border: 2px solid currentColor; }" );

		panel.Layout();

		// The border colour should match the element's computed colour, whatever 'green' parses to.
		Assert.AreEqual( child.ComputedStyle.FontColor.Value, child.ComputedStyle.BorderLeftColor.Value );
	}

	/// <summary>
	/// 'currentColor' uses the *computed* colour, so it picks up a colour inherited from an ancestor
	/// when the element doesn't set its own.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void CurrentColor_UsesInheritedColor()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( "RootPanel { color: blue; } .child { background-color: currentColor; }" );

		panel.Layout();

		Assert.AreEqual( Color.Blue, child.ComputedStyle.BackgroundColor.Value );
	}

	[TestMethod]
	public void Color_Oklch()
	{
		// Anchors: black, white, and the OKLCH coordinates of pure sRGB red.
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), Color.Parse( "oklch(0 0 0)" ).Value );
		Assert.AreEqual( new Color( 1, 1, 1, 1 ), Color.Parse( "oklch(1 0 0)" ).Value );

		var red = Color.Parse( "oklch(0.627955 0.257683 29.2339)" ).Value;
		Assert.AreEqual( 1.0f, red.r, 0.02f );
		Assert.AreEqual( 0.0f, red.g, 0.02f );
		Assert.AreEqual( 0.0f, red.b, 0.02f );

		// The gap input: a teal (red is out of gamut -> clamps to 0).
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-color", "oklch(0.7 0.15 200)" ) );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.r, 0.02f );
		Assert.IsTrue( s.BackgroundColor.Value.b > 0.5f );
	}

	[TestMethod]
	public void Color_Hwb()
	{
		// Pure-hue primaries, white/black, and a mid grey - all exact (hwb is in sRGB space directly).
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), Color.Parse( "hwb(0 0% 0%)" ).Value );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), Color.Parse( "hwb(120 0% 0%)" ).Value );
		Assert.AreEqual( new Color( 0, 0, 1, 1 ), Color.Parse( "hwb(240 0% 0%)" ).Value );
		Assert.AreEqual( new Color( 1, 1, 1, 1 ), Color.Parse( "hwb(0 100% 0%)" ).Value );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), Color.Parse( "hwb(0 0% 100%)" ).Value );

		var grey = Color.Parse( "hwb(0 50% 50%)" ).Value;
		Assert.AreEqual( 0.5f, grey.r, 0.01f );
		Assert.AreEqual( 0.5f, grey.g, 0.01f );
		Assert.AreEqual( 0.5f, grey.b, 0.01f );

		// The gap input: cyan-ish (r=0, full blue).
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-color", "hwb(194 0% 0%)" ) );
		Assert.AreEqual( 0.0f, s.BackgroundColor.Value.r, 0.01f );
		Assert.AreEqual( 1.0f, s.BackgroundColor.Value.b, 0.01f );
	}

	[TestMethod]
	public void Color_Lab()
	{
		// Anchors: black, white, and the (D65) Lab coordinates of pure sRGB red.
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), Color.Parse( "lab(0 0 0)" ).Value );
		Assert.AreEqual( new Color( 1, 1, 1, 1 ), Color.Parse( "lab(100 0 0)" ).Value );

		var red = Color.Parse( "lab(53.2408 80.0925 67.2032)" ).Value;
		Assert.AreEqual( 1.0f, red.r, 0.02f );
		Assert.AreEqual( 0.0f, red.g, 0.02f );
		Assert.AreEqual( 0.0f, red.b, 0.02f );

		// The gap input: a reddish-brown.
		var s = new Styles();
		Assert.IsTrue( s.Set( "background-color", "lab(50% 40 59)" ) );
		Assert.IsTrue( s.BackgroundColor.Value.r > 0.5f );
		Assert.IsTrue( s.BackgroundColor.Value.b < 0.1f );
	}

	// ----------------------------------------------------------------------------
	// LENGTH - math functions (min/max/clamp)
	// ----------------------------------------------------------------------------

	/// <summary>
	/// min() math function resolves to the smallest argument against the reference size.
	/// </summary>
	[TestMethod]
	public void Length_Min()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "min(100%,600px)" ) );
		Assert.AreEqual( 600, s.Width.Value.GetPixels( 1000 ) );
	}

	/// <summary>
	/// clamp(min, val, max) constrains val to the [min, max] range.
	/// </summary>
	[TestMethod]
	public void Length_Clamp()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "clamp(10px,50%,300px)" ) );
		Assert.AreEqual( 300, s.Width.Value.GetPixels( 1000 ) );
	}

	/// <summary>
	/// max() math function resolves to the largest argument.
	/// </summary>
	[TestMethod]
	public void Length_Max()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "max(10px,5%)" ) );
		Assert.AreEqual( 50, s.Width.Value.GetPixels( 1000 ) );
	}

	// ----------------------------------------------------------------------------
	// LENGTH - dynamic/small/large viewport units (dvh/svh/lvh)
	// ----------------------------------------------------------------------------

	/// <summary>
	/// Dynamic/small/large viewport units parse (mapped to the static vh equivalent).
	/// </summary>
	[TestMethod]
	public void Length_Dvh()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100dvh" ) );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Svh()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100svh" ) );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Length_Lvh()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100lvh" ) );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	// ----------------------------------------------------------------------------
	// !important
	// ----------------------------------------------------------------------------

	[TestMethod]
	public void Width_ImportantSuffixTolerated()
	{
		// Styles.Set strips a trailing !important for ALL properties before parsing the value
		// (see Styles.Set.cs), so the declaration still applies. (It is NOT honoured as a
		// cascade override - just tolerated.)
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100px !important" ) );
		Assert.AreEqual( 100, s.Width.Value.Value );
	}

	/// <summary>
	/// !important is stripped so the declaration applies (we don't implement cascade priority).
	/// </summary>
	[TestMethod]
	public void Important_KeywordPropertyApplied()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "display", "flex !important" ) );
		Assert.AreEqual( DisplayMode.Flex, s.Display );
	}

	// ----------------------------------------------------------------------------
	// CSS-wide keywords (inherit / initial / unset / revert)
	// ----------------------------------------------------------------------------

	/// <summary>
	/// The CSS-wide keywords are valid on any property that maps to a single field, so Set should
	/// accept them rather than dropping the declaration. (Shorthands like 'flex' keep handling their
	/// own keyword values - e.g. 'flex: initial' - so they aren't covered here.)
	/// </summary>
	[TestMethod]
	public void Global_KeywordsAccepted()
	{
		var s = new Styles();

		Assert.IsTrue( s.Set( "display", "inherit" ) );
		Assert.IsTrue( s.Set( "color", "initial" ) );
		Assert.IsTrue( s.Set( "width", "unset" ) );
		Assert.IsTrue( s.Set( "background-color", "revert" ) );

		// case-insensitive
		Assert.IsTrue( s.Set( "opacity", "INHERIT" ) );
	}

	/// <summary>
	/// 'initial' resets a property to its specification default, overriding inheritance - color is
	/// normally inherited, so without 'initial' the child would be red.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_InitialOverridesInheritance()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( "RootPanel { color: red; } .child { color: initial; }" );

		panel.Layout();

		Assert.AreEqual( Color.Black, child.ComputedStyle.FontColor.Value );
	}

	/// <summary>
	/// 'inherit' pulls the parent's computed value even for a property that doesn't normally inherit -
	/// background-color isn't inherited, so without 'inherit' the child would be transparent.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_InheritFromParent()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( "RootPanel { background-color: red; } .child { background-color: inherit; }" );

		panel.Layout();

		Assert.AreEqual( Color.Red, child.ComputedStyle.BackgroundColor.Value );
	}

	/// <summary>
	/// 'unset' acts like 'initial' on a non-inherited property, so it resets an earlier rule's value
	/// back to the default (transparent) rather than keeping the red.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_UnsetResetsToInitial()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( ".child { background-color: red; } RootPanel .child { background-color: unset; }" );

		panel.Layout();

		Assert.AreEqual( Color.Transparent, child.ComputedStyle.BackgroundColor.Value );
	}

	/// <summary>
	/// 'unset' on an inherited property (color) acts like 'inherit', so it takes the parent's value
	/// even though the child's own rule sets green.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_UnsetInheritsForInheritedProperty()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( "RootPanel { color: red; } .child { color: green; } RootPanel .child { color: unset; }" );

		panel.Layout();

		Assert.AreEqual( Color.Red, child.ComputedStyle.FontColor.Value );
	}

	// ----------------------------------------------------------------------------
	// CSS-wide keywords on shorthands (expanded to their longhands)
	// ----------------------------------------------------------------------------

	/// <summary>
	/// A CSS-wide keyword on a shorthand applies to each longhand. 'overflow' isn't inherited, so
	/// without 'inherit' the child would be Visible; here both axes take the parent's Hidden.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_ShorthandOverflowInherit()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( "RootPanel { overflow: hidden; } .child { overflow: inherit; }" );

		panel.Layout();

		Assert.AreEqual( OverflowMode.Hidden, child.ComputedStyle.OverflowX.Value );
		Assert.AreEqual( OverflowMode.Hidden, child.ComputedStyle.OverflowY.Value );
	}

	/// <summary>
	/// 'overflow: initial' resets both axes to the default (Visible), overriding the earlier rule -
	/// and crucially does so without the spurious 'Unhandled overflow property' warning the old
	/// fall-through produced.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_ShorthandOverflowInitial()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( ".child { overflow: hidden; } RootPanel .child { overflow: initial; }" );

		panel.Layout();

		Assert.AreEqual( OverflowMode.Visible, child.ComputedStyle.OverflowX.Value );
		Assert.AreEqual( OverflowMode.Visible, child.ComputedStyle.OverflowY.Value );
	}

	/// <summary>
	/// The box-model shorthand 'margin' expands to all four edges, none of which inherit by default,
	/// so 'margin: inherit' makes every edge match the parent's computed margin.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_ShorthandMarginInherit()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( "RootPanel { margin: 7px; } .child { margin: inherit; }" );

		panel.Layout();

		var expected = panel.ComputedStyle.MarginLeft.Value.Value;
		Assert.AreNotEqual( 0, expected ); // sanity: the parent's margin actually applied

		Assert.AreEqual( expected, child.ComputedStyle.MarginLeft.Value.Value );
		Assert.AreEqual( expected, child.ComputedStyle.MarginTop.Value.Value );
		Assert.AreEqual( expected, child.ComputedStyle.MarginRight.Value.Value );
		Assert.AreEqual( expected, child.ComputedStyle.MarginBottom.Value.Value );
	}

	/// <summary>
	/// Last-declaration-wins is preserved per-longhand after expansion: 'overflow: inherit' followed
	/// by 'overflow-x: hidden' inherits y but overrides x.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_ShorthandKeywordThenLonghandValue()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( "RootPanel { overflow: scroll; } .child { overflow: inherit; overflow-x: hidden; }" );

		panel.Layout();

		Assert.AreEqual( OverflowMode.Hidden, child.ComputedStyle.OverflowX.Value );
		Assert.AreEqual( OverflowMode.Scroll, child.ComputedStyle.OverflowY.Value );
	}

	/// <summary>
	/// 'initial'/'unset' on a non-inherited string property must resolve to the property's real
	/// default ("none" for animation-name), never null.
	/// </summary>
	[TestMethod]
	[DoNotParallelize] // Modifies UI System Global
	public void Global_StringPropertyInitialIsNotNull()
	{
		var panel = new RootPanel();
		var child = panel.Add.Panel( "child" );
		panel.StyleSheet.Parse( ".child { animation-name: initial; }" );

		panel.Layout();

		Assert.AreEqual( "none", child.ComputedStyle.AnimationName );
	}

	/// <summary>
	/// 'flex' keeps handling initial/none/auto natively (it has no single backing field), so it is
	/// NOT intercepted as a CSS-wide keyword. 'flex: initial' must still expand to grow 0 / shrink 1.
	/// </summary>
	[TestMethod]
	public void Global_FlexInitialStaysNative()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "initial" ) );

		Assert.AreEqual( 0f, s.FlexGrow );
		Assert.AreEqual( 1f, s.FlexShrink );
	}

	/// <summary>
	/// Every longhand in the shorthand expansion table must resolve to a real backing field, or the
	/// keyword would silently no-op for that longhand.
	/// </summary>
	[TestMethod]
	public void Global_ShorthandExpansionsResolveToFields()
	{
		foreach ( var shorthand in BaseStyles.ShorthandExpansions )
		{
			foreach ( var longhand in shorthand.Value )
				Assert.IsNotNull( BaseStyles.GetStyleField( longhand ), $"{shorthand.Key} -> {longhand} has no backing field" );
		}
	}

	/// <summary>
	/// Clone must deep-copy the recorded keywords - otherwise a clone would share the dictionary and
	/// clearing the keyword on the original (here via 'color: red') would also wipe it from the clone.
	/// </summary>
	[TestMethod]
	public void Global_CloneDeepCopiesKeywords()
	{
		var s = new Styles();
		s.Set( "color", "inherit" );

		var clone = (Styles)s.Clone();
		s.Set( "color", "red" ); // clears the keyword on the original only

		Assert.IsNotNull( clone.CssWide );
		Assert.IsTrue( clone.CssWide.ContainsKey( "font-color" ) );
	}
}
