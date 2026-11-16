using Borealis.Gpu;
using Borealis.Source2;
using Borealis.Source2.Materials;
using Borealis.Source2.Shaders;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Borealis.Backend;

/// <summary>
/// Borealis's <see cref="GpuDevice"/> on <see cref="Borealis.Gpu.Device"/>: resources map one-to-one onto Borealis.Gpu's, and
/// materials are s&amp;box's own - compiled shaders, materials and textures read by Borealis.Source2 from the game's
/// content. What it can't do yet (morphs, engine compute shaders with parameters) draws nothing. See
/// docs/managed/borealis-gpu.md.
/// </summary>
public sealed partial class BackendDevice : GpuDevice, IDisposable
{
	readonly Stopwatch clock = Stopwatch.StartNew();
	readonly Dictionary<string, GpuTexture> loadedTextures = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, GpuMaterial> sharedMaterials = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<IntPtr, SyncPoint> semaphores = new();
	readonly Dictionary<IntPtr, BufferHandle> constantBuffers = new();
	readonly List<(SyncPoint Point, BufferHandle Buffer, int Bytes, GpuReadback Callback)> readbacks = new();
	readonly Dictionary<PipelineKey, PipelineHandle> pipelines = new();
	readonly Dictionary<VertexLayout, VertexLayout> layouts = new();
	long nextHandle;
	int vertexCache;

	/// <summary>The Borealis.Gpu device it renders with.</summary>
	public Device Gpu { get; }

	/// <summary>Where compiled shaders, materials and textures are read from.</summary>
	public ContentFiles Content { get; }

	/// <summary>s&amp;box's compiled shaders, their modules and binding layouts.</summary>
	internal ShaderLibrary Shaders { get; }

	/// <summary>What's bound where nothing else is: black textures, zeroed buffers, the default vertex stream.</summary>
	internal ShaderDefaults Defaults { get; }

	/// <summary>
	/// Draw models and dispatch compute through material objects (persistent sets, constants written once), which is the
	/// default. Off binds by reflection every draw: the reference the tests compare against. See
	/// docs/managed/borealis-materials.md.
	/// </summary>
	public bool MaterialObjects { get; set; } = true;

	/// <summary>
	/// Depth-only draws whose vertex shader reads nothing but positions fetch them from a positions-only copy of the mesh's
	/// vertices (12 bytes a vertex) rather than the full vertices: on Construct the prepass reads 72 MB instead of 158 MB and
	/// is 0.15 ms faster. Models loaded while it's on get the copy (<see cref="PositionStreamBytes"/>).
	/// </summary>
	public bool PositionStreams { get; set; } = true;

	/// <summary>Bytes of positions-only vertex copies made (<see cref="PositionStreams"/>).</summary>
	public long PositionStreamBytes;

	/// <summary>The material objects drawn so far: the one material binding service of this device.</summary>
	internal MaterialObjectCache MaterialCache
	{
		get
		{
			if ( materialCache is { } cache ) return cache;
			lock ( pipelines ) return materialCache ??= new MaterialObjectCache( Gpu, Defaults );
		}
	}

	MaterialObjectCache materialCache;

	/// <summary>Material objects made so far and descriptor sets they wrote, for benchmarks and tests.</summary>
	public (int Objects, int SetsWritten) MaterialObjectStats => materialCache is null ? default : (materialCache.Count, materialCache.SetsWritten);

	/// <summary>Drop a material's objects; they're made again when it next draws. Changing a material does this itself.</summary>
	public void InvalidateMaterial( GpuMaterial material ) => materialCache?.Invalidate( material );

	/// <summary>
	/// Read a loaded material's compiled file (and its shader and textures) again, after it changed on disk; draws pick
	/// the new one up. Values set in code stay. Call between frames. False when it isn't loaded.
	/// </summary>
	public bool ReloadMaterial( string path )
	{
		GpuMaterial loaded;
		lock ( sharedMaterials )
		{
			if ( !sharedMaterials.TryGetValue( path, out loaded ) ) return false;
		}

		if ( loaded is not BackendMaterial material ) return false;
		var file = Content.TryRead( path ) is { } data ? MaterialFile.Read( data ) : null;
		material.Reload( file is null ? null : Shaders.Load( file.Shader ), file, file is null ? null : Textures( file ) );
		return true;
	}

	Dictionary<string, BackendTexture> Textures( MaterialFile file )
	{
		var textures = new Dictionary<string, BackendTexture>( StringComparer.OrdinalIgnoreCase );
		foreach ( var (variable, texture) in file.Textures )
		{
			if ( MaterialTextureFile( texture ) is BackendTexture loaded ) textures[variable] = loaded;
		}

		return textures;
	}

	BackendMaterial Track( BackendMaterial material )
	{
		material.Disposing = m => materialCache?.Invalidate( m );
		material.Library = Shaders;
		return material;
	}

	/// <summary>
	/// New-tier shaders (Slang surfaces and raw modules) and their materials' records, compiled when a
	/// <see cref="SurfaceMaterial"/> of theirs first draws. See docs/managed/shader-format-replacement.md.
	/// </summary>
	public SurfaceShaders Surfaces
	{
		get
		{
			if ( surfaces is { } made ) return made;
			lock ( pipelines ) return surfaces ??= new SurfaceShaders( this );
		}
	}

	SurfaceShaders surfaces;

	/// <summary>
	/// Keep static models' geometry in GPU-driven pages as they load (<see cref="GpuDrivenTables"/>), so
	/// <see cref="GpuDriven"/> can draw them; the classic path draws from the same pages. Set before loading models; on
	/// from the start with <see cref="GpuDrivenByDefault"/>.
	/// </summary>
	public bool GpuDrivenGeometry
	{
		get => gpuDrivenTables is not null;
		set
		{
			if ( value && gpuDrivenTables is null ) gpuDrivenTables = new GpuDrivenTables( Gpu );
		}
	}

	GpuDrivenTables gpuDrivenTables;

	/// <summary>The GPU-driven pages and tables, or null when <see cref="GpuDrivenGeometry"/> is off.</summary>
	internal GpuDrivenTables GpuDrivenTables => gpuDrivenTables;

	/// <summary>
	/// Draw aggregates GPU-driven: shaders' <c>.gpudriven.shader_c</c> variants, per-draw data in tables, one multi-draw
	/// per pipeline and pass state (docs/managed/borealis-gpu-driven.md). Needs <see cref="GpuDrivenGeometry"/> and material
	/// objects; anything it can't draw goes the classic way. On by default (<see cref="GpuDrivenByDefault"/>).
	/// </summary>
	public bool GpuDriven { get; set; }

	/// <summary>The GPU-driven tables' records and bytes, for reports.</summary>
	public string GpuDrivenReport => gpuDrivenTables is not { } t ? "off" : $"{t.Records.Geometry} geometry, {t.Records.Draws} draw and {t.Records.Materials} material records ({t.Bytes.Geometry / 1024} + {t.Bytes.Draws / 1024} + {t.Bytes.Materials / 1024} KiB), {t.Bytes.Pages / (1 << 20)} MiB of pages"
		+ (GpuDrivenLeftClassic.IsEmpty ? "" : "; aggregate batches left classic (batch draws while timed, shader, mode, why): " + string.Join( ", ", GpuDrivenLeftClassic.OrderByDescending( x => x.Value ).Select( x => $"{x.Value} {x.Key}" ) ));

	/// <summary>While <see cref="TimeDraws"/>, aggregate batches the GPU-driven path left to the classic one, by shader, mode and why.</summary>
	internal readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> GpuDrivenLeftClassic = new();

	/// <summary>The vertex layout GPU-driven pipelines are made with: none, their vertex shaders pull their inputs.</summary>
	internal VertexLayout PulledLayout => pulledLayout ??= Layout( new VertexLayout( 4, [] ) );

	/// <summary>The same for pipelines whose pixel shader reads its bucket's one material (a layout of its own keeps their material objects apart).</summary>
	internal VertexLayout PulledLayoutMaterialPerDraw => pulledLayoutPerDraw ??= Layout( new VertexLayout( 8, [] ) );

	VertexLayout pulledLayout, pulledLayoutPerDraw;

	/// <summary>
	/// Split GPU-driven buckets whose pixel shader reads its material (forward, alpha-tested depth) by material, so the
	/// pixel shader reads the record at an offset the compiler knows is uniform (specialization constant 120) rather than
	/// one carried per pixel: more multi-draws, no binds between them, and the classic shader's registers. Off by default:
	/// one multi-draw for every material of a pipeline costs complex 1 to 13 registers but draws Construct faster on CPU
	/// and GPU; on is for a shader whose registers matter more than its draws.
	/// </summary>
	public bool GpuDrivenMaterialBuckets { get; set; }

	/// <summary>
	/// Keep each view's GPU-culled batch records between frames (<see cref="Backend.GpuDrivenRecordMemo"/>), so a batch drawn
	/// again with the same material, geometry, attributes and lighting finds its records without the tables' lookups. On by
	/// default; tests turn it off to compare against the records looked up.
	/// </summary>
	internal bool GpuDrivenRecordMemo { get; set; } = true;

	/// <summary>Batches whose records came from the memo, for tests.</summary>
	internal long GpuDrivenRecordMemoHits;

	/// <summary>
	/// Keep what a call of resident aggregates makes (<see cref="GpuDrivenPlan"/>: its buckets, batch records and
	/// multi-draws) between frames and views of the same kind, so a view pays per frame only for its own culling record, a
	/// check per bucket and its multi-draws (docs/managed/borealis-gpu-driven.md, "Resident aggregates"). On by default;
	/// <c>-no-resident</c> turns it off, and views then prepare their own batches.
	/// </summary>
	public bool GpuDrivenResident { get; set; } = !HasSwitch( "-no-resident" );

	internal override bool DrawsAggregatesResident => GpuDrivenResident && CullsAggregatesGpuDriven && GpuDrivenCompaction && SupportsDrawIndirectCount && !GpuDrivenMaterialBuckets;

	/// <summary>
	/// Make static meshes resident with the aggregates (<see cref="GpuContext.ResidentMeshes"/>): the GPU culls them and picks
	/// their LODs. On by default with <see cref="GpuDrivenResident"/> and <see cref="GpuDrivenMeshes"/>; <c>-no-resident-meshes</c>
	/// turns it off, and views then prepare them.
	/// </summary>
	public bool GpuDrivenResidentMeshes { get; set; } = !HasSwitch( "-no-resident-meshes" );

	internal override bool DrawsMeshesResident => GpuDrivenResidentMeshes && DrawsAggregatesResident && DrawsMeshesGpuDriven;

	/// <summary>
	/// Give the sun's far cascade cache and what's drawn over it resident lists of the casters that aren't static too, which
	/// the GPU takes by when each last moved (<see cref="GpuContext.ResidentFilter"/>). On by default with
	/// <see cref="GpuDrivenResident"/>; <c>-no-resident-settled</c> turns it off, and those views draw their static casters
	/// from a list and the rest their own way.
	/// </summary>
	public bool GpuDrivenResidentSettled { get; set; } = !HasSwitch( "-no-resident-settled" );

