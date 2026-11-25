using System;
using System.Text;
using System.Threading;
using NativeEngine;
using Sandbox;
using Sandbox.Diagnostics;
using Sandbox.Network;

namespace SystemTest;

/// <summary>
/// What the game does every frame outside the renderer allocates nothing once warm (owner, 2026-11-17: zero managed
/// allocation per steady frame): each of these allocated every frame on Construct before. The renderer's own frames are held
/// to the same by <c>RendererAllocationTests</c> and Borealis.Gpu.Test's <c>AllocationTests</c>.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SteadyFrameAllocationTests
{
	/// <summary>
	/// Reading the local connection's name or id outside a network session (the menu's party panel did every frame) refreshes
	/// its info mock without building a <see cref="UserInfo"/>, and reads the user data from the ConVars only once they've changed.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void LocalConnectionInfoAllocatesNothing()
	{
		Allocations.AssertSettles( "the local connection's info", () =>
		{
			var info = ConnectionInfo.GetLocalMock();
			_ = info.SteamId;
			_ = info.Name;
		} );
	}

	/// <summary>A ConVar changing makes the local mock read the user data again.</summary>
	[TestMethod]
	public void LocalConnectionInfoFollowsConVarChanges()
	{
		var info = ConnectionInfo.GetLocalMock();
		info.UserData["zeroalloc_stale"] = "1";
		Assert.AreSame( info, ConnectionInfo.GetLocalMock() );
		Assert.IsTrue( info.UserData.ContainsKey( "zeroalloc_stale" ), "read again without a change" );

		ConVarSystem.Changed();
		ConnectionInfo.GetLocalMock();
		Assert.IsFalse( info.UserData.ContainsKey( "zeroalloc_stale" ), "not read again after a ConVar changed" );
	}

	/// <summary>Input's flip with keys held (walking) keeps the held keys and allocates nothing.</summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void InputFlipWithKeysHeldAllocatesNothing()
	{
		var context = Input.Context.Create( "zeroalloc" );
		context.AccumKeysPressed.Add( ButtonCode.KEY_W );
		context.AccumKeysPressed.Add( ButtonCode.KEY_LSHIFT );
		context.AccumKeysPressed.Add( ButtonCode.KEY_SPACE );
		context.AccumKeysReleased.Add( ButtonCode.KEY_SPACE );
		context.Flip();

		Assert.IsTrue( context.KeysCurrent.Contains( ButtonCode.KEY_W ) );
		Assert.IsTrue( context.AccumKeysPressed.Contains( ButtonCode.KEY_W ), "a held key was dropped" );
		Assert.IsFalse( context.AccumKeysPressed.Contains( ButtonCode.KEY_SPACE ), "a released key was kept" );

		Allocations.AssertSettles( "input's flip with keys held", context.Flip );
		Assert.IsTrue( context.KeysCurrent.Contains( ButtonCode.KEY_W ) && context.KeysPrevious.Contains( ButtonCode.KEY_LSHIFT ) );
	}

	/// <summary>
	/// The dev tools' GPU pass timings, with a scope whose parent changes from frame to frame (a list another queue's
	/// overlaps), make its paths once and allocate nothing after.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void PassTimingsWithAChangingParentAllocateNothing()
	{
		var timings = new GpuPassTimings();
		ulong frame = 0;
		GpuProfilerStats.Row[] a =
		[
			new() { Name = "Borealis frame", Parent = -1, StableId = 1, Measured = true, Duration = 2 },
			new() { Name = "Async compute", Parent = -1, StableId = 2, Measured = true, Duration = 1 },
			new() { Name = "Depth Prepass", Parent = 0, StableId = 3, Measured = true, Duration = 0.5f },
		];
		GpuProfilerStats.Row[] b =
		[
			new() { Name = "Borealis frame", Parent = -1, StableId = 1, Measured = true, Duration = 2 },
			new() { Name = "Async compute", Parent = 0, StableId = 2, Measured = true, Duration = 1 },
			new() { Name = "Depth Prepass", Parent = 1, StableId = 3, Measured = true, Duration = 0.5f },
		];

		Allocations.AssertSettles( "the GPU pass timings", () =>
		{
			timings.AddFrame( ++frame, a, "borealis" );
			timings.AddFrame( ++frame, b, "borealis" );
		} );

		var report = timings.Flip();
		Assert.IsTrue( report.Passes.ContainsKey( "Borealis frame/Async compute/Depth Prepass" ) );
		Assert.IsTrue( report.Passes.ContainsKey( "Borealis frame/Depth Prepass" ) );
	}

	/// <summary>The game window's title stats make their text in a kept builder and hand it over without a string.</summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void TitleStatsAllocateNothing()
	{
		var title = new BorealisGpuTitle();
		var updates = 0;
		Action<StringBuilder> set = text =>
		{
			Assert.IsTrue( text.Length > 0 );
			updates++;
		};

		// Each pass is an update: the title updates four times a second
		Allocations.AssertSettles( "the title's update", () => title.Frame( "Construct", default, 4.2, null, new Vector3( 1, 2, 3 ), set ),
			beforePass: () => Thread.Sleep( 260 ) );
		Assert.IsTrue( updates >= 5, $"only {updates} updates" );
	}

	static byte[] madeInAFrame;

	/// <summary>
	/// The engine's per-frame counts (<see cref="FrameAllocations"/>) allocate nothing themselves and see what a frame did.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void FrameAllocationsCountWithoutAllocating()
	{
		ulong frame = 1_000_000;
		Allocations.AssertSettles( "the frame counts", () => FrameAllocations.MainFrameStart( ++frame ) );

		FrameAllocations.MainFrameStart( ++frame );
		madeInAFrame = new byte[1000];
		FrameAllocations.MainFrameStart( ++frame );
		Assert.IsTrue( FrameAllocations.Main( frame - 1 ) >= 1000, $"the frame that made 1000 bytes counted {FrameAllocations.Main( frame - 1 )}" );
		Assert.AreEqual( -1, FrameAllocations.Main( frame - FrameAllocations.Frames ), "a frame the ring no longer has" );
		StringAssert.Contains( FrameAllocations.Report( frame - 1, frame - 1 ), "main 10" );
	}

	/// <summary>
	/// The native resource cache's sweep (every few seconds) walks its tables with kept enumerators, drops what's gone, and
	/// allocates nothing.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void NativeResourceCacheSweepAllocatesNothing()
	{
		var clock = typeof( NativeResourceCache ).GetField( "expiryClock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static );
		var kept = new object();
		NativeResourceCache.Add( 0x5A2_0001, kept );
		AddUnheld( 0x5A2_0002 );

		Allocations.AssertSettles( "the native resource cache's sweep", NativeResourceCache.Tick, beforePass: () => clock.SetValue( null, 1000.0 ) );

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		clock.SetValue( null, 1000.0 );
		NativeResourceCache.Tick();
		Assert.IsTrue( NativeResourceCache.TryGetValue<object>( 0x5A2_0001, out var found ) && found == kept, "a resource still held is still found" );
		Assert.IsFalse( NativeResourceCache.TryGetValue<object>( 0x5A2_0002, out _ ), "a collected resource's entry went" );
		NativeResourceCache.Remove( 0x5A2_0001 );
	}

	/// <summary>Added from its own frame, so no stack slot of the test's keeps the resource alive past the collect.</summary>
	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
	static void AddUnheld( long handle ) => NativeResourceCache.Add( handle, new object() );

	/// <summary>The resource system's weak index prune (every 30 seconds) allocates nothing.</summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void WeakIndexPruneAllocatesNothing()
	{
		var resources = Game.Resources;
		var last = resources.GetType().GetField( "_lastWeakPrune", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance );
		Allocations.AssertSettles( "the weak index prune", resources.PruneWeakIndex, beforePass: () => last.SetValue( resources, (TimeSince)100f ) );
	}

	/// <summary>
	/// Short names native passes again and again (animation events' types and attachments) come back as the same string,
	/// allocating nothing after the first.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void CachedNativeStringsAllocateNothing()
	{
		var footstep = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8( "AE_FOOTSTEP" );
		var attachment = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8( "foot_L" );
		var again = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8( "AE_FOOTSTEP" );
		try
		{
			var first = Interop.GetCachedString( footstep );
			Assert.AreEqual( "AE_FOOTSTEP", first );
			Assert.AreSame( first, Interop.GetCachedString( again ), "the same text at another address" );
			Assert.AreEqual( "foot_L", Interop.GetCachedString( attachment ) );
			Assert.IsNull( Interop.GetCachedString( IntPtr.Zero ) );

			Allocations.AssertSettles( "an animation event's names", () =>
			{
				Interop.GetCachedString( footstep );
				Interop.GetCachedString( attachment );
			} );
		}
		finally
		{
			System.Runtime.InteropServices.Marshal.FreeCoTaskMem( footstep );
			System.Runtime.InteropServices.Marshal.FreeCoTaskMem( attachment );
			System.Runtime.InteropServices.Marshal.FreeCoTaskMem( again );
		}
	}

	/// <summary>
	/// The frame stats' GPU summary is the device's when read (made only for the tools that show it), or what was set.
	/// </summary>
	[TestMethod]
	public void FrameStatsGpuSummaryIsMadeWhenRead()
	{
		var stats = new FrameStats();
		Assert.AreEqual( "", stats.GpuStatsSummary, "no Borealis.Gpu device here" );
		stats.GpuStatsSummary = "set";
		Assert.AreEqual( "set", stats.GpuStatsSummary );
	}

	/// <summary>
	/// Achievements look up the local player's stats every second without making a key: the same stats for a package's ident
	/// in any case.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void PlayerStatsLookupAllocatesNothing()
	{
		var stats = Sandbox.Services.Stats.GetPlayerStats( "zeroalloc.test", 76561197960287930 );
		Assert.AreSame( stats, Sandbox.Services.Stats.GetPlayerStats( "ZeroAlloc.Test", 76561197960287930 ) );
		Assert.AreNotSame( stats, Sandbox.Services.Stats.GetPlayerStats( "zeroalloc.test", 76561197960287931 ) );

		Allocations.AssertSettles( "the player stats lookup", () => Sandbox.Services.Stats.GetPlayerStats( "zeroalloc.test", 76561197960287930 ) );
	}


	/// <summary>
	/// A key press or release walks the input contexts and the binds with that button, and names its button for the UI,
	/// without allocating: each of these did every time (an iterator, a predicate, a lower-cased copy of the name, and the
	/// UI's ButtonEvent with no panel focused).
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void KeyEventLookupsAllocateNothing()
	{
		var held = Input.Context.Create( "zeroalloc keys" );
		// Without its constructor, which loads the player's binds from the config file system
		var binds = (Sandbox.Engine.BindCollection)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( Sandbox.Engine.BindCollection ) );
		binds.Actions = new();
		binds.Set( "Forward", 0, "w" );
		binds.Set( "Jump", 0, "space" );
		binds.Set( "Run", 0, "shift+w" );

		var found = new System.Collections.Generic.List<string>();
		foreach ( var bind in binds.EnumerateWithButton( "w" ) ) found.Add( bind.Name );
		CollectionAssert.AreEquivalent( new[] { "Forward", "Run" }, found );
		Assert.AreEqual( "w", Sandbox.UI.InputEventQueue.NormalizeButtonName( ButtonCode.KEY_W ) );
		Assert.AreSame( Sandbox.UI.InputEventQueue.NormalizeButtonName( ButtonCode.KEY_W ), Sandbox.UI.InputEventQueue.NormalizeButtonName( ButtonCode.KEY_W ) );

		// The UI's queue makes a key's ButtonEvent only for a panel that handles it: not with nothing focused, nor for a
		// panel whose handlers are the base's (they only pass it up)
		var queue = new Sandbox.UI.InputEventQueue();
		var plain = new Sandbox.UI.Panel();
		var contexts = 0;
		Allocations.AssertSettles( "a key event's lookups", () =>
		{
			contexts = 0;
			foreach ( var context in Input.Contexts ) contexts++;
			foreach ( var bind in binds.EnumerateWithButton( "w" ) ) _ = bind.Name;
			_ = Sandbox.UI.InputEventQueue.NormalizeButtonName( ButtonCode.KEY_W );
			queue.AddButtonEvent( ButtonCode.KEY_W, true, default );
			queue.AddButtonTyped( ButtonCode.KEY_W, default );
			queue.AddButtonEvent( ButtonCode.KEY_W, false, default );
			queue.TickFocused( null );
			queue.AddButtonEvent( ButtonCode.KEY_W, true, default );
			queue.AddButtonTyped( ButtonCode.KEY_W, default );
			queue.TickFocused( plain );
		} );

		Assert.IsTrue( contexts >= 1, "the context made here" );
		GC.KeepAlive( held );

		// A panel that handles them still gets them, named
		var handler = new KeyHandler();
		queue.AddButtonEvent( ButtonCode.KEY_W, true, default );
		queue.AddButtonTyped( ButtonCode.KEY_W, default );
		queue.TickFocused( handler );
		Assert.AreEqual( "w pressed", handler.Event?.ToString() );
		Assert.AreEqual( "w", handler.Typed?.Button );
	}

	sealed class KeyHandler : Sandbox.UI.Panel
	{
		public Sandbox.UI.ButtonEvent Event, Typed;
		public override void OnButtonEvent( Sandbox.UI.ButtonEvent e ) => Event = e;
		public override void OnButtonTyped( Sandbox.UI.ButtonEvent e ) => Typed = e;
	}
}

