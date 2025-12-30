using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class AnimationParse
{
	[TestMethod]
	public void AnimationName()
	{
		var sheet = StyleParser.ParseSheet( ".one { animation-name: Beetroot; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( "Beetroot", sheet.Nodes[0].Styles.AnimationName );
	}

	[TestMethod]
	public void AnimationDuration()
	{
		var sheet = StyleParser.ParseSheet( ".one { animation-name: Beetroot; animation-duration: 3s; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( 3, sheet.Nodes[0].Styles.AnimationDuration );
	}

	[TestMethod]
	public void AnimationTimingFunction()
	{
		var sheet = StyleParser.ParseSheet( ".one { animation-timing-function: ease-out; animation-duration: 3s; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( "ease-out", sheet.Nodes[0].Styles.AnimationTimingFunction );
	}

	[DataRow( "16s", 16.0f )]
	[DataRow( "4s", 4.0f )]
	[DataRow( "-2s", -2.0f )]
	[TestMethod]
	public void AnimationDelay( string value, float testValue )
	{
		var sheet = StyleParser.ParseSheet( $".one {{ animation-delay: {value}; animation-duration: 3s; }}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( testValue, sheet.Nodes[0].Styles.AnimationDelay );
	}

	[TestMethod]
	public void AnimationIterationCount()
	{
		var sheet = StyleParser.ParseSheet( ".one { animation-iteration-count: 16; animation-duration: 3s; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( 16, sheet.Nodes[0].Styles.AnimationIterationCount );

		sheet = StyleParser.ParseSheet( ".one { animation-iteration-count: infinite; animation-duration: 3s; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( float.PositiveInfinity, sheet.Nodes[0].Styles.AnimationIterationCount );
	}

	[TestMethod]
	public void AnimationDirection()
	{
		var sheet = StyleParser.ParseSheet( ".one { animation-direction: normal; animation-duration: 3s; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( "normal", sheet.Nodes[0].Styles.AnimationDirection );
	}

	[TestMethod]
	public void AnimationFillMode()
	{
		var sheet = StyleParser.ParseSheet( ".one { animation-fill-mode: forwards; animation-duration: 3s; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( "forwards", sheet.Nodes[0].Styles.AnimationFillMode );
	}

	[TestMethod]
	public void AnimationPlayState()
	{
		var sheet = StyleParser.ParseSheet( ".one { animation-play-state: running; animation-duration: 3s; }" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.Nodes.Count );
		Assert.AreEqual( "running", sheet.Nodes[0].Styles.AnimationPlayState );
	}
}
