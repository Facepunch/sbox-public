using Sandbox.UI;

namespace UITest.Parsers;

[TestClass]
public class KeyframeParse
{
	[TestMethod]
	public void Basic()
	{
		var sheet = StyleParser.ParseSheet( "@keyframes FrameName " +
			"{ " +
			"	0%" +
			"		{" +
			"			background-color: red;" +
			"		}" +
			"	100%" +
			"		{" +
			"			background-color: green;" +
			"		}" +
			"}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.KeyFrames.Count );

		var frames = sheet.KeyFrames["framename"];

		Assert.IsNotNull( frames );
		Assert.AreEqual( 2, frames.Blocks.Count );
		Assert.AreEqual( 0, frames.Blocks[0].Interval );
		Assert.AreEqual( 1, frames.Blocks[1].Interval );
		Assert.AreEqual( Color.Parse( "red" ), frames.Blocks[0].Styles.BackgroundColor );
		Assert.AreEqual( Color.Parse( "green" ), frames.Blocks[1].Styles.BackgroundColor );
	}

	[TestMethod]
	public void MultiplePercentages()
	{
		var sheet = StyleParser.ParseSheet( "@keyframes FrameName " +
			"{ " +
			"	0%, 50%" +
			"		{" +
			"			background-color: red;" +
			"		}" +
			"	100%" +
			"		{" +
			"			background-color: green;" +
			"		}" +
			"}" );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.KeyFrames.Count );

		var frames = sheet.KeyFrames["framename"];

		Assert.IsNotNull( frames );
		Assert.AreEqual( 3, frames.Blocks.Count );
		Assert.AreEqual( 0, frames.Blocks[0].Interval );
		Assert.AreEqual( 0.5f, frames.Blocks[1].Interval );
		Assert.AreEqual( 1, frames.Blocks[2].Interval );
		Assert.AreEqual( Color.Parse( "red" ), frames.Blocks[0].Styles.BackgroundColor );
		Assert.AreEqual( Color.Parse( "red" ), frames.Blocks[1].Styles.BackgroundColor );
		Assert.AreEqual( Color.Parse( "green" ), frames.Blocks[2].Styles.BackgroundColor );
	}

	[TestMethod]
	public void Invalid()
	{
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes " ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes  {" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes }" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes {" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes Name Something" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes Name @" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes Name @ {" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes Name { }" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes Name { 33px { background-color: red; } }" ); } );
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes Name { start { background-color: red; } }" ); } );

		// forbid duplicate intervals
		Assert.ThrowsException<System.Exception>( () => { StyleParser.ParseSheet( "@keyframes Name { 33% { background-color: red; }  33% { background-color: blue; } }" ); } );
	}

	[TestMethod]
	public void File()
	{
		var sheet = StyleParser.ParseSheet( System.IO.File.ReadAllText( "unittest/styles/keyframes.scss" ) );

		Assert.IsNotNull( sheet );
		Assert.AreEqual( 1, sheet.KeyFrames.Count );

		var frames = sheet.KeyFrames["rotation"];

		Assert.IsNotNull( frames );
		Assert.AreEqual( 2, frames.Blocks.Count );
		Assert.AreEqual( 0, frames.Blocks[0].Interval );
		Assert.AreEqual( 1, frames.Blocks[1].Interval );
		Assert.AreEqual( Color.Parse( "red" ), frames.Blocks[0].Styles.BackgroundColor );
		Assert.AreEqual( Color.Parse( "green" ), frames.Blocks[1].Styles.BackgroundColor );
	}
}
