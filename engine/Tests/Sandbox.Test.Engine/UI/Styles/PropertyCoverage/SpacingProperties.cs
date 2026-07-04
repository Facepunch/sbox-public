using Sandbox.UI;

namespace UITests.PropertyCoverage;

/// <summary>
/// Exhaustive coverage of the CSS spacing properties: padding, margin, gap and inset.
///
/// Notes on s&box's parsing behaviour (verified against Styles.Set.cs):
///   - SetPadding / SetMargin follow the CSS spec side mapping: 1 value = all sides,
///     2 values = top/bottom then left/right, 3 values = top, left/right, bottom,
///     4 values = top, right, bottom, left.
///   - SetGap: one value sets RowGap and ColumnGap; two values set RowGap then ColumnGap.
///   - margin/padding longhands and row-gap/column-gap go through Length.Parse via the
///     generated SetGenerated switch.
/// </summary>
[TestClass]
public class SpacingPropertiesTest
{
	// ---------------------------------------------------------------------
	// padding (shorthand)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void Padding_SingleValue_SetsAllSides()
	{
		var s = new Styles();
		bool ok = s.Set( "padding", "10px" );
		Assert.IsTrue( ok );

		Assert.IsTrue( s.PaddingTop.HasValue );
		Assert.IsTrue( s.PaddingRight.HasValue );
		Assert.IsTrue( s.PaddingBottom.HasValue );
		Assert.IsTrue( s.PaddingLeft.HasValue );

		Assert.AreEqual( 10, s.PaddingTop.Value.Value );
		Assert.AreEqual( 10, s.PaddingRight.Value.Value );
		Assert.AreEqual( 10, s.PaddingBottom.Value.Value );
		Assert.AreEqual( 10, s.PaddingLeft.Value.Value );

		Assert.AreEqual( LengthUnit.Pixels, s.PaddingTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingBottom.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingLeft.Value.Unit );
	}

	[TestMethod]
	public void Padding_SingleValue_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding", "25%" ) );

