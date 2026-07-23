namespace SystemTests;

[TestClass]
public class StableRandomTest
{
	[TestMethod]
	public void RandomDoesntChange()
	{
		Game.SetRandomSeed( 42 );

		Assert.AreEqual( 67, Game.Random.Int( 100 ) );
		Assert.AreEqual( 14, Game.Random.Int( 100 ) );
		Assert.AreEqual( 12, Game.Random.Int( 100 ) );
		Assert.AreEqual( 52, Game.Random.Int( 100 ) );
		Assert.AreEqual( 17, Game.Random.Int( 100 ) );
		Assert.AreEqual( 26, Game.Random.Int( 100 ) );
		Assert.AreEqual( 73, Game.Random.Int( 100 ) );
		Assert.AreEqual( 51, Game.Random.Int( 100 ) );
		Assert.AreEqual( 17, Game.Random.Int( 100 ) );
		Assert.AreEqual( 76, Game.Random.Int( 100 ) );
		Assert.AreEqual( 23, Game.Random.Int( 100 ) );
	}
}
