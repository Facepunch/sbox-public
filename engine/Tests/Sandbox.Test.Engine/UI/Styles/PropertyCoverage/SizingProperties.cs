using Sandbox.UI;

namespace UITests.PropertyCoverage;

/// <summary>
/// Exhaustive coverage of sizing, position-offset and numeric style properties:
/// width / min-width / max-width / height / min-height / max-height (Length?),
/// left / top / right / bottom (Length?),
/// opacity (float?), z-index / order (int?), aspect-ratio (float?).
///
/// All Length? props parse via Length.Parse. Numeric props parse via ParseFloat / ParseInt.
/// aspect-ratio parses via ParseAspectRatio.
/// </summary>
[TestClass]
public class SizingPropertiesTest
{
	// ----------------------------------------------------------------------------------
	// width
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void Width_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "140px" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 140, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "56%" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 56, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_UnitlessNumberIsPixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "200" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 200, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Zero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "0" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 0, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Decimal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "12.5px" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 12.5f, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Em()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "2em" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 2, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Em, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Rem()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "3rem" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 3, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.RootEm, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_ViewWidth()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "50vw" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 50, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewWidth, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_ViewHeight()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "75vh" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 75, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_ViewMin()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "10vmin" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 10, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewMin, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_ViewMax()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "20vmax" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( 20, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewMax, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "auto" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Calc()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "calc(100% - 20px)" ) );
		Assert.IsTrue( s.Width.HasValue );
		Assert.AreEqual( LengthUnit.Expression, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "width", "banana" ) );
		Assert.IsFalse( s.Width.HasValue );
	}

	/// <summary>
	/// min() resolves to the smallest of its arguments (evaluated against the reference size).
	/// </summary>
	[TestMethod]
	public void Width_Min()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "min(100%, 600px)" ) );
		Assert.AreEqual( LengthUnit.Expression, s.Width.Value.Unit );
		Assert.AreEqual( 600, s.Width.Value.GetPixels( 1000 ) );   // min(1000, 600)
	}

	/// <summary>
	/// max() resolves to the largest of its arguments.
	/// </summary>
	[TestMethod]
	public void Width_Max()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "max(10px, 5%)" ) );
		Assert.AreEqual( 50, s.Width.Value.GetPixels( 1000 ) );    // max(10, 50)
	}

	/// <summary>
	/// clamp(min, val, max) resolves to val constrained to the [min, max] range.
	/// </summary>
	[TestMethod]
	public void Width_Clamp()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "clamp(10px, 50%, 300px)" ) );
		Assert.AreEqual( 300, s.Width.Value.GetPixels( 1000 ) );   // clamp(500 -> [10, 300]) = 300
	}

	/// <summary>
	/// Dynamic/small/large viewport units map to the static vh/vw equivalents.
	/// </summary>
	[TestMethod]
	public void Width_Dvh()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100dvh" ) );
		Assert.AreEqual( 100, s.Width.Value.Value );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Svh()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100svh" ) );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Lvh()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100lvh" ) );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Width.Value.Unit );
	}

	[TestMethod]
	public void Width_Dvw()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "width", "100dvw" ) );
		Assert.AreEqual( LengthUnit.ViewWidth, s.Width.Value.Unit );
	}

	// ----------------------------------------------------------------------------------
	// min-width / max-width
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void MinWidth_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "min-width", "50px" ) );
		Assert.IsTrue( s.MinWidth.HasValue );
		Assert.AreEqual( 50, s.MinWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MinWidth.Value.Unit );
	}

	[TestMethod]
	public void MinWidth_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "min-width", "25%" ) );
		Assert.IsTrue( s.MinWidth.HasValue );
		Assert.AreEqual( 25, s.MinWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.MinWidth.Value.Unit );
	}

	[TestMethod]
	public void MinWidth_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "min-width", "auto" ) );
		Assert.IsTrue( s.MinWidth.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.MinWidth.Value.Unit );
	}

	[TestMethod]
	public void MinWidth_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "min-width", "banana" ) );
		Assert.IsFalse( s.MinWidth.HasValue );
	}

	[TestMethod]
	public void MaxWidth_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "max-width", "600px" ) );
		Assert.IsTrue( s.MaxWidth.HasValue );
		Assert.AreEqual( 600, s.MaxWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MaxWidth.Value.Unit );
	}

	[TestMethod]
	public void MaxWidth_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "max-width", "100%" ) );
		Assert.IsTrue( s.MaxWidth.HasValue );
		Assert.AreEqual( 100, s.MaxWidth.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.MaxWidth.Value.Unit );
	}

	[TestMethod]
	public void MaxWidth_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "max-width", "banana" ) );
		Assert.IsFalse( s.MaxWidth.HasValue );
	}

	// ----------------------------------------------------------------------------------
	// height / min-height / max-height
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void Height_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "height", "140px" ) );
		Assert.IsTrue( s.Height.HasValue );
		Assert.AreEqual( 140, s.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Height.Value.Unit );
	}

	[TestMethod]
	public void Height_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "height", "33%" ) );
		Assert.IsTrue( s.Height.HasValue );
		Assert.AreEqual( 33, s.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.Height.Value.Unit );
	}

	[TestMethod]
	public void Height_ViewHeight()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "height", "100vh" ) );
		Assert.IsTrue( s.Height.HasValue );
		Assert.AreEqual( 100, s.Height.Value.Value );
		Assert.AreEqual( LengthUnit.ViewHeight, s.Height.Value.Unit );
	}

	[TestMethod]
	public void Height_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "height", "auto" ) );
		Assert.IsTrue( s.Height.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.Height.Value.Unit );
	}

	[TestMethod]
	public void Height_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "height", "banana" ) );
		Assert.IsFalse( s.Height.HasValue );
	}

	[TestMethod]
	public void MinHeight_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "min-height", "10px" ) );
		Assert.IsTrue( s.MinHeight.HasValue );
		Assert.AreEqual( 10, s.MinHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MinHeight.Value.Unit );
	}

	[TestMethod]
	public void MinHeight_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "min-height", "5%" ) );
		Assert.IsTrue( s.MinHeight.HasValue );
		Assert.AreEqual( 5, s.MinHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.MinHeight.Value.Unit );
	}

	[TestMethod]
	public void MinHeight_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "min-height", "banana" ) );
		Assert.IsFalse( s.MinHeight.HasValue );
	}

	[TestMethod]
	public void MaxHeight_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "max-height", "480px" ) );
		Assert.IsTrue( s.MaxHeight.HasValue );
		Assert.AreEqual( 480, s.MaxHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MaxHeight.Value.Unit );
	}

	[TestMethod]
	public void MaxHeight_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "max-height", "90%" ) );
		Assert.IsTrue( s.MaxHeight.HasValue );
		Assert.AreEqual( 90, s.MaxHeight.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.MaxHeight.Value.Unit );
	}

	[TestMethod]
	public void MaxHeight_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "max-height", "banana" ) );
		Assert.IsFalse( s.MaxHeight.HasValue );
	}

	// ----------------------------------------------------------------------------------
	// position offsets: left / top / right / bottom
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void Left_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "left", "10px" ) );
		Assert.IsTrue( s.Left.HasValue );
		Assert.AreEqual( 10, s.Left.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Left.Value.Unit );
	}

	[TestMethod]
	public void Left_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "left", "50%" ) );
		Assert.IsTrue( s.Left.HasValue );
		Assert.AreEqual( 50, s.Left.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.Left.Value.Unit );
	}

	[TestMethod]
	public void Left_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "left", "auto" ) );
		Assert.IsTrue( s.Left.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.Left.Value.Unit );
	}

	[TestMethod]
	public void Left_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "left", "banana" ) );
		Assert.IsFalse( s.Left.HasValue );
	}

	[TestMethod]
	public void Top_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "top", "20px" ) );
		Assert.IsTrue( s.Top.HasValue );
		Assert.AreEqual( 20, s.Top.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Top.Value.Unit );
	}

	[TestMethod]
	public void Top_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "top", "12.5%" ) );
		Assert.IsTrue( s.Top.HasValue );
		Assert.AreEqual( 12.5f, s.Top.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.Top.Value.Unit );
	}

	[TestMethod]
	public void Top_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "top", "banana" ) );
		Assert.IsFalse( s.Top.HasValue );
	}

	[TestMethod]
	public void Right_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "right", "30px" ) );
		Assert.IsTrue( s.Right.HasValue );
		Assert.AreEqual( 30, s.Right.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Right.Value.Unit );
	}

	[TestMethod]
	public void Right_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "right", "5%" ) );
		Assert.IsTrue( s.Right.HasValue );
		Assert.AreEqual( 5, s.Right.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.Right.Value.Unit );
	}

	[TestMethod]
	public void Right_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "right", "banana" ) );
		Assert.IsFalse( s.Right.HasValue );
	}

	[TestMethod]
	public void Bottom_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "bottom", "40px" ) );
		Assert.IsTrue( s.Bottom.HasValue );
		Assert.AreEqual( 40, s.Bottom.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Bottom.Value.Unit );
	}

	[TestMethod]
	public void Bottom_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "bottom", "0%" ) );
		Assert.IsTrue( s.Bottom.HasValue );
		Assert.AreEqual( 0, s.Bottom.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.Bottom.Value.Unit );
	}

	[TestMethod]
	public void Bottom_Negative()
	{
		// Position offsets allow negative values; float.Parse handles the leading minus.
		var s = new Styles();
		Assert.IsTrue( s.Set( "bottom", "-15px" ) );
		Assert.IsTrue( s.Bottom.HasValue );
		Assert.AreEqual( -15, s.Bottom.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.Bottom.Value.Unit );
	}

	[TestMethod]
	public void Bottom_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "bottom", "banana" ) );
		Assert.IsFalse( s.Bottom.HasValue );
	}

	// ----------------------------------------------------------------------------------
	// opacity (float?)
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void Opacity_Fraction()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "opacity", "0.5" ) );
		Assert.IsTrue( s.Opacity.HasValue );
		Assert.AreEqual( 0.5f, s.Opacity.Value );
	}

	[TestMethod]
	public void Opacity_Zero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "opacity", "0" ) );
		Assert.IsTrue( s.Opacity.HasValue );
		Assert.AreEqual( 0.0f, s.Opacity.Value );
	}

	[TestMethod]
	public void Opacity_One()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "opacity", "1" ) );
		Assert.IsTrue( s.Opacity.HasValue );
		Assert.AreEqual( 1.0f, s.Opacity.Value );
	}

	[TestMethod]
	public void Opacity_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "opacity", "banana" ) );
		Assert.IsFalse( s.Opacity.HasValue );
	}

	/// <summary>
	/// opacity accepts a percentage (50% == 0.5).
	/// </summary>
	[TestMethod]
	public void Opacity_Percentage()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "opacity", "50%" ) );
		Assert.IsTrue( s.Opacity.HasValue );
		Assert.AreEqual( 0.5f, s.Opacity.Value );
	}

	// ----------------------------------------------------------------------------------
	// z-index (int?)
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void ZIndex_Positive()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "z-index", "5" ) );
		Assert.IsTrue( s.ZIndex.HasValue );
		Assert.AreEqual( 5, s.ZIndex.Value );
	}

	[TestMethod]
	public void ZIndex_Negative()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "z-index", "-1" ) );
		Assert.IsTrue( s.ZIndex.HasValue );
		Assert.AreEqual( -1, s.ZIndex.Value );
	}

	[TestMethod]
	public void ZIndex_Zero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "z-index", "0" ) );
		Assert.IsTrue( s.ZIndex.HasValue );
		Assert.AreEqual( 0, s.ZIndex.Value );
	}

	[TestMethod]
	public void ZIndex_InvalidWord()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "z-index", "banana" ) );
		Assert.IsFalse( s.ZIndex.HasValue );
	}

	[TestMethod]
	public void ZIndex_NonIntegerRejected()
	{
		// z-index parses with ParseInt; a decimal value is not an integer and is rejected.
		var s = new Styles();
		Assert.IsFalse( s.Set( "z-index", "2.5" ) );
		Assert.IsFalse( s.ZIndex.HasValue );
	}

	// ----------------------------------------------------------------------------------
	// order (int?)
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void Order_Positive()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "order", "3" ) );
		Assert.IsTrue( s.Order.HasValue );
		Assert.AreEqual( 3, s.Order.Value );
	}

	[TestMethod]
	public void Order_Negative()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "order", "-2" ) );
		Assert.IsTrue( s.Order.HasValue );
		Assert.AreEqual( -2, s.Order.Value );
	}

	[TestMethod]
	public void Order_Invalid()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "order", "banana" ) );
		Assert.IsFalse( s.Order.HasValue );
	}

	// ----------------------------------------------------------------------------------
	// aspect-ratio (float?)
	// ----------------------------------------------------------------------------------

	[TestMethod]
	public void AspectRatio_SingleNumber()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "aspect-ratio", "1" ) );
		Assert.IsTrue( s.AspectRatio.HasValue );
		Assert.AreEqual( 1.0f, s.AspectRatio.Value );
	}

	[TestMethod]
	public void AspectRatio_Decimal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "aspect-ratio", "0.5" ) );
		Assert.IsTrue( s.AspectRatio.HasValue );
		Assert.AreEqual( 0.5f, s.AspectRatio.Value );
	}

	[TestMethod]
	public void AspectRatio_RatioSlash()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "aspect-ratio", "16/9" ) );
		Assert.IsTrue( s.AspectRatio.HasValue );
		Assert.AreEqual( 16.0f / 9.0f, s.AspectRatio.Value );
	}

	[TestMethod]
	public void AspectRatio_RatioSlashSpaced()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "aspect-ratio", "16 / 9" ) );
		Assert.IsTrue( s.AspectRatio.HasValue );
		Assert.AreEqual( 16.0f / 9.0f, s.AspectRatio.Value );
	}

	[TestMethod]
	public void AspectRatio_RatioColon()
	{
		// ParseAspectRatio splits on space, ':' and '/', so "4:3" is treated like 4/3.
		var s = new Styles();
		Assert.IsTrue( s.Set( "aspect-ratio", "4:3" ) );
		Assert.IsTrue( s.AspectRatio.HasValue );
		Assert.AreEqual( 4.0f / 3.0f, s.AspectRatio.Value );
	}

	[TestMethod]
	public void AspectRatio_InvalidYieldsNullButSetTrue()
	{
		// aspect-ratio's Set always returns true; an unparseable value just leaves
		// AspectRatio null (ParseFloat returns null for a single non-numeric token).
		var s = new Styles();
		Assert.IsTrue( s.Set( "aspect-ratio", "banana" ) );
		Assert.IsFalse( s.AspectRatio.HasValue );
	}

	/// <summary>
	/// aspect-ratio accepts the 'auto &lt;ratio&gt;' form (using the ratio) as well as plain ratios.
	/// </summary>
	[TestMethod]
	public void AspectRatioAuto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "aspect-ratio", "auto 16/9" ) );
		Assert.AreEqual( 16f / 9f, s.AspectRatio.Value, 0.001f );

		Assert.IsTrue( s.Set( "aspect-ratio", "4/3" ) );
		Assert.AreEqual( 4f / 3f, s.AspectRatio.Value, 0.001f );
	}
}
