using Sandbox.UI;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// Where popups land.
/// </summary>
[TestClass]
[DoNotParallelize] // Popups are global state
public class PopupPositionTest
{
	[TestInitialize]
	public void Setup()
	{
		BasePopup.CloseAll();
	}

	[TestMethod]
	public void RightTopSitsBesideTheSourceAlignedToItsTop()
	{
		var root = CreateRoot();
		var source = new Panel { Parent = root };
		source.Style.Position = PositionMode.Absolute;
		source.Style.Left = 100;
		source.Style.Top = 200;
		source.Style.Width = 50;
		source.Style.Height = 20;
		root.Layout();

		// The game's base sheet makes popups absolute - there's no sheet here
		var popup = new Popup( source, Popup.PositionMode.RightTop, 4 );
		popup.Style.Position = PositionMode.Absolute;
		popup.Style.Width = 80;
		popup.Style.Height = 40;
		root.Layout();
		root.Layout();

		Assert.AreEqual( 154, popup.Box.Rect.Left, 0.5f );
		Assert.AreEqual( 200, popup.Box.Rect.Top, 0.5f );
	}
}
