using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Borealis.Backend;

/// <summary>
/// Compute pipelines the frames after a load make whatever the map, and the parts of graphics pipelines they draw with
/// (the composite morph atlas's), queued as the load starts so the device's builders make them behind the loading screen
/// (<see cref="QueueLoadPipelines"/>).
/// </summary>
public sealed partial class BackendDevice
{
	/// <summary>
	/// Whether a load queues the compute pipelines its first frames make whatever the map (on unless
	/// <c>BOREALIS_LOAD_PIPELINES=0</c>, to compare).
	/// </summary>
	public static bool QueuesLoadPipelines { get; set; } = Environment.GetEnvironmentVariable( "BOREALIS_LOAD_PIPELINES" ) != "0";

	/// <summary>The cube capture's filter shader (<see cref="CubeCapture"/>).</summary>
	internal const string CubeFilterShader = "shaders/envmap_filtering_cs.shader";

	// The filter's variants a capture dispatches (CubeCapture.Filter): the downsample, then GGX at either quality
	static readonly (string Name, int Value)[][] cubeFilterCombos =
	[
		[("D_PASS", 0), ("D_QUALITY", 0)],
		[("D_PASS", 1), ("D_QUALITY", 0)],
		[("D_PASS", 1), ("D_QUALITY", 1)],
	];

	// Compute shaders dispatched through a material object (BackendContext.Dispatch: the engine's line renderer's, the frame
	// buffer copy's masks and mips), with the combos they're dispatched with
	static readonly (string Shader, (string Name, int Value)[] Combos)[] objectDispatches =
	[
		("shaders/line_cs.shader", []),
		("shaders/depthaware_mask_cs.shader", []),
		("shaders/framecopy_mask_cs.shader", [("D_COPY", 1)]),
		("shaders/framecopy_mask_cs.shader", [("D_COPY", 2)]),
		("shaders/framecopy_mips_cs.shader", []),
		("shaders/framecopy_tail_cs.shader", []),
		("shaders/downsample_cs.shader", [("D_DOWNSAMPLE_METHOD", 1)]),
		// The depth chain (SharedPasses.BuildDepthChain): min/max, fused with the depth's resolve or not, from multisampled
		// depth (the main view) or not (an envmap capture face, whose first frame after a load made them inline)
		("shaders/downsample_spd.shader", [("D_DOWNSAMPLE_METHOD", 5), ("D_FUSED_DEPTH", 1), ("D_MSAA", 0)]),
		("shaders/downsample_spd.shader", [("D_DOWNSAMPLE_METHOD", 5), ("D_FUSED_DEPTH", 0), ("D_MSAA", 0)]),
		("shaders/downsample_spd.shader", [("D_DOWNSAMPLE_METHOD", 5), ("D_FUSED_DEPTH", 1), ("D_MSAA", 1)]),
		("shaders/downsample_spd.shader", [("D_DOWNSAMPLE_METHOD", 5), ("D_FUSED_DEPTH", 0), ("D_MSAA", 1)]),
		("shaders/depthresolve_cs.shader", [("D_MSAA", 0), ("D_CONSERVATIVE_QUARTER", 0)]),
		("shaders/depthresolve_cs.shader", [("D_MSAA", 1), ("D_CONSERVATIVE_QUARTER", 0)]),
		// Skinning (BackendContext.DispatchSkinning), plain and deformed: the first skinned model drawn after a load made it
		("shaders/skinning_cs.shader", [("D_MORPH", 0), ("D_DEFORMATION_VOLUME", 0)]),
		("shaders/skinning_cs.shader", [("D_MORPH", 1), ("D_DEFORMATION_VOLUME", 0)]),
		("shaders/skinning_cs.shader", [("D_MORPH", 0), ("D_DEFORMATION_VOLUME", 1)]),
		// Screen-space reflections (ScreenSpaceReflectionsPass.Trace): the classify, then the intersect and the upscale, with
		// the G-buffer's normals multisampled or not. The load's last frames made them on the render thread (60-210 ms each
		// with an empty driver cache, 0.3 s before the load ended on Mow)
		("shaders/screen_space_reflections_classify_cs.shader", [("D_MSAA_NORMALS", 1)]),
		("shaders/screen_space_reflections_classify_cs.shader", [("D_MSAA_NORMALS", 0)]),
		("shaders/screen_space_reflections_cs.shader", [("D_PASS", 1), ("D_MSAA_NORMALS", 1)]),
		("shaders/screen_space_reflections_cs.shader", [("D_PASS", 2), ("D_MSAA_NORMALS", 1)]),
		("shaders/screen_space_reflections_cs.shader", [("D_PASS", 1), ("D_MSAA_NORMALS", 0)]),
		("shaders/screen_space_reflections_cs.shader", [("D_PASS", 2), ("D_MSAA_NORMALS", 0)]),
	];

