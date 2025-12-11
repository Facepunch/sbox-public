using System.Collections.Generic;

namespace Networking;

[TestClass]
public class NetDictionary
{
	[TestMethod]
	public void AddRemoveAndCount()
	{
		var dictionary = new NetDictionary<string, int>();
		Assert.IsTrue( dictionary.Count == 0 );

		dictionary.Add( "foo", 0 );
		Assert.IsTrue( dictionary.Count == 1 );
		Assert.AreEqual( dictionary["foo"], 0 );
		Assert.IsTrue( dictionary.ContainsKey( "foo" ) );

		dictionary.Remove( "foo" );
		Assert.IsTrue( dictionary.Count == 0 );
	}

	[TestMethod]
	public void Iterate()
	{
		var dictionary = new NetDictionary<string, int>();

		dictionary.Add( "a", 1 );
		dictionary.Add( "b", 2 );
		dictionary.Add( "c", 3 );

		var current = 0;
		foreach ( var (k, v) in dictionary )
		{
			var testKey = string.Empty;

			if ( current == 0 )
				testKey = "a";
			else if ( current == 1 )
				testKey = "b";
			else if ( current == 2 )
				testKey = "c";

			Assert.AreEqual( k, testKey );
			Assert.AreEqual( v, current + 1 );

			current++;
		}

		Assert.AreEqual( 3, current );

		Assert.AreEqual( 1, dictionary["a"] );
		Assert.AreEqual( 2, dictionary["b"] );
		Assert.AreEqual( 3, dictionary["c"] );
	}

	[TestMethod]
	public void ValidAccess()
	{
		var dictionary = new NetDictionary<string, int>();

		Assert.ThrowsException<KeyNotFoundException>( () =>
		{
			var _ = dictionary["a"];
		} );
	}
}
