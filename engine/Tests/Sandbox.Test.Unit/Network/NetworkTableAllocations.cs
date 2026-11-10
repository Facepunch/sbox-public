using System;
using System.Collections.Generic;
using Sandbox.Network;

namespace NetworkTests;

[TestClass]
public class NetworkTableAllocationsTest
{
	static NetworkTable.Entry RegisterVector( NetworkTable table, int slot )
	{
		var value = Vector3.Zero;
		var entry = new NetworkTable.Entry { TargetType = typeof( Vector3 ), GetValue = () => value, SetValue = v => value = (Vector3)v };
		table.Register( slot, entry );
		entry.IsDirty = false;
		return entry;
	}

	[TestMethod]
	public void UpdateSlotHashValueTypeDoesNotAllocate()
	{
		using var table = new NetworkTable();
		var entry = RegisterVector( table, 3 );

		table.UpdateSlotHash( 3, new Vector3( 1, 2, 3 ) );

		var before = GC.GetAllocatedBytesForCurrentThread();

		for ( var i = 0; i < 1000; i++ )
		{
			table.UpdateSlotHash( 3, new Vector3( i, 0, 0 ) );
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.AreEqual( 0, allocated );
		Assert.IsTrue( entry.IsDirty );
	}

	[TestMethod]
	public void NetworkTransformsSetDoesNotAllocate()
	{
		var table = new NetworkTransforms();
		table.Set( 0, Transform.Zero );
		table.Set( 0, new Transform( Vector3.One ) );

		var before = GC.GetAllocatedBytesForCurrentThread();

		for ( var i = 0; i < 1000; i++ )
		{
			table.Set( 0, new Transform( new Vector3( i, 0, 0 ) ) );
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.AreEqual( 0, allocated );
		Assert.AreEqual( new Vector3( 999, 0, 0 ), table.Get( 0 ).Position );
	}

	[TestMethod]
	public void ValueTypeListHashDoesNotBoxElements()
	{
		var list = Enumerable.Range( 0, 32 ).Select( i => new Vector3( i, 0, 0 ) ).ToList();

		using var table = new NetworkTable();
		var entry = new NetworkTable.Entry { TargetType = typeof( List<Vector3> ), GetValue = () => list, SetValue = v => list = (List<Vector3>)v };
		table.Register( 3, entry );
		entry.IsDirty = false;

		object boxed = list;
		table.UpdateSlotHash( 3, boxed );
		Assert.IsFalse( entry.IsDirty );

		var before = GC.GetAllocatedBytesForCurrentThread();

		for ( var i = 0; i < 100; i++ )
		{
			table.UpdateSlotHash( 3, boxed );
		}

		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.AreEqual( 0, allocated );

		list[5] = Vector3.One;
		table.UpdateSlotHash( 3, boxed );
		Assert.IsTrue( entry.IsDirty );
	}

	[TestMethod]
	public void UpdateSlotHashGenericMatchesBoxed()
	{
		using var table = new NetworkTable();
		var entry = RegisterVector( table, 3 );

		var v = new Vector3( 4, 5, 6 );
		table.UpdateSlotHash( 3, v );
		Assert.IsTrue( entry.IsDirty );

		entry.IsDirty = false;
		table.UpdateSlotHash( 3, (object)v );
		Assert.IsFalse( entry.IsDirty );
	}
}
