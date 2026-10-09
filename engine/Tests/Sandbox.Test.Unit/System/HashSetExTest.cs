using System;
using System.Collections.Generic;
using Sandbox.Utility;

namespace SystemTests;

[TestClass]
public class HashSetExTest
{
	/// <summary>
	/// Additions to a <see cref="HashSetEx{T}"/> while enumerating it must be
	/// deferred until after the enumeration.
	/// </summary>
	[TestMethod]
	[DataRow( 1_000 )]
	//[DataRow( 10_000 )]
	//[DataRow( 100_000 )]
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
	//[DataRow( 10_000 )]
	//[DataRow( 100_000 )]
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
	//[DataRow( 10_000, 1_000 )]
	//[DataRow( 100_000, 1_000 )]
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

	/// <summary>
	/// <see cref="HashSetEx{T}.EnumerateLocked"/> returns a struct enumerable, so iterating it
	/// mustn't allocate. This is the whole reason it isn't a normal iterator method.
	/// </summary>
	[TestMethod]
	public void EnumerationIsAllocationFree()
	{
		var hashSet = new HashSetEx<object>();

		for ( var i = 0; i < 64; ++i )
		{
			hashSet.Add( new object() );
		}

		var count = 0;

		// Warm up, so the list is built and everything is jitted

		for ( var i = 0; i < 4; ++i )
		{
			foreach ( var item in hashSet.EnumerateLocked() ) count++;
			foreach ( var item in hashSet.EnumerateLocked( true ) ) count++;
		}

		count = 0;

		var before = GC.GetAllocatedBytesForCurrentThread();

		foreach ( var item in hashSet.EnumerateLocked() ) count++;
		foreach ( var item in hashSet.EnumerateLocked( true ) ) count++;

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.AreEqual( 0, allocated, $"Enumeration allocated {allocated} bytes" );
		Assert.AreEqual( 64 + 64, count );
	}

	/// <summary>
	/// Breaking out of the loop early must still release the lock, otherwise the
	/// list would never be rebuilt again.
	/// </summary>
	[TestMethod]
	public void BreakReleasesLock()
	{
		var hashSet = new HashSetEx<int>();

		hashSet.Add( 1 );
		hashSet.Add( 2 );
		hashSet.Add( 3 );

		foreach ( var item in hashSet.EnumerateLocked() )
		{
			break;
		}

		hashSet.Add( 4 );

		Assert.AreEqual( 4, hashSet.List.Count );
	}

	/// <summary>
	/// Throwing out of the loop must still release the lock.
	/// </summary>
	[TestMethod]
	public void ExceptionReleasesLock()
	{
		var hashSet = new HashSetEx<int>();

		hashSet.Add( 1 );
		hashSet.Add( 2 );
		hashSet.Add( 3 );

		try
		{
			foreach ( var item in hashSet.EnumerateLocked() )
			{
				throw new InvalidOperationException( "Test" );
			}
		}
		catch ( InvalidOperationException )
		{
			// Expected
		}

		hashSet.Add( 4 );

		Assert.AreEqual( 4, hashSet.List.Count );
	}

	/// <summary>
	/// Nested enumeration of the same set must work, with the inner loop seeing the same
	/// snapshot, and the lock only released once the outer loop is done.
	/// </summary>
	[TestMethod]
	public void NestedEnumeration()
	{
		var hashSet = new HashSetEx<int>();

		hashSet.Add( 1 );
		hashSet.Add( 2 );
		hashSet.Add( 3 );

		var innerCount = 0;

		foreach ( var outer in hashSet.EnumerateLocked() )
		{
			hashSet.Add( outer + 100 );

			var thisInnerCount = 0;

			foreach ( var inner in hashSet.EnumerateLocked() )
			{
				thisInnerCount++;
			}

			// Inner loop sees the snapshot the outer loop is holding, not the additions

			Assert.AreEqual( 3, thisInnerCount );

			innerCount += thisInnerCount;

			// Still locked by the outer loop, so the list is stale

			Assert.AreEqual( 3, hashSet.List.Count );
		}

		Assert.AreEqual( 9, innerCount );
		Assert.AreEqual( 6, hashSet.List.Count );
	}

	private class ValidThing : IValid
	{
		public bool IsValid { get; set; } = true;
	}

	/// <summary>
	/// With null checks enabled, invalid items must be skipped and removed from the set.
	/// </summary>
	[TestMethod]
	public void NullChecksSkipAndRemoveInvalid()
	{
		var hashSet = new HashSetEx<ValidThing>();

		var things = new ValidThing[4];

		for ( var i = 0; i < things.Length; ++i )
		{
			things[i] = new ValidThing { IsValid = i % 2 == 0 };
			hashSet.Add( things[i] );
		}

		var enumerated = new List<ValidThing>();

		foreach ( var thing in hashSet.EnumerateLocked( true ) )
		{
			enumerated.Add( thing );
		}

		Assert.AreEqual( 2, enumerated.Count );
		Assert.IsTrue( enumerated.TrueForAll( x => x.IsValid ) );
		Assert.AreEqual( 2, hashSet.Count );

		// Everything invalid, including the first item, so nothing is enumerated

		foreach ( var thing in enumerated )
		{
			thing.IsValid = false;
		}

		var secondPassCount = 0;

		foreach ( var thing in hashSet.EnumerateLocked( true ) )
		{
			secondPassCount++;
		}

		Assert.AreEqual( 0, secondPassCount );
		Assert.AreEqual( 0, hashSet.Count );
	}
}
