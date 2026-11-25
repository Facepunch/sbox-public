using System;
using System.Collections.Generic;
using System.Threading;
using Sandbox.Rendering;

namespace BorealisTests;

/// <summary>
/// Command lists across a hand-over (docs/reference/borealis-render-thread.md): while a frame handed over may replay on the
/// render thread, the main thread's first change to a list since the hand-over keeps the list as it was for that frame and
/// records into a copy, so the render thread replays the list as it was handed over and the main thread what it records now.
/// A list nobody records into isn't copied, nor anything in serial mode.
/// </summary>
[TestClass, DoNotParallelize]
public class CommandListHandOverTests
{
	[TestMethod]
	public void TheRenderThreadReplaysTheListAsItWasHandedOver()
	{
		var overlapping = RenderSync.Overlapping;
		var seen = new List<string>();
		var list = new CommandList( "hand-over" );
		try
		{
			list.AddAction( () => seen.Add( "first frame" ) );

			RenderSync.Overlapping = true;
			RenderSync.HandOver();
			var splits = RenderSync.ListSplits;

			// The game records the next frame into the list: reset, two entries
			list.Reset();
			list.AddAction( () => seen.Add( "next frame" ) );
			list.AddAction( () => seen.Add( "next frame" ) );
			Assert.AreEqual( splits + 1, RenderSync.ListSplits, "copied once, at the first change" );

			OnRenderThread( list.Execute );
			CollectionAssert.AreEqual( new[] { "first frame" }, seen, "the render thread replays the list as it was handed over" );

			seen.Clear();
			list.Execute();
			CollectionAssert.AreEqual( new[] { "next frame", "next frame" }, seen, "the main thread replays what it recorded" );

			// The next hand-over: the render thread replays what the main thread recorded, until it records again
			RenderSync.HandOver();
			seen.Clear();
			OnRenderThread( list.Execute );
			CollectionAssert.AreEqual( new[] { "next frame", "next frame" }, seen );
			Assert.AreEqual( splits + 1, RenderSync.ListSplits, "a list left alone isn't copied" );

			// Recording more without a reset keeps what was there, for the main thread, and the frame's copy as it was
			list.AddAction( () => seen.Add( "added" ) );
			seen.Clear();
			OnRenderThread( list.Execute );
			CollectionAssert.AreEqual( new[] { "next frame", "next frame" }, seen );
			seen.Clear();
			list.Execute();
			CollectionAssert.AreEqual( new[] { "next frame", "next frame", "added" }, seen );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
		}
	}

	[TestMethod]
	public void InsertedListsReplayAsTheyWereHandedOver()
	{
		var overlapping = RenderSync.Overlapping;
		var seen = new List<string>();
		var outer = new CommandList( "outer" );
		var inner = new CommandList( "inner" );
		try
		{
			inner.AddAction( () => seen.Add( "inner as handed over" ) );
			outer.InsertList( inner );

			RenderSync.Overlapping = true;
			RenderSync.HandOver();
			inner.Reset();
			inner.AddAction( () => seen.Add( "inner recorded since" ) );

			OnRenderThread( outer.Execute );
			CollectionAssert.AreEqual( new[] { "inner as handed over" }, seen );

			seen.Clear();
			outer.Execute();
			CollectionAssert.AreEqual( new[] { "inner recorded since" }, seen );
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
		var seen = new List<string>();
		var list = new CommandList( "serial" );
		try
		{
			RenderSync.Overlapping = false;
			list.AddAction( () => seen.Add( "a" ) );
			RenderSync.HandOver();
			var splits = RenderSync.ListSplits;
			list.Reset();
			list.AddAction( () => seen.Add( "b" ) );
			Assert.AreEqual( splits, RenderSync.ListSplits );

			OnRenderThread( list.Execute );
			CollectionAssert.AreEqual( new[] { "b" }, seen, "with nothing overlapping a replay anywhere reads what's recorded" );
		}
		finally
		{
			RenderSync.Overlapping = overlapping;
		}
	}

	// Run on a thread marked as the render thread, and rethrow what it threw
	static void OnRenderThread( Action action )
	{
		Exception error = null;
		var thread = new Thread( () =>
		{
			RenderSync.MarkRenderThread( true );
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
