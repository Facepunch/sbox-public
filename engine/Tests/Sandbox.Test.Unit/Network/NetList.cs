using System;
using System.Collections.Generic;
using System.Collections.Specialized;

namespace NetworkTests;

[TestClass]
public class NetListTest
{
	[TestMethod]
	public void ConstructFromEnumerableAndForEach()
	{
		var source = new List<int> { 3, 1, 2 };
		using var list = new NetList<int>( source );
		source.Clear();
		var visited = new List<int>();
		list.ForEach( visited.Add );
		CollectionAssert.AreEqual( new[] { 3, 1, 2 }, visited );
		Assert.ThrowsException<ArgumentNullException>( () => new NetList<int>( null ) );
		Assert.ThrowsException<ArgumentNullException>( () => list.ForEach( null ) );
		Assert.ThrowsException<InvalidOperationException>( () => list.ForEach( _ => list.Add( 4 ) ) );
	}

	[TestMethod]
	public void RemoveAllPreservesPredicateOrderAndRecordsRemovals()
	{
		using var list = new NetList<int>( new[] { 1, 2, 3, 4, 5 } );
		var visited = new List<int>();
		var removedIndices = new List<int>();
		list.OnChanged = change =>
		{
			Assert.AreEqual( NotifyCollectionChangedAction.Remove, change.Type );
			removedIndices.Add( change.Index );
		};
		Assert.AreEqual( 2, list.RemoveAll( x => { visited.Add( x ); return x % 2 == 0; } ) );
		CollectionAssert.AreEqual( new[] { 1, 2, 3, 4, 5 }, visited );
		CollectionAssert.AreEqual( new[] { 1, 3, 5 }, list.ToArray() );
		CollectionAssert.AreEqual( new[] { 3, 1 }, removedIndices );
		Assert.AreEqual( 0, list.RemoveAll( _ => false ) );
		Assert.ThrowsException<ArgumentNullException>( () => list.RemoveAll( null ) );
	}

	[TestMethod]
	public void AddRemoveAndCount()
	{
		using var list = new NetList<int>();
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
		using var list = new NetList<int>();

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
	public void OnChangedIsInvokedWhenItemIsAdded()
	{
		using var list = new NetList<int>();

		var callCount = 0;
		NetListChangeEvent<int> receivedEvent = default;

		list.OnChanged = change =>
		{
			callCount++;
			receivedEvent = change;
		};

		list.Add( 42 );

		Assert.AreEqual( 1, callCount );
		Assert.AreEqual( NotifyCollectionChangedAction.Add, receivedEvent.Type );
		Assert.AreEqual( 0, receivedEvent.Index );
		Assert.AreEqual( 42, receivedEvent.NewValue );
	}

	[TestMethod]
	public void OnChangedIsInvokedWhenItemIsRemoved()
	{
		using var list = new NetList<int>();

		list.Add( 10 );
		list.Add( 20 );

		var callCount = 0;
		NetListChangeEvent<int> receivedEvent = default;

		list.OnChanged = change =>
		{
			callCount++;
			receivedEvent = change;
		};

		list.Remove( 10 );

		Assert.AreEqual( 1, callCount );
		Assert.AreEqual( NotifyCollectionChangedAction.Remove, receivedEvent.Type );
		Assert.AreEqual( 0, receivedEvent.Index ); // 10 was at index 0
		Assert.AreEqual( 10, receivedEvent.OldValue ); // removed value
	}

	[TestMethod]
	public void OnChangedIsInvokedWhenListIsCleared()
	{
		using var list = new NetList<int>();

		list.Add( 1 );
		list.Add( 2 );

		var callCount = 0;
		NetListChangeEvent<int> receivedEvent = default;

		list.OnChanged = change =>
		{
			callCount++;
			receivedEvent = change;
		};

		list.Clear();

		Assert.AreEqual( 1, callCount );
		Assert.AreEqual( NotifyCollectionChangedAction.Reset, receivedEvent.Type );
		Assert.AreEqual( 0, list.Count );
	}

	[TestMethod]
	public void OnChangedIsNotInvokedWhenNoChangeOccurs()
	{
		using var list = new NetList<int>();
		var callCount = 0;

		list.OnChanged = _ =>
		{
			callCount++;
		};

		list.Add( 5 );

		// Removing an item that does not exist should not trigger a change
		list.Remove( 999 );

		Assert.AreEqual( 1, callCount );
	}

	[TestMethod]
	public void ValidAccess()
	{
		using var list = new NetList<int>();

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
		{
			list[0] = 1;
		} );
	}
}
