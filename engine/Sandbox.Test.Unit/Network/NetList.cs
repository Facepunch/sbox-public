using System;

namespace Networking;

[TestClass]
public class NetList
{
	[TestMethod]
	public void AddRemoveAndCount()
	{
		var list = new NetList<int>();
		Assert.IsTrue( list.Count == 0 );

		list.Add( 3 );
		Assert.IsTrue( list.Count == 1 );
		Assert.AreEqual( 3, list[0] );

		list.Remove( 3 );
		Assert.IsTrue( list.Count == 0 );
	}

	[TestMethod]
	public void Iterate()
	{
		var list = new NetList<int>();

		list.Add( 1 );
		list.Add( 2 );
		list.Add( 3 );

		var current = 0;
		foreach ( var item in list )
		{
			current++;
			Assert.AreEqual( item, current );
		}

		Assert.AreEqual( 1, list[0] );
		Assert.AreEqual( 2, list[1] );
		Assert.AreEqual( 3, list[2] );
	}

	[TestMethod]
	public void ValidAccess()
	{
		var list = new NetList<int>();

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
		{
			list[0] = 1;
		} );
	}
}
