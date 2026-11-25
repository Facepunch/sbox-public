using Sandbox.Internal;
using Sandbox.Network;
using System;

namespace NetworkTests;

/// <summary>
/// Connection.All is reused while nothing changes, replaced when connections change, and never changed in place.
/// </summary>
[TestClass]
[DoNotParallelize]
public class ConnectionAllTest
{
	[TestMethod]
	public void OfflineIsJustLocal()
	{
		var previousSystem = Networking.System;
		try
		{
			Networking.System = null;

			Assert.AreEqual( 1, Connection.All.Count );
			Assert.AreSame( Connection.Local, Connection.All[0] );
			Assert.AreSame( Connection.All, Connection.All );
		}
		finally { Networking.System = previousSystem; }
	}

	[TestMethod]
	public void FollowsConnectionInfoWithoutChangingOldLists()
	{
		var previousSystem = Networking.System;
		var previousLocal = Connection.Local;
		try
		{
			var system = new NetworkSystem( "connection-all", new TypeLibrary() );
			Networking.System = system;
			Connection.Local = new LocalConnection( Guid.NewGuid() );

			var a = new MockConnection( Guid.NewGuid() );
			var b = new MockConnection( Guid.NewGuid() );

			system.ConnectionInfo.Add( Connection.Local );
			system.ConnectionInfo.Add( a );
			system.ConnectionInfo.Add( b );

			var first = Connection.All;
			Assert.AreEqual( 3, first.Count );
			Assert.AreSame( Connection.Local, first[0] );
			Assert.AreEqual( a.Id, first[1].Id );
			Assert.AreEqual( b.Id, first[2].Id );
			Assert.AreSame( first, Connection.All, "unchanged connections reuse the list" );

			var visited = 0;
			foreach ( var c in first )
			{
				if ( visited++ == 0 )
					system.ConnectionInfo.Remove( a.Id );
			}

			Assert.AreEqual( 3, visited );
			Assert.AreEqual( 3, first.Count, "a list already handed out doesn't change" );

			var second = Connection.All;
			Assert.AreNotSame( first, second );
			CollectionAssert.AreEqual( new[] { first[0], first[2] }, second.ToArray() );

			var c2 = new MockConnection( Guid.NewGuid() );
			system.ConnectionInfo.Add( c2 );
			Assert.AreEqual( 3, Connection.All.Count );
			Assert.IsTrue( Connection.All.Any( x => x.Id == c2.Id ) );

			Connection.Local = new LocalConnection( Guid.NewGuid() );
			Assert.IsFalse( Connection.All.Contains( first[0] ), "the old local connection is gone once it's replaced" );
		}
		finally
		{
			Connection.Local = previousLocal;
			Networking.System = previousSystem;
		}
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void UnchangedConnectionsDoNotAllocate()
	{
		var previousSystem = Networking.System;
		var previousLocal = Connection.Local;
		try
		{
			var system = new NetworkSystem( "connection-all-allocs", new TypeLibrary() );
			Networking.System = system;
			Connection.Local = new LocalConnection( Guid.NewGuid() );

			system.ConnectionInfo.Add( Connection.Local );
			for ( var i = 0; i < 8; i++ )
				system.ConnectionInfo.Add( new MockConnection( Guid.NewGuid() ) );

			var count = 0;
			Allocations.AssertSettles( "Connection.All", () => count += Connection.All.Count );

			Assert.IsTrue( count > 0 );
		}
		finally
		{
			Connection.Local = previousLocal;
			Networking.System = previousSystem;
		}
	}
}
