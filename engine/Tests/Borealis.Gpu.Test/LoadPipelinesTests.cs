using Borealis.Backend;
using Borealis.Gpu;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// The compute pipelines a load's first frames make whatever the map (<see cref="BackendDevice.QueueLoadPipelines"/>): made
/// on the builders as the load starts, and taken where they're dispatched rather than made there.
/// </summary>
[TestClass]
[DoNotParallelize]
public class LoadPipelinesTests : GpuTest
{
	/// <summary>
	/// A load starting queues the cube filter's three variants, the resident cull's, the line renderer's, the frame copy's
	/// and GPU-driven visibility's; the pipelines their own dispatch paths then make (by reflection, or through a material object) are those,
	/// taken from the builders (none made again), on a first run's empty cache. With it off, the same dispatch paths make
	/// them where they're asked for.
	/// </summary>
	[TestMethod]
	public void ALoadMakesTheCullAndCubeFilterPipelinesAhead()
	{
		Assert.AreEqual( 0, MadeWhereDispatched( queue: true ), "the dispatch paths took the pipelines the load queued" );
		Assert.IsTrue( MadeWhereDispatched( queue: false ) >= 3, "without it they're made where they're dispatched" );
	}

	/// <summary>
	/// A load starting queues the parts of the composite morph atlas's two graphics pipelines (<see cref="MorphAtlas"/>), so
	/// its draws, as <see cref="MorphAtlas.Generate"/> makes them, only link them: no part made where drawn. With it off, the
	/// draws make their parts.
	/// </summary>
	[TestMethod]
	public void ALoadMakesTheMorphCompositePartsAhead()
	{
		Assert.AreEqual( 0, PartsMadeWhereDrawn( queue: true ), "the composite's draws linked the parts the load made" );
		Assert.IsTrue( PartsMadeWhereDrawn( queue: false ) >= 2, "without it they're made where they're drawn" );
	}

