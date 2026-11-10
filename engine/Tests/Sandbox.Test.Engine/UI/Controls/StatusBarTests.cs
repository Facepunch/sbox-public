using Sandbox.UI;
using System.Collections.Generic;
using static UITests.UiTesting;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class StatusBarTests
{
	bool previousRenderText;
	UISurface surface;
	StatusBar bar;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		previousRenderText = DisableTextRendering();
		surface = new UISurface { Size = new Vector2( 600, 100 ), DpiScale = 1 };
		bar = new StatusBar { Parent = surface.Root };
		bar.Style.Width = 600;
	}

	[TestCleanup]
	public void Cleanup()
	{
		try { surface.Dispose(); }
		finally { TextBlock.ui_rendertext = previousRenderText; }
	}

	void Frame() { for ( int i = 0; i < 3; i++ ) UiTesting.Frame( surface ); }

	[TestMethod]
	public void TemporaryMessageRetainsWidgetsAndPermanentStatus()
	{
		var left = bar.AddLeft( new TextEntry { Text = "retained state" } );
		var right = bar.AddRight( new Label( "Online" ) );
		bar.ShowMessage( "Saved", 2 );
		Frame();
		Assert.IsFalse( left.IsVisible );
		Assert.IsTrue( right.IsVisible );
		bar.ExpireMessage( bar.TimeNow + 3 );
		Frame();
		Assert.AreEqual( "", bar.Message );
		Assert.IsTrue( left.IsVisible );
		Assert.AreEqual( "retained state", left.Text );
	}

	[TestMethod]
	public void ReplacementAndRepeatedMessageResetDeadline()
	{
		bar.ShowMessage( "Saved", 1 );
		bar.ShowMessage( "Saved", 10 );
		bar.ExpireMessage( bar.TimeNow + 2 );
		Assert.AreEqual( "Saved", bar.Message );
		bar.ShowMessage( "Waiting", 0 );
		bar.ExpireMessage( double.MaxValue );
		Assert.AreEqual( "Waiting", bar.Message );
		bar.ShowMessage( "Connected", 1 );
		bar.ExpireMessage( bar.TimeNow + 2 );
		Assert.AreEqual( "", bar.Message );
	}

	[TestMethod]
	public void MessageNotificationsAndRemovalOwnership()
	{
		var messages = new List<string>();
		bar.MessageChanged += messages.Add;
		bar.ShowMessage( "Saved" );
		bar.ShowMessage( "Saved" );
		bar.ClearMessage();
		bar.ClearMessage();
		CollectionAssert.AreEqual( new[] { "Saved", "" }, messages );
		var widget = bar.AddRight( new Panel() );
		Assert.IsTrue( bar.RemoveWidget( widget ) );
		Assert.IsTrue( widget.IsValid );
		Assert.IsNull( widget.Parent );
		Assert.IsFalse( bar.RemoveWidget( widget ) );
		widget.Delete( true );
	}

	[TestMethod]
	public void LongMessageDoesNotPushPermanentWidgetOutsideBar()
	{
		bar.Style.Width = 220;
		var right = bar.AddRight( new Label( "Online" ) );
		bar.ShowMessage( new string( 'W', 200 ), 0 );
		Frame();
		Assert.IsTrue( right.Box.Rect.Right <= bar.Box.Rect.Right );
		Assert.IsTrue( right.IsVisible );
	}
}
