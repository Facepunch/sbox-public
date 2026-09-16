using Sandbox.Engine;
using Sandbox.UI;

namespace UITests;

/// <summary>
/// A layered panel's offscreen target has to leave room for its outset box-shadow, which is drawn
/// inside the layer. Sized to the margin box it was clipped to the panel's own rectangle, which
/// read as the border radius going wrong the moment a `filter` put the panel on a layer.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PanelLayerRectTest
{
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.UISystem.Clear();
	}

	static Panel Styled( string boxShadow )
	{
		var root = UiTesting.CreateRoot();

		var panel = root.AddChild<Panel>();
		panel.Style.Width = 200;
		panel.Style.Height = 80;

		if ( boxShadow is not null )
			Assert.IsTrue( panel.Style.Set( "box-shadow", boxShadow ), boxShadow );

		root.BuildStyleRules();
		root.Layout();

		return panel;
	}

	[TestMethod]
	public void NoShadowLeavesTheLayerAtTheMarginBox()
	{
		var panel = Styled( null );
		Assert.AreEqual( panel.Box.RectOuter, panel.PanelLayerRect, "nothing to make room for" );
	}

	[TestMethod]
	public void AnInsetShadowNeedsNoRoom()
	{
		var panel = Styled( "inset 0px 12px 0px red" );
		Assert.AreEqual( panel.Box.RectOuter, panel.PanelLayerRect, "an inset shadow draws inside the box" );
	}

	[TestMethod]
	public void AHardOffsetShadowGrowsTheSideItFallsOn()
	{
		var panel = Styled( "0px 12px 0px red" );

		var outer = panel.Box.RectOuter;
		var layer = panel.PanelLayerRect;
		var scale = panel.ScaleToScreen;

		Assert.AreEqual( outer.Top, layer.Top, 0.01f, "a downward shadow needs no room above" );
		Assert.AreEqual( outer.Left, layer.Left, 0.01f, "nor to the left" );
		Assert.AreEqual( outer.Right, layer.Right, 0.01f, "nor to the right" );
		Assert.AreEqual( outer.Bottom + 12f * scale, layer.Bottom, 0.01f, "and 12px below, where it lands" );
	}

	[TestMethod]
	public void BlurAndSpreadReachEverySide()
	{
		var panel = Styled( "0px 0px 10px 4px red" );

		var outer = panel.Box.RectOuter;
		var layer = panel.PanelLayerRect;
		var reach = 14f * panel.ScaleToScreen;

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
		var layer = panel.PanelLayerRect;
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
		Assert.AreEqual( panel.Box.RectOuter, panel.PanelLayerRect, "an invisible shadow is not drawn, so needs no room" );
	}
}
