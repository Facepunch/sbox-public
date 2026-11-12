using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize]
public class InlineStyleReliabilityTests
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	static Panel CreatePanel( out RootPanel root )
	{
		root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		root.StyleSheet.Parse( ".target { width: 80px; height: 20px; }" );
		var panel = new Panel { Parent = root };
		panel.AddClass( "target" );
		return panel;
	}

	static void AssertWidth( Panel panel, RootPanel root, float expected )
	{
		root.Layout();
		Assert.AreEqual( expected, panel.Style.Width.Value.Value );
		Assert.AreEqual( expected, panel.ComputedStyle.Width.Value.Value );
	}

	[TestMethod]
	public void LaterSetterReturningToAttributeValueSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Width = 300;
		panel.Style.Width = 240;

		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 240 );
	}

	[TestMethod]
	public void LaterSetterOfUnchangedValueSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Width = 240;

		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 240 );
	}

	[TestMethod]
	public void LaterStyleSetReturningToAttributeValueSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		Assert.IsTrue( panel.Style.Set( "width: 300px;" ) );
		Assert.IsTrue( panel.Style.Set( "width: 240px;" ) );

		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 240 );
	}

	[TestMethod]
	public void LaterNullSetterSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Width = null;

		panel.SetProperty( "style", null );
		root.Layout();
		Assert.IsNull( panel.Style.Width );
		Assert.AreEqual( 80f, panel.ComputedStyle.Width.Value.Value );
	}

	[TestMethod]
	public void LaterShorthandOfUnchangedValueSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Padding = 5;
		panel.SetProperty( "style", "padding: 12px;" );
		panel.Style.Padding = 12;

		panel.SetProperty( "style", null );
		root.Layout();
		Assert.AreEqual( 12f, panel.Style.PaddingLeft.Value.Value );
		Assert.AreEqual( 12f, panel.Style.PaddingTop.Value.Value );
		Assert.AreEqual( 12f, panel.Style.PaddingRight.Value.Value );
		Assert.AreEqual( 12f, panel.Style.PaddingBottom.Value.Value );
		Assert.AreEqual( 12f, panel.ComputedStyle.PaddingLeft.Value.Value );
		Assert.AreEqual( 12f, panel.ComputedStyle.PaddingTop.Value.Value );
		Assert.AreEqual( 12f, panel.ComputedStyle.PaddingRight.Value.Value );
		Assert.AreEqual( 12f, panel.ComputedStyle.PaddingBottom.Value.Value );
	}

	[TestMethod]
	[DataRow( "inherit" )]
	[DataRow( "initial" )]
	public void LaterSetterHistoryClearsAttributeKeyword( string keyword )
	{
		var panel = CreatePanel( out var root );
		root.Style.Width = 400;
		panel.Style.Width = 120;
		panel.SetProperty( "style", $"width: {keyword};" );
		Assert.IsTrue( panel.Style.CssWide.ContainsKey( "width" ) );
		panel.Style.Width = 300;
		panel.Style.Width = 240;

		panel.SetProperty( "style", null );
		Assert.IsTrue( panel.Style.CssWide is null || !panel.Style.CssWide.ContainsKey( "width" ) );
		AssertWidth( panel, root, 240 );
	}

	[TestMethod]
	public void UnrelatedSetterDoesNotKeepRemovedAttributeProperty()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Height = 30;

		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 120 );
		Assert.AreEqual( 30f, panel.Style.Height.Value.Value );
		Assert.AreEqual( 30f, panel.ComputedStyle.Height.Value.Value );
	}

	[TestMethod]
	[DataRow( "width: 240px;", 240f )]
	[DataRow( "width: 360px;", 360f )]
	public void LaterAttributeAppliesThenRestoresLatestProgrammaticWrite( string nextAttribute, float nextWidth )
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Width = 240;

		panel.SetProperty( "style", nextAttribute );
		AssertWidth( panel, root, nextWidth );
		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 240 );
	}

	[TestMethod]
	public void LaterFromReplacesAttributeValuesAndSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.Style.Height = 70;
		panel.SetProperty( "style", "width: 240px; height: 40px; color: red;" );
		panel.Style.From( new Styles { Width = 240, FontColor = Color.Blue } );

		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 240 );
		Assert.IsNull( panel.Style.Height );
		Assert.AreEqual( 20f, panel.ComputedStyle.Height.Value.Value );
		Assert.AreEqual( Color.Blue, panel.Style.FontColor.Value );
		Assert.AreEqual( Color.Blue, panel.ComputedStyle.FontColor.Value );
	}

	[TestMethod]
	public void LaterAddOfUnchangedValueDoesNotKeepUnrelatedAttribute()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px; height: 40px;" );
		panel.Style.Add( new Styles { Width = 240 } );

		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 240 );
		Assert.IsNull( panel.Style.Height );
		Assert.AreEqual( 20f, panel.ComputedStyle.Height.Value.Value );
	}

	[TestMethod]
	public void LaterSetOfUnchangedKeywordSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		root.Style.Width = 400;
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: inherit;" );
		Assert.IsTrue( panel.Style.Set( "width", "inherit" ) );

		panel.SetProperty( "style", null );
		root.Layout();
		Assert.IsNotNull( panel.Style.CssWide );
		Assert.IsTrue( panel.Style.CssWide.ContainsKey( "width" ) );
		Assert.AreEqual( CssWideKeyword.Inherit, panel.Style.CssWide["width"] );
		Assert.AreEqual( root.ComputedStyle.Width.Value.Value, panel.ComputedStyle.Width.Value.Value );
	}

	[TestMethod]
	[DataRow( "inherit" )]
	[DataRow( "initial" )]
	public void UnchangedTypedSetterImmediatelyReplacesAttributeKeyword( string keyword )
	{
		var panel = CreatePanel( out var root );
		root.Style.Width = 400;
		panel.Style.Width = 240;
		panel.SetProperty( "style", $"width: {keyword};" );
		root.Layout();

		panel.Style.Width = 240;
		AssertWidth( panel, root, 240 );
		Assert.IsTrue( panel.Style.CssWide is null || !panel.Style.CssWide.ContainsKey( "width" ) );
		panel.SetProperty( "style", null );
		AssertWidth( panel, root, 240 );
	}

	[TestMethod]
	public void LaterFontColorSetterOfUnchangedAliasSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.FontColor = Color.Red;
		panel.SetProperty( "style", "color: blue;" );
		panel.Style.FontColor = Color.Blue;

		panel.SetProperty( "style", null );
		root.Layout();
		Assert.AreEqual( Color.Blue, panel.Style.FontColor.Value );
		Assert.AreEqual( Color.Blue, panel.ComputedStyle.FontColor.Value );
	}

	[TestMethod]
	public void LaterOverflowSetterOfUnchangedValueSurvivesRemoval()
	{
		var panel = CreatePanel( out var root );
		panel.Style.Overflow = OverflowMode.Visible;
		panel.SetProperty( "style", "overflow: hidden;" );
		panel.Style.Overflow = OverflowMode.Hidden;

		panel.SetProperty( "style", null );
		root.Layout();
		Assert.AreEqual( OverflowMode.Hidden, panel.Style.OverflowX.Value );
		Assert.AreEqual( OverflowMode.Hidden, panel.Style.OverflowY.Value );
		Assert.AreEqual( OverflowMode.Hidden, panel.ComputedStyle.OverflowX.Value );
		Assert.AreEqual( OverflowMode.Hidden, panel.ComputedStyle.OverflowY.Value );
	}

	[TestMethod]
	public void LaterTransitionReturningToAttributeValueSurvivesRemoval()
	{
		// Composite declarations are checked without starting the texture renderer.
		var panel = new Panel();
		Assert.IsTrue( panel.Style.Set( "transition: opacity 1s;" ) );
		panel.SetProperty( "style", "transition: opacity 2s;" );
		Assert.IsTrue( panel.Style.Set( "transition: opacity 3s;" ) );
		Assert.IsTrue( panel.Style.Set( "transition: opacity 2s;" ) );

		panel.SetProperty( "style", null );
		Assert.IsTrue( panel.Style.HasTransitions );
		Assert.AreEqual( 1, panel.Style.Transitions.List.Count );
		Assert.AreEqual( "opacity", panel.Style.Transitions.List.Single().Property );
		Assert.AreEqual( 2000f, panel.Style.Transitions.List.Single().Duration.Value );
	}

	[TestMethod]
	public void LaterTransitionShorthandDoesNotKeepDuplicateAttributeEntries()
	{
		var panel = new Panel();
		panel.SetProperty( "style", "transition-duration: 1s, 2s;" );
		Assert.IsTrue( panel.Style.Set( "transition: height 1s;" ) );

		panel.SetProperty( "style", null );
		Assert.AreEqual( 1, panel.Style.Transitions.List.Count );
		Assert.AreEqual( "height", panel.Style.Transitions.List.Single().Property );
		Assert.AreEqual( 1000f, panel.Style.Transitions.List.Single().Duration.Value );
	}

	[TestMethod]
	public void LaterTransitionLonghandDoesNotKeepUntouchedDuplicateAttributeEntry()
	{
		var panel = new Panel();
		panel.SetProperty( "style", "transition-duration: 1s, 2s;" );
		Assert.IsTrue( panel.Style.Set( "transition-duration", "3s" ) );

		panel.SetProperty( "style", null );
		Assert.AreEqual( 1, panel.Style.Transitions.List.Count );
		Assert.AreEqual( "all", panel.Style.Transitions.List.Single().Property );
		Assert.AreEqual( 3000f, panel.Style.Transitions.List.Single().Duration.Value );
	}

	[TestMethod]
	public void LaterAddOfTransitionDurationDoesNotKeepAttributeDelay()
	{
		var panel = new Panel();
		panel.SetProperty( "style", "transition: opacity 2s 5s;" );
		var styles = new Styles { Transitions = new TransitionList() };
		styles.Transitions.List.Add( new TransitionDesc { Property = "opacity", Duration = 2000 } );
		panel.Style.Add( styles );

		panel.SetProperty( "style", null );
		Assert.AreEqual( 1, panel.Style.Transitions.List.Count );
		Assert.AreEqual( "opacity", panel.Style.Transitions.List.Single().Property );
		Assert.AreEqual( 2000f, panel.Style.Transitions.List.Single().Duration.Value );
		Assert.AreEqual( 0f, panel.Style.Transitions.List.Single().Delay.GetValueOrDefault() );
	}

	[TestMethod]
	public void LaterShadowReturningToAttributeValueSurvivesRemoval()
	{
		var panel = new Panel();
		Assert.IsTrue( panel.Style.Set( "box-shadow: 1px 2px 0px red;" ) );
		panel.SetProperty( "style", "box-shadow: 3px 4px 0px blue;" );
		Assert.IsTrue( panel.Style.Set( "box-shadow: 5px 6px 0px white;" ) );
		Assert.IsTrue( panel.Style.Set( "box-shadow: 3px 4px 0px blue;" ) );

		panel.SetProperty( "style", null );
		Assert.AreEqual( 1, panel.Style.BoxShadow.Count );
		Assert.AreEqual( 3f, panel.Style.BoxShadow[0].OffsetX );
		Assert.AreEqual( 4f, panel.Style.BoxShadow[0].OffsetY );
		Assert.AreEqual( Color.Blue, panel.Style.BoxShadow[0].Color );
	}

	[TestMethod]
	public void LaterGradientReturningToAttributeValueSurvivesRemoval()
	{
		var panel = new Panel();
		Assert.IsTrue( panel.Style.Set( "background-image: linear-gradient(red, blue);" ) );
		panel.SetProperty( "style", "background-image: linear-gradient(blue, white);" );
		Assert.IsTrue( panel.Style.Set( "background-image: linear-gradient(red, white);" ) );
		Assert.IsTrue( panel.Style.Set( "background-image: linear-gradient(blue, white);" ) );

		panel.SetProperty( "style", null );
		Assert.AreEqual( 2, panel.Style.BackgroundGradient.ColorOffsets.Length );
		Assert.AreEqual( Color.Blue, panel.Style.BackgroundGradient.ColorOffsets[0].color );
		Assert.AreEqual( Color.White, panel.Style.BackgroundGradient.ColorOffsets[1].color );
	}
}
