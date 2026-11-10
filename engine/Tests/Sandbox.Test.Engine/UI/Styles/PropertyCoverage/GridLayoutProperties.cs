using Sandbox.UI;

namespace UITests.PropertyCoverage;

[TestClass]
public class GridLayoutPropertiesTest
{
	// =====================================================================
	// display -> Display : DisplayMode? { Block, Grid }
	// =====================================================================

	[TestMethod]
	public void Display_Block()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "display", "block" ) );
		Assert.AreEqual( DisplayMode.Block, s.Display );
	}

	[TestMethod]
	public void Display_FlowRoot_IsBlock()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "display", "flow-root" ) );
		Assert.AreEqual( DisplayMode.Block, s.Display );
	}

	[TestMethod]
	public void Display_Grid()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "display", "grid" ) );
		Assert.AreEqual( DisplayMode.Grid, s.Display );
	}

	// =====================================================================
	// grid-template-columns / rows, grid-auto-columns / rows -> string (parsed by the layout engine)
	// =====================================================================

	[TestMethod]
	public void GridTemplateColumns_KeepsText()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-template-columns", "repeat( 3, 1fr ) minmax( 100px, auto )" ) );
		Assert.AreEqual( "repeat( 3, 1fr ) minmax( 100px, auto )", s.GridTemplateColumns );
	}

	[TestMethod]
	public void GridTemplateRows_Default_IsNone()
	{
		var s = new Styles();
		s.FillDefaults();
		Assert.AreEqual( "none", s.GridTemplateRows );
	}

	[TestMethod]
	public void GridDefaultsDoNotChangeLegacyStringDefaults()
	{
		var s = new Styles();
		s.FillDefaults();
		Assert.IsNull( s.Content );
		Assert.IsNull( s.Cursor );
		Assert.IsNull( s.FontFamily );
		Assert.IsNull( s.AnimationName );
		Assert.IsNull( s.AnimationDirection );
		Assert.IsNull( s.AnimationFillMode );
		Assert.IsNull( s.AnimationPlayState );
		Assert.IsNull( s.AnimationTimingFunction );
		Assert.AreEqual( BorderShape.None, s.BorderShape );
	}

	[DataTestMethod]
	[DataRow( "grid-template-columns", "none", "1fr 2fr" )]
	[DataRow( "grid-template-rows", "none", "40px auto" )]
	[DataRow( "grid-auto-columns", "auto", "80px" )]
	[DataRow( "grid-auto-rows", "auto", "60px" )]
	[DataRow( "grid-column-start", "auto", "2" )]
	[DataRow( "grid-column-end", "auto", "span 3" )]
	[DataRow( "grid-row-start", "auto", "header" )]
	[DataRow( "grid-row-end", "auto", "4" )]
	public void GridStrings_DefaultsAndCopies( string property, string initial, string custom )
	{
		var field = BaseStyles.GetStyleField( property );
		var styles = new Styles();
		Assert.IsNull( field.GetValue( styles ) );
		styles.FillDefaults();
		Assert.AreEqual( initial, field.GetValue( styles ) );
		Assert.IsTrue( styles.IsDefault( property ) );

		Assert.IsTrue( styles.Set( property, custom ) );
		styles.FillDefaults();
		Assert.AreEqual( custom, field.GetValue( styles ) );
		Assert.IsFalse( styles.IsDefault( property ) );

		var added = new Styles();
		added.Add( styles );
		added.Add( new Styles() );
		var copied = new Styles();
		copied.From( styles );
		foreach ( var copy in new[] { added, copied, (Styles)styles.Clone() } )
		{
			Assert.AreEqual( custom, field.GetValue( copy ) );
			Assert.IsFalse( copy.IsDefault( property ) );
			copy.From( new Styles() );
			Assert.IsNull( field.GetValue( copy ) );
			copy.FillDefaults();
			Assert.AreEqual( initial, field.GetValue( copy ) );
			Assert.IsTrue( copy.IsDefault( property ) );
		}

		foreach ( var keyword in new[] { "initial", "unset" } )
		{
			Assert.IsTrue( styles.Set( property, keyword ) );
			styles.ResolveCssWide( new Styles() );
			Assert.AreEqual( initial, field.GetValue( styles ) );
			Assert.IsTrue( styles.IsDefault( property ) );
		}
	}

	[TestMethod]
	public void GridAutoRows_KeepsText()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-auto-rows", "minmax( 40px, auto )" ) );
		Assert.AreEqual( "minmax( 40px, auto )", s.GridAutoRows );
	}

	[TestMethod]
	public void GridTemplate_Shorthand_SplitsRowsAndColumns()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-template", "auto 1fr / 200px 1fr" ) );
		Assert.AreEqual( "auto 1fr", s.GridTemplateRows );
		Assert.AreEqual( "200px 1fr", s.GridTemplateColumns );
	}

	// =====================================================================
	// grid-auto-flow -> GridAutoFlow
	// =====================================================================

	[TestMethod]
	public void GridAutoFlow_Row()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-auto-flow", "row" ) );
		Assert.AreEqual( GridAutoFlow.Row, s.GridAutoFlow );
	}

	[TestMethod]
	public void GridAutoFlow_ColumnDense()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-auto-flow", "column dense" ) );
		Assert.AreEqual( GridAutoFlow.ColumnDense, s.GridAutoFlow );
	}

	[TestMethod]
	public void GridAutoFlow_Dense_IsRowDense()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-auto-flow", "dense" ) );
		Assert.AreEqual( GridAutoFlow.RowDense, s.GridAutoFlow );
	}

	[TestMethod]
	public void GridAutoFlow_Invalid_ReturnsFalse()
	{
		var s = new Styles();
		Assert.IsFalse( s.Set( "grid-auto-flow", "row column" ) );
		Assert.AreEqual( null, s.GridAutoFlow );
	}

	// =====================================================================
	// grid-column / grid-row / grid-area placement shorthands -> strings
	// =====================================================================

	[TestMethod]
	public void GridColumn_StartAndEnd()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-column", "2 / span 3" ) );
		Assert.AreEqual( "2", s.GridColumnStart );
		Assert.AreEqual( "span 3", s.GridColumnEnd );
	}

	[TestMethod]
	public void GridColumn_StartOnly_EndIsAuto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-column", "3" ) );
		Assert.AreEqual( "3", s.GridColumnStart );
		Assert.AreEqual( "auto", s.GridColumnEnd );
	}

	[TestMethod]
	public void GridRow_NamedLine_AppliesToBothEdges()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-row", "sidebar" ) );
		Assert.AreEqual( "sidebar", s.GridRowStart );
		Assert.AreEqual( "sidebar", s.GridRowEnd );
	}

	[TestMethod]
	public void GridArea_FourValues()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-area", "1 / 2 / 3 / 4" ) );
		Assert.AreEqual( "1", s.GridRowStart );
		Assert.AreEqual( "2", s.GridColumnStart );
		Assert.AreEqual( "3", s.GridRowEnd );
		Assert.AreEqual( "4", s.GridColumnEnd );
	}

	[TestMethod]
	public void GridArea_TwoValues_EndsAreAuto()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "grid-area", "2 / 1" ) );
		Assert.AreEqual( "2", s.GridRowStart );
		Assert.AreEqual( "1", s.GridColumnStart );
		Assert.AreEqual( "auto", s.GridRowEnd );
		Assert.AreEqual( "auto", s.GridColumnEnd );
	}

	// =====================================================================
	// justify-items / justify-self / place-* -> Align
	// =====================================================================

	[TestMethod]
	public void JustifyItems_Center()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-items", "center" ) );
		Assert.AreEqual( Align.Center, s.JustifyItems );
	}

	[TestMethod]
	public void JustifySelf_End()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "justify-self", "flex-end" ) );
		Assert.AreEqual( Align.FlexEnd, s.JustifySelf );
	}

	[TestMethod]
	public void PlaceItems_TwoValues()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "place-items", "center stretch" ) );
		Assert.AreEqual( Align.Center, s.AlignItems );
		Assert.AreEqual( Align.Stretch, s.JustifyItems );
	}

	[TestMethod]
	public void PlaceSelf_OneValue_AppliesToBoth()
	{
		var s = new Styles();
		Assert.IsTrue( s.Set( "place-self", "flex-start" ) );
		Assert.AreEqual( Align.FlexStart, s.AlignSelf );
		Assert.AreEqual( Align.FlexStart, s.JustifySelf );
	}
}
