using Borealis.Backend;
using Borealis.Gpu;
using System.Runtime.CompilerServices;
using System.Threading;

namespace BorealisGpuTests;

/// <summary>
/// A view's GPU-driven visibility resources (ids target, mask, stamps, compacted triangles) go once the view has: a game
/// that loads and closes maps makes new views each time, and kept their resources for good (~650 MiB a Construct round).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ViewLifetimeTests : GpuTest
{
	[MethodImpl( MethodImplOptions.NoInlining )]
	static void UseAndDropView( BackendDevice backend )
	{
		var view = new object();
		var state = backend.VisibilityFor( view, TextureInfo.RenderTarget( 64, 32, Format.D32Float ) );
		backend.IdsTargetsFor( state );
		backend.PrepareIdsTargets( state );
		Assert.IsTrue( state.Mask.IsValid && state.Ids.IsValid );
	}

	[TestMethod]
	public void GoneViewsFreeTheirVisibility()
	{
		using var backend = new BackendDevice( Device );
		backend.BeginFrame();
		var (buffers, textures) = (Device.Buffers.Count, Device.Textures.Count);

		UseAndDropView( backend );
		Assert.IsTrue( Device.Buffers.Count > buffers, "the view made buffers" );

		GC.Collect();
		GC.WaitForPendingFinalizers();
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) backend.BeginFrame();

		Assert.AreEqual( 1, backend.VisibilityViewsFreed );
		Assert.AreEqual( buffers, Device.Buffers.Count, "buffers back to before" );
		Assert.AreEqual( textures, Device.Textures.Count, "textures back to before" );
	}

	/// <summary>
	/// A small view's set is sized to it (a triangle per sample in the compacted buffers), and a view unused for a while has
	/// its set freed, to be made again if it draws again.
	/// </summary>
	[TestMethod]
	public void SmallAndIdleViewsHoldLittle()
	{
		using var backend = new BackendDevice( Device );
		backend.BeginFrame();
		var view = new object();
		var state = backend.VisibilityFor( view, TextureInfo.RenderTarget( 64, 32, Format.D32Float ) );
		Assert.AreEqual( 1 << 16, state.CompactedCapacity, "a 64x32 view's compacted buffers hold the minimum" );
		Assert.AreEqual( 1L << 16, Device.SizeOf( state.Compacted ) / 4 );
		var buffers = Device.Buffers.Count;

		for ( int i = 0; i <= BackendDevice.IdleVisibilityFrames + 60; i++ ) backend.BeginFrame();
		Assert.AreEqual( 1, backend.VisibilityViewsIdled );
		Assert.IsFalse( state.Ready );
		Assert.IsTrue( Device.Buffers.Count < buffers, "its buffers were freed" );

		var again = backend.VisibilityFor( view, TextureInfo.RenderTarget( 64, 32, Format.D32Float ) );
		Assert.AreSame( state, again );
		Assert.IsTrue( again.Ready && again.Mask.IsValid, "made again when the view draws again" );
		GC.KeepAlive( view );
	}

	/// <summary>
	/// A view's ids targets are made only once a prepass call asks for them, before the view's next frame (so every call of a
	/// frame decides the same), at the view's size, and freed once no prepass has drawn ids for a while while the rest of its
	/// set stays: a view whose forward pass compacts but whose prepass draws no ids held 5.9 MB of targets at 1280x800.
	/// </summary>
	[TestMethod]
	public void IdsTargetsAreMadeOnlyForPrepassesThatDrawIds()
	{
		using var backend = new BackendDevice( Device );
		backend.BeginFrame();
		var view = new object();
		var textures = Device.Textures.Count;
		var state = backend.VisibilityFor( view, TextureInfo.RenderTarget( 64, 32, Format.D32Float ) );
		Assert.AreEqual( textures, Device.Textures.Count, "no targets made with the view" );

		// Nobody asked: none made before the next frame
		backend.PrepareIdsTargets( state );
		Assert.IsFalse( state.Ids.IsValid || state.IdsReady );

		// Asked: not there for this frame's calls, there for the next frame's
		Assert.IsFalse( backend.IdsTargetsFor( state ) );
		Assert.IsFalse( backend.IdsTargetsFor( state ), "the rest of the frame decides the same" );
		backend.BeginFrame();
		backend.PrepareIdsTargets( state );
		Assert.IsTrue( backend.IdsTargetsFor( state ) && state.Ids.IsValid && state.Scratch.IsValid );
		Assert.AreEqual( (64, 32), (Device.InfoOf( state.Ids ).Width, Device.InfoOf( state.Ids ).Height) );
		Assert.AreEqual( textures + 2, Device.Textures.Count );

		// The view kept clearing (its forward pass), no prepass drawing ids: the targets go, the rest stays
		for ( int i = 0; i <= BackendDevice.IdleVisibilityFrames + 60; i++ )
		{
			backend.BeginFrame();
			state.ClearedFrame = backend.GpuDrivenFrame;
		}

		Assert.AreEqual( 1, backend.VisibilityIdsIdled );
		Assert.AreEqual( 0, backend.VisibilityViewsIdled );
		Assert.IsTrue( state.Ready && state.Mask.IsValid && !state.IdsReady && !state.Ids.IsValid );
		for ( int i = 0; i <= Device.FramesInFlight; i++ ) backend.BeginFrame();
		Device.WaitIdle();
		Assert.AreEqual( textures, Device.Textures.Count, "its targets were freed" );
		GC.KeepAlive( view );
	}

	/// <summary>
	/// Visibility buffers made after the frame cleared the view's (its target grew: a child view drawn at another size inside
	/// a cube capture; its prepass's meshlet buffers, made at their first use) are cleared as that clear would have, before
	/// the frame's calls use them: otherwise their cursors hold whatever the memory last held, and compaction writes past
	/// the buffer. Made before the frame's clear, they're left to it.
	/// </summary>
	[TestMethod]
	public void BuffersMadeAfterTheFramesClearAreCleared()
	{
		using var backend = new BackendDevice( Device );
		backend.BeginFrame();
		var view = new object();
		var state = backend.VisibilityFor( view, TextureInfo.RenderTarget( 64, 32, Format.D32Float ) );
		Assert.AreEqual( 0, backend.VisibilityClearedMidFrame, "a new view's first frame draws without its set" );

		// Not cleared this frame yet: the frame's clear covers what's made now
		backend.BeginFrame();
		backend.VisibilityFor( view, TextureInfo.RenderTarget( 256, 256, Format.D32Float ) );
		Assert.AreEqual( 0, backend.VisibilityClearedMidFrame, "grown before the frame's clear" );

		// Cleared this frame (BeginGpuDrivenView), then grown and its prepass buffers made
		state.ClearedFrame = backend.GpuDrivenFrame;
		var capacity = state.CompactedCapacity;
		backend.VisibilityFor( view, TextureInfo.RenderTarget( 1024, 512, Format.D32Float ) );
		Assert.IsTrue( state.CompactedCapacity > capacity, "the compacted buffers grew" );
		Assert.AreEqual( 1, backend.VisibilityClearedMidFrame, "the grown buffers' cursors were set" );

		Assert.IsTrue( backend.PrepassCompactedFor( state ).IsValid );
		Assert.AreEqual( 2, backend.VisibilityClearedMidFrame, "the prepass buffers' cursors and stamps were cleared" );
		Assert.IsTrue( state.MeshletStampsCleared );

		// Smaller again: nothing made
		backend.VisibilityFor( view, TextureInfo.RenderTarget( 64, 32, Format.D32Float ) );
		Assert.AreEqual( 2, backend.VisibilityClearedMidFrame );
		Device.WaitIdle();
		GC.KeepAlive( view );
	}

	/// <summary>
	/// A view's prepass buffers made on one thread while another records with the view: once its meshlet stamps say they're
	/// cleared, every prepass buffer is there. Recording threads read them without the lock, and a call that bound one
	/// before it was made threw "No buffer (a default handle)" in the middle of its list.
	/// </summary>
	[TestMethod]
	public void PrepassBuffersAreThereOnceMarkedCleared()
	{
		using var backend = new BackendDevice( Device );
		for ( int round = 0; round < 8; round++ )
		{
			backend.BeginFrame();
			var view = new object();
			var state = backend.VisibilityFor( view, TextureInfo.RenderTarget( 256, 256, Format.D32Float ) );
			state.ClearedFrame = backend.GpuDrivenFrame;

			var seen = 0;
			var broken = 0;
			var done = false;
			var watcher = new Thread( () =>
			{
				while ( !Volatile.Read( ref done ) )
				{
					if ( !state.MeshletStampsCleared ) continue;
					seen++;
					if ( !state.PrepassCompacted.IsValid || !state.PrepassCompacted16.IsValid || !state.MeshletStamps.IsValid ) broken++;
				}
			} );
			watcher.Start();
			backend.PrepassCompactedFor( state );
			Volatile.Write( ref done, true );
			watcher.Join();

			Assert.IsTrue( state.MeshletStampsCleared );
			Assert.AreEqual( 0, broken, $"round {round}: cleared before its buffers were made ({seen} reads)" );
			GC.KeepAlive( view );
		}

		Device.WaitIdle();
	}
}
