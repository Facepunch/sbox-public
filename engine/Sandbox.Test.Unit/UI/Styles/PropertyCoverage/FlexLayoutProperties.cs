using Sandbox.UI;

namespace UITest.PropertyCoverage;

[TestClass]
public class FlexLayoutProperties
{
	// =====================================================================
	// display -> Display : DisplayMode? { Flex, None, Contents }
	// =====================================================================

	[TestMethod]
	public void Display_Flex()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "display", "flex" ) );
		Assert.AreEqual( DisplayMode.Flex, s.Display );
	}

	[TestMethod]
	public void Display_None()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "display", "none" ) );
		Assert.AreEqual( DisplayMode.None, s.Display );
	}

	[TestMethod]
	public void Display_Contents()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "display", "contents" ) );
		Assert.AreEqual( DisplayMode.Contents, s.Display );
	}

	[TestMethod]
	public void Display_Invalid_ReturnsFalseAndNull()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "display", "bullshit" ) );
		Assert.AreEqual( null, s.Display );
	}

	// =====================================================================
	// flex-direction -> FlexDirection : { Row, Column, RowReverse, ColumnReverse }
	// =====================================================================

	[TestMethod]
	public void FlexDirection_Row()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-direction", "row" ) );
		Assert.AreEqual( FlexDirection.Row, s.FlexDirection );
	}

	[TestMethod]
	public void FlexDirection_Column()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-direction", "column" ) );
		Assert.AreEqual( FlexDirection.Column, s.FlexDirection );
	}

	[TestMethod]
	public void FlexDirection_RowReverse()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-direction", "row-reverse" ) );
		Assert.AreEqual( FlexDirection.RowReverse, s.FlexDirection );
	}

	[TestMethod]
	public void FlexDirection_ColumnReverse()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-direction", "column-reverse" ) );
		Assert.AreEqual( FlexDirection.ColumnReverse, s.FlexDirection );
	}

	[TestMethod]
	public void FlexDirection_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "flex-direction", "diagonal" ) );
		Assert.AreEqual( null, s.FlexDirection );
	}

	// =====================================================================
	// flex-wrap -> FlexWrap : Wrap { NoWrap, Wrap, WrapReverse }
	// =====================================================================

	[TestMethod]
	public void FlexWrap_NoWrap()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-wrap", "nowrap" ) );
		Assert.AreEqual( Wrap.NoWrap, s.FlexWrap );
	}

	[TestMethod]
	public void FlexWrap_Wrap()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-wrap", "wrap" ) );
		Assert.AreEqual( Wrap.Wrap, s.FlexWrap );
	}

	[TestMethod]
	public void FlexWrap_WrapReverse()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-wrap", "wrap-reverse" ) );
		Assert.AreEqual( Wrap.WrapReverse, s.FlexWrap );
	}

	[TestMethod]
	public void FlexWrap_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "flex-wrap", "maybe" ) );
		Assert.AreEqual( null, s.FlexWrap );
	}

	// =====================================================================
	// flex (shorthand) -> FlexGrow, FlexShrink, FlexBasis
	// Expansions sourced from SetFlex in Styles.Set.cs
	// =====================================================================

	[TestMethod]
	public void Flex_None_Expands_0_0_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "none" ) );
		Assert.AreEqual( 0.0f, s.FlexGrow );
		Assert.AreEqual( 0.0f, s.FlexShrink );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.FlexBasis.Value.Unit );
	}

	[TestMethod]
	public void Flex_Auto_Expands_1_1_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "auto" ) );
		Assert.AreEqual( 1.0f, s.FlexGrow );
		Assert.AreEqual( 1.0f, s.FlexShrink );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.FlexBasis.Value.Unit );
	}

	[TestMethod]
	public void Flex_Initial_Expands_0_1_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "initial" ) );
		Assert.AreEqual( 1.0f, s.FlexGrow );
		Assert.AreEqual( 0.0f, s.FlexShrink );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.FlexBasis.Value.Unit );
	}

	[TestMethod]
	public void Flex_SingleNumberOne_Expands_1_1_0()
	{
		// "flex: 1" expands to grow=1 shrink=1 basis=0
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "1" ) );
		Assert.AreEqual( 1.0f, s.FlexGrow );
		Assert.AreEqual( 1.0f, s.FlexShrink );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( 0, s.FlexBasis.Value.Value );
	}

	[TestMethod]
	public void Flex_ThreeValue_1_1_0()
	{
		// GAP: 'flex: 1 1 0' should expand to grow=1 shrink=1 basis=0. SetFlex tries float.TryParse
		// before TryReadLength, so the basis '0' (a unitless number) is read as a third float and
		// overwrites FlexShrink with 0 instead of being treated as the basis length - shrink ends 0.
		var s = new Styles();
		s.Set( "flex", "1 1 0" );
		Assert.AreEqual( 1.0f, s.FlexGrow );
		Assert.AreEqual( 1.0f, s.FlexShrink );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( 0, s.FlexBasis.Value.Value );
	}

	[TestMethod]
	public void Flex_ThreeValue_0_0_100px()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "0 0 100px" ) );
		Assert.AreEqual( 0.0f, s.FlexGrow );
		Assert.AreEqual( 0.0f, s.FlexShrink );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( 100, s.FlexBasis.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FlexBasis.Value.Unit );
	}

	[TestMethod]
	public void Flex_TwoValue_Grow_Shrink()
	{
		// "flex: 2 3" -> grow=2, shrink=3 (basis untouched - see gap test for the basis behaviour)
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "2 3" ) );
		Assert.AreEqual( 2.0f, s.FlexGrow );
		Assert.AreEqual( 3.0f, s.FlexShrink );
	}

	[TestMethod]
	public void Flex_GrowAndBasis()
	{
		// "flex: 0 30px" -> grow=0, then 30px read as length -> basis=30px (shrink defaults to 1)
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "0 30px" ) );
		Assert.AreEqual( 0.0f, s.FlexGrow );
		Assert.AreEqual( 1.0f, s.FlexShrink );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( 30, s.FlexBasis.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FlexBasis.Value.Unit );
	}

	[TestMethod]
	public void Flex_SingleNumberTwo_SetsGrow()
	{
		// GAP: per CSS spec, "flex: 2" should expand to grow=2 shrink=1 basis=0.
		// Current engine sets grow=2 but leaves basis null (auto) instead of 0.
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex", "2" ) );
		Assert.AreEqual( 2.0f, s.FlexGrow );
		// GAP: s.FlexBasis.Value.Value == 0 (web spec) vs FlexBasis stays auto/null (current)
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( 0, s.FlexBasis.Value.Value );
	}

	// =====================================================================
	// flex-grow -> FlexGrow (float?)
	// =====================================================================

	[TestMethod]
	public void FlexGrow_Integer()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-grow", "2" ) );
		Assert.AreEqual( 2.0f, s.FlexGrow );
	}

	[TestMethod]
	public void FlexGrow_Fraction()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-grow", "0.5" ) );
		Assert.AreEqual( 0.5f, s.FlexGrow );
	}

	[TestMethod]
	public void FlexGrow_Zero()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-grow", "0" ) );
		Assert.AreEqual( 0.0f, s.FlexGrow );
	}

	// =====================================================================
	// flex-shrink -> FlexShrink (float?)
	// =====================================================================

	[TestMethod]
	public void FlexShrink_Integer()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-shrink", "3" ) );
		Assert.AreEqual( 3.0f, s.FlexShrink );
	}

	[TestMethod]
	public void FlexShrink_Fraction()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-shrink", "0.25" ) );
		Assert.AreEqual( 0.25f, s.FlexShrink );
	}

	// =====================================================================
	// flex-basis -> FlexBasis (Length?)
	// =====================================================================

	[TestMethod]
	public void FlexBasis_Pixels()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-basis", "120px" ) );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( 120, s.FlexBasis.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, s.FlexBasis.Value.Unit );
	}

	[TestMethod]
	public void FlexBasis_Percent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-basis", "50%" ) );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( 50, s.FlexBasis.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, s.FlexBasis.Value.Unit );
	}

	[TestMethod]
	public void FlexBasis_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-basis", "auto" ) );
		Assert.IsTrue( s.FlexBasis.HasValue );
		Assert.AreEqual( LengthUnit.Auto, s.FlexBasis.Value.Unit );
	}

	// =====================================================================
	// justify-content -> JustifyContent : Justify
	// =====================================================================

	[TestMethod]
	public void JustifyContent_FlexStart()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "flex-start" ) );
		Assert.AreEqual( Justify.FlexStart, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_Center()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "center" ) );
		Assert.AreEqual( Justify.Center, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_FlexEnd()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "flex-end" ) );
		Assert.AreEqual( Justify.FlexEnd, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_SpaceBetween()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "space-between" ) );
		Assert.AreEqual( Justify.SpaceBetween, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_SpaceAround()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "space-around" ) );
		Assert.AreEqual( Justify.SpaceAround, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_SpaceEvenly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "space-evenly" ) );
		Assert.AreEqual( Justify.SpaceEvenly, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "justify-content", "middle" ) );
		Assert.AreEqual( null, s.JustifyContent );
	}

	/// <summary>
	/// start/left/normal map to FlexStart; end/right map to FlexEnd.
	/// </summary>
	[TestMethod]
	public void JustifyContent_Start()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "start" ) );
		Assert.AreEqual( Justify.FlexStart, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_End()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "end" ) );
		Assert.AreEqual( Justify.FlexEnd, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_Left()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "left" ) );
		Assert.AreEqual( Justify.FlexStart, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_Right()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "right" ) );
		Assert.AreEqual( Justify.FlexEnd, s.JustifyContent );
	}

	[TestMethod]
	public void JustifyContent_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "normal" ) );
		Assert.AreEqual( Justify.FlexStart, s.JustifyContent );
	}

	/// <summary>
	/// justify-content: stretch falls back to flex-start for flex layout.
	/// </summary>
	[TestMethod]
	public void JustifyContent_Stretch()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-content", "stretch" ) );
		Assert.AreEqual( Justify.FlexStart, s.JustifyContent );
	}

	// =====================================================================
	// align-items -> AlignItems : Align
	// =====================================================================

	[TestMethod]
	public void AlignItems_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "auto" ) );
		Assert.AreEqual( Align.Auto, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_FlexStart()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "flex-start" ) );
		Assert.AreEqual( Align.FlexStart, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_FlexEnd()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "flex-end" ) );
		Assert.AreEqual( Align.FlexEnd, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_Center()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "center" ) );
		Assert.AreEqual( Align.Center, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_Stretch()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "stretch" ) );
		Assert.AreEqual( Align.Stretch, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_Baseline()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "baseline" ) );
		Assert.AreEqual( Align.Baseline, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_SpaceBetween()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "space-between" ) );
		Assert.AreEqual( Align.SpaceBetween, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_SpaceAround()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "space-around" ) );
		Assert.AreEqual( Align.SpaceAround, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_SpaceEvenly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "space-evenly" ) );
		Assert.AreEqual( Align.SpaceEvenly, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "align-items", "middle" ) );
		Assert.AreEqual( null, s.AlignItems );
	}

	/// <summary>
	/// start/self-start map to FlexStart, end/self-end to FlexEnd, normal to Stretch.
	/// </summary>
	[TestMethod]
	public void AlignItems_Start()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "start" ) );
		Assert.AreEqual( Align.FlexStart, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_End()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "end" ) );
		Assert.AreEqual( Align.FlexEnd, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_Normal()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "normal" ) );
		Assert.AreEqual( Align.Stretch, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_SelfStart()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "self-start" ) );
		Assert.AreEqual( Align.FlexStart, s.AlignItems );
	}

	[TestMethod]
	public void AlignItems_SelfEnd()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-items", "self-end" ) );
		Assert.AreEqual( Align.FlexEnd, s.AlignItems );
	}

	// =====================================================================
	// align-self -> AlignSelf : Align
	// =====================================================================

	[TestMethod]
	public void AlignSelf_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-self", "auto" ) );
		Assert.AreEqual( Align.Auto, s.AlignSelf );
	}

	[TestMethod]
	public void AlignSelf_FlexStart()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-self", "flex-start" ) );
		Assert.AreEqual( Align.FlexStart, s.AlignSelf );
	}

	[TestMethod]
	public void AlignSelf_FlexEnd()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-self", "flex-end" ) );
		Assert.AreEqual( Align.FlexEnd, s.AlignSelf );
	}

	[TestMethod]
	public void AlignSelf_Center()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-self", "center" ) );
		Assert.AreEqual( Align.Center, s.AlignSelf );
	}

	[TestMethod]
	public void AlignSelf_Stretch()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-self", "stretch" ) );
		Assert.AreEqual( Align.Stretch, s.AlignSelf );
	}

	[TestMethod]
	public void AlignSelf_Baseline()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-self", "baseline" ) );
		Assert.AreEqual( Align.Baseline, s.AlignSelf );
	}

	[TestMethod]
	public void AlignSelf_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "align-self", "nonsense" ) );
		Assert.AreEqual( null, s.AlignSelf );
	}

	// =====================================================================
	// align-content -> AlignContent : Align
	// =====================================================================

	[TestMethod]
	public void AlignContent_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "auto" ) );
		Assert.AreEqual( Align.Auto, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_FlexStart()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "flex-start" ) );
		Assert.AreEqual( Align.FlexStart, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_FlexEnd()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "flex-end" ) );
		Assert.AreEqual( Align.FlexEnd, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_Center()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "center" ) );
		Assert.AreEqual( Align.Center, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_Stretch()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "stretch" ) );
		Assert.AreEqual( Align.Stretch, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_SpaceBetween()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "space-between" ) );
		Assert.AreEqual( Align.SpaceBetween, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_SpaceAround()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "space-around" ) );
		Assert.AreEqual( Align.SpaceAround, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_SpaceEvenly()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "space-evenly" ) );
		Assert.AreEqual( Align.SpaceEvenly, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_Baseline()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "align-content", "baseline" ) );
		Assert.AreEqual( Align.Baseline, s.AlignContent );
	}

	[TestMethod]
	public void AlignContent_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "align-content", "nonsense" ) );
		Assert.AreEqual( null, s.AlignContent );
	}

	// =====================================================================
	// position -> Position : PositionMode { Static, Relative, Absolute }
	// =====================================================================

	[TestMethod]
	public void Position_Static()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "position", "static" ) );
		Assert.AreEqual( PositionMode.Static, s.Position );
	}

	[TestMethod]
	public void Position_Relative()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "position", "relative" ) );
		Assert.AreEqual( PositionMode.Relative, s.Position );
	}

	[TestMethod]
	public void Position_Absolute()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "position", "absolute" ) );
		Assert.AreEqual( PositionMode.Absolute, s.Position );
	}

	[TestMethod]
	public void Position_Invalid_ReturnsFalseAndNull()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "position", "bullshit" ) );
		Assert.AreEqual( null, s.Position );
	}

	// =====================================================================
	// overflow -> Overflow : OverflowMode { Visible, Hidden, Scroll, Clip, ClipWhole }
	// =====================================================================

	[TestMethod]
	public void Overflow_Visible()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow", "visible" ) );
		Assert.AreEqual( OverflowMode.Visible, s.Overflow );
	}

	[TestMethod]
	public void Overflow_Hidden()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow", "hidden" ) );
		Assert.AreEqual( OverflowMode.Hidden, s.Overflow );
	}

	[TestMethod]
	public void Overflow_Scroll()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow", "scroll" ) );
		Assert.AreEqual( OverflowMode.Scroll, s.Overflow );
	}

	[TestMethod]
	public void Overflow_Clip()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow", "clip" ) );
		Assert.AreEqual( OverflowMode.Clip, s.Overflow );
	}

	[TestMethod]
	public void Overflow_ClipWhole()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow", "clip-whole" ) );
		Assert.AreEqual( OverflowMode.ClipWhole, s.Overflow );
	}

	[TestMethod]
	public void Overflow_Shorthand_SetsBothAxes()
	{
		// The overflow shorthand sets both OverflowX and OverflowY.
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow", "hidden" ) );
		Assert.AreEqual( OverflowMode.Hidden, s.OverflowX );
		Assert.AreEqual( OverflowMode.Hidden, s.OverflowY );
	}

	[TestMethod]
	public void Overflow_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "overflow", "nonsense" ) );
	}

	/// <summary>
	/// overflow: auto maps to scroll (we have no scroll-when-needed mode).
	/// </summary>
	[TestMethod]
	public void Overflow_Auto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow", "auto" ) );
		Assert.AreEqual( OverflowMode.Scroll, s.Overflow );
	}

	// =====================================================================
	// overflow-x -> OverflowX : OverflowMode
	// =====================================================================

	[TestMethod]
	public void OverflowX_Hidden()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-x", "hidden" ) );
		Assert.AreEqual( OverflowMode.Hidden, s.OverflowX );
	}

	[TestMethod]
	public void OverflowX_Scroll()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-x", "scroll" ) );
		Assert.AreEqual( OverflowMode.Scroll, s.OverflowX );
	}

	[TestMethod]
	public void OverflowX_Visible()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-x", "visible" ) );
		Assert.AreEqual( OverflowMode.Visible, s.OverflowX );
	}

	[TestMethod]
	public void OverflowX_Clip()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-x", "clip" ) );
		Assert.AreEqual( OverflowMode.Clip, s.OverflowX );
	}

	[TestMethod]
	public void OverflowX_ClipWhole()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-x", "clip-whole" ) );
		Assert.AreEqual( OverflowMode.ClipWhole, s.OverflowX );
	}

	[TestMethod]
	public void OverflowX_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "overflow-x", "nonsense" ) );
	}

	// =====================================================================
	// overflow-y -> OverflowY : OverflowMode
	// =====================================================================

	[TestMethod]
	public void OverflowY_Hidden()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-y", "hidden" ) );
		Assert.AreEqual( OverflowMode.Hidden, s.OverflowY );
	}

	[TestMethod]
	public void OverflowY_Scroll()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-y", "scroll" ) );
		Assert.AreEqual( OverflowMode.Scroll, s.OverflowY );
	}

	[TestMethod]
	public void OverflowY_Visible()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-y", "visible" ) );
		Assert.AreEqual( OverflowMode.Visible, s.OverflowY );
	}

	[TestMethod]
	public void OverflowY_Clip()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-y", "clip" ) );
		Assert.AreEqual( OverflowMode.Clip, s.OverflowY );
	}

	[TestMethod]
	public void OverflowY_ClipWhole()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-y", "clip-whole" ) );
		Assert.AreEqual( OverflowMode.ClipWhole, s.OverflowY );
	}

	[TestMethod]
	public void OverflowY_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "overflow-y", "nonsense" ) );
	}

	// =====================================================================
	// overflow-x / overflow-y independence: setting one axis only affects that axis,
	// and a scroll on either axis surfaces through the combined Overflow getter.
	// =====================================================================

	[TestMethod]
	public void OverflowAxes_Independent()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "overflow-x", "hidden" ) );
		Assert.IsTrue( s.Set( "overflow-y", "scroll" ) );
		Assert.AreEqual( OverflowMode.Hidden, s.OverflowX );
		Assert.AreEqual( OverflowMode.Scroll, s.OverflowY );
		// Overflow getter returns Scroll if either axis is Scroll.
		Assert.AreEqual( OverflowMode.Scroll, s.Overflow );
	}

	/// <summary>
	/// flex-flow sets flex-direction and flex-wrap together, in any order.
	/// </summary>
	[TestMethod]
	public void FlexFlow()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "flex-flow", "column wrap" ) );
		Assert.AreEqual( Sandbox.UI.FlexDirection.Column, s.FlexDirection.Value );
		Assert.AreEqual( Wrap.Wrap, s.FlexWrap.Value );

		var s2 = new Styles();
		Assert.IsTrue( s2.Set( "flex-flow", "wrap-reverse row-reverse" ) );
		Assert.AreEqual( Sandbox.UI.FlexDirection.RowReverse, s2.FlexDirection.Value );
		Assert.AreEqual( Wrap.WrapReverse, s2.FlexWrap.Value );
	}
}
