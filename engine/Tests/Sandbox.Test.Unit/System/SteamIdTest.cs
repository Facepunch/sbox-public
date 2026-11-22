using System;
using System.Collections.Generic;

namespace SystemTests;

[TestClass]
public class SteamIdTest
{
	[TestMethod]
	public void EqualityAndHashingFollowTheId()
	{
		SteamId a = 76561197960279927UL;
		SteamId b = 76561197960279927L;
		SteamId c = 76561197960279928UL;

		Assert.IsTrue( a.Equals( b ) );
		Assert.IsTrue( a.Equals( (object)b ) );
		Assert.IsFalse( a.Equals( c ) );
		Assert.IsFalse( a.Equals( (object)76561197960279927UL ) );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );

		var set = new HashSet<SteamId> { a };
		Assert.IsTrue( set.Contains( b ) );
		Assert.IsFalse( set.Contains( c ) );
		Assert.AreEqual( HashCode.Combine( (SteamId?)a ), HashCode.Combine( (SteamId?)b ) );
	}

	[TestMethod]
	public void HashingDoesNotAllocate()
	{
		SteamId? id = 76561197960279927UL;
		var set = new HashSet<SteamId> { 1UL };
		_ = HashCode.Combine( id );
		_ = set.Contains( 1UL );

		var before = GC.GetAllocatedBytesForCurrentThread();
		for ( var i = 0; i < 100; i++ )
		{
			_ = HashCode.Combine( id );
			_ = set.Contains( 1UL );
		}

		Assert.AreEqual( 0, GC.GetAllocatedBytesForCurrentThread() - before );
	}
}
