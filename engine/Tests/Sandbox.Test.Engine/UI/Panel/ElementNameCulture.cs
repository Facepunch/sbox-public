using Sandbox.UI;
using System.Globalization;

namespace UITests.Panels;

[TestClass]
[DoNotParallelize] // Changes the current culture
public class ElementNameCulture
{
	/// <summary>
	/// Element names come from the type name and are matched against lowercase selectors, so they
	/// can't follow the current culture - under tr-TR "IconPanel" would become "ıconpanel".
	/// </summary>
	[TestMethod]
	public void ElementNameIsCultureInvariant()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo( "tr-TR" );

		try
		{
			var root = new RootPanel();
			root.PanelBounds = new Rect( 0, 0, 1000, 1000 );
			root.StyleSheet.Parse( "image { width: 37px; }" );

			var image = root.AddChild<Image>();
			root.Layout();

			Assert.AreEqual( "iconpanel", new IconPanel().ElementName );
			Assert.AreEqual( "image", image.ElementName );
			Assert.AreEqual( 37, image.Box.Rect.Width, 0.01f );
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}
}
