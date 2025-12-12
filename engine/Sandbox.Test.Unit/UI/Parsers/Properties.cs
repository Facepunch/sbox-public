
using Sandbox.UI;

namespace UITest.Parsers;


[TestClass]
public class Properties
{
	[TestMethod]
	public void Set()
	{
		{
			Styles styles = new Styles();
			styles.Set( "width", "140px" );

			// Did it read width right?
			Assert.IsTrue( styles.Width.HasValue );
			Assert.AreEqual( 140, styles.Width.Value.Value );
			Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );
		}
	}

	[TestMethod]
	public void SingleProperty()
	{
		var styles = new Styles();
		var p = new Parse( "width: 100px" );
		StyleParser.ParseStyles( ref p, styles );

		// Did it read width right?
		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 100, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );
	}

	[TestMethod]
	public void SinglePropertyWithSpacing()
	{
		var styles = new Styles();
		var p = new Parse( "  width: 100px    " );
		StyleParser.ParseStyles( ref p, styles );

		// Did it read width right?
		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 100, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );
	}

	[TestMethod]
	public void MultipleProperties()
	{
		var styles = new Styles();
		var p = new Parse( "width: 100px; height: 10%;" );
		StyleParser.ParseStyles( ref p, styles );

		// Did it read width right?
		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 100, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );

		Assert.IsTrue( styles.Height.HasValue );
		Assert.AreEqual( 10, styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, styles.Height.Value.Unit );

		styles = new Styles();
		p = new Parse( " height: 10%; width: 100px" );
		StyleParser.ParseStyles( ref p, styles );

		// Did it read width right?
		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 100, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );

		Assert.IsTrue( styles.Height.HasValue );
		Assert.AreEqual( 10, styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, styles.Height.Value.Unit );

		styles = new Styles();
		p = new Parse( "  height: 10%;       \n\rwidth: 100px  " );
		StyleParser.ParseStyles( ref p, styles );

		// Did it read width right?
		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 100, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );

		Assert.IsTrue( styles.Height.HasValue );
		Assert.AreEqual( 10, styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, styles.Height.Value.Unit );

		styles = new Styles();
		p = new Parse( "\r\nheight: 10%;\n\rwidth: 100px;\r\n" );
		StyleParser.ParseStyles( ref p, styles );

		// Did it read width right?
		Assert.IsTrue( styles.Width.HasValue );
		Assert.AreEqual( 100, styles.Width.Value.Value );
		Assert.AreEqual( LengthUnit.Pixels, styles.Width.Value.Unit );

		Assert.IsTrue( styles.Height.HasValue );
		Assert.AreEqual( 10, styles.Height.Value.Value );
		Assert.AreEqual( LengthUnit.Percentage, styles.Height.Value.Unit );
	}

	[TestMethod]
	public void OpacityParse()
	{
		{
			Styles styles = new Styles();
			styles.Set( "opacity", "1" );
			Assert.IsTrue( styles.Opacity.HasValue );
			Assert.AreEqual( 1, styles.Opacity.Value );
		}

		{
			Styles styles = new Styles();
			styles.Set( "opacity", "0.2" );
			Assert.IsTrue( styles.Opacity.HasValue );
			Assert.AreEqual( 0.2f, styles.Opacity.Value );
		}
	}
}
