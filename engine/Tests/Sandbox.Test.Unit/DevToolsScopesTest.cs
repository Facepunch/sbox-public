using Sandbox.Diagnostics;
using System;

namespace EngineTests;

/// <summary>
/// The dev tools' CPU scopes: what each was opened inside, and every scope's time kept frame by frame.
/// </summary>
[TestClass]
public class DevToolsScopesTest
{
	/// <summary>
	/// A scope opened inside another on the same thread records it as its parent; the outer one is open again once the
	/// inner one closes, so a sibling opened after gets the same parent.
	/// </summary>
	[TestMethod]
	public void ScopesKnowWhatTheyreIn()
	{
		var outer = PerformanceStats.Timings.Get( "DevToolsScopesTest outer" );
		var inner = PerformanceStats.Timings.Get( "DevToolsScopesTest inner" );
		var sibling = PerformanceStats.Timings.Get( "DevToolsScopesTest sibling" );

		// What Scope() does around the timing, without the native profiler it also starts
		var top = outer.Opened();
		PerformanceStats.Timings.Closed( inner.Opened() );
		PerformanceStats.Timings.Closed( sibling.Opened() );
		PerformanceStats.Timings.Closed( top );

		Assert.AreEqual( outer, inner.Parent );
		Assert.AreEqual( outer, sibling.Parent, "the outer scope is open again after the inner one closes" );
		Assert.IsNull( outer.Parent );
	}

	/// <summary>
	/// The frame ring keeps every scope's time and calls for each frame, including scopes that appear after it started.
	/// </summary>
	[TestMethod]
	public void FramesKeepEveryScope()
	{
		var frames = new DevTools.FrameHistory();
		var timing = PerformanceStats.Timings.Get( $"DevToolsScopesTest {Guid.NewGuid()}" );

		timing.AddMilliseconds( 2, 3 );
		frames.Push( 10, 0, 0, 0 );
		timing.Flip();
		frames.Push( 10, 0, 0, 0 );

		var index = -1;
		for ( int i = 0; i < frames.ScopeCount; i++ )
			if ( frames.Scope( i ) == timing ) index = i;

		Assert.IsTrue( index >= 0, "a scope made after the ring was is taken on" );
		Assert.AreEqual( 0, frames.ScopeMs( 0, index ), "the newest frame had none" );
		Assert.AreEqual( 2, frames.ScopeMs( 1, index ), 0.001f );
		Assert.AreEqual( 3, frames.ScopeCalls( 1, index ) );
		Assert.AreEqual( 2, frames.Clone().ScopeMs( 1, index ), 0.001f, "a paused copy keeps them" );
	}

	[TestMethod]
	public void FramesKeepTheGcPause()
	{
		// The GC pause scope is only booked when the next frame starts, so the frame's own measure is what's kept
		var frames = new DevTools.FrameHistory();
		frames.Push( 10, 0, 0, 0, gcMs: 4 );

		var index = -1;
		for ( int i = 0; i < frames.ScopeCount; i++ )
			if ( frames.Scope( i ) == PerformanceStats.Timings.GcPause ) index = i;

		Assert.IsTrue( index >= 0 );
		Assert.AreEqual( 4, frames.ScopeMs( 0, index ), 0.001f );
	}
}
