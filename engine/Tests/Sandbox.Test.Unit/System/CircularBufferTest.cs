using Sandbox.Utility;
using System;

namespace SystemTests;

[TestClass]
public class CircularBufferTest
{
	/// <summary>
	/// A fresh buffer should be empty, then fill up as items are pushed.
	/// </summary>
	[TestMethod]
	public void PushBackFills()
	{
		var buffer = new CircularBuffer<int>( 3 );

		Assert.IsTrue( buffer.IsEmpty );
		Assert.AreEqual( 3, buffer.Capacity );

		buffer.PushBack( 1 );
		buffer.PushBack( 2 );

		Assert.AreEqual( 2, buffer.Size );
		Assert.AreEqual( 1, buffer.Front() );
		Assert.AreEqual( 2, buffer.Back() );

		buffer.PushBack( 3 );
		Assert.IsTrue( buffer.IsFull );
	}

	/// <summary>
	/// Pushing onto a full buffer should overwrite the element at the
	/// opposite end rather than throwing.
	/// </summary>
	[TestMethod]
	public void PushBackWhenFullOverwritesFront()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushBack( 1 );
		buffer.PushBack( 2 );
		buffer.PushBack( 3 );

		buffer.PushBack( 4 );

		Assert.AreEqual( 3, buffer.Size );
		Assert.AreEqual( 2, buffer.Front() );
		Assert.AreEqual( 4, buffer.Back() );
		CollectionAssert.AreEqual( new[] { 2, 3, 4 }, buffer.ToArray() );
	}

	/// <summary>
	/// PushFront should prepend, and when full overwrite the back element.
	/// </summary>
	[TestMethod]
	public void PushFront()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushFront( 1 );
		buffer.PushFront( 2 );

		CollectionAssert.AreEqual( new[] { 2, 1 }, buffer.ToArray() );

		buffer.PushFront( 3 );
		buffer.PushFront( 4 );

		CollectionAssert.AreEqual( new[] { 4, 3, 2 }, buffer.ToArray() );
	}

	/// <summary>
	/// Popping from either end should remove exactly that element.
	/// </summary>
	[TestMethod]
	public void PopBothEnds()
	{
		var buffer = new CircularBuffer<int>( 4 );
		buffer.PushBack( 1 );
		buffer.PushBack( 2 );
		buffer.PushBack( 3 );

		buffer.PopFront();
		CollectionAssert.AreEqual( new[] { 2, 3 }, buffer.ToArray() );

		buffer.PopBack();
		CollectionAssert.AreEqual( new[] { 2 }, buffer.ToArray() );
	}

	/// <summary>
	/// The indexer should address elements from the front, and throw
	/// beyond the current size.
	/// </summary>
	[TestMethod]
	public void Indexer()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushBack( 10 );
		buffer.PushBack( 20 );

		Assert.AreEqual( 10, buffer[0] );
		Assert.AreEqual( 20, buffer[1] );
		Assert.ThrowsException<IndexOutOfRangeException>( () => buffer[2] );
	}

	/// <summary>
	/// Negative indices must throw after the internal array has wrapped,
	/// without changing any valid element.
	/// </summary>
	[TestMethod]
	public void IndexerRejectsNegativeIndicesAfterWrap()
	{
		var buffer = new CircularBuffer<int>( 3, new[] { 1, 2, 3 } );
		buffer.PushBack( 4 );

		foreach ( int index in new[] { -1, -2, -3, int.MinValue } )
		{
			Assert.ThrowsException<IndexOutOfRangeException>( () => buffer[index] );
			Assert.ThrowsException<IndexOutOfRangeException>( () => buffer[index] = 99 );
			CollectionAssert.AreEqual( new[] { 2, 3, 4 }, buffer.ToArray() );
		}

		Assert.AreEqual( 2, buffer[0] );
		Assert.AreEqual( 4, buffer[2] );
		buffer[2] = 5;
		CollectionAssert.AreEqual( new[] { 2, 3, 5 }, buffer.ToArray() );
	}

	/// <summary>
	/// Index bounds apply to fresh, partially filled and shifted buffers.
	/// </summary>
	[TestMethod]
	public void IndexerRejectsOutOfRangeIndices()
	{
		var empty = new CircularBuffer<int>( 3 );
		var partial = new CircularBuffer<int>( 3, new[] { 1, 2 } );
		var full = new CircularBuffer<int>( 3, new[] { 1, 2, 3 } );
		var shifted = new CircularBuffer<int>( 3, new[] { 1, 2, 3 } );
		shifted.PopFront();
		var frontWrapped = new CircularBuffer<int>( 3, new[] { 1, 2, 3 } );
		frontWrapped.PushFront( 0 );
		var emptied = new CircularBuffer<int>( 3, new[] { 1, 2, 3 } );
		emptied.PopFront();
		emptied.PopFront();
		emptied.PopFront();

		foreach ( var buffer in new[] { empty, partial, full, shifted, frontWrapped, emptied } )
		{
			var contents = buffer.ToArray();
			foreach ( int index in new[] { -1, -2, int.MinValue, buffer.Size, buffer.Size + 1, int.MaxValue } )
			{
				Assert.ThrowsException<IndexOutOfRangeException>( () => buffer[index] );
				Assert.ThrowsException<IndexOutOfRangeException>( () => buffer[index] = 99 );
				CollectionAssert.AreEqual( contents, buffer.ToArray() );
			}
		}
	}

	/// <summary>
	/// Clear should empty the buffer so it can be reused.
	/// </summary>
	[TestMethod]
	public void Clear()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushBack( 1 );
		buffer.PushBack( 2 );

		buffer.Clear();

		Assert.IsTrue( buffer.IsEmpty );
		Assert.AreEqual( 0, buffer.ToArray().Length );

		buffer.PushBack( 5 );
		Assert.AreEqual( 5, buffer.Front() );
	}

	/// <summary>
	/// Front, Back and Pop on an empty buffer should throw rather than
	/// return garbage.
	/// </summary>
	[TestMethod]
	public void EmptyBufferThrows()
	{
		var buffer = new CircularBuffer<int>( 3 );

		Assert.ThrowsException<InvalidOperationException>( () => buffer.Front() );
		Assert.ThrowsException<InvalidOperationException>( () => buffer.Back() );
		Assert.ThrowsException<InvalidOperationException>( () => buffer.PopBack() );
		Assert.ThrowsException<InvalidOperationException>( () => buffer.PopFront() );
	}

	/// <summary>
	/// Enumeration should yield elements front to back, even after the
	/// buffer has wrapped around its internal array.
	/// </summary>
	[TestMethod]
	public void EnumerationAfterWrap()
	{
		var buffer = new CircularBuffer<int>( 3 );
		for ( int i = 1; i <= 5; i++ )
		{
			buffer.PushBack( i );
		}

		CollectionAssert.AreEqual( new[] { 3, 4, 5 }, buffer.ToList() );
	}
}