		Assert.AreEqual( 25, s.PaddingTop.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.PaddingTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Percentage, s.PaddingRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Percentage, s.PaddingBottom.Value.Unit );
		Assert.AreEqual( LengthUnit.Percentage, s.PaddingLeft.Value.Unit );
	}

	[TestMethod]
	public void Padding_TwoValues_TopBottomAndLeftRight()
	{
		// s&box: first value -> top+bottom (via Padding = a), second value -> left+right.
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding", "10px 20px" ) );

		Assert.AreEqual( 10, s.PaddingTop.Value.Value );
		Assert.AreEqual( 10, s.PaddingBottom.Value.Value );
		Assert.AreEqual( 20, s.PaddingLeft.Value.Value );
		Assert.AreEqual( 20, s.PaddingRight.Value.Value );
	}

	[TestMethod]
	public void Padding_ThreeValues()
	{
		// s&box actual behaviour: top+bottom = 10 (from single Padding=a), left+right = 20,
		// then the third value overwrites bottom = 5.
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding", "10px 20px 5px" ) );

		Assert.AreEqual( 10, s.PaddingTop.Value.Value );
		Assert.AreEqual( 20, s.PaddingRight.Value.Value );
		Assert.AreEqual( 5, s.PaddingBottom.Value.Value );
		Assert.AreEqual( 20, s.PaddingLeft.Value.Value );
	}

	[TestMethod]
	public void Padding_FourValues_TopRightBottomLeft()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding", "1px 2px 3px 4px" ) );

		Assert.AreEqual( 1, s.PaddingTop.Value.Value );
		Assert.AreEqual( 2, s.PaddingRight.Value.Value );
		Assert.AreEqual( 3, s.PaddingBottom.Value.Value );
		Assert.AreEqual( 4, s.PaddingLeft.Value.Value );

		Assert.AreEqual( LengthUnit.Pixels, s.PaddingTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingBottom.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingLeft.Value.Unit );
	}

	[TestMethod]
	public void Padding_FourValues_WithZeroes()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding", "0 2px 0 4px" ) );

		Assert.AreEqual( 0, s.PaddingTop.Value.Value );
		Assert.AreEqual( 2, s.PaddingRight.Value.Value );
		Assert.AreEqual( 0, s.PaddingBottom.Value.Value );
		Assert.AreEqual( 4, s.PaddingLeft.Value.Value );
	}

	[TestMethod]
	public void Padding_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		// Empty value: SetPadding returns false (nothing to read).
		Assert.IsFalse( s.Set( "padding", "" ) );
	}

	// ---------------------------------------------------------------------
	// padding longhands
	// ---------------------------------------------------------------------

	[TestMethod]
	public void PaddingLeft_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding-left", "10px" ) );
		Assert.IsTrue( s.PaddingLeft.HasValue );
		Assert.AreEqual( 10, s.PaddingLeft.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingLeft.Value.Unit );

		// Other sides untouched.
		Assert.IsFalse( s.PaddingTop.HasValue );
		Assert.IsFalse( s.PaddingRight.HasValue );
		Assert.IsFalse( s.PaddingBottom.HasValue );
	}

	[TestMethod]
	public void PaddingTop_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding-top", "10px" ) );
		Assert.IsTrue( s.PaddingTop.HasValue );
		Assert.AreEqual( 10, s.PaddingTop.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingTop.Value.Unit );
		Assert.IsFalse( s.PaddingLeft.HasValue );
	}

	[TestMethod]
	public void PaddingRight_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding-right", "10px" ) );
		Assert.IsTrue( s.PaddingRight.HasValue );
		Assert.AreEqual( 10, s.PaddingRight.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingRight.Value.Unit );
		Assert.IsFalse( s.PaddingLeft.HasValue );
	}

	[TestMethod]
	public void PaddingBottom_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding-bottom", "10px" ) );
		Assert.IsTrue( s.PaddingBottom.HasValue );
		Assert.AreEqual( 10, s.PaddingBottom.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.PaddingBottom.Value.Unit );
		Assert.IsFalse( s.PaddingLeft.HasValue );
	}

	[TestMethod]
	public void PaddingLonghand_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "padding-top", "50%" ) );
		Assert.AreEqual( 50, s.PaddingTop.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.PaddingTop.Value.Unit );
	}

	[TestMethod]
	public void PaddingLonghand_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "padding-top", "notalength" ) );
		Assert.IsFalse( s.PaddingTop.HasValue );
	}

	// ---------------------------------------------------------------------
	// margin (shorthand)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void Margin_SingleValue_SetsAllSides()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin", "10px" ) );

		Assert.IsTrue( s.MarginTop.HasValue );
		Assert.IsTrue( s.MarginRight.HasValue );
		Assert.IsTrue( s.MarginBottom.HasValue );
		Assert.IsTrue( s.MarginLeft.HasValue );

		Assert.AreEqual( 10, s.MarginTop.Value.Value );
		Assert.AreEqual( 10, s.MarginRight.Value.Value );
		Assert.AreEqual( 10, s.MarginBottom.Value.Value );
		Assert.AreEqual( 10, s.MarginLeft.Value.Value );

		Assert.AreEqual( LengthUnit.Pixels, s.MarginTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginBottom.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginLeft.Value.Unit );
	}

	[TestMethod]
	public void Margin_TwoValues_TopBottomAndLeftRight()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin", "10px 20px" ) );

		Assert.AreEqual( 10, s.MarginTop.Value.Value );
		Assert.AreEqual( 10, s.MarginBottom.Value.Value );
		Assert.AreEqual( 20, s.MarginLeft.Value.Value );
		Assert.AreEqual( 20, s.MarginRight.Value.Value );
	}

	[TestMethod]
	public void Margin_ThreeValues()
	{
		// s&box actual: top = 10 (single set), left+right = 20, bottom = 30.
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin", "10px 20px 30px" ) );

		Assert.AreEqual( 10, s.MarginTop.Value.Value );
		Assert.AreEqual( 20, s.MarginRight.Value.Value );
		Assert.AreEqual( 30, s.MarginBottom.Value.Value );
		Assert.AreEqual( 20, s.MarginLeft.Value.Value );
	}

	[TestMethod]
	public void Margin_FourValues_TopRightBottomLeft()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin", "10px 20px 30px 40px" ) );

		Assert.AreEqual( 10, s.MarginTop.Value.Value );
		Assert.AreEqual( 20, s.MarginRight.Value.Value );
		Assert.AreEqual( 30, s.MarginBottom.Value.Value );
		Assert.AreEqual( 40, s.MarginLeft.Value.Value );
	}

	[TestMethod]
	public void Margin_Auto_SingleValue()
	{
		// margin: auto -> all four sides become LengthUnit.Auto.
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin", "auto" ) );

		Assert.IsTrue( s.MarginTop.HasValue );
		Assert.IsTrue( s.MarginRight.HasValue );
		Assert.IsTrue( s.MarginBottom.HasValue );
		Assert.IsTrue( s.MarginLeft.HasValue );

		Assert.AreEqual( LengthUnit.Auto, s.MarginTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Auto, s.MarginRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Auto, s.MarginBottom.Value.Unit );
		Assert.AreEqual( LengthUnit.Auto, s.MarginLeft.Value.Unit );
	}

	[TestMethod]
	public void Margin_PixelTopBottom_AutoLeftRight()
	{
		// Common centering idiom: margin: 0 auto.
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin", "0 auto" ) );

		Assert.AreEqual( LengthUnit.Pixels, s.MarginTop.Value.Unit );
		Assert.AreEqual( 0, s.MarginTop.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginBottom.Value.Unit );
		Assert.AreEqual( 0, s.MarginBottom.Value.Value );

		Assert.AreEqual( LengthUnit.Auto, s.MarginLeft.Value.Unit );
		Assert.AreEqual( LengthUnit.Auto, s.MarginRight.Value.Unit );
	}

	[TestMethod]
	public void Margin_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "margin", "" ) );
	}

	// ---------------------------------------------------------------------
	// margin longhands
	// ---------------------------------------------------------------------

	[TestMethod]
	public void MarginLeft_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-left", "10px" ) );
		Assert.IsTrue( s.MarginLeft.HasValue );
		Assert.AreEqual( 10, s.MarginLeft.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginLeft.Value.Unit );

		Assert.IsFalse( s.MarginTop.HasValue );
		Assert.IsFalse( s.MarginRight.HasValue );
		Assert.IsFalse( s.MarginBottom.HasValue );
	}

	[TestMethod]
	public void MarginTop_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-top", "10px" ) );
		Assert.IsTrue( s.MarginTop.HasValue );
		Assert.AreEqual( 10, s.MarginTop.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginTop.Value.Unit );
		Assert.IsFalse( s.MarginLeft.HasValue );
	}

	[TestMethod]
	public void MarginRight_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-right", "10px" ) );
		Assert.IsTrue( s.MarginRight.HasValue );
		Assert.AreEqual( 10, s.MarginRight.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginRight.Value.Unit );
		Assert.IsFalse( s.MarginLeft.HasValue );
	}

	[TestMethod]
	public void MarginBottom_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-bottom", "10px" ) );
		Assert.IsTrue( s.MarginBottom.HasValue );
		Assert.AreEqual( 10, s.MarginBottom.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.MarginBottom.Value.Unit );
		Assert.IsFalse( s.MarginLeft.HasValue );
	}

	[TestMethod]
	public void MarginLonghand_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-left", "auto" ) );
		Assert.IsTrue( s.MarginLeft.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.MarginLeft.Value.Unit );
	}

	[TestMethod]
	public void MarginLonghand_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-top", "12.5%" ) );
		Assert.AreEqual( 12.5f, s.MarginTop.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.MarginTop.Value.Unit );
	}

	[TestMethod]
	public void MarginLonghand_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "margin-top", "notalength" ) );
		Assert.IsFalse( s.MarginTop.HasValue );
	}

	// ---------------------------------------------------------------------
	// gap (shorthand)
	// ---------------------------------------------------------------------

	[TestMethod]
	public void Gap_SingleValue_SetsRowAndColumn()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "gap", "10px" ) );

		Assert.IsTrue( s.RowGap.HasValue );
		Assert.IsTrue( s.ColumnGap.HasValue );

		Assert.AreEqual( 10, s.RowGap.Value.Value );
		Assert.AreEqual( 10, s.ColumnGap.Value.Value );

		Assert.AreEqual( LengthUnit.Pixels, s.RowGap.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.ColumnGap.Value.Unit );
	}

	[TestMethod]
	public void Gap_TwoValues_RowThenColumn()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "gap", "10px 20px" ) );

		Assert.AreEqual( 10, s.RowGap.Value.Value );
		Assert.AreEqual( 20, s.ColumnGap.Value.Value );

		Assert.AreEqual( LengthUnit.Pixels, s.RowGap.Value.Unit );
		Assert.AreEqual( LengthUnit.Pixels, s.ColumnGap.Value.Unit );
	}

	[TestMethod]
	public void Gap_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "gap", "5%" ) );
		Assert.AreEqual( 5, s.RowGap.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.RowGap.Value.Unit );
		Assert.AreEqual( 5, s.ColumnGap.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.ColumnGap.Value.Unit );
	}

	[TestMethod]
	public void Gap_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		// First token cannot be parsed as a length -> SetGap returns false.
		Assert.IsFalse( s.Set( "gap", "notalength" ) );
	}

	/// <summary>
	/// 'gap: normal' computes to the initial value (no gap) for our flex layout.
	/// </summary>
	[TestMethod]
	public void Gap_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "gap", "normal" ) );
		Assert.AreEqual( 0, s.RowGap.Value.Value );
		Assert.AreEqual( 0, s.ColumnGap.Value.Value );
	}

	// ---------------------------------------------------------------------
	// row-gap / column-gap longhands
	// ---------------------------------------------------------------------

	[TestMethod]
	public void RowGap_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "row-gap", "15px" ) );
		Assert.IsTrue( s.RowGap.HasValue );
		Assert.AreEqual( 15, s.RowGap.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.RowGap.Value.Unit );
	}

	[TestMethod]
	public void ColumnGap_Longhand()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "column-gap", "25px" ) );
		Assert.IsTrue( s.ColumnGap.HasValue );
		Assert.AreEqual( 25, s.ColumnGap.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.ColumnGap.Value.Unit );
	}

	[TestMethod]
	public void GapLonghand_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "row-gap", "10%" ) );
		Assert.AreEqual( 10, s.RowGap.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.RowGap.Value.Unit );
	}

	[TestMethod]
	public void GapLonghand_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "row-gap", "notalength" ) );
		Assert.IsFalse( s.RowGap.HasValue );
	}

	// ---------------------------------------------------------------------
	// inset (shorthand for top/right/bottom/left)
	// ---------------------------------------------------------------------

	/// <summary>
	/// inset: shorthand for top/right/bottom/left (1-4 values).
	/// </summary>
	[TestMethod]
	public void Inset_SingleValue()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "inset", "0" ) );
		Assert.AreEqual( 0, s.Top.Value.Value );
		Assert.AreEqual( 0, s.Right.Value.Value );
		Assert.AreEqual( 0, s.Bottom.Value.Value );
		Assert.AreEqual( 0, s.Left.Value.Value );
	}

	[TestMethod]
	public void Inset_TwoValues()
	{
		// top/bottom = 10px, left/right = 20px
		var s = new Styles();
		Assert.IsTrue( s.Set( "inset", "10px 20px" ) );
		Assert.AreEqual( 10, s.Top.Value.Value );
		Assert.AreEqual( 20, s.Right.Value.Value );
		Assert.AreEqual( 10, s.Bottom.Value.Value );
		Assert.AreEqual( 20, s.Left.Value.Value );
	}

	// ---------------------------------------------------------------------
	// margin with calc() and multiple values
	// ---------------------------------------------------------------------

	/// <summary>
	/// 'margin: calc(100% - 20px) auto' - calc() is one token, then 'auto' applies to left/right.
	/// </summary>
	[TestMethod]
	public void Margin_CalcWithAuto()
	{
		var s = new Styles();
		s.Set( "margin", "calc(100% - 20px) auto" );
		Assert.IsTrue( s.MarginTop.HasValue && s.MarginTop.Value.Unit == LengthUnit.Expression );
		Assert.IsTrue( s.MarginLeft.HasValue && s.MarginLeft.Value.Unit == LengthUnit.Auto, "margin 'auto' after calc() not parsed" );
	}

	[TestMethod]
	public void Margin_CalcSingleValue()
	{
		// A single calc() value (no following tokens) parses as an Expression length on all sides.
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin", "calc(100% - 20px)" ) );

		Assert.IsTrue( s.MarginTop.HasValue );
		Assert.AreEqual( LengthUnit.Expression, s.MarginTop.Value.Unit );
		Assert.AreEqual( LengthUnit.Expression, s.MarginRight.Value.Unit );
		Assert.AreEqual( LengthUnit.Expression, s.MarginBottom.Value.Unit );
		Assert.AreEqual( LengthUnit.Expression, s.MarginLeft.Value.Unit );
	}

	/// <summary>
	/// The single-side logical box properties alias onto their physical equivalents.
	/// </summary>
	[TestMethod]
	public void LogicalSides()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-block-start", "1px" ) );
		Assert.IsTrue( s.Set( "margin-block-end", "2px" ) );
		Assert.IsTrue( s.Set( "margin-inline-start", "3px" ) );
		Assert.IsTrue( s.Set( "margin-inline-end", "4px" ) );

		Assert.AreEqual( 1, s.MarginTop.Value.Value );
		Assert.AreEqual( 2, s.MarginBottom.Value.Value );
		Assert.AreEqual( 3, s.MarginLeft.Value.Value );
		Assert.AreEqual( 4, s.MarginRight.Value.Value );

		Assert.IsTrue( s.Set( "inset-block-start", "5px" ) );
		Assert.IsTrue( s.Set( "inset-inline-end", "6px" ) );
		Assert.AreEqual( 5, s.Top.Value.Value );
		Assert.AreEqual( 6, s.Right.Value.Value );
	}

	/// <summary>
	/// The block/inline logical shorthands set both sides of one axis (one or two values).
	/// </summary>
	[TestMethod]
	public void LogicalAxisShorthands()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "margin-block", "5px 10px" ) );
		Assert.AreEqual( 5, s.MarginTop.Value.Value );
		Assert.AreEqual( 10, s.MarginBottom.Value.Value );

		Assert.IsTrue( s.Set( "margin-inline", "8px" ) );
		Assert.AreEqual( 8, s.MarginLeft.Value.Value );
		Assert.AreEqual( 8, s.MarginRight.Value.Value );

		Assert.IsTrue( s.Set( "padding-block", "2px 4px" ) );
		Assert.AreEqual( 2, s.PaddingTop.Value.Value );
		Assert.AreEqual( 4, s.PaddingBottom.Value.Value );

		Assert.IsTrue( s.Set( "inset-inline", "7px" ) );
		Assert.AreEqual( 7, s.Left.Value.Value );
		Assert.AreEqual( 7, s.Right.Value.Value );
	}
}
