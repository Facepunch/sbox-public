using Editor;
using Sandbox.UI;

namespace UITests.Controls;

/// <summary>
/// Where a popup window is placed for the position a popup asked for. A window can't be placed by
/// its own size before it exists, so the modes that would need it fall back to the nearest that
/// don't.
/// </summary>
[TestClass]
public class PopupWindowTest
{
	static readonly Rect Source = new( 100, 200, 50, 20 );
	static readonly Vector2 Mouse = new( 400, 300 );

	[TestMethod]
	public void BelowLeftHangsUnderTheSource()
	{
		Assert.AreEqual( new Vector2( 100, 224 ), PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.BelowLeft, 4 ) );
	}

	[TestMethod]
	public void RightModesSitBesideTheSourceAlignedToItsTop()
	{
		Assert.AreEqual( new Vector2( 154, 200 ), PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.RightTop, 4 ) );
		Assert.AreEqual( new Vector2( 154, 200 ), PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.Right, 4 ) );
		Assert.AreEqual( new Vector2( 154, 200 ), PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.RightBottom, 4 ) );
	}

	[TestMethod]
	public void LeftModesSitOnTheSourcesLeftEdge()
	{
		Assert.AreEqual( new Vector2( 96, 200 ), PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.Left, 4 ) );
	}

	[TestMethod]
	public void UnderMouseIsTheMouse()
	{
		Assert.AreEqual( Mouse, PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.UnderMouse, 4 ) );
	}

	[TestMethod]
	public void ModesThatNeedTheWindowsSizeFallBackToBelow()
	{
		Assert.AreEqual( new Vector2( 100, 224 ), PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.AboveLeft, 4 ) );
		Assert.AreEqual( new Vector2( 100, 224 ), PanelWindow.PopupPosition( Source, Mouse, Popup.PositionMode.BelowCenter, 4 ) );
	}
}
