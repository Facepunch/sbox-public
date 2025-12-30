using Sandbox.UI;

namespace UITest.Panels;

[TestClass]
[DoNotParallelize] // Modfiies UI System Global
public partial class BorderPadding
{
	[TestMethod]
	public void BorderDoesntChangeSize()
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 1000, 1000 );

		var panela = root.Add.Panel();
		panela.Style.Set( "width: 100px; height: 100px;" );

		root.Layout();

		Assert.AreEqual( 100, panela.Box.Right );
		Assert.AreEqual( 100, panela.Box.Bottom );

		panela.Style.Set( "width: 100px; height: 100px; border: 10px solid red;" );
		root.Layout();

		Assert.AreEqual( 100, panela.Box.Right );
		Assert.AreEqual( 100, panela.Box.Bottom );
	}
}