	// GPU-driven visibility's compute (BackendContext.GpuDriven: the cull, the ids' mark at either sample count, the
	// compaction's five kinds, the meshlet cull), each dispatched through a material object
	static readonly (string Shader, (string Name, int Value)[] Combos)[] gpuDrivenDispatches =
	[
		("shaders/borealis_gpu_driven_cull_cs.shader", []),
		("shaders/borealis_gpu_driven_mark_cs.shader", [("D_MSAA", 0)]),
		("shaders/borealis_gpu_driven_mark_cs.shader", [("D_MSAA", 1)]),
		("shaders/borealis_gpu_driven_compact_cs.shader", [("D_PARTS", 0)]),
		("shaders/borealis_gpu_driven_compact_cs.shader", [("D_PARTS", 1)]),
		("shaders/borealis_gpu_driven_compact_cs.shader", [("D_PARTS", 2)]),
		("shaders/borealis_gpu_driven_compact_cs.shader", [("D_PARTS", 3)]),
		("shaders/borealis_gpu_driven_compact_cs.shader", [("D_PARTS", 4)]),
		("shaders/borealis_meshlet_cull_cs.shader", []),
	];

	// Graphics draws a frame after a load makes whatever the map, drawn through a material object (ShaderDrawer.Draw): the
	// composite morph atlas's two passes (MorphAtlas.Generate), 47 + 62 ms of the render thread in a capture face after a load
	static readonly (string Shader, (string Name, int Value)[] Combos, Format Color, VertexStreamLayout Layout)[] objectDraws =
	[
		(MorphAtlas.Shader, [("D_COMPOSITETYPE", 0)], Format.RGBA16Float, MorphAtlas.Layout),
		(MorphAtlas.Shader, [("D_COMPOSITETYPE", 1)], Format.RGBA16Float, MorphAtlas.Layout),
	];

	Task loadPipelines;
	volatile bool stopLoadPipelines;

	// What the load's worker did with each pipeline, for its line in the log
	readonly List<string> loadPipelinesSaid = new();

	/// <summary>
	/// As a load starts (<see cref="Loading"/>), the compute pipelines its first frames make whatever the map are made on the
	/// device's builders (<see cref="Device.QueueComputePipeline"/>), their variants on a worker: the resident cull's, the
	/// cube capture filter's, the engine's line renderer's, the frame buffer copy's, the depth chain's,
	/// skinning's, screen-space reflections' and GPU-driven visibility's. Made where
	/// they're first dispatched, each took 0.04-0.6 s of the render thread around a load with an empty driver cache.
	/// Each load (a pipeline once made isn't queued again); the load waits behind its screen for them
	/// (<see cref="CompilingShaders"/>).
	/// </summary>
	internal void QueueLoadPipelines()
	{
		if ( !QueuesLoadPipelines || loadPipelines is { IsCompleted: false } ) return;
		var cull = GpuDrivenResidentCull ? ResidentCullShader : null;
		var library = Shaders;
		var gpu = Gpu;
		var objects = MaterialCache;
		var info = Passes.Info;
		var gpuDriven = GpuDriven;
		loadPipelines = Task.Run( () =>
		{
			loadPipelinesSaid.Clear();
			if ( cull is not null ) Queue( gpu, library, cull, [], null, objectsWithConstants: objects );
			if ( library.Load( CubeFilterShader ) is { } filter )
			{
				foreach ( var combos in cubeFilterCombos ) Queue( gpu, library, filter, combos, null );
			}

			foreach ( var (path, combos) in gpuDriven ? objectDispatches.Concat( gpuDrivenDispatches ) : objectDispatches )
			{
				if ( !stopLoadPipelines && library.Load( path ) is { } shader ) Queue( gpu, library, shader, combos, objects );
			}

			foreach ( var (path, combos, color, layout) in objectDraws )
			{
				if ( !stopLoadPipelines && library.Load( path ) is { } shader ) QueueParts( gpu, library, shader, combos, color, layout, objects );
			}

			info?.Invoke( $"Borealis.Gpu: a load queued {loadPipelinesSaid.Count} pipelines ahead: {string.Join( "; ", loadPipelinesSaid )}" );
			loadPipelinesSaid.Clear();
		} );
	}

	/// <summary>Whether the load's queued pipelines (<see cref="QueueLoadPipelines"/>) are still being made.</summary>
	internal bool LoadPipelinesPending => loadPipelines is { IsCompleted: false } || Gpu.ComputePipelinesQueued > 0;

