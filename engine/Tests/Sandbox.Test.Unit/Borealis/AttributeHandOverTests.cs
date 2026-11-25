using System;
using System.Threading;
using Sandbox.Rendering;

namespace BorealisTests;

/// <summary>
/// Attribute sets and command lists across a hand-over (docs/reference/borealis-render-thread.md): while a frame handed over
/// may be drawing on the render thread, the main thread's first write since the hand-over keeps the set as it was for that
/// frame and goes on in a copy, so the render thread reads every value as it was handed over; and once warm, the copies a
/// steady frame makes at the hand-over allocate nothing.
/// </summary>
[TestClass, DoNotParallelize]
public class AttributeHandOverTests
{
	[TestMethod]
	public void TheRenderThreadReadsTheSetAsItWasHandedOver()
	{
		var overlapping = RenderSync.Overlapping;
		var set = RenderAttributes.CreateManaged();
		try
		{
			set.Set( "Tint", new Vector4( 1, 0, 0, 1 ) );
			set.SetCombo( "D_MODE", 1 );

			RenderSync.Overlapping = true;
			RenderSync.HandOver();
			var splits = RenderSync.AttributeSplits;

			// The game writes the next frame's values: the same token again (its array is reused in place), a new one, a combo
			set.Set( "Tint", new Vector4( 0, 1, 0, 1 ) );
			set.Set( "Added", 2.0f );
			set.SetCombo( "D_MODE", 2 );
			Assert.AreEqual( splits + 1, RenderSync.AttributeSplits, "copied once, at the first write" );

			(Vector4 Tint, float Added, bool HasAdded, int? Mode) seen = default;
			OnRenderThread( () => seen = (set.GetVector4( "Tint" ), set.GetFloat( "Added" ), set.Managed.Get( "Added" ) is not null, set.Managed.GetCombo( "D_MODE" )) );
			Assert.AreEqual( new Vector4( 1, 0, 0, 1 ), seen.Tint, "the render thread reads the value handed over" );
			Assert.IsFalse( seen.HasAdded, "and not what was set since" );
			Assert.AreEqual( 1, seen.Mode );

			Assert.AreEqual( new Vector4( 0, 1, 0, 1 ), set.GetVector4( "Tint" ), "the main thread reads what it wrote" );
			Assert.AreEqual( 2.0f, set.GetFloat( "Added" ) );
			Assert.AreEqual( 2, set.Managed.GetCombo( "D_MODE" ) );

			// The next hand-over: the render thread reads what the main thread wrote
			RenderSync.HandOver();
			OnRenderThread( () => seen = (set.GetVector4( "Tint" ), set.GetFloat( "Added" ), true, set.Managed.GetCombo( "D_MODE" )) );
			Assert.AreEqual( new Vector4( 0, 1, 0, 1 ), seen.Tint );
			Assert.AreEqual( 2.0f, seen.Added );
			Assert.AreEqual( 2, seen.Mode );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
		}
	}

	[TestMethod]
	public void SerialModeNeverCopies()
	{
		var overlapping = RenderSync.Overlapping;
		var set = RenderAttributes.CreateManaged();
		try
		{
			RenderSync.Overlapping = false;
			set.Set( "Tint", new Vector4( 1, 0, 0, 1 ) );
			RenderSync.HandOver();
			var splits = RenderSync.AttributeSplits;
			set.Set( "Tint", new Vector4( 0, 1, 0, 1 ) );
			Assert.AreEqual( splits, RenderSync.AttributeSplits );

			Vector4 seen = default;
			OnRenderThread( () => seen = set.GetVector4( "Tint" ) );
			Assert.AreEqual( new Vector4( 0, 1, 0, 1 ), seen );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
		}
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void HandOverCopiesAllocateNothingOnceWarm()
	{
		var overlapping = RenderSync.Overlapping;
		var set = RenderAttributes.CreateManaged();
		var list = new CommandList( "steady" );
		Action noop = static () => { };
		try
		{
			RenderSync.Overlapping = true;
			set.Set( "Tint", new Vector4( 1, 0, 0, 1 ) );
			set.Set( "Transform", Matrix.Identity );
			set.SetCombo( "D_MODE", 1 );

			// A frame as a steady game records it: hand-over, then the set written again and the list recorded again
			Allocations.AssertSettles( "a set written and a list recorded after each hand-over", () =>
			{
				RenderSync.HandOver();
				set.Set( "Tint", new Vector4( 0, 1, 0, 1 ) );
				set.Set( "Transform", Matrix.Identity );
				set.SetCombo( "D_MODE", 2 );
				list.Reset();
				list.AddAction( noop );
				list.AddAction( noop );
			} );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
		}
	}

	// Run on a thread marked as the render thread (as the game window's is), and rethrow what it threw
	static void OnRenderThread( Action action )
	{
		Exception error = null;
		var thread = new Thread( () =>
		{
			ThreadSafe.MarkRenderThread();
			try
			{
				action();
			}
			catch ( Exception e )
			{
				error = e;
			}
		} );
		thread.Start();
		thread.Join();
		if ( error is not null ) throw new AggregateException( error );
	}
}
