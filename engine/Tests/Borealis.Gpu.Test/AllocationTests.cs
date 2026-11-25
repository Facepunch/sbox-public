using Borealis;
using Borealis.Backend;
using BorealisGpuSample;
using Sandbox;

namespace BorealisGpuTests;

/// <summary>
/// The renderer allocates nothing in steady state (owner, 2026-11-17): once a scene has drawn a few frames and nothing
/// loads or changes, Borealis's frames on the main thread and its recording jobs on workers allocate no managed memory
/// (<see cref="RendererAllocations"/>). The scene: s&amp;box models (one skinned), boxes and spheres, some of them
/// moving, a sun with cascaded shadows, point and spot lights, drawn GPU-driven and recorded in parallel. The engine's
/// side (post processing, UI, particles, custom objects, the Borealis.Gpu frame handover) is held to the same in the
/// integration tests (<c>RendererAllocationTests</c>) and counted in game by the same counter (perf stats).
/// </summary>
[TestClass]
[DoNotParallelize]
public class AllocationTests : GpuTest
{
	// Frames in a row that must allocate nothing, and the most it may take to get there (pipelines and caches warm up first)
	const int SettledFrames = 8, MaxFrames = 200;

	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void SteadyFramesDontAllocate()
	{
		using var scene = new BorealisScene( Device );
		scene.System.ParallelRecording = true;
		scene.GpuDriven = true;

		var bytes = new List<long>( MaxFrames );
		var settled = 0;
		for ( int frame = 0; frame < MaxFrames && settled < SettledFrames; frame++ )
		{
			Device.BeginFrame();
			RendererAllocations.TakeFrame();
			scene.Render( 640, 400, frame / 60f );
			var allocated = RendererAllocations.TakeFrame();
			bytes.Add( allocated );
			settled = allocated == 0 ? settled + 1 : 0;
		}

		Device.WaitIdle();
		TestContext.WriteLine( $"renderer allocations per frame: {string.Join( ", ", bytes )}" );
		Assert.AreEqual( SettledFrames, settled, $"the renderer kept allocating: never {SettledFrames} frames in a row with nothing in {MaxFrames}. Bytes per frame: {string.Join( ", ", bytes )}" );
	}

	/// <summary>
	/// Destroying a texture, a buffer and a view, and freeing them once the GPU is done, allocates nothing: their records wait
	/// in the device's garbage by value, not in a callback each (texture streaming and memory relocation destroy them every
	/// few frames).
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void DestroyingResourcesAllocatesNothing()
	{
		Borealis.Gpu.TextureHandle texture = default;
		Borealis.Gpu.BufferHandle buffer = default;
		Borealis.Gpu.TextureViewHandle view = default;
		var made = 0;
		Allocations.AssertSettles( "destroying and freeing resources", () =>
		{
			Device.Destroy( view );
			Device.Destroy( texture );
			Device.Destroy( buffer );
			for ( int i = 0; i <= Device.FramesInFlight + 1; i++ ) Device.BeginFrame();
		}, beforePass: () =>
		{
			texture = Device.CreateTexture( new Borealis.Gpu.TextureInfo { Width = 8, Height = 8, Depth = 1, Layers = 1, Mips = 1, Samples = 1, Format = Borealis.Gpu.Format.RGBA8Unorm, Usage = Borealis.Gpu.TextureUsage.Sampled, Name = "Destroyed" } );
			buffer = Device.CreateBuffer( new Borealis.Gpu.BufferInfo { Size = 4096, Usage = Borealis.Gpu.BufferUsage.Storage, Memory = Borealis.Gpu.MemoryLocation.Device, Name = "Destroyed" } );
			view = Device.CreateView( texture, new Borealis.Gpu.TextureViewInfo { Dimension = Borealis.Gpu.TextureDimension.Texture2D, MipCount = 1 }, bindless: true );
			made++;
		} );

		Assert.IsTrue( made >= 5 );
		Assert.IsFalse( Device.IsAlive( texture ), "the last texture went" );
	}

	sealed class CameraStages : IRenderStages
	{
		public void Render( RenderStage stage, in StageTarget target ) { }
	}

	/// <summary>
	/// A game camera's frame (one with stages) whose depth chain and contact shadows go on the async compute queue beside
	/// the sun's shadow maps: its handoffs, semaphores and layout fix-ups allocate nothing either once steady.
	/// </summary>
	[TestMethod]
	[TestCategory( Allocations.Category )]
	public void SteadyFramesOverlappingComputeDontAllocate()
	{
		if ( !Device.Capabilities.AsyncCompute ) Assert.Inconclusive( "No async compute queue" );

		using var backend = new BackendDevice( Device );
		var previousHost = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( backend );
		try
		{
			using var system = new RenderSystem { ParallelRecording = true };
			using var target = ViewTarget.Create( new Vector2Int( 320, 240 ), 1, TextureFormat.RGBA16161616F, TextureFormat.D32FS8, "Overlapped allocations" );
			var world = new RenderWorld();
			world.Lighting.SunDirection = new Vector3( -1, 0.2f, -0.8f ).Normal;
			world.Lighting.SunColor = new Color( 4, 4, 4 );
			world.Lighting.SunContactShadows = true;
			var material = backend.CreateMaterial( "Overlapped grey", Color.Gray );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), Transform.Zero ) );
			world.Add( new MeshObject( ObjectAttributeTests.BoxMesh( backend, material ), new Transform( new Vector3( 0, 0, -52.5f ), Rotation.Identity, new Vector3( 20, 20, 0.05f ) ) ) );
			var view = new RenderView { Name = "Overlapped allocations", FieldOfView = 60, ZNear = 4, ZFar = 4000, Viewport = new Rect( 0, 0, 320, 240 ) };
			view.Rotation = Rotation.From( 35, 90, 0 );
			view.Position = -view.Rotation.Forward * 450;
			var stages = new CameraStages();

			var bytes = new List<long>( MaxFrames );
			var settled = 0;
			for ( int frame = 0; frame < MaxFrames && settled < SettledFrames; frame++ )
			{
				backend.BeginFrame();
				RenderHost.Current.NextFrame();
				RendererAllocations.TakeFrame();
				system.Render( world, view, target, stages );
				var allocated = RendererAllocations.TakeFrame();
				Assert.IsTrue( system.Frame.AsyncCompute, "the frame overlaps compute" );
				bytes.Add( allocated );
				settled = allocated == 0 ? settled + 1 : 0;
			}

			Device.WaitIdle();
			TestContext.WriteLine( $"renderer allocations per frame: {string.Join( ", ", bytes )}" );
			Assert.AreEqual( SettledFrames, settled, $"the renderer kept allocating: never {SettledFrames} frames in a row with nothing in {MaxFrames}. Bytes per frame: {string.Join( ", ", bytes )}" );
		}
		finally
		{
			Device.WaitIdle();
			RenderHost.Current = previousHost;
		}
	}
}
