using Sandbox.Engine;
using Sandbox.UI;

namespace UITests;

/// <summary>
/// Cached layer bounds must contain outset box-shadows and refresh when style or layout changes.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PanelLayerBoundsTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	static Panel Styled( string boxShadow, string style = null )
	{
		var root = UiTesting.CreateRoot();

		var panel = root.AddChild<Panel>();
		panel.Style.Width = 200;
		panel.Style.Height = 80;
		Assert.IsTrue( panel.Style.Set( "isolation", "isolate" ) );
		if ( style is not null ) panel.Style.Set( style );

		if ( boxShadow is not null )
			Assert.IsTrue( panel.Style.Set( "box-shadow", boxShadow ), boxShadow );

		root.BuildStyleRules();
		root.Layout();
		Assert.IsTrue( panel.HasPanelLayer );

		return panel;
	}

	[TestMethod]
	public void NoShadowLeavesTheLayerAtTheMarginBox()
	{
		var panel = Styled( null );
		Assert.AreEqual( panel.Box.RectOuter, panel.PanelLayerBounds, "nothing to make room for" );
	}

	[TestMethod]
	public void AnInsetShadowNeedsNoRoom()
	{
		var panel = Styled( "inset 0px 12px 0px red" );
		Assert.AreEqual( panel.Box.RectOuter, panel.PanelLayerBounds, "an inset shadow draws inside the box" );
	}

	[TestMethod]
	public void AHardOffsetShadowGrowsTheSideItFallsOn()
	{
		var panel = Styled( "0px 12px 0px red" );

		var outer = panel.Box.RectOuter;
		var layer = panel.PanelLayerBounds;
		var scale = panel.ScaleToScreen;

		Assert.AreEqual( outer.Top, layer.Top, 0.01f, "a downward shadow needs no room above" );
		Assert.AreEqual( outer.Left, layer.Left, 0.01f, "nor to the left" );
		Assert.AreEqual( outer.Right, layer.Right, 0.01f, "nor to the right" );
		Assert.AreEqual( outer.Bottom + 12f * scale, layer.Bottom, 0.01f, "and 12px below, where it lands" );
	}

	[TestMethod]
	[DataRow( "0px 0px 10px 4px red", 19f )]
	[DataRow( "0px 0px 1px 4px red", 6f )]
	public void BlurAndSpreadReachEverySide( string shadow, float reach )
	{
		var panel = Styled( shadow );
		Assert.AreEqual( 1f, panel.ScaleToScreen );

		var outer = panel.Box.RectOuter;
		var layer = panel.PanelLayerBounds;

		Assert.AreEqual( outer.Left - reach, layer.Left, 0.01f );
		Assert.AreEqual( outer.Top - reach, layer.Top, 0.01f );
		Assert.AreEqual( outer.Right + reach, layer.Right, 0.01f );
		Assert.AreEqual( outer.Bottom + reach, layer.Bottom, 0.01f );
	}

	[TestMethod]
	public void SeveralShadowsTakeTheWidestReachPerSide()
	{
		var panel = Styled( "-20px 0px 0px red, 0px 30px 0px blue" );

		var outer = panel.Box.RectOuter;
		var layer = panel.PanelLayerBounds;
		var scale = panel.ScaleToScreen;

		Assert.AreEqual( outer.Left - 20f * scale, layer.Left, 0.01f, "the leftward shadow decides the left" );
		Assert.AreEqual( outer.Bottom + 30f * scale, layer.Bottom, 0.01f, "the downward one decides the bottom" );
		Assert.AreEqual( outer.Right, layer.Right, 0.01f, "and neither reaches right" );
		Assert.AreEqual( outer.Top, layer.Top, 0.01f, "or up" );
	}

	[TestMethod]
	public void AFullyTransparentShadowIsIgnored()
	{
		var panel = Styled( "0px 40px 0px rgba( 0, 0, 0, 0 )" );
		Assert.AreEqual( panel.Box.RectOuter, panel.PanelLayerBounds, "an invisible shadow is not drawn, so needs no room" );
	}

	[TestMethod]
	[DataRow( "margin: 20px;" )]
	[DataRow( "margin-bottom: -10px;" )]
	public void ShadowBoundsOriginateAtTheBorderBox( string style )
	{
		var panel = Styled( "0px 12px 0px red", style );
		var expected = panel.Box.RectOuter;
		expected.Add( panel.Box.Rect + new Vector2( 0, 12f * panel.ScaleToScreen ) );

		Assert.AreEqual( expected, panel.PanelLayerBounds );
	}

	[TestMethod]
	public void FractionalShadowBoundsRoundOutward()
	{
		var panel = Styled( "-0.5px 0.5px 0px red" );
		Assert.AreEqual( 1f, panel.ScaleToScreen );
		Assert.AreEqual( panel.Box.Rect.Grow( 1, 0, 0, 1 ), panel.PanelLayerBounds );
	}

	[TestMethod]
	public void BoundsRefreshWhenShadowsChangeOrAreRemoved()
	{
		var panel = Styled( "0px 12px 0px red" );
		var root = panel.FindRootPanel();
		var original = panel.PanelLayerBounds;

		Assert.IsTrue( panel.Style.Set( "box-shadow", "-20px 0px 0px red" ) );
		root.Layout();
		Assert.AreEqual( panel.Box.Rect.Grow( 20f * panel.ScaleToScreen, 0, 0, 0 ), panel.PanelLayerBounds );
		Assert.AreNotEqual( original, panel.PanelLayerBounds );

		Assert.IsTrue( panel.Style.Set( "box-shadow", "none" ) );
		root.Layout();
		Assert.AreEqual( panel.Box.RectOuter, panel.PanelLayerBounds );
	}

	[TestMethod]
	public void BoundsRefreshWhenPanelMovesOrResizes()
	{
		var panel = Styled( "0px 12px 0px red", "position: absolute; left: 10px; top: 20px;" );
		var root = panel.FindRootPanel();
		var original = panel.PanelLayerBounds;

		panel.Style.Set( "left: 50px; top: 60px; width: 300px; height: 100px;" );
		root.Layout();

		Assert.AreNotEqual( original, panel.PanelLayerBounds );
		Assert.AreEqual( panel.Box.Rect.Grow( 0, 0, 0, 12f * panel.ScaleToScreen ), panel.PanelLayerBounds );
	}
}
