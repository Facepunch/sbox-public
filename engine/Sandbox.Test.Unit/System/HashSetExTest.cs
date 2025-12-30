using Sandbox.Utility;

namespace SystemTest;

[TestClass]
public class HashSetExTest
{
	/// <summary>
	/// Additions to a <see cref="HashSetEx{T}"/> while enumerating it must be
	/// deferred until after the enumeration.
	/// </summary>
	[TestMethod]
	[DataRow( 1_000 )]
	[DataRow( 10_000 )]
	[DataRow( 100_000 )]
	//[DataRow( 1_000_000 )]
	public void DeferredAdd( int itemCount )
	{
		var hashSet = new HashSetEx<int>();

		for ( var i = 0; i < itemCount; ++i )
		{
			hashSet.Add( i );
		}

		Assert.AreEqual( itemCount, hashSet.Count );

		var enumeratedCount = 0;

		foreach ( var item in hashSet.EnumerateLocked() )
		{
			// Test item < itemCount here to avoid endless loop if EnumerateLocked doesn't work

			if ( item < itemCount )
			{
				hashSet.Add( item + itemCount );
			}

			enumeratedCount++;
		}

		Assert.AreEqual( itemCount, enumeratedCount );
		Assert.AreEqual( itemCount * 2, hashSet.Count );
	}

	/// <summary>
	/// Removals from a <see cref="HashSetEx{T}"/> while enumerating it must be
	/// deferred until after the enumeration.
	/// </summary>
	[TestMethod]
	[DataRow( 1_000 )]
	[DataRow( 10_000 )]
	[DataRow( 100_000 )]
	//[DataRow( 1_000_000 )]
	public void DeferredRemove( int itemCount )
	{
		var hashSet = new HashSetEx<int>();

		for ( var i = 0; i < itemCount; ++i )
		{
			hashSet.Add( i );
		}

		Assert.AreEqual( itemCount, hashSet.Count );

		foreach ( var item in hashSet.EnumerateLocked() )
		{
			hashSet.Remove( item );
		}

		Assert.AreEqual( 0, hashSet.Count );
	}

	/// <summary>
	/// Stress test iterating variously sized <see cref="HashSetEx{T}"/> 1,000 times, with
	/// the set being modified between each iteration.
	/// </summary>
	[TestMethod]
	[DataRow( 1_000, 1_000 )]
	[DataRow( 10_000, 1_000 )]
	[DataRow( 100_000, 1_000 )]
	public void WorstCaseIteration( int itemCount, int iterations )
	{
		var hashSet = new HashSetEx<int>();

		for ( var i = 0; i < itemCount; ++i )
		{
			hashSet.Add( i );
		}

		for ( var i = 0; i < iterations; ++i )
		{
			Assert.AreEqual( itemCount, hashSet.Count );

			var sum = 0;

			foreach ( var item in hashSet.EnumerateLocked() )
			{
				sum += item;
			}

			// Force list to dirty

			hashSet.Add( -1 );
			hashSet.Remove( -1 );
		}
	}
}
