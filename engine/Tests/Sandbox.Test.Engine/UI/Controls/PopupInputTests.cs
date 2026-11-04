using Sandbox.UI;
using static UITests.UiTesting;

namespace UITests.Controls;

/// <summary>
/// A popup floats away from what opened it, so what it doesn't handle goes back there.
/// </summary>
[TestClass]
[DoNotParallelize] // Popups are global state
public class PopupInputTest
{
	sealed class KeyRecorder : Panel
	{
		public string LastKey;

		public override void OnButtonTyped( ButtonEvent e )
		{
			LastKey = e.Button;
		}
	}

	[TestInitialize]
	public void Setup()
	{
		BasePopup.CloseAll();
	}

	[TestMethod]
	public void UnhandledKeysGoToTheSourceNotTheParent()
	{
		var root = CreateRoot();
		var rootKeys = new KeyRecorder { Parent = root };
		var source = new KeyRecorder { Parent = rootKeys };
		var popup = new Popup( source, Popup.PositionMode.BelowLeft, 0 );

		Assert.AreEqual( root, popup.Parent, "the popup floats in the root" );

		popup.OnButtonTyped( new ButtonEvent( "f5", true, 0, default ) );

		Assert.AreEqual( "f5", source.LastKey );
		Assert.IsNull( rootKeys.LastKey );
	}

	[TestMethod]
	public void WithoutASourceKeysBubbleToTheParent()
	{
		var root = CreateRoot();
		var holder = new KeyRecorder { Parent = root };
		var popup = new Popup { Parent = holder };

		popup.OnButtonTyped( new ButtonEvent( "f5", true, 0, default ) );

		Assert.AreEqual( "f5", holder.LastKey );
	}
}
