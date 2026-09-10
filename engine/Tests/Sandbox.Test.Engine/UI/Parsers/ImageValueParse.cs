using Sandbox.UI;

namespace UITests.Parsers;

/// <summary>
/// url() and gradient values are read by walking to the closing bracket. An empty url() is
/// ordinary - a razor binding whose source hasn't loaded yet renders as one - so it has to
/// parse rather than throw.
/// </summary>
[TestClass]
public class ImageValueParseTest
{
	static Styles ParseStyles( string declaration )
	{
		var sheet = StyleParser.ParseSheet( $".class{{ {declaration} }}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );

		return sheet.Nodes[0].Styles;
	}

	[DataTestMethod]
	[DataRow( "url()" )]
	[DataRow( "url( )" )]
	[DataRow( "url('')" )]
	public void EmptyUrlIsNoImage( string value )
	{
		var styles = ParseStyles( $"background-image: {value};" );

		Assert.IsTrue( styles.RawValues["background-image"].IsValid, $"'{value}' didn't parse" );
	}

	/// <summary>The bracket walk has to survive nested brackets - rgba() inside a gradient.</summary>
	[TestMethod]
	public void NestedBracketsAreRead()
	{
		var styles = ParseStyles( "background-image: linear-gradient( rgba( 255, 0, 0, 1 ), rgba( 0, 0, 255, 1 ) );" );

		Assert.AreEqual( 2, styles.BackgroundGradient.ColorOffsets.Length );
	}

	/// <summary>A bracket that never closes is still a mistake worth reporting.</summary>
	[TestMethod]
	public void UnclosedBracketThrows()
	{
		Assert.ThrowsException<System.Exception>( () => ParseStyles( "background-image: url( abc;" ) );
	}
}