	/// <summary>At dispose: the worker making the load's variants stops, and is waited for, before the device goes.</summary>
	void StopLoadPipelines()
	{
		stopLoadPipelines = true;
		try
		{
			loadPipelines?.Wait();
		}
		catch ( AggregateException )
		{
			// Its failures are the dispatches' to report
		}
	}

	/// <summary>Waits for the load's pipelines' variants to be made and queued, for tests.</summary>
	internal void WaitForLoadPipelinesQueued() => loadPipelines?.Wait();

	// The variant a dispatch with these combos makes (ShaderDrawer.Variant, no material, no specialization), once its module
	// is compiled, and its pipeline queued: with the sets of its material object from the device's cache, as
	// ShaderDrawer.BindForDispatch makes it, or (no cache) the variant's own, as a dispatch by reflection does. The resident
	// cull dispatches one with material constants through a material object (objectsWithConstants), others by reflection
	void Queue( Device gpu, ShaderLibrary library, ShaderFile shader, (string Name, int Value)[] combos, MaterialObjectCache objects, MaterialObjectCache objectsWithConstants = null )
	{
		try
		{
			for ( var tries = 0; tries < 2400 && !stopLoadPipelines; tries++ )
			{
				var variant = library.Variant( shader, shader.Modes[0].Name, [], name => Combo( combos, name ), static _ => null, false, out var pending );
				// A module still compiling (Slang at run time) gives its programs' variant, or none: waited for
				if ( !pending && variant?.Compute?.Bytecode is null )
				{
					loadPipelinesSaid.Add( $"{System.IO.Path.GetFileNameWithoutExtension( shader.Name )}: no compute variant" );
					return;
				}
				if ( !pending )
				{
					if ( objectsWithConstants is not null && System.Linq.Enumerable.Any( variant.Resources, r => r.Kind == ShaderResourceKind.MaterialConstants ) ) objects = objectsWithConstants;
					var queued = gpu.QueueComputePipeline( ShaderDrawer.ComputeInfo( variant, objects?.LayoutOf( variant, immediate: true ).Sets ) );
					loadPipelinesSaid.Add( $"{System.IO.Path.GetFileNameWithoutExtension( shader.Name )} {string.Join( ' ', combos.Select( c => $"{c.Name}={c.Value}" ) )}{(objects is null ? "" : " (object)")}{(queued ? "" : " (not queued)")}" );
					return;
				}

				Thread.Sleep( 50 );
			}
		}
		catch ( Exception e ) when ( e is not OutOfMemoryException )
		{
			// Made where it's dispatched instead, which reports what's wrong
			loadPipelinesSaid.Add( $"{System.IO.Path.GetFileNameWithoutExtension( shader.Name )}: {e.GetType().Name} {e.Message}" );
		}
	}

	// The parts of the pipeline a draw with these combos makes through a material object (ShaderDrawer.BindForDraw: one
	// colour target, no depth, one sample, a triangle list, no instance ids), queued on the builders once its variant is made,
	// so the draw only links them
	void QueueParts( Device gpu, ShaderLibrary library, ShaderFile shader, (string Name, int Value)[] combos, Format color, VertexStreamLayout layout, MaterialObjectCache objects )
	{
		try
		{
			for ( var tries = 0; tries < 2400 && !stopLoadPipelines; tries++ )
			{
				var variant = library.Variant( shader, shader.Modes[0].Name, [], name => Combo( combos, name ), static _ => null, false, out var pending );
				if ( !pending && variant?.Vertex?.Bytecode is null )
				{
					loadPipelinesSaid.Add( $"{System.IO.Path.GetFileNameWithoutExtension( shader.Name )}: no graphics variant" );
					return;
				}
				if ( !pending )
				{
					var info = ShaderDrawer.GraphicsInfo( variant, color, Format.Unknown, 1, layout, PrimitiveTopology.TriangleList, objects.LayoutOf( variant, immediate: true ).Sets, false, OverlayStencil.None );
					gpu.QueuePipelineParts( info, PipelineParts.All, speculative: false, order: int.MinValue );
					loadPipelinesSaid.Add( $"{System.IO.Path.GetFileNameWithoutExtension( shader.Name )} {string.Join( ' ', combos.Select( c => $"{c.Name}={c.Value}" ) )} (parts)" );
					return;
				}

				Thread.Sleep( 50 );
			}
		}
		catch ( Exception e ) when ( e is not OutOfMemoryException )
		{
			// Made where it's drawn instead, which reports what's wrong
			loadPipelinesSaid.Add( $"{System.IO.Path.GetFileNameWithoutExtension( shader.Name )}: {e.GetType().Name} {e.Message}" );
		}
	}

	static int? Combo( (string Name, int Value)[] combos, string name )
	{
		foreach ( var (combo, value) in combos )
		{
			if ( combo == name ) return value;
		}

		return null;
	}
}
