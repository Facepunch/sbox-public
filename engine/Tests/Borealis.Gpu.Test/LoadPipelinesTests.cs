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
			(string Path, string Combo, int Value)[] dispatches =
			[
				("shaders/line_cs.shader", null, 0),
				("shaders/depthaware_mask_cs.shader", null, 0),
				("shaders/framecopy_mask_cs.shader", "D_COPY", 1),
				("shaders/framecopy_mask_cs.shader", "D_COPY", 2),
				("shaders/framecopy_mips_cs.shader", null, 0),
				("shaders/framecopy_tail_cs.shader", null, 0),
				("shaders/downsample_cs.shader", "D_DOWNSAMPLE_METHOD", 1),
				("shaders/borealis_gpu_driven_cull_cs.shader", null, 0),
				("shaders/borealis_gpu_driven_mark_cs.shader", "D_MSAA", 0),
				("shaders/borealis_gpu_driven_mark_cs.shader", "D_MSAA", 1),
				("shaders/borealis_gpu_driven_compact_cs.shader", "D_PARTS", 0),
				("shaders/borealis_gpu_driven_compact_cs.shader", "D_PARTS", 4),
				("shaders/borealis_meshlet_cull_cs.shader", null, 0),
			];
			foreach ( var (path, combo, value) in dispatches )
			{
				if ( backend.Shaders.Load( path ) is not { } shader ) continue;
				attributes.Clear();
				if ( combo is not null ) attributes.SetCombo( combo, value );
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