	static int PartsMadeWhereDrawn( bool queue )
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-load-parts-{Environment.ProcessId}-{Guid.NewGuid():n}" );
		var was = BackendDevice.QueuesLoadPipelines;
		BackendDevice.QueuesLoadPipelines = queue;
		try
		{
			using var device = Device.Create( new DeviceOptions { Presentation = false, Validation = true, PipelineCacheFolder = folder, PipelinePartManifest = true } );
			if ( !device.Capabilities.PipelineLibraries ) Assert.Inconclusive( "The device has no fast-linking graphics pipeline libraries" );
			using var backend = new BackendDevice( device );
			var shader = (backend.MaterialFromShader( MorphAtlas.Shader ) as BackendMaterial)?.Shader;
			if ( shader is null ) Assert.Inconclusive( "morph_composite isn't built" );

			backend.Loading = true;
			backend.WaitForLoadPipelinesQueued();
			Assert.IsTrue( device.WaitForPendingPipelines( TimeSpan.FromSeconds( 120 ) ) );
			var before = device.PipelineLibraryCreation.Parts;

			// As MorphAtlas.Generate draws: both passes into the RGBA16F atlas, through a drawer of its own; the vertices have
			// no area, so nothing is drawn
			using var drawer = new ShaderDrawer( backend );
			var target = device.CreateTexture( TextureInfo.RenderTarget( 64, 64, Format.RGBA16Float, name: "Atlas" ) );
			backend.BeginFrame();
			var list = device.BeginCommandList( QueueType.Graphics, "Morph composite" );
			list.Barrier( TextureBarrier.Transition( target, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.TopOfPipe, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
			list.BeginRendering( [ColorTarget.Of( target, default )] );
			var upload = list.AllocateUpload( MorphAtlas.Layout.Stride * 6, 16 );
			upload.Data.Clear();
			var attributes = new ShaderAttributes();
			foreach ( var pass in new[] { 0, 1 } )
			{
				attributes.SetCombo( "D_COMPOSITETYPE", pass );
				Assert.IsTrue( drawer.Draw( list, shader, attributes, Format.RGBA16Float, 1, upload.Buffer, upload.Offset, MorphAtlas.Layout, 6, PrimitiveTopology.TriangleList, new System.Numerics.Vector4( 0, 0, 64, 64 ) ) );
			}

			list.EndRendering();
			device.Submit( list );
			device.WaitIdle();
			device.Destroy( target );
			return device.PipelineLibraryCreation.Parts - before;
		}
		finally
		{
			BackendDevice.QueuesLoadPipelines = was;
			if ( Directory.Exists( folder ) ) Directory.Delete( folder, recursive: true );
		}
	}

	static int MadeWhereDispatched( bool queue )
	{
		var folder = Path.Combine( Path.GetTempPath(), $"borealis-load-pipelines-{Environment.ProcessId}-{Guid.NewGuid():n}" );
		var was = BackendDevice.QueuesLoadPipelines;
		BackendDevice.QueuesLoadPipelines = queue;
		try
		{
			using var device = Device.Create( new DeviceOptions { Presentation = false, Validation = true, PipelineCacheFolder = folder, PipelinePartManifest = true } );
			if ( !device.Capabilities.PipelineLibraries ) Assert.Inconclusive( "The device has no fast-linking graphics pipeline libraries" );
			using var backend = new BackendDevice( device ) { GpuDriven = true };
			var filter = backend.Shaders.Load( BackendDevice.CubeFilterShader );
			if ( filter is null ) Assert.Inconclusive( "envmap_filtering_cs isn't built" );

			backend.Loading = true;
			backend.WaitForLoadPipelinesQueued();
			Assert.IsTrue( device.WaitForPendingPipelines( TimeSpan.FromSeconds( 120 ) ) );
			Assert.IsFalse( backend.LoadPipelinesPending );
			var before = device.ComputePipelineCreation.Made;
			if ( queue ) Assert.IsTrue( before >= 7, $"the filter's three variants and the line's and masks' four were made ahead ({before})" );

			// As CubeCapture.Filter and the resident cull ask for them: by reflection, through drawers of their own
			using var drawer = new ShaderDrawer( backend ) { MaterialObjects = false };
			var attributes = new ShaderAttributes();
			foreach ( var (pass, quality) in new[] { (0, 0), (1, 0), (1, 1) } )
			{
				attributes.Clear();
				attributes.SetCombo( "D_PASS", pass );
				attributes.SetCombo( "D_QUALITY", quality );
				drawer.ReflectedComputePipeline( drawer.Variant( filter, attributes ) );
			}

			using var cullDrawer = new ShaderDrawer( backend );
			if ( backend.ResidentCullShader is { } cull && cullDrawer.Variant( cull, new ShaderAttributes() ) is { Compute.Bytecode: not null } variant )
			{
				cullDrawer.ReflectedComputePipeline( variant );
			}

			// As BackendContext.Dispatch does, through a material object: dispatches of no groups, so nothing runs
			using var objectDrawer = new ShaderDrawer( backend );
			backend.BeginFrame();
			var list = device.BeginCommandList( QueueType.Graphics, "Load pipelines" );
			(string Path, (string Name, int Value)[] Combos)[] dispatches =
			[
				("shaders/line_cs.shader", []),
				("shaders/depthaware_mask_cs.shader", []),
				("shaders/framecopy_mask_cs.shader", [("D_COPY", 1)]),
				("shaders/framecopy_mask_cs.shader", [("D_COPY", 2)]),
				("shaders/framecopy_mips_cs.shader", []),
				("shaders/framecopy_tail_cs.shader", []),
				("shaders/downsample_cs.shader", [("D_DOWNSAMPLE_METHOD", 1)]),
				("shaders/downsample_spd.shader", [("D_DOWNSAMPLE_METHOD", 5), ("D_FUSED_DEPTH", 1), ("D_MSAA", 0)]),
				("shaders/downsample_spd.shader", [("D_DOWNSAMPLE_METHOD", 5), ("D_FUSED_DEPTH", 0), ("D_MSAA", 0)]),
				("shaders/downsample_spd.shader", [("D_DOWNSAMPLE_METHOD", 5), ("D_FUSED_DEPTH", 1), ("D_MSAA", 1)]),
				("shaders/depthresolve_cs.shader", [("D_MSAA", 0)]),
				("shaders/skinning_cs.shader", []),
				("shaders/skinning_cs.shader", [("D_MORPH", 1)]),
				("shaders/skinning_cs.shader", [("D_DEFORMATION_VOLUME", 1)]),
				("shaders/borealis_gpu_driven_cull_cs.shader", []),
				("shaders/borealis_gpu_driven_mark_cs.shader", [("D_MSAA", 0)]),
				("shaders/borealis_gpu_driven_mark_cs.shader", [("D_MSAA", 1)]),
				("shaders/borealis_gpu_driven_compact_cs.shader", [("D_PARTS", 0)]),
				("shaders/borealis_gpu_driven_compact_cs.shader", [("D_PARTS", 4)]),
				("shaders/borealis_meshlet_cull_cs.shader", []),
			];
			foreach ( var (path, combos) in dispatches )
			{
				if ( backend.Shaders.Load( path ) is not { } shader ) continue;
				attributes.Clear();
				foreach ( var (name, value) in combos ) attributes.SetCombo( name, value );
				objectDrawer.Dispatch( list, shader, attributes, 0, 0, 0 );
			}

			device.Submit( list );
			device.WaitIdle();
			return device.ComputePipelineCreation.Made - before;
		}
		finally
		{
			BackendDevice.QueuesLoadPipelines = was;
			if ( Directory.Exists( folder ) ) Directory.Delete( folder, recursive: true );
		}
	}
}
