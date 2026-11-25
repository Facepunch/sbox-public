using Sandbox;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

[TestClass]
[DoNotParallelize]
public class ClosedGameMemoryTests
{
	// Something like a load: small objects and big arrays, every other one kept, all written so their pages are committed
	[MethodImpl( MethodImplOptions.NoInlining )]
	static List<object> FillAndDropHalf()
	{
		var all = new List<object>();
		for ( int i = 0; i < 400; i++ ) all.Add( new byte[1 << 20] );
		for ( int i = 0; i < 2_000_000; i++ ) all.Add( new int[16] );
		foreach ( var o in all ) if ( o is byte[] b ) Array.Fill( b, (byte)1 );
		var kept = new List<object>();
		for ( int i = 0; i < all.Count; i += 2 ) kept.Add( all[i] );
		return kept;
	}

	/// <summary>
	/// What a load left free, between what's still live, stays committed through an ordinary collection; the collection
	/// after a close gives it back.
	/// </summary>
	[TestMethod]
	public void GivesBackWhatAClosedGameLeftCommitted()
	{
		var kept = FillAndDropHalf();
		GC.Collect( GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false );
		var before = GC.GetGCMemoryInfo();
		if ( ClosedGameMemory.Unused( before ) < ClosedGameMemory.Threshold )
			Assert.Inconclusive( $"The collector gave back the free memory by itself: {before.TotalCommittedBytes >> 20} MiB committed, {before.HeapSizeBytes >> 20} MiB heap, {before.FragmentedBytes >> 20} MiB of it free" );

		var given = ClosedGameMemory.Return();
		var after = GC.GetGCMemoryInfo();
		GC.KeepAlive( kept );
		Assert.IsTrue( given >= ClosedGameMemory.Threshold, $"gave back {given >> 20} MiB" );
		Assert.IsTrue( ClosedGameMemory.Unused( after ) < ClosedGameMemory.Threshold / 2, $"{after.TotalCommittedBytes >> 20} MiB committed, {after.HeapSizeBytes >> 20} MiB heap, {after.FragmentedBytes >> 20} MiB of it free" );
	}

	/// <summary>With little unused it doesn't stop the threads for nothing.</summary>
	[TestMethod]
	public void DoesNothingWithLittleFree()
	{
		var count = GC.CollectionCount( GC.MaxGeneration );
		Assert.AreEqual( 0, ClosedGameMemory.Return( threshold: long.MaxValue ) );
		Assert.AreEqual( count, GC.CollectionCount( GC.MaxGeneration ) );
	}
}