	internal override bool DrawsSettledResident => GpuDrivenResidentSettled && DrawsAggregatesResident;

	/// <summary>
	/// Cull every view's resident plans in setup, in one dispatch of <c>borealis_resident_cull_cs</c> (a thread per entry looping
	/// over the views), for the calls to draw from (<see cref="BackendContext.CullResident"/>). On by default;
	/// <c>-no-resident-cull</c> turns it off, and each call culls its own.
	/// </summary>
	public bool GpuDrivenResidentCull { get; set; } = !HasSwitch( "-no-resident-cull" );

	/// <summary>
	/// In the resident cull, a forward pass that compacts lists what its prepass draws ids for from its plan's records, in the
	/// same dispatch (<see cref="GpuDrivenPlan.IdsFrom"/>). On by default; <c>-resident-cull-stamps</c> turns it off, and such
	/// passes cull in a second dispatch that reads the stamps their prepass's culling wrote.
	/// </summary>
	public bool GpuDrivenResidentListsFromIds { get; set; } = !HasSwitch( "-resident-cull-stamps" );

	/// <summary>The resident cull's kept calls, this frame's assignments and plans' records.</summary>
	internal readonly ResidentCullState ResidentCulls = new();

	/// <summary>Calls drawn from the resident cull, and its dispatches, items, views and entries, for tests and reports.</summary>
	internal long ResidentCullCalls, ResidentCullDispatches, ResidentCullItems, ResidentCullViews, ResidentCullEntries, ResidentCullListsFromIds;

	internal void CountResidentCull( int items, int views, long entries )
	{
		System.Threading.Interlocked.Increment( ref ResidentCullDispatches );
		System.Threading.Interlocked.Add( ref ResidentCullItems, items );
		System.Threading.Interlocked.Add( ref ResidentCullViews, views );
		System.Threading.Interlocked.Add( ref ResidentCullEntries, entries );
	}

	/// <summary>The resident cull's compute shader, or null when it isn't built.</summary>
	internal ShaderFile ResidentCullShader
	{
		get
		{
			if ( !residentCullShaderLoaded )
			{
				residentCullShader = Shaders.Load( "shaders/borealis_resident_cull_cs.shader" );
				residentCullShaderLoaded = true;
			}

			return residentCullShader;
		}
	}

	ShaderFile residentCullShader;
	bool residentCullShaderLoaded;

	internal override long LightingFreeKey( ShaderParameters attributes ) => attributes is BackendParameters parameters ? parameters.LightingFreeKey : base.LightingFreeKey( attributes );

	/// <summary>A resident owner's fragment records and its draws' (tinted records, each mesh's draw records), for reuse.</summary>
	internal override void ReleaseResident( object owner )
	{
		if ( gpuDrivenTables is not { } tables ) return;
		tables.ReleaseFragments( owner );
		if ( owner is not GpuContext.ResidentMeshes meshes ) return;
		foreach ( var keys in meshes.VisibilityKeys )
		{
			foreach ( var key in keys ) tables.ReleaseFragments( key );
		}
	}

	internal override bool TakesResident( GpuMaterial material ) => material switch
	{
		SurfaceMaterial surface => !surface.RenderState.DrawsTranslucent && surface.RenderState.DepthWrite && surface.RenderState.DepthTest && Surfaces.For( surface ) is { } program
			&& program.GpuDrivenPass( false, surface.RenderState.AlphaTest, false ) is not null && program.GpuDrivenPass( true, surface.RenderState.AlphaTest, false ) is not null,
		BackendMaterial { Shader: { } shader } => Shaders.GpuDriven( shader ) is not null,
		_ => false,
	};

	/// <summary>Resident calls drawn from a kept plan, and plans made, for tests and reports.</summary>
	internal long GpuDrivenPlanHits, GpuDrivenPlanBuilds;

	readonly ConditionalWeakTable<object, GpuDrivenPlans> gpuDrivenPlans = new();

	/// <summary>The plans kept for a resident list, made the first time.</summary>
	internal GpuDrivenPlans PlansFor( object list ) => gpuDrivenPlans.GetValue( list, static _ => new() );

	/// <summary>When set, a copy of each GPU-culled call's batch records, for tests.</summary>
	internal List<uint[]> GpuDrivenRecordLog { get; set; }

	readonly ConditionalWeakTable<object, GpuDrivenRecordMemos> gpuDrivenRecordMemos = new();

	/// <summary>A view's record memos, made the first time.</summary>
	internal GpuDrivenRecordMemos RecordMemosFor( object view ) => gpuDrivenRecordMemos.GetValue( view, static _ => new() );

	readonly System.Collections.Concurrent.ConcurrentDictionary<uint, BackendParameters> gpuDrivenMaterialParameters = new();
	static readonly StringToken GpuDrivenMaterialName = new( "BorealisGpuDrivenMaterial_t" );

	/// <summary>
	/// The object attributes a bucket of one material draws with: <c>BorealisGpuDrivenMaterial_t</c> holding its pixel
	/// material record, made once per record, so buckets of other materials differ only in a dynamic offset.
	/// </summary>
	internal BackendParameters GpuDrivenMaterialParameters( uint pixelMaterial ) => gpuDrivenMaterialParameters.GetOrAdd( pixelMaterial, static record =>
	{
		var parameters = new BackendParameters();
		parameters.SetData( GpuDrivenMaterialName, new System.Numerics.Vector4( BitConverter.UInt32BitsToSingle( record ), 0, 0, 0 ) );
		return parameters;
	} );

	/// <summary>Time every model draw's binding and recording into <see cref="DrawTimes"/>, for benchmarks.</summary>
	public bool TimeDraws { get; set; }

	/// <summary>Model draws timed with <see cref="TimeDraws"/> on.</summary>
	public DrawTimings DrawTimes { get; } = new();

	/// <summary>
	/// While set, contexts that begin recording count every draw into it (<c>borealislab -drawcensus</c>, debug only): set it
	/// for one frame, then clear it. Null by default.
	/// </summary>
	internal DrawCensus Census { get; set; }

	/// <summary>The unit box the 2D sky draws (native's well-known box shape, half a unit each way), made once.</summary>
	internal RenderMesh SkyBox => skyBox ??= MakeSkyBox();

	RenderMesh skyBox;

