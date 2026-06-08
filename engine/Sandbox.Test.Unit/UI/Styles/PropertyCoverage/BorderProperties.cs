using Sandbox.UI;

namespace UITest.PropertyCoverage;

[TestClass]
public class BorderProperties
{
	// ------------------------------------------------------------------
	// border (shorthand) -> per-side width + per-side color
	// The style keyword (solid/dashed/...) is parsed then ignored.
	// ------------------------------------------------------------------

	[TestMethod]
	public void Border_WidthStyleColor()
	{
		var s = new Styles();
		bool ok = s.Set( "border", "10px solid red" );
		Assert.IsTrue( ok );

		Assert.IsTrue( s.BorderLeftWidth.HasValue );
		Assert.IsTrue( s.BorderTopWidth.HasValue );
		Assert.IsTrue( s.BorderRightWidth.HasValue );
		Assert.IsTrue( s.BorderBottomWidth.HasValue );

		Assert.AreEqual( 10, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderBottomWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderLeftWidth.Value.Unit );

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderBottomColor.Value );
	}

	[TestMethod]
	public void Border_StyleWidthColor_OrderIndependent()
	{
		// CSS allows the components in any order
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "solid 10px red" ) );

		Assert.AreEqual( 10, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderBottomColor.Value );
	}

	[TestMethod]
	public void Border_WidthColor_NoStyle()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "10px red" ) );

		Assert.AreEqual( 10, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderBottomColor.Value );
	}

	[TestMethod]
	public void Border_WidthOnly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "10px" ) );

		Assert.IsTrue( s.BorderLeftWidth.HasValue );
		Assert.IsTrue( s.BorderTopWidth.HasValue );
		Assert.IsTrue( s.BorderRightWidth.HasValue );
		Assert.IsTrue( s.BorderBottomWidth.HasValue );
		Assert.AreEqual( 10, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderBottomWidth.Value.Value );

		// No color component means colors stay unset
		Assert.IsFalse( s.BorderLeftColor.HasValue );
		Assert.IsFalse( s.BorderTopColor.HasValue );
		Assert.IsFalse( s.BorderRightColor.HasValue );
		Assert.IsFalse( s.BorderBottomColor.HasValue );
	}

	[TestMethod]
	public void Border_ColorOnly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "red" ) );

		// No width component means widths stay unset
		Assert.IsFalse( s.BorderLeftWidth.HasValue );
		Assert.IsFalse( s.BorderTopWidth.HasValue );
		Assert.IsFalse( s.BorderRightWidth.HasValue );
		Assert.IsFalse( s.BorderBottomWidth.HasValue );

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderBottomColor.Value );
	}

	[TestMethod]
	public void Border_None_SetsWidthZero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "10px solid red" ) );
		Assert.IsTrue( s.Set( "border", "none" ) );

		Assert.AreEqual( 0.0f, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 0.0f, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 0.0f, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 0.0f, s.BorderBottomWidth.Value.Value );
	}

	[TestMethod]
	public void Border_Zero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "0" ) );

		Assert.IsTrue( s.BorderLeftWidth.HasValue );
		Assert.IsTrue( s.BorderTopWidth.HasValue );
		Assert.IsTrue( s.BorderRightWidth.HasValue );
		Assert.IsTrue( s.BorderBottomWidth.HasValue );
		Assert.AreEqual( 0, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 0, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 0, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 0, s.BorderBottomWidth.Value.Value );
	}

	[TestMethod]
	public void Border_RgbaColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "10px solid rgba( 0, 0, 0, 0.1 )" ) );

		Assert.AreEqual( 10, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 10, s.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), s.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), s.BorderTopColor.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), s.BorderRightColor.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 0.1f ), s.BorderBottomColor.Value );
	}

	// ------------------------------------------------------------------
	// border-left / -right / -top / -bottom (per-side shorthand)
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderLeft_WidthColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-left", "10px solid red" ) );

		Assert.IsTrue( s.BorderLeftWidth.HasValue );
		Assert.IsFalse( s.BorderTopWidth.HasValue );
		Assert.IsFalse( s.BorderRightWidth.HasValue );
		Assert.IsFalse( s.BorderBottomWidth.HasValue );
		Assert.AreEqual( 10, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderLeftWidth.Value.Unit );

		Assert.IsTrue( s.BorderLeftColor.HasValue );
		Assert.IsFalse( s.BorderTopColor.HasValue );
		Assert.IsFalse( s.BorderRightColor.HasValue );
		Assert.IsFalse( s.BorderBottomColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderLeftColor.Value );
	}

	[TestMethod]
	public void BorderRight_WidthColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-right", "5px solid #00ff00" ) );

		Assert.IsTrue( s.BorderRightWidth.HasValue );
		Assert.IsFalse( s.BorderLeftWidth.HasValue );
		Assert.AreEqual( 5, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.BorderRightColor.Value );
		Assert.IsFalse( s.BorderLeftColor.HasValue );
	}

	/// <summary>
	/// Non-solid border styles (dashed/dotted/double/etc) are accepted; the keyword is consumed (we
	/// don't render the style) so the width and colour still apply.
	/// </summary>
	[TestMethod]
	public void Border_NonSolidStyle()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border", "1px dashed red" ) );
		Assert.IsTrue( s.BorderTopWidth.HasValue );
		Assert.AreEqual( 1, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderTopColor.Value );
	}

	[TestMethod]
	public void BorderTop_WidthColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-top", "2px solid blue" ) );

		Assert.IsTrue( s.BorderTopWidth.HasValue );
		Assert.IsFalse( s.BorderBottomWidth.HasValue );
		Assert.AreEqual( 2, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( new Color( 0, 0, 1, 1 ), s.BorderTopColor.Value );
		Assert.IsFalse( s.BorderBottomColor.HasValue );
	}

	[TestMethod]
	public void BorderBottom_WidthColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-bottom", "3px solid black" ) );

		Assert.IsTrue( s.BorderBottomWidth.HasValue );
		Assert.IsFalse( s.BorderTopWidth.HasValue );
		Assert.AreEqual( 3, s.BorderBottomWidth.Value.Value );
		Assert.AreEqual( new Color( 0, 0, 0, 1 ), s.BorderBottomColor.Value );
		Assert.IsFalse( s.BorderTopColor.HasValue );
	}

	[TestMethod]
	public void BorderSide_None_SetsWidthZero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-left", "10px" ) );
		Assert.IsTrue( s.Set( "border-left", "none" ) );

		Assert.IsTrue( s.BorderLeftWidth.HasValue );
		Assert.AreEqual( 0.0f, s.BorderLeftWidth.Value.Value );
	}

	// ------------------------------------------------------------------
	// border-color -> all four colors
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderColor_AppliesToAllSides()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-color", "red" ) );

		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderLeftColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderTopColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderRightColor.Value );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderBottomColor.Value );
	}

	[TestMethod]
	public void BorderColor_Hex()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-color", "#f0f0" ) );

		// #f0f0 -> r=1 g=0 b=1 a=0
		Assert.AreEqual( 1.0f, s.BorderTopColor.Value.r );
		Assert.AreEqual( 0.0f, s.BorderTopColor.Value.g );
		Assert.AreEqual( 1.0f, s.BorderTopColor.Value.b );
		Assert.AreEqual( 0.0f, s.BorderTopColor.Value.a );
	}

	[TestMethod]
	public void BorderColor_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		// A length is not a valid color
		Assert.IsFalse( s.Set( "border-color", "10px" ) );
		Assert.IsFalse( s.BorderTopColor.HasValue );
	}

	// ------------------------------------------------------------------
	// border-width -> single Length applied to all four
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderWidth_AppliesToAllSides()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-width", "7px" ) );

		Assert.IsTrue( s.BorderLeftWidth.HasValue );
		Assert.IsTrue( s.BorderTopWidth.HasValue );
		Assert.IsTrue( s.BorderRightWidth.HasValue );
		Assert.IsTrue( s.BorderBottomWidth.HasValue );
		Assert.AreEqual( 7, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( 7, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 7, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 7, s.BorderBottomWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderTopWidth.Value.Unit );
	}

	[TestMethod]
	public void BorderWidth_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-width", "5%" ) );

		Assert.AreEqual( 5, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.BorderTopWidth.Value.Unit );
		Assert.AreEqual( LengthUnit.Percentage, s.BorderLeftWidth.Value.Unit );
	}

	[TestMethod]
	public void BorderWidth_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		// A color is not a valid length
		Assert.IsFalse( s.Set( "border-width", "red" ) );
		Assert.IsFalse( s.BorderTopWidth.HasValue );
	}

	/// <summary>
	/// border-width takes 1-4 values for top/right/bottom/left.
	/// </summary>
	[TestMethod]
	public void BorderWidth_FourValues()
	{
		var s = new Styles();
		s.Set( "border-width", "1px 2px 3px 4px" );

		Assert.AreEqual( 1, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( 2, s.BorderRightWidth.Value.Value );
		Assert.AreEqual( 3, s.BorderBottomWidth.Value.Value );
		Assert.AreEqual( 4, s.BorderLeftWidth.Value.Value );
	}

	// ------------------------------------------------------------------
	// border-*-width longhands
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderLeftWidth()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-left-width", "10px" ) );
		Assert.IsTrue( s.BorderLeftWidth.HasValue );
		Assert.AreEqual( 10, s.BorderLeftWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderLeftWidth.Value.Unit );
		Assert.IsFalse( s.BorderRightWidth.HasValue );
	}

	[TestMethod]
	public void BorderRightWidth()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-right-width", "11px" ) );
		Assert.IsTrue( s.BorderRightWidth.HasValue );
		Assert.AreEqual( 11, s.BorderRightWidth.Value.Value );
		Assert.IsFalse( s.BorderLeftWidth.HasValue );
	}

	[TestMethod]
	public void BorderTopWidth()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-top-width", "12px" ) );
		Assert.IsTrue( s.BorderTopWidth.HasValue );
		Assert.AreEqual( 12, s.BorderTopWidth.Value.Value );
		Assert.IsFalse( s.BorderBottomWidth.HasValue );
	}

	[TestMethod]
	public void BorderBottomWidth()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-bottom-width", "13px" ) );
		Assert.IsTrue( s.BorderBottomWidth.HasValue );
		Assert.AreEqual( 13, s.BorderBottomWidth.Value.Value );
		Assert.IsFalse( s.BorderTopWidth.HasValue );
	}

	[TestMethod]
	public void BorderWidthLonghand_Em()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-top-width", "2em" ) );
		Assert.AreEqual( 2, s.BorderTopWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Em, s.BorderTopWidth.Value.Unit );
	}

	[TestMethod]
	public void BorderWidthLonghand_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "border-top-width", "notalength" ) );
		Assert.IsFalse( s.BorderTopWidth.HasValue );
	}

	// ------------------------------------------------------------------
	// border-*-color longhands
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderLeftColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-left-color", "red" ) );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderLeftColor.Value );
		Assert.IsFalse( s.BorderRightColor.HasValue );
	}

	[TestMethod]
	public void BorderRightColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-right-color", "#00ff00" ) );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.BorderRightColor.Value );
		Assert.IsFalse( s.BorderLeftColor.HasValue );
	}

	[TestMethod]
	public void BorderTopColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-top-color", "blue" ) );
		Assert.AreEqual( new Color( 0, 0, 1, 1 ), s.BorderTopColor.Value );
		Assert.IsFalse( s.BorderBottomColor.HasValue );
	}

	[TestMethod]
	public void BorderBottomColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-bottom-color", "rgba( 0, 0, 0, 0.5 )" ) );
		Assert.AreEqual( new Color( 0, 0, 0, 0.5f ), s.BorderBottomColor.Value );
		Assert.IsFalse( s.BorderTopColor.HasValue );
	}

	[TestMethod]
	public void BorderColorLonghand_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "border-top-color", "10px" ) );
		Assert.IsFalse( s.BorderTopColor.HasValue );
	}

	// ------------------------------------------------------------------
	// border-radius (shorthand) -> four corner radii
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderRadius_OneValue()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-radius", "123px" ) );

		Assert.IsTrue( s.BorderTopLeftRadius.HasValue );
		Assert.IsTrue( s.BorderTopRightRadius.HasValue );
		Assert.IsTrue( s.BorderBottomRightRadius.HasValue );
		Assert.IsTrue( s.BorderBottomLeftRadius.HasValue );
		Assert.AreEqual( 123, s.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( 123, s.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( 123, s.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( 123, s.BorderBottomLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderTopLeftRadius.Value.Unit );
	}

	[TestMethod]
	public void BorderRadius_TwoValues()
	{
		// "10px 20px" -> TL/BR = 10, TR/BL = 20
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-radius", "10px 20px" ) );

		Assert.AreEqual( 10, s.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( 20, s.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( 10, s.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( 20, s.BorderBottomLeftRadius.Value.Value );
	}

	[TestMethod]
	public void BorderRadius_FourValues()
	{
		// "10px 20px 30px 40px" -> TL=10 TR=20 BR=30 BL=40
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-radius", "10px 20px 30px 40px" ) );

		Assert.AreEqual( 10, s.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( 20, s.BorderTopRightRadius.Value.Value );
		Assert.AreEqual( 30, s.BorderBottomRightRadius.Value.Value );
		Assert.AreEqual( 40, s.BorderBottomLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderBottomLeftRadius.Value.Unit );
	}

	[TestMethod]
	public void BorderRadius_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-radius", "50%" ) );
		Assert.AreEqual( 50, s.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.BorderTopLeftRadius.Value.Unit );
		Assert.AreEqual( LengthUnit.Percentage, s.BorderBottomRightRadius.Value.Unit );
	}

	[TestMethod]
	public void BorderRadius_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "border-radius", "notalength" ) );
		Assert.IsFalse( s.BorderTopLeftRadius.HasValue );
	}

	// ------------------------------------------------------------------
	// border-radius corner longhands
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderTopLeftRadius()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-top-left-radius", "123px" ) );
		Assert.IsTrue( s.BorderTopLeftRadius.HasValue );
		Assert.IsFalse( s.BorderTopRightRadius.HasValue );
		Assert.IsFalse( s.BorderBottomLeftRadius.HasValue );
		Assert.IsFalse( s.BorderBottomRightRadius.HasValue );
		Assert.AreEqual( 123, s.BorderTopLeftRadius.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderTopLeftRadius.Value.Unit );
	}

	[TestMethod]
	public void BorderTopRightRadius()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-top-right-radius", "44px" ) );
		Assert.IsTrue( s.BorderTopRightRadius.HasValue );
		Assert.IsFalse( s.BorderTopLeftRadius.HasValue );
		Assert.AreEqual( 44, s.BorderTopRightRadius.Value.Value );
	}

	[TestMethod]
	public void BorderBottomRightRadius()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-bottom-right-radius", "55px" ) );
		Assert.IsTrue( s.BorderBottomRightRadius.HasValue );
		Assert.IsFalse( s.BorderTopLeftRadius.HasValue );
		Assert.AreEqual( 55, s.BorderBottomRightRadius.Value.Value );
	}

	[TestMethod]
	public void BorderBottomLeftRadius()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-bottom-left-radius", "66px" ) );
		Assert.IsTrue( s.BorderBottomLeftRadius.HasValue );
		Assert.IsFalse( s.BorderTopLeftRadius.HasValue );
		Assert.AreEqual( 66, s.BorderBottomLeftRadius.Value.Value );
	}

	// ------------------------------------------------------------------
	// outline (shorthand, same behaviour as border) + longhands
	// ------------------------------------------------------------------

	[TestMethod]
	public void Outline_WidthStyleColor()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "outline", "10px solid red" ) );

		Assert.IsTrue( s.OutlineWidth.HasValue );
		Assert.AreEqual( 10, s.OutlineWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.OutlineWidth.Value.Unit );
		Assert.IsTrue( s.OutlineColor.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.OutlineColor.Value );
	}

	[TestMethod]
	public void Outline_ColorOnly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "outline", "blue" ) );

		Assert.IsFalse( s.OutlineWidth.HasValue );
		Assert.AreEqual( new Color( 0, 0, 1, 1 ), s.OutlineColor.Value );
	}

	[TestMethod]
	public void Outline_None_SetsWidthZero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "outline", "5px solid red" ) );
		Assert.IsTrue( s.Set( "outline", "none" ) );

		Assert.IsTrue( s.OutlineWidth.HasValue );
		Assert.AreEqual( 0.0f, s.OutlineWidth.Value.Value );
	}

	[TestMethod]
	public void OutlineWidth_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "outline-width", "8px" ) );
		Assert.IsTrue( s.OutlineWidth.HasValue );
		Assert.AreEqual( 8, s.OutlineWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.OutlineWidth.Value.Unit );
	}

	[TestMethod]
	public void OutlineWidth_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "outline-width", "red" ) );
		Assert.IsFalse( s.OutlineWidth.HasValue );
	}

	[TestMethod]
	public void OutlineColor_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "outline-color", "#00ff00" ) );
		Assert.AreEqual( new Color( 0, 1, 0, 1 ), s.OutlineColor.Value );
	}

	[TestMethod]
	public void OutlineColor_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "outline-color", "10px" ) );
		Assert.IsFalse( s.OutlineColor.HasValue );
	}

	[TestMethod]
	public void OutlineOffset_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "outline-offset", "4px" ) );
		Assert.IsTrue( s.OutlineOffset.HasValue );
		Assert.AreEqual( 4, s.OutlineOffset.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.OutlineOffset.Value.Unit );
	}

	[TestMethod]
	public void OutlineOffset_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "outline-offset", "notalength" ) );
		Assert.IsFalse( s.OutlineOffset.HasValue );
	}

	// ------------------------------------------------------------------
	// border-image (smoke test) + longhands
	// ------------------------------------------------------------------

	[TestMethod]
	public void BorderImage_SmokeTest()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-image", "url( /ui/border.png ) 50 fill stretch" ) );
	}

	[TestMethod]
	public void BorderImageWidthLeft()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-image-width-left", "10px" ) );
		Assert.IsTrue( s.BorderImageWidthLeft.HasValue );
		Assert.AreEqual( 10, s.BorderImageWidthLeft.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.BorderImageWidthLeft.Value.Unit );
	}

	[TestMethod]
	public void BorderImageWidthRight()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-image-width-right", "11px" ) );
		Assert.IsTrue( s.BorderImageWidthRight.HasValue );
		Assert.AreEqual( 11, s.BorderImageWidthRight.Value.Value );
	}

	[TestMethod]
	public void BorderImageWidthTop()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-image-width-top", "12px" ) );
		Assert.IsTrue( s.BorderImageWidthTop.HasValue );
		Assert.AreEqual( 12, s.BorderImageWidthTop.Value.Value );
	}

	[TestMethod]
	public void BorderImageWidthBottom()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-image-width-bottom", "13px" ) );
		Assert.IsTrue( s.BorderImageWidthBottom.HasValue );
		Assert.AreEqual( 13, s.BorderImageWidthBottom.Value.Value );
	}

	[TestMethod]
	public void BorderImageTint()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "border-image-tint", "red" ) );
		Assert.IsTrue( s.BorderImageTint.HasValue );
		Assert.AreEqual( new Color( 1, 0, 0, 1 ), s.BorderImageTint.Value );
	}

	[TestMethod]
	public void BorderImageTint_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "border-image-tint", "10px" ) );
		Assert.IsFalse( s.BorderImageTint.HasValue );
	}
}
