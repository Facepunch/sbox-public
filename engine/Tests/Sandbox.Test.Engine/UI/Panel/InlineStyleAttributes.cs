using Sandbox.Engine;
using Sandbox.UI;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize]
public class InlineStyleAttributeTests
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	static RootPanel CreateRoot( string rules = null )
	{
		var root = new RootPanel { PanelBounds = new Rect( 0, 0, 1000, 1000 ) };
		if ( rules is not null ) root.StyleSheet.Parse( rules );
		return root;
	}

	static Panel CreatePanel( RootPanel root )
	{
		var panel = new Panel { Parent = root };
		panel.AddClass( "target" );
		return panel;
	}

	[TestMethod]
	public void ReplacementRemovesOldPropertyAndPreservesStyleObject()
	{
		var root = CreateRoot();
		var panel = CreatePanel( root );
		var style = panel.Style;
		style.Position = PositionMode.Absolute;
		style.Width = 100;
		style.Height = 20;
		panel.SetProperty( "style", "right: 0px;" );
		root.Layout();
		Assert.AreEqual( 0f, style.Right.Value.Value );
		Assert.AreEqual( 900f, panel.Box.Rect.Left, 0.01f );

		panel.SetProperty( "style", "left: 0px;" );
		root.Layout();
		Assert.AreSame( style, panel.Style );
		Assert.IsNull( style.Right );
		Assert.AreEqual( 0f, style.Left.Value.Value );
		Assert.AreEqual( Length.Undefined, panel.ComputedStyle.Right.Value );
		Assert.AreEqual( 0f, panel.Box.Rect.Left, 0.01f );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	[DataRow( " \t " )]
	public void EmptyAttributeFallsBackToStylesheet( string emptyStyle )
	{
		var root = CreateRoot( ".target { width: 120px; height: 20px; color: blue; }" );
		var panel = CreatePanel( root );
		panel.SetProperty( "style", "width: 240px; color: red;" );
		root.Layout();
		Assert.AreEqual( 240f, panel.ComputedStyle.Width.Value.Value );
		Assert.AreEqual( Color.Red, panel.ComputedStyle.FontColor.Value );

		panel.SetProperty( "style", emptyStyle );
		root.Layout();
		Assert.IsNull( panel.Style.Width );
		Assert.IsNull( panel.Style.FontColor );
		Assert.AreEqual( 120f, panel.ComputedStyle.Width.Value.Value );
		Assert.AreEqual( Color.Blue, panel.ComputedStyle.FontColor.Value );
	}

	[TestMethod]
	public void UnrelatedProgrammaticStyleSurvivesAttributeReplacements()
	{
		var root = CreateRoot();
		var panel = CreatePanel( root );
		panel.Style.Width = 120;
		panel.Style.Set( "height: 30px; color: blue;" );
		panel.SetProperty( "style", "left: 0px;" );
		panel.SetProperty( "style", "right: 0px;" );
		root.Layout();

		Assert.AreEqual( 120f, panel.Style.Width.Value.Value );
		Assert.AreEqual( 30f, panel.Style.Height.Value.Value );
		Assert.AreEqual( Color.Blue, panel.Style.FontColor.Value );
		Assert.IsNull( panel.Style.Left );
		Assert.AreEqual( 120f, panel.ComputedStyle.Width.Value.Value );
		Assert.AreEqual( 30f, panel.ComputedStyle.Height.Value.Value );
		Assert.AreEqual( Color.Blue, panel.ComputedStyle.FontColor.Value );
	}

	[TestMethod]
	public void RemovedAttributeRestoresEarlierProgrammaticValue()
	{
		var root = CreateRoot();
		var panel = CreatePanel( root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		root.Layout();
		Assert.AreEqual( 240f, panel.Style.Width.Value.Value );

		panel.SetProperty( "style", "height: 20px;" );
		root.Layout();
		Assert.AreEqual( 120f, panel.Style.Width.Value.Value );
		Assert.AreEqual( 120f, panel.ComputedStyle.Width.Value.Value );
	}

	[TestMethod]
	public void RemovedAttributePreservesLaterPropertySetter()
	{
		var root = CreateRoot();
		var panel = CreatePanel( root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Width = 300;
		panel.SetProperty( "style", "height: 20px;" );
		root.Layout();

		Assert.AreEqual( 300f, panel.Style.Width.Value.Value );
		Assert.AreEqual( 300f, panel.ComputedStyle.Width.Value.Value );
	}

	[TestMethod]
	public void RemovedAttributePreservesLaterStyleSet()
	{
		var root = CreateRoot();
		var panel = CreatePanel( root );
		panel.Style.Set( "width: 120px; color: blue;" );
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Set( "width: 320px;" );
		panel.SetProperty( "style", null );
		root.Layout();

		Assert.AreEqual( 320f, panel.Style.Width.Value.Value );
		Assert.AreEqual( Color.Blue, panel.Style.FontColor.Value );
		Assert.AreEqual( 320f, panel.ComputedStyle.Width.Value.Value );
	}

	[TestMethod]
	public void ShorthandAndAliasDoNotLeakIntoReplacement()
	{
		var root = CreateRoot( ".target { margin: 5px; color: blue; }" );
		var panel = CreatePanel( root );
		panel.SetProperty( "style", "margin: 10px 20px 30px 40px; color: red;" );
		root.Layout();
		Assert.AreEqual( 40f, panel.ComputedStyle.MarginLeft.Value.Value );
		Assert.AreEqual( Color.Red, panel.ComputedStyle.FontColor.Value );

		panel.SetProperty( "style", "margin-left: 25px;" );
		root.Layout();
		Assert.AreEqual( 25f, panel.ComputedStyle.MarginLeft.Value.Value );
		Assert.AreEqual( 5f, panel.ComputedStyle.MarginTop.Value.Value );
		Assert.AreEqual( 5f, panel.ComputedStyle.MarginRight.Value.Value );
		Assert.AreEqual( 5f, panel.ComputedStyle.MarginBottom.Value.Value );
		Assert.AreEqual( Color.Blue, panel.ComputedStyle.FontColor.Value );
	}

	[TestMethod]
	public void CssWideKeywordDoesNotSurviveAttributeRemoval()
	{
		var root = CreateRoot( ".target { width: 120px; color: blue; }" );
		root.Style.Width = 400;
		root.Style.FontColor = Color.Red;
		var panel = CreatePanel( root );
		panel.SetProperty( "style", "width: inherit; color: inherit;" );
		root.Layout();
		Assert.AreEqual( root.ComputedStyle.Width.Value.Value, panel.ComputedStyle.Width.Value.Value );
		Assert.AreEqual( Color.Red, panel.ComputedStyle.FontColor.Value );

		panel.SetProperty( "style", "height: 20px;" );
		root.Layout();
		Assert.IsTrue( panel.Style.CssWide is null || panel.Style.CssWide.Count == 0 );
		Assert.AreEqual( 120f, panel.ComputedStyle.Width.Value.Value );
		Assert.AreEqual( Color.Blue, panel.ComputedStyle.FontColor.Value );
	}

	[TestMethod]
	public void CompositeStylesDoNotSurviveAttributeReplacement()
	{
		// Declaration checks don't need the texture renderer initialized by a booted engine.
		var panel = new Panel();
		panel.SetProperty( "style", "transition: opacity 2s; box-shadow: 1px 2px 0px red; background-image: linear-gradient(red, blue);" );
		Assert.IsTrue( panel.Style.HasTransitions );
		Assert.AreEqual( 1, panel.Style.BoxShadow.Count );
		Assert.AreEqual( 2, panel.Style.BackgroundGradient.ColorOffsets.Length );

		panel.SetProperty( "style", "width: 20px;" );
		Assert.IsFalse( panel.Style.HasTransitions );
		Assert.AreEqual( 0, panel.Style.BoxShadow.Count );
		Assert.AreEqual( 0, panel.Style.BackgroundGradient.ColorOffsets.Length );
	}

	[TestMethod]
	public void RemovingDuplicateTransitionLonghandsClearsEveryEntry()
	{
		var panel = new Panel();
		panel.SetProperty( "style", "transition-duration: 1s, 2s;" );
		Assert.AreEqual( 2, panel.Style.Transitions.List.Count );
		Assert.AreEqual( 1000f, panel.Style.Transitions.List[0].Duration.Value );
		Assert.AreEqual( 2000f, panel.Style.Transitions.List[1].Duration.Value );

		panel.SetProperty( "style", "width: 20px;" );
		Assert.IsFalse( panel.Style.HasTransitions );
		Assert.AreEqual( 0, panel.Style.Transitions.List.Count );
		Assert.AreEqual( 20f, panel.Style.Width.Value.Value );
	}

	[TestMethod]
	public void LaterTransitionPropertyRenameSurvivesAttributeRemoval()
	{
		var panel = new Panel();
		Assert.IsTrue( panel.Style.Set( "transition: width 1s;" ) );
		panel.SetProperty( "style", "transition-property: opacity;" );
		Assert.AreEqual( "opacity", panel.Style.Transitions.List.Single().Property );
		Assert.IsTrue( panel.Style.Set( "transition-property", "height" ) );

		panel.SetProperty( "style", null );
		Assert.AreEqual( 1, panel.Style.Transitions.List.Count );
		Assert.AreEqual( "height", panel.Style.Transitions.List.Single().Property );
		Assert.AreEqual( 1000f, panel.Style.Transitions.List.Single().Duration.Value );
	}

	[TestMethod]
	public void LaterTransitionClearSurvivesAttributeRemoval()
	{
		var panel = new Panel();
		Assert.IsTrue( panel.Style.Set( "transition: width 1s;" ) );
		panel.SetProperty( "style", "transition-property: opacity;" );
		Assert.AreEqual( "opacity", panel.Style.Transitions.List.Single().Property );
		panel.Style.Transitions.Clear();

		panel.SetProperty( "style", null );
		Assert.IsFalse( panel.Style.HasTransitions );
		Assert.AreEqual( 0, panel.Style.Transitions.List.Count );
	}

	[TestMethod]
	[DataRow( "initial" )]
	[DataRow( "inherit" )]
	public void LaterTypedSetterSurvivesRemovalOfCssWideAttribute( string keyword )
	{
		var root = CreateRoot();
		root.Style.Width = 400;
		var panel = CreatePanel( root );
		panel.Style.Width = 120;
		panel.SetProperty( "style", $"width: {keyword};" );
		Assert.IsTrue( panel.Style.CssWide.ContainsKey( "width" ) );
		panel.Style.Width = 300;

		panel.SetProperty( "style", "height: 20px;" );
		root.Layout();
		Assert.IsTrue( panel.Style.CssWide is null || !panel.Style.CssWide.ContainsKey( "width" ) );
		Assert.AreEqual( 300f, panel.Style.Width.Value.Value );
		Assert.AreEqual( 300f, panel.ComputedStyle.Width.Value.Value );
	}

	[TestMethod]
	public void ProgrammaticStylesSetRemainsAdditive()
	{
		var styles = new Styles();
		Assert.IsTrue( styles.Set( "width: 120px; color: blue;" ) );
		Assert.IsTrue( styles.Set( "height: 30px;" ) );

		Assert.AreEqual( 120f, styles.Width.Value.Value );
		Assert.AreEqual( 30f, styles.Height.Value.Value );
		Assert.AreEqual( Color.Blue, styles.FontColor.Value );
	}

	[TestMethod]
	public void NullAttributeWithNoPriorAttributeKeepsProgrammaticStyle()
	{
		var panel = new Panel();
		panel.Style.Width = 120;
		panel.SetProperty( "style", null );
		Assert.AreEqual( 120f, panel.Style.Width.Value.Value );
	}

	[TestMethod]
	public void ReapplyingSameAttributeStillAppliesItsValues()
	{
		var panel = new Panel();
		panel.SetProperty( "style", "width: 240px;" );
		panel.Style.Width = 300;
		panel.SetProperty( "style", "width: 240px;" );
		Assert.AreEqual( 240f, panel.Style.Width.Value.Value );
	}

	[TestMethod]
	public void RazorReusesPanelWhileUpdatingAndClearingInlineStyle()
	{
		var root = CreateRoot();
		var host = new Panel { Parent = root };
		var tree = new PanelRenderTreeBuilder( host );
		void Render( string style )
		{
			tree.Start();
			tree.OpenElement( 0, "div" );
			tree.AddAttributeString( 1, "style", style );
			tree.CloseElement();
			tree.Finish();
			root.Layout();
		}

		Render( "right: 0px;" );
		var panel = host.Children.Single();
		Assert.AreEqual( 0f, panel.Style.Right.Value.Value );
		Render( "left: 0px;" );
		Assert.AreSame( panel, host.Children.Single() );
		Assert.IsNull( panel.Style.Right );
		Assert.AreEqual( 0f, panel.Style.Left.Value.Value );
		Render( null );
		Assert.AreSame( panel, host.Children.Single() );
		Assert.IsNull( panel.Style.Left );
		Assert.AreEqual( Length.Undefined, panel.ComputedStyle.Left.Value );
	}

	[TestMethod]
	public void InvalidReplacementDoesNotKeepPreviousStyle()
	{
		var root = CreateRoot( ".target { width: 120px; }" );
		var panel = CreatePanel( root );
		panel.SetProperty( "style", "width: 240px;" );
		panel.SetProperty( "style", "not-a-style-property: 12px;" );
		root.Layout();

		Assert.IsNull( panel.Style.Width );
		Assert.AreEqual( 120f, panel.ComputedStyle.Width.Value.Value );
	}
}
