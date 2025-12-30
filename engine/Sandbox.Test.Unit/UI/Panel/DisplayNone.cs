using Sandbox.UI;
namespace UITest.Panels;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public partial class DisplayNone
{
	[TestMethod]
	public void BasicNone()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var panela = root.Add.Panel();
		panela.Style.Set( "display: none;" );

		var panelb = root.Add.Panel();
		panelb.Style.Set( "width: 100px; height: 100px;" );

		root.Layout();

		// Should be flowing downwards by default

		Assert.AreEqual( 0, panela.Box.Left );
		Assert.AreEqual( 0, panela.Box.Top );
		Assert.AreEqual( false, panela.IsVisible );

		Assert.AreEqual( 0, panelb.Box.Left );
		Assert.AreEqual( 0, panelb.Box.Top );
	}

	[TestMethod]
	public void BasicSwitch()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var panela = root.Add.Panel();
		panela.Style.Set( "display: flex; width: 100px; height: 100px;" );

		var panelb = root.Add.Panel();
		panelb.Style.Set( "width: 100px; height: 100px;" );

		root.Layout();

		// Should be flowing downwards by default

		Assert.AreEqual( 0, panela.Box.Left );
		Assert.AreEqual( 0, panela.Box.Top );
		Assert.AreEqual( true, panela.IsVisible );

		Assert.AreEqual( 100, panelb.Box.Left );
		Assert.AreEqual( 0, panelb.Box.Top );


		panela.Style.Set( "display: none;" );
		root.Layout();

		Assert.AreEqual( DisplayMode.None, panela.ComputedStyle.Display );
		Assert.AreEqual( 0, panela.Box.Left );
		Assert.AreEqual( 0, panela.Box.Top );
		Assert.AreEqual( false, panela.IsVisible );

		Assert.AreEqual( 0, panelb.Box.Left );
		Assert.AreEqual( 0, panelb.Box.Top );
	}

}