	RenderMesh MakeSkyBox()
	{
		var vertices = new MeshVertex[8];
		for ( int i = 0; i < 8; i++ ) vertices[i] = new MeshVertex( new Vector3( (i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f ), Vector3.Up, default );

		// Two triangles a face; the sky draws with culling off, so the winding doesn't matter
		uint[] indices = [0, 1, 3, 0, 3, 2, 4, 6, 7, 4, 7, 5, 0, 4, 5, 0, 5, 1, 2, 3, 7, 2, 7, 6, 0, 2, 6, 0, 6, 4, 1, 5, 7, 1, 7, 3];
		return BackendMesh.Create( this, vertices, indices, null, "Sky box" );
	}

	/// <summary>
	/// A quad covering clip space at depth 0.5, with <c>Graphics.Blit</c>'s uvs: what full-screen draws draw
	/// (<see cref="BackendContext.DrawFullscreen"/>).
	/// </summary>
	internal RenderMesh FullscreenQuad => fullscreenQuad ??= BackendMesh.Create( this,
		[
			new MeshVertex( new Vector3( -1, -1, 0.5f ), new Vector3( 0, 0, -1 ), new Vector2( 0, 1 ) ),
			new MeshVertex( new Vector3( 1, -1, 0.5f ), new Vector3( 0, 0, -1 ), new Vector2( 1, 1 ) ),
			new MeshVertex( new Vector3( -1, 1, 0.5f ), new Vector3( 0, 0, -1 ), new Vector2( 0, 0 ) ),
			new MeshVertex( new Vector3( 1, 1, 0.5f ), new Vector3( 0, 0, -1 ), new Vector2( 1, 0 ) ),
		], [0, 1, 2, 2, 1, 3], null, "Fullscreen quad" );

	RenderMesh fullscreenQuad;

	/// <summary>
	/// When set, the next frame context to begin a list of its own resets timestamps <c>Index</c> and <c>Index + 1</c> of
	/// <c>Queries</c> and writes the first at the start of its list, so a frame's GPU time can be read from there to a
	/// timestamp the caller writes after it; cleared once used. For benchmarks.
	/// </summary>
	internal (TimestampQueries Queries, int Index)? FrameStartTimestamp { get; set; }

	/// <summary>
	/// When set, every GPU scope Borealis records (its layers and setup) and every list a context submits is timed, and
	/// counted with <see cref="PassTimer.Statistics"/>. For benchmarks.
	/// </summary>
	internal PassTimer PassTimer { get; set; }

	/// <summary>
	/// Submit a frame's lists together, in one submission (<see cref="Device.SubmitLater"/>), rather than one submission
	/// each: on an RX 7900 XTX each submission drains the GPU, which cost Construct's frame recorded on workers (40
	/// lists) 0.66 ms of GPU time. Sent on when the render system has submitted the frame (<see cref="EndSubmitting"/>).
	/// </summary>
	internal bool BatchSubmits { get; set; } = true;

	/// <summary>
	/// Barriers wait for and block only the stages and accesses of the uses on either side (a texture's layouts, a buffer
	/// barrier's given stages) rather than every stage and access. Off for comparisons.
	/// </summary>
	internal bool ExactBarriers { get; set; } = true;

	/// <summary>
	/// The frame buffer copy gets native's depth-aware mask and blurred mips (<c>BackendContext.GenerateFrameBufferCopyMips</c>).
	/// Off for comparisons.
	/// </summary>
	internal bool FrameBufferCopyMips { get; set; } = true;

	/// <summary>
	/// A layer's draws split over lists recorded apart keep one rendering scope on the GPU: each list suspends its last scope
	/// and the next resumes it (<see cref="RenderingFlags"/>), when the plan says the next carries on its draws. Needs
	/// <see cref="BatchSubmits"/>. Off by default: on Construct it made no measurable GPU difference, and the 1.4.321
	/// validation layer reports a scope that both resumes and suspends (a chain of three lists or more) as resuming nothing.
	/// </summary>
	internal bool CarryScopes { get; set; }

	/// <summary>
	/// A list recorded beside others starts from where the lists before leave its textures, as learnt the last time the frame
	/// had the same shape (<see cref="GpuContext.PlanLayouts"/>), and moves them itself, rather than by a fix-up list at
	/// submission. Off for comparisons.
	/// </summary>
	internal bool LayoutPlans { get; set; } = true;

	/// <summary>Send the frame's lists held for one submission (<see cref="BatchSubmits"/>) to the GPU.</summary>
	public override void EndSubmitting() => Gpu.Flush();

	readonly BackendTexture black;
	readonly BackendTexture sheetMetadata;

	/// <summary>
	/// Render Borealis on <paramref name="gpu"/>, which stays the caller's, with compiled content from
	/// <paramref name="content"/> (by default the checkout's <c>game/core</c> above the application).
	/// </summary>
	public BackendDevice( Device gpu, ContentFiles content = null )
	{
		Gpu = gpu;
		Content = content ?? ContentFiles.FindGame();
		Shaders = new ShaderLibrary( gpu, Content );
		Passes = new PassModules( this );
		SlangPasses = SlangPassesByDefault;
		CompileGpuDrivenWithShaderCompiler();
		Defaults = new ShaderDefaults( gpu );

		// Bindless buffers shaders may read: nothing animated, no pipeline texture indices
		gpu.SetBindlessBuffer( ShaderAbi.BindlessVertexCache, Defaults.Storage );
		gpu.SetBindlessBuffer( ShaderAbi.BindlessPipelineTextures, Defaults.Storage );

		black = Placeholder( "Black", cube: false );
		sheetMetadata = Placeholder( "Sheet metadata", cube: false );

		// GPU-driven by default: the pages before any model loads, which the classic path draws from too
		if ( GpuDrivenByDefault )
		{
			GpuDrivenGeometry = true;
			GpuDriven = true;
		}
	}

	/// <summary>
	/// Whether a new device draws GPU-driven (<see cref="GpuDriven"/> and <see cref="GpuDrivenGeometry"/>): on unless the
	/// process was started with <c>-no-gpudriven</c>, for comparisons.
	/// </summary>
	public static bool GpuDrivenByDefault { get; set; } = !HasSwitch( "-no-gpudriven" );

	static bool HasSwitch( string name ) => Environment.GetCommandLineArgs().Any( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>The value after a switch on the command line, or null.</summary>
	static string SwitchValue( string name )
	{
		var args = Environment.GetCommandLineArgs();
		for ( int i = 0; i + 1 < args.Length; i++ )
		{
			if ( args[i].Equals( name, StringComparison.OrdinalIgnoreCase ) ) return args[i + 1];
		}

		return null;
	}

	/// <summary>
	/// Meshlet culling in GPU-driven depth prepasses (docs/managed/borealis-gpu-driven.md, "Meshlets"): index ranges are cut
	/// into meshlets after they load, and a prepass draws only the meshlets that can reach a sample. On by default;
	/// <c>-no-meshlets</c> turns it off, <c>-meshlets 32x32</c> picks the meshlet size (64x124 by default).
	/// </summary>
	public bool Meshlets { get; set; } = !HasSwitch( "-no-meshlets" );

	/// <summary>Threads building meshlets at once: a quarter of the cores, so the frame keeps the rest.</summary>
	public static int MeshletThreads { get; set; } = Math.Max( 1, Environment.ProcessorCount / 4 );

	Borealis.Backend.Meshlets.MeshletStore meshletStore;
	bool meshletStoreTried;
	readonly object meshletStoreLock = new();

	/// <summary>The meshlets of GPU-driven ranges, or null when <see cref="Meshlets"/> is off or the library isn't there.</summary>
	internal Borealis.Backend.Meshlets.MeshletStore MeshletStore
	{
		get
		{
			if ( !Meshlets || gpuDrivenTables is null ) return null;
			if ( Volatile.Read( ref meshletStoreTried ) ) return meshletStore;
			lock ( meshletStoreLock )
			{
				if ( !meshletStoreTried )
				{
					meshletStore = Borealis.Backend.Meshlets.MeshOptimizer.Available ? new Borealis.Backend.Meshlets.MeshletStore( Gpu, Borealis.Backend.Meshlets.MeshletConfig.Parse( SwitchValue( "-meshlets" ) ), MeshletThreads ) : null;
					if ( meshletStore is not null ) gpuDrivenTables.Meshlets = meshletStore;
					Volatile.Write( ref meshletStoreTried, true );
				}
			}

			return meshletStore;
		}
	}

	/// <summary>The pass modules shaders without a material draw from (<see cref="SlangPasses"/>).</summary>
	public PassModules Passes { get; }

	/// <summary>
	/// Whether shaders that have a pass module beside them (post-processing, UI, tools, compute, the engine's fullscreen
	/// passes) draw from it, compiled with Slang (on), or from their compiled <c>.shader_c</c> programs. Set it before
	/// drawing: variants already made keep their stages.
	/// </summary>
	public bool SlangPasses
	{
		get => Shaders.Modules is not null;
		set => Shaders.Modules = value ? Passes : null;
	}

	/// <summary>
	/// Whether a new device draws pass modules (<see cref="SlangPasses"/>): on unless the process was started with
	/// <c>-no-slang-passes</c> (or with <c>BOREALIS_NO_SLANG_PASSES=1</c>, for test runs), which draws every shader from its
	/// compiled programs, for comparisons.
	/// </summary>
	public static bool SlangPassesByDefault { get; set; } = !HasSwitch( "-no-slang-passes" ) && Environment.GetEnvironmentVariable( "BOREALIS_NO_SLANG_PASSES" ) != "1";

	/// <summary>
	/// Shaders without a GPU-driven variant beside them (a package's) get one compiled at load by the ShaderCompiler
	/// beside the application, from the source embedded in their compiled file, into <c>game/.source2/borealisgpudriven</c>;
	/// their draws stay classic until it's ready. One compiled by an older engine is recompiled the same way first, into
	/// <c>game/.source2/borealisshaderupgrade</c>. Nothing when there's no ShaderCompiler or game folder.
	/// </summary>
	void CompileGpuDrivenWithShaderCompiler()
	{
		// Beside the application (BorealisLab, SceneLab), or beside this assembly when the application is elsewhere: the game's
		// sbox.exe is in game/, its managed assemblies and the ShaderCompiler in game/bin/managed
		var compiler = new[] { AppContext.BaseDirectory, Path.GetDirectoryName( typeof( BackendDevice ).Assembly.Location ) }
			.Where( x => !string.IsNullOrEmpty( x ) ).Select( x => Path.Combine( x, EngineFolders.Executable( "ShaderCompiler" ) ) ).FirstOrDefault( File.Exists ) ?? "";
		var core = Content.Roots.FirstOrDefault( x => File.Exists( Path.Combine( x, "shaders", "borealis_gpu_driven.fxc" ) ) );
		if ( !File.Exists( compiler ) || core is null ) return;

		var game = Path.GetDirectoryName( core );
		Shaders.GpuDrivenCache = Path.Combine( game, ".source2", "borealisgpudriven" );
		// The variant's form, the wrapper it appends and the vfx_vulkan that rewrites it: a stale variant from an older
		// rewriter, whose record offsets may not be the ones the backend writes, can read a bindless index out of range
		var wrapper = Convert.ToHexString( System.Security.Cryptography.SHA256.HashData( File.ReadAllBytes( Path.Combine( core, "shaders", "borealis_gpu_driven.fxc" ) ) ), 0, 8 );
		var vfx = new FileInfo( Path.Combine( AppContext.BaseDirectory, "..", EngineFolders.Native, EngineFolders.Library( "vfx_vulkan" ) ) );
		var rewriter = vfx.Exists ? $".{vfx.Length:x}.{vfx.LastWriteTimeUtc.Ticks:x}" : "";
		Shaders.GpuDrivenCompilerVersion = $"{ShaderLibrary.GpuDrivenVersion}.{wrapper}{rewriter}";
		System.Threading.Tasks.Task.Run( Shaders.PruneGpuDrivenCache );
		Shaders.GpuDrivenCompiler = ( source, output ) =>
		{
			// Core shaders embed no source: build-shaders makes theirs, and one without stays classic
			if ( Path.GetFullPath( source ).StartsWith( core, StringComparison.OrdinalIgnoreCase ) ) return false;
			return RunShaderCompiler( compiler, game, ["--recompile", source, "-gpudriven", "-q", "--out", output] );
		};

		// A package shader an older engine compiled is recompiled from its embedded source as it loads, as the engine's
		// ShaderUpgrade does when a package installs
		Shaders.UpgradeCache = Path.Combine( game, ".source2", "borealisshaderupgrade" );
		Shaders.UpgraderVersion = $"{File.GetLastWriteTimeUtc( compiler ).Ticks}";
		Shaders.Upgrader = ( source, output ) => RunShaderCompiler( compiler, game, ["--recompile", source, "-q", "--out", output] );
	}

	static bool RunShaderCompiler( string compiler, string workingDirectory, string[] arguments )
	{
		var info = new ProcessStartInfo( compiler ) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
		foreach ( var argument in arguments ) info.ArgumentList.Add( argument );

		using var process = Process.Start( info );
		if ( process is null ) return false;
		process.OutputDataReceived += static ( _, _ ) => { };
		process.ErrorDataReceived += static ( _, _ ) => { };
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		if ( process.WaitForExit( TimeSpan.FromMinutes( 10 ) ) ) return process.ExitCode == 0;

		process.Kill( true );
		return false;
	}

	BackendTexture Placeholder( string name, bool cube )
	{
		var texture = (BackendTexture)CreateTexture( new TextureDesc { Width = 1, Height = 1, Depth = 1, Format = TextureFormat.RGBA8888, Samples = 1, Mips = 1, Cube = cube, RenderTarget = cube }, name );
		Clear( texture, Color.Black );
		return texture;
	}

	static Format Map( TextureFormat format ) => format switch
	{
		TextureFormat.RGBA8888 => Format.RGBA8Unorm,
		TextureFormat.BGRA8888 => Format.BGRA8Unorm,
		TextureFormat.A8 => Format.R8Unorm,
		TextureFormat.R16F => Format.R16Float,
		TextureFormat.R32F => Format.R32Float,
		TextureFormat.R32_UINT => Format.R32Uint,
		TextureFormat.RG1616F => Format.RG16Float,
		TextureFormat.RG3232F => Format.RG32Float,
		TextureFormat.R11G11B10F => Format.RG11B10Float,
		TextureFormat.RGB323232F => Format.RGB32Float,
		TextureFormat.RGBA16161616 => Format.RGBA16Unorm,
		TextureFormat.RGBA16161616F => Format.RGBA16Float,
		TextureFormat.RGBA32323232F => Format.RGBA32Float,
		TextureFormat.RGBA1010102 => Format.RGB10A2Unorm,
		TextureFormat.BGRA1010102 => Format.BGR10A2Unorm,
		TextureFormat.D16 => Format.D16Unorm,
		TextureFormat.D32 => Format.D32Float,
		TextureFormat.D24S8 => Format.D24UnormS8Uint,
		TextureFormat.D32FS8 => Format.D32FloatS8Uint,
		_ => Format.RGBA8Unorm,
	};

	/// <summary>The format a texture is in on the GPU (D24S8 may be D32S8 there, as <see cref="Device"/> makes it).</summary>
	internal static Format FormatOf( BackendTexture texture ) => Map( texture.Format );

	/// <inheritdoc/>
	public override GpuTexture CreateTexture( in TextureDesc desc, string name )
	{
		var format = Map( desc.Format );
		var samples = Math.Max( desc.Samples, 1 );
		var usage = TextureUsage.CopySource | TextureUsage.CopyDestination;
		// Multisampled targets aren't sampled, except depth: a frame's depth chain reads it (D_MSAA), as Source 2's targets allow
		if ( samples == 1 || format.IsDepth() ) usage |= TextureUsage.Sampled;
		if ( desc.RenderTarget ) usage |= format.IsDepth() ? TextureUsage.DepthTarget : TextureUsage.ColorTarget;
		if ( desc.Storage ) usage |= TextureUsage.Storage;

		// Formats that can't be drawn to or written by compute here still make a texture; nothing will draw to it
		if ( !Gpu.Supports( format, usage ) ) usage &= ~(TextureUsage.ColorTarget | TextureUsage.Storage);

		var info = new TextureInfo
		{
			Width = desc.Width,
			Height = desc.Height,
			Depth = desc.Depth > 1 ? desc.Depth : 1,
			Layers = 1,
			Mips = Math.Max( desc.Mips, 1 ),
			Samples = samples,
			Dimension = desc.Cube ? TextureDimension.TextureCube : desc.Depth > 1 ? TextureDimension.Texture3D : TextureDimension.Texture2D,
			Format = format,
			Usage = usage,
			Name = name,
		};

		var texture = new BackendTexture( this, Gpu.CreateTexture( info ), desc, name );
		if ( !desc.Data.IsEmpty && !desc.Cube && desc.Depth <= 1 )
		{
			Gpu.WriteTexture( texture.Handle, desc.Data.Span );
			texture.Layout = ImageLayout.ShaderRead;
		}

		return texture;
	}

	/// <summary>
	/// A compiled texture (<c>.vtex_c</c>) from the content, loaded once; a black 1x1 stand-in (a cube for cube paths)
	/// when it isn't there or its format has no Borealis.Gpu form.
	/// </summary>
	public override GpuTexture LoadTexture( string path ) => LoadOnce( path, streamed: false );

	/// <summary>
	/// A material's texture: with <see cref="StreamTextures"/>, its tail first, and the mips its draws ask for streamed
	/// in later; otherwise as <see cref="LoadTexture(string)"/> loads it.
	/// </summary>
	internal GpuTexture MaterialTextureFile( string path ) => LoadOnce( path, streamed: StreamTextures );

	GpuTexture LoadOnce( string path, bool streamed )
	{
		lock ( loadedTextures )
		{
			if ( loadedTextures.TryGetValue( path, out var texture ) )
			{
				// Asked for whole by something that doesn't ask for mips
				if ( !streamed && texture is BackendTexture { Stream: { } stream } ) stream.Pinned = true;
				return texture;
			}

			return loadedTextures[path] = Load( path, streamed ) ?? Placeholder( path, cube: path.Contains( "cube", StringComparison.OrdinalIgnoreCase ) );
		}
	}

	BackendTexture Load( string path, bool streamed )
	{
		// Read ahead on a worker for a model loading asynchronously (LoadModelAsync), else from the content's folders or
		// packages (a map's VPK); a streamed texture's tail, unless it doesn't stream
		if ( !readTextures.TryRemove( path, out var texture ) || (!streamed && texture?.TopMip > 0) )
		{
			texture = streamed ? TextureStreamer.ReadMips( Content, path, TextureFile.Tail ) : null;
			if ( texture is null || (texture.TopMip > 0 && !TextureStreamer.Streams( texture )) )
			{
				var data = Content.TryRead( path );
				if ( data is null ) return null;
				texture = TextureFile.Read( data );
			}
		}

		if ( texture is null ) return null;
		if ( texture.Format == Format.Unknown ) return null;
		if ( texture.TopMip > 0 && !TextureStreamer.Streams( texture ) ) return null;

		var desc = new TextureDesc
		{
			Width = texture.Width,
			Height = texture.Height,
			Depth = texture.Depth,
			Mips = texture.Mips,
			Samples = 1,
			Cube = texture.Dimension is TextureDimension.TextureCube,
			Format = TextureFormat.RGBA8888,
		};

		var made = new BackendTexture( this, texture.Create( Gpu, path ), desc, path ) { Layout = ImageLayout.ShaderRead, Dimension = texture.Dimension };
		if ( streamed && TextureStreamer.Streams( texture ) ) made.Stream = Streamer.Add( made, path, texture );
		return made;
	}

	/// <inheritdoc/>
	public override GpuTexture Black => black;

	/// <inheritdoc/>
	public override int MissingTextureViewIndex( bool srgb ) => black.ViewIndex( srgb, TextureViewDimension.Default );

	/// <inheritdoc/>
	public override void Clear( GpuTexture texture, Color color )
	{
		var t = (BackendTexture)texture;
		var list = Gpu.BeginCommandList( QueueType.Graphics, "Clear texture" );
		list.Barrier( TextureBarrier.Transition( t.Handle, t.Layout, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( t.Handle, new System.Numerics.Vector4( color.r, color.g, color.b, color.a ) );
		list.Barrier( TextureBarrier.Transition( t.Handle, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead ) );
		Gpu.Wait( Gpu.Submit( list ) );
		t.Layout = ImageLayout.ShaderRead;
	}

	/// <summary>
	/// Move a target a frame drew into to where shaders sample it, in a list of its own that reaches the GPU before anything
	/// submitted after: a view rendered for another's draws to read (a water body's refraction).
	/// </summary>
	public void MakeReadable( GpuTexture texture )
	{
		if ( texture is not BackendTexture t || t.Layout == ImageLayout.ShaderRead ) return;

		var list = Gpu.BeginCommandList( QueueType.Graphics, "Borealis readable" );
		list.Barrier( TextureBarrier.Transition( t.Handle, t.Layout, ImageLayout.ShaderRead, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead ) );
		if ( BatchSubmits ) Gpu.SubmitLater( list );
		else Gpu.Submit( list );
		t.Layout = ImageLayout.ShaderRead;
	}

	/// <inheritdoc/>
	public override IDisposable CreateBuffer( int elementCount, int elementSize, GpuBuffer.UsageFlags usage, string name )
	{
		var bufferUsage = BufferUsage.Storage;
		if ( (usage & GpuBuffer.UsageFlags.Vertex) != 0 ) bufferUsage |= BufferUsage.Vertex;
		if ( (usage & GpuBuffer.UsageFlags.Index) != 0 ) bufferUsage |= BufferUsage.Index;
		if ( (usage & GpuBuffer.UsageFlags.IndirectDrawArguments) != 0 ) bufferUsage |= BufferUsage.Indirect;

		var size = (long)elementCount * elementSize;
		return new BackendBuffer( this, Gpu.CreateBuffer( new BufferInfo { Size = size, Usage = bufferUsage, Name = name } ), size, name );
	}

	/// <inheritdoc/>
	public override void SetData( GpuBuffer buffer, ReadOnlySpan<byte> data ) => Gpu.WriteBuffer( ((BackendBuffer)buffer.Resource).Handle, data );

	/// <inheritdoc/>
	public override ShaderParameters CreateParameters() => new BackendParameters();

	/// <summary>
	/// A light probe volume's attributes here: <c>PerBatchLightProbeVolumeConstantBuffer_t</c> as
	/// <c>CSceneLightProbeVolumeObject::CreateConstants</c> makes it - world to the box's [0, 1] through the volume's
	/// transform, the probe texture's texel-centre range, its three textures' bindless indices - and the flags for baked
	/// lighting from a probe. Null unless <paramref name="probe"/> is a 3D texture here; an index or scalar texture that
	/// isn't is the probe's, as the shader samples each as 3D.
	/// </summary>
	public ShaderParameters LightProbeVolumeParameters( GpuTexture probe, GpuTexture indices, GpuTexture scalars, Transform transform, Vector3 mins, Vector3 maxs )
	{
		if ( probe is not BackendTexture { Dimension: TextureDimension.Texture3D } ) return null;
		if ( indices is not BackendTexture { Dimension: TextureDimension.Texture3D } ) indices = probe;
		if ( scalars is not BackendTexture { Dimension: TextureDimension.Texture3D } ) scalars = probe;

		var size = maxs - mins;
		var position = transform.Position;
		Vector3[] axes = [transform.Rotation.Forward, transform.Rotation.Left, transform.Rotation.Up];
		float[] boxMins = [mins.x, mins.y, mins.z];
		float[] boxSizes = [size.x, size.y, size.z];
		var rows = new Vector4[3];
		for ( int i = 0; i < 3; i++ )
		{
			var a = axes[i] / boxSizes[i];
			rows[i] = new Vector4( a.x, a.y, a.z, -(Vector3.Dot( axes[i], position ) + boxMins[i]) / boxSizes[i] );
		}

		const int Layers = 6;
		var info = Gpu.InfoOf( ((BackendTexture)probe).Handle );
		var half = new Vector3( 0.5f / info.Width, 0.5f / info.Height, 0.5f / Math.Max( info.Depth, 1 ) );
		var constants = new LightProbeVolumeConstants
		{
			Row0 = rows[0],
			Row1 = rows[1],
			Row2 = rows[2],
			TextureMin = new Vector4( half.x, half.y, half.z, 0 ),
			TextureMax = new Vector4( 1 - half.x, 1 - half.y, 1f / Layers - half.z, 0 ),
			Colour = new Vector4( 1, 1, 1, 0 ),
			Probe = probe.BindlessIndex,
			Indices = indices.BindlessIndex,
			Scalars = scalars.BindlessIndex,
		};

		var attributes = CreateParameters();
		attributes.SetData( "PerBatchLightProbeVolumeConstantBuffer_t", constants );
		attributes.Set( "UsesBakedLightmaps", false );
		attributes.Set( "UsesBakedLightingFromProbe", true );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_PROBE", 1 );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP", 0 );
		return attributes;
	}

	/// <summary><c>PerBatchLightProbeVolumeConstantBuffer_t</c>.</summary>
	struct LightProbeVolumeConstants
	{
		public Vector4 Row0, Row1, Row2;
		public Vector4 TextureMin, TextureMax, Colour;
		public int Probe, Indices, Scalars;
	}

	/// <summary>
	/// Lightmap attributes over <paramref name="lightmaps"/> (texture paths), as native's <c>SetSceneObjectLightmapAttributes</c>
	/// binds a world's: the textures by their base names, their bindless indices and <paramref name="uvScale"/> in
	/// <c>BakedLightingConstantBuffer_t</c>, and the lightmap combo. Null when none is readable; those that aren't go in
	/// <paramref name="problems"/>.
	/// </summary>
	public ShaderParameters LightmapParameters( IEnumerable<string> lightmaps, System.Numerics.Vector2 uvScale, List<string> problems = null )
	{
		var textures = new List<(string Name, GpuTexture Texture)>();
		foreach ( var path in lightmaps )
		{
			var name = Path.GetFileNameWithoutExtension( path );
			if ( name == "debug_chart_color" ) continue;
			if ( LoadTexture( path ) is BackendTexture { Dimension: TextureDimension.Texture2DArray } texture ) textures.Add( (name, texture) );
			else problems?.Add( $"lightmap {path} isn't readable" );
		}

		if ( textures.Count == 0 ) return null;

		var constants = new LightmapConstants { UvScale = new Vector4( uvScale.X > 0 ? uvScale.X : 1, uvScale.Y > 0 ? uvScale.Y : 1, 0, 0 ) };
		var attributes = CreateParameters();
		for ( int i = 0; i < textures.Count && i < 8; i++ )
		{
			attributes.Set( textures[i].Name, textures[i].Texture );
			constants.Indices[i] = textures[i].Texture.BindlessIndex;
		}

		attributes.SetData( "BakedLightingConstantBuffer_t", constants );
		attributes.Set( "UsesBakedLightmaps", true );
		attributes.Set( "UsesBakedLightingFromProbe", false );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_PROBE", 0 );
		attributes.SetCombo( "D_BAKED_LIGHTING_FROM_LIGHTMAP", 1 );
		return attributes;
	}

	readonly Dictionary<string, ShaderParameters> mapLightmaps = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The lightmaps of the map in <paramref name="folder"/> (<c>maps/construct_25</c>), from its world file
	/// (<see cref="LightmapParameters"/>), read once; null without lightmaps or a readable world.
	/// </summary>
	public ShaderParameters MapLightmapParameters( string folder )
	{
		if ( string.IsNullOrEmpty( folder ) ) return null;
		lock ( mapLightmaps )
		{
			if ( mapLightmaps.TryGetValue( folder, out var cached ) ) return cached;
			ShaderParameters made = null;
			try
			{
				var world = Borealis.Source2.World.WorldFile.Load( Content, $"{folder}/world.vwrld" );
				made = LightmapParameters( world.Lightmaps, world.LightmapUvScale );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or KeyNotFoundException )
			{
				made = null;
			}

			return mapLightmaps[folder] = made;
		}
	}

	/// <summary>Eight lightmap bindless indices (six used), as two int4s.</summary>
	[InlineArray( 8 )]
	struct LightmapIndices
	{
		int element;
	}

	/// <summary><c>BakedLightingConstantBuffer_t</c>: the uv scale and the lightmaps' bindless indices.</summary>
	struct LightmapConstants
	{
		public Vector4 UvScale;
		public LightmapIndices Indices;
	}

	/// <inheritdoc/>
	public override GpuTexture SheetMetadata => sheetMetadata;

	/// <summary>A compiled material's texture for a shader variable (<c>g_tSkyTexture</c>), or null.</summary>
	public GpuTexture MaterialTexture( string material, string variable )
	{
		if ( LoadMaterial( material ) is not BackendMaterial m || !m.Textures.TryGetValue( variable, out var texture ) ) return null;

		// Used outside the material's draws, which are what ask for its mips
		if ( texture.Stream is { } stream ) stream.Pinned = true;
		return texture;
	}

	/// <summary>
	/// A compiled material (<c>.vmat_c</c>) with its shader and textures; one that isn't there, or whose shader isn't,
	/// draws nothing.
	/// </summary>
	public override GpuMaterial LoadMaterial( string path )
	{
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.TryGetValue( path, out var material ) ) return material;
			return sharedMaterials[path] = Converted( path ) ?? Material( path, null );
		}
	}

	/// <summary>
	/// A material whose <c>.shader</c> has a surface standing in for it (<see cref="SurfaceShaders.Convert"/>), as that
	/// surface with the material's values; null when it has none or doesn't compile.
	/// </summary>
	GpuMaterial Converted( string path )
	{
		var tier = Surfaces;
		if ( !tier.HasConversions ) return null;
		if ( (readMaterials.TryGetValue( path, out var read ) ? read : Content.TryRead( path ) is { } data ? ReadMaterial( data ) : null) is not { } file ) return null;
		if ( tier.ConversionFor( file.Shader ) is not { } surface || tier.Compile( surface ) is not { } compiled ) return null;
		if ( !ConvertedMaterial.Accepts( compiled, file ) || tier.ConvertMaterial?.Invoke( path ) == false ) return null;
		tier.CountConverted( path );
		readMaterials.TryRemove( path, out _ );
		var converted = ConvertedMaterial.Create( compiled, file, path, MaterialTextureFile );

		// The .shader material, for draws the surface can't make
		converted.SetFallback( () =>
		{
			lock ( sharedMaterials ) return Material( path, null );
		} );

		// Its scene attributes (overlay, NoZPrepass, ...) are the .shader material's: that material without textures
		converted.SetAttributes( () => new BackendMaterial( path, Shaders.Load( file.Shader ), file, null ) );
		return converted;
	}

	/// <summary>A material that's a shader with its defaults.</summary>
	public override GpuMaterial MaterialFromShader( string shaderPath )
	{
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.TryGetValue( shaderPath, out var material ) ) return material;
			return sharedMaterials[shaderPath] = Track( new BackendMaterial( shaderPath, Shaders.Load( shaderPath ), null, null ) );
		}
	}

	/// <inheritdoc/>
	public override GpuMaterial CreateMaterial( string name, string shaderPath ) => Track( new BackendMaterial( name, Shaders.Load( shaderPath ), null, null ) );

	/// <summary>
	/// A lit material of one colour, for meshes made with <see cref="BackendMesh.Create"/>:
	/// <c>materials/default/white.vmat</c> (complex.shader, white, flat normal) tinted with <paramref name="albedo"/>.
	/// </summary>
	public GpuMaterial CreateMaterial( string name, Color albedo )
	{
		var values = new Dictionary<string, float[]>( StringComparer.OrdinalIgnoreCase ) { ["g_vColorTint"] = [albedo.r, albedo.g, albedo.b, 0] };
		return Content.Find( "materials/default/white.vmat" ) is null
			? Track( new BackendMaterial( name, Shaders.Load( "shaders/complex.shader" ), null, null, values ) )
			: Material( "materials/default/white.vmat", values, name );
	}

	/// <summary>
	/// A compiled material with <paramref name="values"/> (shader variables by name) over its own. It draws nothing if
	/// it, or its shader, isn't built.
	/// </summary>
	public GpuMaterial LoadMaterial( string path, IReadOnlyDictionary<string, float[]> values, string name = null )
		=> Material( path, values is null ? null : new( values, StringComparer.OrdinalIgnoreCase ), name );

	/// <summary>A material file, or null when it can't be read (KV3 with binary blobs isn't yet), so it draws nothing.</summary>
	static MaterialFile ReadMaterial( byte[] data )
	{
		try
		{
			return MaterialFile.Read( data );
		}
		catch ( System.IO.InvalidDataException )
		{
			return null;
		}
	}

	BackendMaterial Material( string path, Dictionary<string, float[]> values, string name = null )
	{
		var file = readMaterials.TryRemove( path, out var read ) ? read : Content.TryRead( path ) is { } data ? ReadMaterial( data ) : null;
		if ( file is null ) return Track( new BackendMaterial( name ?? path, null, null, null, values ) );
		return Track( new BackendMaterial( name ?? path, Shaders.Load( file.Shader ), file, Textures( file ), values ) );
	}

	/// <summary>
	/// Compiled models: a <c>.vmdl_c</c> from the content as a mesh Borealis draws, with its LODs and materials, or null
	/// when it isn't built. See <see cref="BackendModel.Load"/>.
	/// </summary>
	public RenderMesh LoadModel( string path, ulong? meshGroups = null, int materialGroup = 0 ) => BackendModel.Load( this, path, meshGroups, materialGroup );

	/// <summary>The vertex layouts pipelines have been made for, for tests.</summary>
	internal VertexLayout[] PipelineLayouts
	{
		get
		{
			lock ( pipelines ) return [.. pipelines.Keys.Select( k => k.Layout ).Distinct()];
		}
	}

	/// <summary>One instance of each distinct vertex layout, which pipelines are keyed by.</summary>
	internal VertexLayout Layout( VertexLayout layout )
	{
		lock ( layouts )
		{
			if ( layouts.TryGetValue( layout, out var existing ) ) return existing;
			layouts[layout] = layout;
			return layout;
		}
	}

	/// <summary>The colour an ids pipeline is keyed by (<see cref="Pipeline"/>'s ids), which no other pipeline draws into.</summary>
	internal const Format IdsColorKey = Format.R32Uint;

	/// <summary>
	/// A pipeline for a variant drawing vertices laid out as <paramref name="layout"/> into targets of these formats,
	/// made once. Mesh vertices are stream 0, instance ids stream 1 (<c>TEXCOORD13</c>), and inputs the mesh lacks, or
	/// has as integers where the shader wants floats (or the other way round), read the default stream 2, as native's
	/// input layouts do for streams a mesh doesn't have. A vertex-cache layout reads vertex ids from streams 2 and 3 and
	/// the default stream from 4.
	/// </summary>
	internal PipelineHandle Pipeline( ShaderVariant variant, VertexLayout layout, Format color, Format depth, int samples, bool depthOnly, BindingLayoutHandle[] sets = null, bool materialPerDraw = false, bool ids = false, ShaderVariant stateFrom = null )
	{
		var key = new PipelineKey( variant, layout, ids ? IdsColorKey : color, depth, samples, depthOnly, sets, materialPerDraw, stateFrom );
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;

			// A vertex-cache draw reads its vertex ids from streams 2 and 3, as floats
			var vertexCache = layout.VertexCache;
			var attributes = ShaderDefaults.MapVertices( variant.VertexInputs, input =>
			{
				if ( input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex == 13 ) return new VertexAttribute( 0, 1, VertexFormat.UInt, 0 );
				if ( vertexCache && input.Semantic == VertexSemantic.TexCoord && input.SemanticIndex is 14 or 15 ) return new VertexAttribute( 0, input.SemanticIndex == 14 ? 2 : 3, VertexFormat.Float, 0 );
				if ( layout.Find( input.Semantic, input.SemanticIndex ) is not { } field ) return null;
				var integer = field.Format is VertexFormat.UByte4 or VertexFormat.UInt or VertexFormat.UInt2 or VertexFormat.UInt4;
				if ( integer != (input.Kind != SpirvScalar.Float) ) return null;
				return new VertexAttribute( 0, 0, field.Format, field.Offset );
			}, defaultBuffer: vertexCache ? 4 : 2 );

			VertexBufferLayout[] streams = vertexCache
				? [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 4 ), new( 4 ), new( 0 )]
				: [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 0 )];
			// The visibility-driven forward pass's id prepass: target 0 takes the authored output (its alpha drives alpha to
			// coverage) and goes nowhere, target 1 the ids (a GPU-driven pixel shader's id output)
			Format[] colors = ids ? [Format.R8Unorm, Format.R32Uint] : color == Format.Unknown ? [] : [color];
			var info = variant.PipelineInfo( streams, attributes, colors, depth, samples, depthBias: depthOnly || color == Format.Unknown || ids, $"{variant}{(depthOnly ? " depth" : ids ? " ids" : "")}" );
			if ( ids ) info.Blend = [BlendState.Opaque, BlendState.Opaque];
			if ( sets is not null ) info.Sets = sets;

			// borealis_gpu_driven_ids drawing another variant's depth: that variant's culling, depth and stencil
			if ( stateFrom?.State is { } state )
			{
				info.CullMode = state.Cull;
				info.DepthClamp = !state.DepthClip;
				info.DepthTest = depth != Format.Unknown && state.DepthTest;
				info.DepthWrite = depth != Format.Unknown && state.DepthWrite;
				info.DepthCompare = state.DepthCompare;
				info.StencilTest = depth.HasStencil() && state.StencilTest;
				info.StencilFront = state.StencilFront;
				info.StencilBack = state.StencilBack;
				info.StencilReadMask = state.StencilReadMask;
				info.StencilWriteMask = state.StencilWriteMask;
			}

			// A GPU-driven pixel shader reading its bucket's one material (borealis_gpu_driven.h's specialization constant)
			if ( materialPerDraw && info.FragmentShader.IsValid ) info.FragmentConstants = [.. info.FragmentConstants ?? [], new SpecializationConstant( GpuDrivenMaterialPerDrawConstant, 1 )];

			// Stripped pixel shaders: depth passes of opaque materials don't need them (alpha-tested ones keep theirs)
			if ( depthOnly && color == Format.Unknown )
			{
				info.FragmentShader = default;
				info.FragmentEntry = null;
				info.FragmentConstants = null;
			}

			pipeline = Gpu.CreateGraphicsPipeline( info );
			pipelines[key] = pipeline;
			if ( DumpPixelShaders is { } dump && info.FragmentShader.IsValid ) DumpPixelShader( dump, variant, info.FragmentConstants, materialPerDraw );
			return pipeline;
		}
	}

	/// <summary>
	/// <c>BOREALIS_DUMP_PIXEL_SHADERS=folder</c>: write every pixel shader a pipeline is made with, specialization constants
	/// applied, as <c>shader_mode_static_dynamic_kind.spv</c> (kind: classic, gpudriven, or gpudriven-perdraw), so register
	/// counts of the classic and GPU-driven forms of one combo can be compared offline (amdllpc).
	/// </summary>
	static readonly string DumpPixelShaders = Environment.GetEnvironmentVariable( "BOREALIS_DUMP_PIXEL_SHADERS" );

	static void DumpPixelShader( string folder, ShaderVariant variant, SpecializationConstant[] constants, bool materialPerDraw )
	{
		var stage = variant.Pixel;
		var kind = !stage.GpuDriven ? "classic" : materialPerDraw ? "gpudriven-perdraw" : "gpudriven";
		var name = $"{System.IO.Path.GetFileName( variant.Shader.Name ).Split( '.' )[0]}_{variant.Mode?.Name}_s{stage.Static.Id}_d{stage.Dynamic.Id}_{kind}.spv";
		var path = System.IO.Path.Combine( folder, name );
		if ( System.IO.File.Exists( path ) ) return;

		if ( Specialized( stage.Bytecode.Shared.Spirv, constants ) is not { } spirv ) return;
		System.IO.Directory.CreateDirectory( folder );
		System.IO.File.WriteAllBytes( path, spirv );
	}

	/// <summary>A copy of <paramref name="source"/> with <paramref name="constants"/> as its specialization constants' defaults; null when it doesn't parse.</summary>
	internal static byte[] Specialized( byte[] source, SpecializationConstant[] constants )
	{
		var spirv = (byte[])source.Clone();
		var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( spirv.AsSpan() );
		var specIds = new Dictionary<uint, uint>();
		for ( int pass = 0; pass < 2; pass++ )
		{
			for ( int i = 5; i < words.Length; )
			{
				var count = (int)(words[i] >> 16);
				var opcode = (int)(words[i] & 0xffff);
				if ( count == 0 ) return null;
				if ( pass == 0 && opcode == 71 && count >= 4 && words[i + 2] == 1 ) specIds[words[i + 1]] = words[i + 3];
				else if ( pass == 1 && opcode is 48 or 49 or 50 && specIds.TryGetValue( words[i + 2], out var id ) )
				{
					foreach ( var c in constants ?? [] )
					{
						if ( c.Id != id ) continue;
						if ( opcode == 50 ) words[i + 3] = c.Value;
						else words[i] = (words[i] & 0xffff0000) | (uint)(c.Value != 0 ? 48 : 49);
					}
				}

				i += count;
			}
		}

		return spirv;
	}

	/// <summary>
	/// <c>BOREALIS_DUMP_SURFACE_SHADERS=folder</c>: write the pixel shader of every specialised surface pipeline, its
	/// constants applied, as <c>surface_pass_key.spv</c> with the feature values beside it (<c>.txt</c>), for register counts.
	/// </summary>
	internal static readonly string DumpSurfaceShaders = Environment.GetEnvironmentVariable( "BOREALIS_DUMP_SURFACE_SHADERS" );

	internal readonly record struct PipelineKey( ShaderVariant Variant, VertexLayout Layout, Format Color, Format Depth, int Samples, bool DepthOnly, BindingLayoutHandle[] Sets = null, bool MaterialPerDraw = false, ShaderVariant StateFrom = null );

	/// <summary><c>BOREALIS_GPU_DRIVEN_MATERIAL_PER_DRAW_ID</c>: set, a GPU-driven pixel shader reads <c>g_nBorealisPixelMaterial</c>.</summary>
	const uint GpuDrivenMaterialPerDrawConstant = 120;

	/// <summary>A compute pipeline for a compute shader's variant (with <paramref name="sets"/> for its layout's), made once.</summary>
	internal PipelineHandle ComputePipeline( ShaderVariant variant, BindingLayoutHandle[] sets = null )
	{
		var key = new PipelineKey( variant, null, Format.Unknown, Format.Unknown, 0, false, sets );
		lock ( pipelines )
		{
			if ( pipelines.TryGetValue( key, out var pipeline ) ) return pipeline;
			pipeline = Gpu.CreateComputePipeline( new ComputePipelineInfo
			{
				Shader = variant.Compute.Module,
				Entry = variant.Compute.Bytecode.EntryPoint( ShaderProgramType.Compute ),
				Constants = variant.Compute.Constants,
				Sets = sets ?? variant.Sets,
				Name = variant.ToString(),
			} );
			pipelines[key] = pipeline;
			return pipeline;
		}
	}

	internal override GpuContext CreateContext( GpuContext frame = null ) => new BackendContext( this, (BackendContext)frame );

	internal override bool DrawsAggregatesGpuDriven => GpuDriven && MaterialObjects && GpuDrivenTables is not null;

	internal override bool CullsAggregatesGpuDriven => DrawsAggregatesGpuDriven && GpuDrivenCulling;

	internal override bool DrawsMeshesGpuDriven => GpuDrivenMeshes && DrawsAggregatesGpuDriven;

	/// <summary>Its materials read their shaders' stencil state (<see cref="BackendMaterial.NeedsStencil"/>).</summary>
	internal override bool KnowsStencilUse => true;

	/// <summary>
	/// Draw a map's static overlays GPU-driven (on by default; <c>-no-gpudriven-overlays</c> turns it off): those of one
	/// render order as one call, so draws of a pipeline go together whatever their material, which reorders overlays within
	/// a render order where they overlap.
	/// </summary>
	public bool GpuDrivenOverlays { get; set; } = !HasSwitch( "-no-gpudriven-overlays" );

	internal override bool DrawsOverlaysGpuDriven => GpuDrivenOverlays && DrawsAggregatesGpuDriven;

	/// <summary>
	/// Static meshes' opaque draws join the aggregates' GPU-driven call (docs/managed/borealis-gpu-driven.md). On by default:
	/// on Construct it saves GPU time but costs CPU time, mostly on worker threads, where the one GPU-driven run records what
	/// workers drew between them; <c>-no-gpudriven-meshes</c> turns it off.
	/// </summary>
	public bool GpuDrivenMeshes { get; set; } = !HasSwitch( "-no-gpudriven-meshes" );

	/// <summary>
	/// Cull GPU-driven aggregates' fragments on the GPU (<c>borealis_gpu_driven_cull_cs</c>), by the same frustum and size
	/// tests the CPU makes, from records written once: the CPU writes 16 bytes per aggregate a frame instead of an instance
	/// record and command per visible fragment. On by default; off writes them on the CPU, to compare.
	/// </summary>
	public bool GpuDrivenCulling { get; set; } = true;

	/// <summary>
	/// With <see cref="GpuDrivenCulling"/>, the culling writes only the kept fragments' commands, packed at the start of
	/// each multi-draw, which draws as many as it kept (<c>DrawIndexedIndirectCount</c>). On by default where the device
	/// takes a draw count from a buffer; off leaves a command per fragment with no instance where culled, to compare.
	/// </summary>
	public bool GpuDrivenCompaction { get; set; } = true;

	/// <summary>The culling compute shader, or null when it isn't built.</summary>
	internal ShaderFile GpuDrivenCullShader
	{
		get
		{
			if ( !gpuDrivenCullShaderLoaded )
			{
				gpuDrivenCullShader = Shaders.Load( "shaders/borealis_gpu_driven_cull_cs.shader" );
				gpuDrivenCullShaderLoaded = true;
			}

			return gpuDrivenCullShader;
		}
	}

	ShaderFile gpuDrivenCullShader;
	bool gpuDrivenCullShaderLoaded;

	/// <summary>
	/// The visibility-driven forward pass (docs/managed/borealis-gpu-driven.md): with <see cref="GpuDrivenCulling"/> and
	/// compaction, a view's GPU-driven depth prepass also writes ids, a pass marks the triangles that own a sample, and its
	/// forward pass draws only those. On by default (it only acts where <see cref="GpuDriven"/> does); off with
	/// <c>-no-gpudriven-visibility</c>, for comparisons.
	/// </summary>
	public bool GpuDrivenVisibility { get; set; } = !HasSwitch( "-no-gpudriven-visibility" );

	/// <summary>The frame's stamp for <see cref="GpuDrivenVisibility"/>: fragments the prepass drew ids for this frame carry it.</summary>
	internal uint GpuDrivenFrame { get; private set; } = 1;

	/// <summary>The id prepass material for depth buckets without a pixel shader, the mark and compaction shaders, or null when they aren't built.</summary>
	internal (BackendMaterial Ids, ShaderFile Mark, ShaderFile Compact) GpuDrivenVisibilityShaders
	{
		get
		{
			// Loaded once; read without a lock by every GPU-driven call after
			if ( Volatile.Read( ref visibilityShadersLoaded ) ) return (visibilityIds, visibilityMark, visibilityCompact);
			lock ( visibilityViews )
			{
				if ( !visibilityShadersLoaded )
				{
					if ( Shaders.Load( "shaders/borealis_gpu_driven_ids.shader" ) is not null ) visibilityIds = MaterialFromShader( "shaders/borealis_gpu_driven_ids.shader" ) as BackendMaterial;
					visibilityMark = Shaders.Load( "shaders/borealis_gpu_driven_mark_cs.shader" );
					visibilityCompact = Shaders.Load( "shaders/borealis_gpu_driven_compact_cs.shader" );
					Volatile.Write( ref visibilityShadersLoaded, true );
				}

				return (visibilityIds, visibilityMark, visibilityCompact);
			}
		}
	}

	BackendMaterial visibilityIds;
	ShaderFile visibilityMark, visibilityCompact;
	bool visibilityShadersLoaded;

	/// <summary>
	/// <c>borealis_meshlet_cull_cs</c>, which culls a prepass's meshlets, when its pass module draws (its <c>.shader</c> only
	/// declares the constants): null with <see cref="SlangPasses"/> off or the module missing, and prepasses draw whole.
	/// </summary>
	internal ShaderFile MeshletCullShader
	{
		get
		{
			if ( !SlangPasses ) return null;
			if ( Volatile.Read( ref meshletShaderLoaded ) ) return meshletShader;
			lock ( visibilityViews )
			{
				if ( !meshletShaderLoaded )
				{
					var shader = Shaders.Load( "shaders/borealis_meshlet_cull_cs.shader" );
					meshletShader = shader is not null && Passes.ModuleOf( shader ) is not null ? shader : null;
					Volatile.Write( ref meshletShaderLoaded, true );
				}

				return meshletShader;
			}
		}
	}

	ShaderFile meshletShader;
	bool meshletShaderLoaded;

	/// <summary>What meshlets have been built, for reports.</summary>
	public string MeshletReport => meshletStore is not { } s ? (Meshlets ? "meshlets: none (meshopt not found or no GPU-driven geometry)" : "meshlets: off")
		: $"meshlets {s.Config}: {s.Published:N0} ranges published of {s.Requested:N0} asked for ({s.Refused:N0} overlapping refused, {s.Pending:N0} pending), {s.Meshlets:N0} meshlets over {s.Triangles:N0} triangles, built in {s.BuildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms of worker time; pass module {(MeshletCullShader is null ? "missing" : "drawing")}";

	/// <summary>
	/// Debug only (<c>borealislab -capture-visibility</c>): GPU-driven forward passes also replay their multi-draws into this
	/// figure's targets. Set it before the first frame, so the view's ids and mask can be copied out too.
	/// </summary>
	public VisibilityFigure Figure { get; set; }

	/// <summary>
	/// The figure's ids target and mask (the last GPU-driven forward pass's view's, for <see cref="Figure"/>), the bits
	/// aggregates use from 0 and those the frame's mesh draws use from <see cref="FigureFrameBitBase"/>.
	/// </summary>
	public (TextureHandle Ids, BufferHandle Mask, long AggregateBits, long MeshBits) FigureVisibility { get; internal set; }

	/// <summary>Where the frame's mesh draws' vertex bits start in the mask.</summary>
	public const long FigureFrameBitBase = GpuDrivenTables.FrameVisibilityBitBase;

	internal BackendMaterial FigureMaterial => figureMaterial ??= Shaders.Load( "shaders/borealis_gpu_driven_figure.shader" ) is not null ? MaterialFromShader( "shaders/borealis_gpu_driven_figure.shader" ) as BackendMaterial : null;
	BackendMaterial figureMaterial;

	readonly Dictionary<(ShaderVariant StateFrom, int Mode, Format Depth), PipelineHandle> figurePipelines = new();

	/// <summary>
	/// The figure's pipeline for <paramref name="mode"/> (0 count, 1 flat triangles, 2 dark edges, 3 faint edges), culling
	/// as <paramref name="stateFrom"/> does: counts and faint edges add up with no depth test, flat triangles and dark edges
	/// test the view's depth without writing it.
	/// </summary>
	internal PipelineHandle FigurePipeline( ShaderVariant variant, ShaderVariant stateFrom, int mode, Format depth, BindingLayoutHandle[] sets )
	{
		lock ( figurePipelines )
		{
			if ( figurePipelines.TryGetValue( (stateFrom, mode, depth), out var made ) ) return made;

			Format[] colors = [mode == 0 ? Format.R16Float : Format.RGBA8Unorm];
			var info = variant.PipelineInfo( [], [], colors, depth, 1, depthBias: false, $"{variant} figure {mode}" );
			info.Sets = sets;
			info.CullMode = stateFrom.State.Cull;
			info.Wireframe = mode >= 2;
			info.DepthTest = mode is 1 or 2;
			info.DepthWrite = false;
			info.DepthCompare = CompareFunction.GreaterEqual;
			info.StencilTest = false;
			info.Blend = [mode is 0 or 3 ? BlendState.Additive : BlendState.Opaque];
			return figurePipelines[(stateFrom, mode, depth)] = Gpu.CreateGraphicsPipeline( info );
		}
	}

	/// <summary>A view's visibility resources: its ids target, triangle mask, fragment stamps and compacted indices.</summary>
	internal sealed class VisibilityView
	{
		public TextureHandle Ids, Scratch;
		public int Width, Height, Samples;
		public BufferHandle Mask, Stamps;
		public readonly BufferHandle[] Compacted = new BufferHandle[3];

		/// <summary>
		/// The prepass's kept meshlets' indices (<see cref="Meshlets"/>), made when a prepass first culls meshlets: written
		/// and drawn within a frame, after barriers on the frames before, so one is enough. Its cursor is its first word.
		/// </summary>
		public BufferHandle PrepassCompacted;

		/// <summary>
		/// A bit per meshlet (by its index among every meshlet) its prepass kept, and a stamp per visibility fragment whose
		/// bits this frame's prepass wrote: where forward's compaction scans only kept meshlets. Made with
		/// <see cref="PrepassCompacted"/>; the stamps are cleared once, by the first prepass after.
		/// </summary>
		public BufferHandle MeshletBits, MeshletStamps;
		public bool MeshletStampsCleared;
		public bool StampsCleared;

		/// <summary>The frame (<see cref="GpuDrivenFrame"/>) its mask was last cleared in, before any call drew ids into it.</summary>
		public uint ClearedFrame;

		/// <summary>Whether its buffers and targets are made, which <see cref="VisibilityFor"/> sets last, under its lock.</summary>
		public volatile bool Ready;

		/// <summary>
		/// Indices at the start of each compacted buffer that hold its cursor instead: every call compacting into the view
		/// this frame takes its triangles' range from it on the GPU, so calls recorded on different workers reserve nothing.
		/// </summary>
		public const uint CompactedCursorIndices = 4;
	}

	/// <summary>A view's visibility resources if it has any yet.</summary>
	internal VisibilityView ExistingVisibility( object view )
	{
		lock ( visibilityViews ) return visibilityViews.TryGetValue( view, out var state ) && state.Mask.IsValid ? state : null;
	}

	/// <summary>Indices each compacted index buffer holds; a fragment that doesn't fit draws whole.</summary>
	internal const int VisibilityCompactedIndices = 4 << 20;

	/// <summary>Indices a view's prepass compacted buffer holds: Construct Overview keeps ~10M at 64x124.</summary>
	internal const int PrepassCompactedIndices = 12 << 20;

	/// <summary>Meshlets a view's meshlet bits cover; one past it draws whole.</summary>
	internal const int MeshletBitCapacity = 1 << 22;

	/// <summary>
	/// A view's prepass compacted buffer (<see cref="VisibilityView.PrepassCompacted"/>), with its meshlet bits and stamps,
	/// made the first time.
	/// </summary>
	internal BufferHandle PrepassCompactedFor( VisibilityView state )
	{
		if ( state.PrepassCompacted.IsValid ) return state.PrepassCompacted;
		lock ( visibilityViews )
		{
			if ( state.PrepassCompacted.IsValid ) return state.PrepassCompacted;
			state.MeshletBits = Gpu.CreateBuffer( new BufferInfo { Size = MeshletBitCapacity / 8, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis meshlet bits" } );
			state.MeshletStamps = Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityFragments * 4L, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis meshlet stamps" } );
			var buffer = Gpu.CreateBuffer( new BufferInfo { Size = PrepassCompactedIndices * 4L, Usage = BufferUsage.Index | BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis prepass meshlet triangles" } );
			visibilityBuffers.Add( state.MeshletBits );
			visibilityBuffers.Add( state.MeshletStamps );
			visibilityBuffers.Add( buffer );
			state.PrepassCompacted = buffer;
			return buffer;
		}
	}

	readonly ConditionalWeakTable<object, VisibilityView> visibilityViews = new();
	readonly List<BufferHandle> visibilityBuffers = new();
	readonly List<TextureHandle> visibilityTextures = new();

	/// <summary>A view's visibility resources, made the first time and remade when its depth target changes size.</summary>
	internal VisibilityView VisibilityFor( object view, TextureInfo depth )
	{
		// Made already, for a depth target of this size: found without a lock, as every GPU-driven call of the view asks
		if ( visibilityViews.TryGetValue( view, out var made ) && made.Ready && (depth.Width == 0 || made.Width == depth.Width && made.Height == depth.Height && made.Samples == depth.Samples) ) return made;

		lock ( visibilityViews )
		{
			var state = visibilityViews.GetValue( view, static _ => new VisibilityView() );
			if ( !state.Mask.IsValid )
			{
				// Copied out only for the debug figure
				var figure = Figure is not null;
				state.Mask = Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityBits / 8, Usage = BufferUsage.Storage | BufferUsage.CopyDestination | (figure ? BufferUsage.CopySource : 0), Name = "Borealis visibility mask" } );
				state.Stamps = Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityFragments * 4L, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis visibility stamps" } );
				for ( int i = 0; i < state.Compacted.Length; i++ )
				{
					state.Compacted[i] = Gpu.CreateBuffer( new BufferInfo { Size = VisibilityCompactedIndices * 4L, Usage = BufferUsage.Index | BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis visibility triangles" } );
					visibilityBuffers.Add( state.Compacted[i] );
				}

				visibilityBuffers.Add( state.Mask );
				visibilityBuffers.Add( state.Stamps );
			}

			if ( depth.Width != 0 && (state.Width != depth.Width || state.Height != depth.Height || state.Samples != depth.Samples) )
			{
				foreach ( var old in (TextureHandle[])[state.Ids, state.Scratch] )
				{
					if ( !old.IsValid ) continue;
					visibilityTextures.Remove( old );
					Gpu.Destroy( old );
				}

				// Target 0 of the id prepass: the authored output, which only its alpha (to coverage) is wanted from
				state.Scratch = Gpu.CreateTexture( new TextureInfo { Width = depth.Width, Height = depth.Height, Depth = 1, Layers = 1, Mips = 1, Samples = depth.Samples, Dimension = TextureDimension.Texture2D, Format = Format.R8Unorm, Usage = TextureUsage.ColorTarget, Name = "Borealis visibility scratch" } );
				visibilityTextures.Add( state.Scratch );

				state.Ids = Gpu.CreateTexture( new TextureInfo
				{
					Width = depth.Width,
					Height = depth.Height,
					Depth = 1,
					Layers = 1,
					Mips = 1,
					Samples = depth.Samples,
					Dimension = TextureDimension.Texture2D,
					Format = Format.R32Uint,
					Usage = TextureUsage.ColorTarget | TextureUsage.Sampled | (Figure is not null ? TextureUsage.CopySource : 0),
					Name = "Borealis visibility ids",
				} );
				visibilityTextures.Add( state.Ids );
				(state.Width, state.Height, state.Samples) = (depth.Width, depth.Height, depth.Samples);
			}

			state.Ready = true;
			return state;
		}
	}

	/// <summary>Async compute isn't wired yet: contexts all record on the graphics queue.</summary>
	public override bool SupportsAsyncCompute => false;

	/// <inheritdoc/>
	public override bool SupportsDrawIndirectCount => Gpu.Capabilities.DrawIndirectCount;

	//
	// The vertex cache: skinned vertices compute skinning writes for the frame (CachedAnimatedVertex_t, 24 bytes each), which
	// vertex shaders read through the bindless table's vertex cache binding. One region per frame in flight, so a frame
	// writes while the one before is still read.
	//

	/// <summary>Bytes a cached vertex takes (<c>CachedAnimatedVertex_t</c>).</summary>
	internal const int VertexCacheStride = 24;

	readonly Lock vertexCacheLock = new();
	BufferHandle vertexCacheBuffer;
	int vertexCacheCapacity;
	ulong vertexCacheFrame = ulong.MaxValue;
	bool vertexCacheGrew;

	/// <summary>The vertex cache, or an invalid handle before anything was skinned.</summary>
	internal BufferHandle VertexCacheBuffer => vertexCacheBuffer;

	/// <summary>
	/// Room for <paramref name="vertices"/> skinned vertices in this frame's region of the vertex cache; the first one's
	/// index. A region that's too small grows the cache, after the GPU is idle, and the next frame waits for this one too,
	/// since this frame's earlier blocks may lie in the next one's region.
	/// </summary>
	public override int AllocateVertexCache( int vertices )
	{
		lock ( vertexCacheLock )
		{
			if ( Gpu.FrameNumber != vertexCacheFrame )
			{
				if ( vertexCacheGrew ) Gpu.WaitIdle();
				vertexCacheGrew = false;
				vertexCacheFrame = Gpu.FrameNumber;
				vertexCache = 0;
			}

			if ( vertexCache + vertices > vertexCacheCapacity ) GrowVertexCache( vertexCache + vertices );

			var first = Gpu.FrameSlot * vertexCacheCapacity + vertexCache;
			vertexCache += vertices;
			return first;
		}
	}

	void GrowVertexCache( int needed )
	{
		Gpu.WaitIdle();
		var capacity = Math.Max( 1 << 16, vertexCacheCapacity );
		while ( capacity < needed ) capacity *= 2;

		var buffer = Gpu.CreateBuffer( new BufferInfo { Size = (long)capacity * VertexCacheStride * Gpu.FramesInFlight, Usage = BufferUsage.Storage, Name = "Borealis vertex cache" } );
		Gpu.SetBindlessBuffer( ShaderAbi.BindlessVertexCache, buffer );
		if ( vertexCacheBuffer.IsValid ) Gpu.Destroy( vertexCacheBuffer );
		vertexCacheBuffer = buffer;
		vertexCacheCapacity = capacity;
		vertexCacheGrew = true;
	}

	/// <summary>Vertices <see cref="VertexIds"/> numbers (native's <c>VERTEX_ID_BUFFER_MAX_VERTICES</c>).</summary>
	internal const int MaxVertexIds = 131072;

	BufferHandle vertexIds;

	/// <summary>
	/// 0, 1, 2 ... as floats, one per vertex (native's <c>SCENESYSTEMVB_INSTANCE_VERTEX_ID</c>): bound at an offset, it gives
	/// a vertex-cache draw's vertices their place in the cache.
	/// </summary>
	internal BufferHandle VertexIds
	{
		get
		{
			lock ( vertexCacheLock )
			{
				if ( vertexIds.IsValid ) return vertexIds;
				var ids = new float[MaxVertexIds];
				for ( int i = 0; i < ids.Length; i++ ) ids[i] = i;
				var buffer = Gpu.CreateBuffer( new BufferInfo { Size = ids.Length * sizeof( float ), Usage = BufferUsage.Vertex, Name = "Borealis vertex ids" } );
				Gpu.WriteBuffer( buffer, System.Runtime.InteropServices.MemoryMarshal.AsBytes( ids.AsSpan() ) );
				return vertexIds = buffer;
			}
		}
	}

	/// <summary>The frame's composite morph atlas, which <see cref="BackendMorphs"/> queue meshes in.</summary>
	internal MorphAtlas Morphs => morphs ??= new MorphAtlas( this );

	MorphAtlas morphs;

	/// <inheritdoc/>
	public override Vector4 MorphSubrect( int handle ) => Morphs.Subrect( handle );

	/// <inheritdoc/>
	public override void BeginSubmitting( GpuSwapChain swapChain ) { }

	/// <inheritdoc/>
	public override void SetSubmitTag( ulong tag ) { }

	/// <inheritdoc/>
	public override float RenderTime => (float)clock.Elapsed.TotalSeconds;

	/// <inheritdoc/>
	public override IntPtr CreateConstantBuffer( int size )
	{
		var handle = (IntPtr)Interlocked.Increment( ref nextHandle );
		var buffer = Gpu.CreateBuffer( new BufferInfo { Size = size, Usage = BufferUsage.Uniform, Name = "Borealis constants" } );
		lock ( constantBuffers ) constantBuffers[handle] = buffer;
		return handle;
	}

	/// <inheritdoc/>
	public override void DestroyConstantBuffer( IntPtr buffer )
	{
		lock ( constantBuffers )
		{
			if ( constantBuffers.Remove( buffer, out var handle ) ) Gpu.Destroy( handle );
		}
	}

	/// <inheritdoc/>
	public override int SwapChainSamples( GpuSwapChain swapChain ) => 1;

	//
	// Semaphores: Borealis's binary semaphores become points on Borealis.Gpu's queue timelines
	//

	internal GpuSemaphore NewSemaphore() => new( (IntPtr)Interlocked.Increment( ref nextHandle ) );

	internal void Signalled( GpuSemaphore semaphore, SyncPoint point )
	{
		lock ( semaphores ) semaphores[semaphore.Handle] = point;
	}

	internal SyncPoint PointOf( GpuSemaphore semaphore )
	{
		lock ( semaphores )
		{
			if ( !semaphores.Remove( semaphore.Handle, out var point ) ) throw new InvalidOperationException( $"Waiting on semaphore {semaphore.Handle} before its signaller was submitted" );
			return point;
		}
	}

	//
	// Readbacks: copies to the CPU, delivered once their submission has finished
	//

	internal void AddReadback( SyncPoint point, BufferHandle buffer, int bytes, GpuReadback callback )
	{
		lock ( readbacks ) readbacks.Add( (point, buffer, bytes, callback) );
	}

	/// <summary>
	/// Deliver finished readbacks. Call once a frame; <see cref="BeginFrame"/> does.
	/// </summary>
	public void Poll()
	{
		List<(SyncPoint Point, BufferHandle Buffer, int Bytes, GpuReadback Callback)> due = null;
		lock ( readbacks )
		{
			for ( int i = 0; i < readbacks.Count; i++ )
			{
				if ( !Gpu.IsComplete( readbacks[i].Point ) ) continue;
				(due ??= new()).Add( readbacks[i] );
				readbacks.RemoveAt( i-- );
			}
		}

		if ( due is null ) return;
		foreach ( var (_, buffer, bytes, callback) in due )
		{
			callback( Gpu.Map( buffer )[..bytes] );
			Gpu.Destroy( buffer );
		}
	}

	/// <summary>
	/// Start the next frame: <see cref="Device.BeginFrame"/>, then deliver finished readbacks.
	/// </summary>
	public void BeginFrame()
	{
		Gpu.BeginFrame();
		gpuDrivenTables?.BeginFrame();
		meshletStore?.BeginFrame();
		GpuDrivenFrame++;
		loadFrame++;
		Poll();

		// Streamed textures' new images, written now, ahead of the frame's work
		if ( streamer is { } textures )
		{
			using ( Gpu.DeferUploads() )
				textures.Update();

			if ( textures.Uploaded > 0 ) Gpu.Flush();
		}
	}

	/// <summary>
	/// The Borealis.Gpu texture behind a Borealis texture made here, and the layout its last recorded use left it in.
	/// </summary>
	public static (TextureHandle Texture, ImageLayout Layout) Unwrap( GpuTexture texture ) => texture is BackendTexture t ? (t.Handle, t.Layout) : default;

	/// <summary>
	/// Note that recording outside Borealis moved a texture to <paramref name="layout"/>.
	/// </summary>
	public static void SetLayout( GpuTexture texture, ImageLayout layout )
	{
		if ( texture is BackendTexture t ) t.Layout = layout;
	}

	/// <summary>
	/// Free what this backend made. Borealis's own resources (targets, buffers) are freed by Borealis or with the device.
	/// </summary>
	public void Dispose()
	{
		Gpu.WaitIdle();
		Poll();
		materialCache?.Dispose();
		surfaces?.Dispose();
		ResidentCulls.Dispose( Gpu );
		meshletStore?.Dispose();
		gpuDrivenTables?.Dispose();
		lock ( visibilityViews )
		{
			foreach ( var buffer in visibilityBuffers ) Gpu.Destroy( buffer );
			foreach ( var texture in visibilityTextures ) Gpu.Destroy( texture );
			visibilityBuffers.Clear();
			visibilityTextures.Clear();
		}

		lock ( pipelines )
		{
			foreach ( var pipeline in pipelines.Values ) Gpu.Destroy( pipeline );
			pipelines.Clear();
		}

		Shaders.Dispose();
		Defaults.Dispose();
	}
}

/// <summary>
/// A <see cref="RenderHost"/> rendering through a <see cref="BackendDevice"/>, with async compute off: it has one queue.
/// </summary>
public class BackendRenderHost( BackendDevice device ) : RenderHost
{
	/// <inheritdoc/>
	public override GpuDevice Device => device;

	/// <inheritdoc/>
	public override bool AsyncCompute => false;

	/// <summary>
	/// The debug views drawn into the frame (none by default): the labs' <c>-debug-views</c>.
	/// </summary>
	public DebugViews Views { get; set; }

	/// <inheritdoc/>
	public override DebugViews DebugViews => Views;

	/// <summary>
	/// GPU occlusion culling (off by default): the culler's compute pass, and culled draws from its arguments and surviving
	/// instance ids.
	/// </summary>
	public bool Occlusion { get; set; }

	/// <inheritdoc/>
	public override bool OcclusionCulling => Occlusion;
}
