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
/// docs/reference/borealis-gpu.md.
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
	/// docs/reference/borealis-materials.md.
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
	/// <see cref="SurfaceMaterial"/> of theirs first draws. See docs/reference/borealis-slang-materials.md.
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

	/// <summary>Write the surface pipeline manifest beside the pipeline cache if it changed (<see cref="SurfacePipelineManifest"/>).</summary>
	internal bool SavePipelineManifest() => (surfaces?.Manifest?.Save() ?? false) | Shaders.SaveModuleManifest();

	/// <summary>What the surface pipeline manifest held and did, for reports.</summary>
	internal string PipelineManifestReport => surfaces?.Manifest is not { } m ? "surface pipeline manifest: off" : $"surface pipeline manifest: {m.Loaded} read, {m.Replayed} made ahead, {m.Dropped} dropped, {m.Recorded} recorded";

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
			if ( value && gpuDrivenTables is null ) gpuDrivenTables = new GpuDrivenTables( Gpu ) { LevelRoom = GeneratedLods && Borealis.Backend.Meshlets.MeshOptimizer.Available ? LodLevelRoom : 0 };
		}
	}

	GpuDrivenTables gpuDrivenTables;

	/// <summary>The GPU-driven pages and tables, or null when <see cref="GpuDrivenGeometry"/> is off.</summary>
	internal GpuDrivenTables GpuDrivenTables => gpuDrivenTables;

	/// <summary>
	/// Draw aggregates GPU-driven: shaders' <c>.gpudriven.shader_c</c> variants, per-draw data in tables, one multi-draw
	/// per pipeline and pass state (docs/reference/borealis-gpu-driven.md). Needs <see cref="GpuDrivenGeometry"/> and material
	/// objects; anything it can't draw goes the classic way. On by default (<see cref="GpuDrivenByDefault"/>).
	/// </summary>
	public bool GpuDriven { get; set; }

	/// <summary>The GPU-driven tables' records and bytes, for reports.</summary>
	public string GpuDrivenReport => gpuDrivenTables is not { } t ? "off" : $"{t.Records.Geometry} geometry, {t.Records.Draws} draw and {t.Records.Materials} material records ({t.Bytes.Geometry / 1024} + {t.Bytes.Draws / 1024} + {t.Bytes.Materials / 1024} KiB), {t.Bytes.Pages / (1 << 20)} MiB of pages; visibility {t.VisibilityUse.Bits / 8 / (1 << 20)} of {GpuDrivenTables.FrameVisibilityBitBase / 8 / (1 << 20)} MiB of owners' vertex bits, {t.VisibilityNoRoom} requests without room, {t.VisibilityRunsAged} runs let go unnamed"
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
	/// check per bucket and its multi-draws (docs/reference/borealis-gpu-driven.md, "Resident aggregates"). On by default;
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
	/// Make a map's static overlays resident with the meshes: a list per render order, culled on the GPU with its order kept.
	/// On by default with <see cref="GpuDrivenResidentMeshes"/> and <see cref="GpuDrivenOverlays"/>; <c>-no-resident-overlays</c>
	/// turns it off, and views then prepare them.
	/// </summary>
	public bool GpuDrivenResidentOverlays { get; set; } = !HasSwitch( "-no-resident-overlays" );

	/// <summary>
	/// Resolve what a surface stage binds from the pass once per pass state, and only what objects' attributes set for each
	/// draw, so its draws bind the same set (<see cref="Device.ReusesTransientSets"/>); <c>-no-pass-bindings</c> turns both off.
	/// </summary>
	public bool SurfacePassBindings
	{
		get => surfacePassBindings;
		set => Gpu.ReusesTransientSets = surfacePassBindings = value;
	}

	bool surfacePassBindings = true;

	internal override bool DrawsOverlaysResident => GpuDrivenResidentOverlays && DrawsMeshesResident && DrawsOverlaysGpuDriven;

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

	/// <summary>Prepass calls culled in two phases (<see cref="HiZOcclusion"/>), and those that tested against a Hi-Z.</summary>
	internal long HiZCalls, HiZTested;

	readonly List<(uint Frame, BufferHandle Buffer, long Offset)> hizCounts = new();

	/// <summary>A two-phase call's candidates' arguments, whose counts <see cref="HiZCounts"/> reads once its frame is done.</summary>
	internal void CountHiZ( BufferHandle buffer, long offset, bool tested )
	{
		System.Threading.Interlocked.Increment( ref HiZCalls );
		if ( tested ) System.Threading.Interlocked.Increment( ref HiZTested );
		lock ( hizCounts )
		{
			if ( hizCounts.Count > 0 && hizCounts[0].Frame != GpuDrivenFrame ) hizCounts.Clear();
			hizCounts.Add( (GpuDrivenFrame, buffer, offset) );
		}
	}

	/// <summary>
	/// The last GPU-driven frame's two-phase calls' candidates (fragments its prepasses left for the second phase) and how many
	/// of them the Hi-Z culled, read from the frame's regions: only once the GPU is done with it, before the next frame.
	/// </summary>
	internal (long Candidates, long Occluded) HiZCounts()
	{
		var (candidates, occluded) = (0L, 0L);
		lock ( hizCounts )
		{
			foreach ( var (frame, buffer, offset) in hizCounts )
			{
				if ( frame != GpuDrivenFrame ) continue;
				var words = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, uint>( Gpu.Map( buffer ).Slice( (int)offset, 32 ) );
				candidates += words[3];
				occluded += words[4];
			}
		}

		return (candidates, occluded);
	}

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

	internal override int ProgramsVersion => surfaces?.ReadyVersion ?? 0;

	internal override bool TakesResident( GpuMaterial material ) => material switch
	{
		SurfaceMaterial surface => !surface.RenderState.DrawsTranslucent && surface.RenderState.DepthWrite && surface.RenderState.DepthTest && Surfaces.For( surface ) is { } program
			&& program.GpuDrivenPass( false, surface.RenderState.AlphaTest, false ) is not null && program.GpuDrivenPass( true, surface.RenderState.AlphaTest, false ) is not null,
		BackendMaterial { Shader: { } shader } => Shaders.GpuDriven( shader ) is not null,
		_ => false,
	};

	internal override bool TakesOverlay( GpuMaterial material ) => material switch
	{
		SurfaceMaterial surface => Surfaces.For( surface ) is { } program && program.GpuDrivenPass( false, surface.RenderState.AlphaTest, false, surface.RenderState.DrawsTranslucent ) is not null
			&& program.GpuDrivenPass( false, surface.RenderState.AlphaTest, true, surface.RenderState.DrawsTranslucent ) is not null,
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
		return BackendMesh.Create( this, vertices, indices, null, "Sky box", pooled: false );
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
		], [0, 1, 2, 2, 1, 3], null, "Fullscreen quad", pooled: false );

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
		SurfacePassBindings = !HasSwitch( "-no-pass-bindings" );
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

	/// <summary>
	/// A progress marker before each classic draw, named for its shader variant or material, so a device-loss report names
	/// the draw the GPU stopped in (<c>-borealisgpu-drawmarkers</c>; with the device's progress markers). Off by default: it
	/// costs two marker writes a draw, for diagnosing hangs.
	/// </summary>
	internal static bool DrawMarkers { get; set; } = HasSwitch( "-borealisgpu-drawmarkers" );

	static readonly ConditionalWeakTable<object, string> drawMarkerNames = new();

	/// <summary>A draw marker's name for a shader variant or material, made once for each.</summary>
	internal static string DrawMarkerName( object what ) => drawMarkerNames.GetValue( what, static w => $"draw {(w is GpuMaterial m ? m.Name : w.ToString())}" );

	internal static bool HasSwitch( string name ) => Environment.GetCommandLineArgs().Any( x => x.Equals( name, StringComparison.OrdinalIgnoreCase ) );

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
	/// Meshlet culling in GPU-driven depth prepasses (docs/reference/borealis-gpu-driven.md, "Meshlets"): index ranges are cut
	/// into meshlets after they load, and a prepass draws only the meshlets that can reach a sample. On by default;
	/// <c>-no-meshlets</c> turns it off, <c>-meshlets 32x32</c> picks the meshlet size (64x124 by default).
	/// </summary>
	public bool Meshlets { get; set; } = !HasSwitch( "-no-meshlets" );

	/// <summary>
	/// Meshlet culling in GPU-driven shadow views too (docs/reference/borealis-gpu-driven.md, "Meshlets"): their depth-only
	/// buckets draw only the meshlets inside the view. Off by default (a meshlet pass per shadow call costs more GPU time
	/// than the triangles it saves in SS2 and Construct); <c>-shadow-meshlets</c> turns it on, with <see cref="Meshlets"/>.
	/// </summary>
	public bool ShadowMeshlets { get; set; } = HasSwitch( "-shadow-meshlets" );

	/// <summary>
	/// Two-phase Hi-Z occlusion in GPU-driven depth prepasses that draw ids (docs/reference/borealis-gpu-driven.md, "Hi-Z
	/// occlusion"): the fragments forward found visible last frame draw first, a Hi-Z is built from their depth, and the rest
	/// draw only where they pass it. Off by default (an experiment); <c>-hiz-occlusion</c> turns it on.
	/// </summary>
	public bool HiZOcclusion { get; set; } = HasSwitch( "-hiz-occlusion" );

	/// <summary>
	/// Fragments under which a prepass call draws in one phase with <see cref="HiZOcclusion"/>: a Hi-Z build and a second ids
	/// pass cost more than a call this small (the 3D skybox's) can save.
	/// </summary>
	internal int HiZMinFragments { get; set; } = 2048;

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
					meshletStore = Borealis.Backend.Meshlets.MeshOptimizer.Available ? new Borealis.Backend.Meshlets.MeshletStore( Gpu, Borealis.Backend.Meshlets.MeshletConfig.Parse( SwitchValue( "-meshlets" ) ), MeshletThreads, MeshletCacheFolder() ) { LocalData = MeshletTasks && Gpu.Capabilities.MeshShaders } : null;
					if ( meshletStore is not null ) gpuDrivenTables.Meshlets = meshletStore;
					Volatile.Write( ref meshletStoreTried, true );
				}
			}

			return meshletStore;
		}
	}

	/// <summary>
	/// Generated LODs for GPU-driven aggregates (docs/reference/borealis-gpu-driven.md, "Generated LODs"): coarser levels of
	/// each fragment's range are simplified after load, and the culls draw each fragment at the coarsest whose error stays
	/// under <see cref="LodPixels"/>. On by default; <c>-no-lodgen</c> turns it off. Set before loading models: index pages
	/// keep room for the levels (<see cref="LodLevelRoom"/>).
	/// </summary>
	public bool GeneratedLods { get; set; } = !HasSwitch( "-no-lodgen" );

	/// <summary>Room index pages keep for generated levels, as a fraction of their meshes' indices.</summary>
	public static float LodLevelRoom { get; set; } = 0.75f;

	/// <summary>The error a generated level may have, in pixels at the fragment's nearest point (texels in a texel-sized cascade); <c>-lodgen-pixels</c>.</summary>
	public float LodPixels { get; set; } = float.TryParse( SwitchValue( "-lodgen-pixels" ), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pixels ) && pixels > 0 ? pixels : 1.0f;

	/// <summary>Threads simplifying at once: a quarter of the cores, as meshlets take.</summary>
	public static int LodThreads { get; set; } = Math.Max( 1, Environment.ProcessorCount / 4 );

	Borealis.Backend.Lods.LodStore lodStore;
	bool lodStoreTried;

	/// <summary>The generated levels of GPU-driven ranges, or null when <see cref="GeneratedLods"/> is off or the library isn't there.</summary>
	internal Borealis.Backend.Lods.LodStore LodStore
	{
		get
		{
			if ( !GeneratedLods || gpuDrivenTables is null || gpuDrivenTables.LevelRoom <= 0 ) return null;
			if ( Volatile.Read( ref lodStoreTried ) ) return lodStore;
			lock ( meshletStoreLock )
			{
				if ( !lodStoreTried )
				{
					lodStore = new Borealis.Backend.Lods.LodStore( Gpu, gpuDrivenTables, Borealis.Backend.Lods.LodConfig.Default, LodThreads, CacheFolder( "-no-lod-cache", "borealislods" ) );
					gpuDrivenTables.Lods = lodStore;
					Volatile.Write( ref lodStoreTried, true );
				}
			}

			// Outside the lock: the meshlet store takes it too
			if ( lodStore is not null && lodStore.Meshlets is null ) lodStore.Meshlets = MeshletStore;
			return lodStore;
		}
	}

	/// <summary>Ranges whose meshlets or generated levels are still being built or waiting to be published.</summary>
	public int BuildsPending => (meshletStore?.Pending ?? 0) + (lodStore?.Pending ?? 0);

	/// <summary>Ranges asked for meshlets or generated levels so far: still moving while draws find their GPU-driven variants.</summary>
	public long BuildsRequested => (meshletStore?.Requested ?? 0) + (meshletStore?.Refused ?? 0) + (lodStore?.Requested ?? 0);

	/// <summary>View records written, by how they pick generated levels (<c>LodMode</c>: none, by distance, by texels, kept).</summary>
	internal readonly long[] LodModes = new long[4];

	/// <summary>
	/// Count the culls' level changes in views that keep levels (borealislab's <c>-lod-shimmer</c>): every change, those
	/// against the fragment's last change, to coarser and to finer levels, as of a few frames ago; null without generated LODs.
	/// </summary>
	public bool CountLodChanges
	{
		get => lodStore?.CountChanges ?? false;
		set
		{
			if ( LodStore is { } store ) store.CountChanges = value;
		}
	}

	/// <summary>The level changes counted so far (<see cref="CountLodChanges"/>).</summary>
	public (long Changes, long Reversals, long Coarser, long Finer)? LodChanges => lodStore?.Changes;

	/// <summary>What generated LODs have been built, for reports.</summary>
	public string LodReport
	{
		get
		{
			if ( lodStore is not { } s ) return GeneratedLods ? "generated LODs: none (meshopt not found or no GPU-driven geometry)" : "generated LODs: off";
			var (room, used) = gpuDrivenTables.LevelBytes;
			return $"generated LODs ({s.Config}, {LodPixels:0.##} px; view records by mode {string.Join( '/', LodModes )}, {s.SlicesTaken} views keeping levels): {s.Published:N0} ranges published of {s.Requested:N0} asked for ({s.Simplest:N0} simplify no further, {s.Dropped:N0} without room, {s.Pending:N0} pending, {s.WaitingForColor.Ranges:N0} of {s.WaitingForColor.Triangles:N0} triangles drawn in no GPU-driven colour pass), {s.Levels:N0} levels, {s.LevelTriangles:N0} level triangles over {s.BaseTriangles:N0}, {s.NoRoom:N0} levels without room; built in {s.BuildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms of worker time ({s.CacheHits:N0} from the cache); level indices {used / 1048576.0:0.0} MB of {room / 1048576.0:0.0} MB of room, tables {s.Bytes / 1048576.0:0.0} MB; layouts {string.Join( "; ", s.FieldsSeen.OrderByDescending( f => f.Value ).Take( 4 ).Select( f => $"{f.Value} x {f.Key}" ) )}";
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

		// In the game, on a worker: its materials draw nothing until it's done, rather than the frame waiting seconds for it
		Shaders.UpgradeInBackground = SurfaceShaders.CompileInBackgroundByDefault;
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
		// Multisampled targets aren't sampled, except depth (a frame's depth chain reads it, D_MSAA, as Source 2's targets
		// allow) and those that ask (the opaque pass's normals, which reflections read)
		if ( samples == 1 || format.IsDepth() || desc.SampledMultisampled ) usage |= TextureUsage.Sampled;
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

		// Initial pixels for mip 0: a 2D texture's, or every slice of a volume's
		var texture = new BackendTexture( this, Gpu.CreateTexture( info ), desc, name );
		if ( !desc.Data.IsEmpty && !desc.Cube )
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
	internal GpuTexture MaterialTextureFile( string path )
	{
		var texture = LoadOnce( path, streamed: StreamTextures, hold: collectingTextures is not null );
		collectingTextures?.Add( path );
		return texture;
	}

	GpuTexture LoadOnce( string path, bool streamed, bool hold = false )
	{
		lock ( loadedTextures )
		{
			// Held by the material being made for a model's draws (ModelMaterial), else pinned for good
			if ( !string.IsNullOrEmpty( path ) )
			{
				if ( !textureLives.TryGetValue( path, out var life ) ) textureLives[path] = life = new ContentLife();
				if ( hold ) life.Holders++;
				else life.Pinned = true;
			}

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
		// packages (a map's VPK); a streamed texture's tail, unless it doesn't stream. Its mips are pooled until written.
		TextureFile texture = null;
		try
		{
			if ( !readTextures.TryRemove( path, out texture ) || (!streamed && texture?.TopMip > 0) )
			{
				texture?.Release();
				texture = streamed ? TextureStreamer.ReadMips( Content, path, TextureFile.Tail ) : null;
				if ( texture is null || (texture.TopMip > 0 && !TextureStreamer.Streams( texture )) )
				{
					texture?.Release();
					texture = TextureStreamer.ReadWhole( Content, path );
					if ( texture is null ) return null;
				}
			}

			return Make( path, streamed, texture );
		}
		finally
		{
			texture?.Release();
		}
	}

	BackendTexture Make( string path, bool streamed, TextureFile texture )
	{
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

		var made = new BackendTexture( this, CreateTexture( texture, path ), desc, path ) { Layout = ImageLayout.ShaderRead, Dimension = texture.Dimension };
		if ( streamed && TextureStreamer.Streams( texture ) ) made.Stream = Streamer.Add( made, path, texture );
		else if ( streamed ) Streamer.Keep( made );
		return made;
	}

	// A texture file's image with every mip written: the one its read made ahead (ImageAheadBytes) when it's for this file
	TextureHandle CreateTexture( TextureFile texture, string path )
	{
		if ( !imagesAhead.TryRemove( path, out var ahead ) ) return texture.Create( Gpu, path );
		if ( ahead.File != texture )
		{
			Gpu.Destroy( ahead.Image );
			return texture.Create( Gpu, path );
		}

		try
		{
			for ( int mip = texture.TopMip; mip < texture.Mips; mip++ ) texture.Write( Gpu, ahead.Image, mip );
		}
		catch
		{
			Gpu.Destroy( ahead.Image );
			throw;
		}

		return ahead.Image;
	}

	/// <inheritdoc/>
	public override GpuTexture Black => black;

	/// <inheritdoc/>
	public override int MissingTextureViewIndex( bool srgb ) => black.ViewIndex( srgb, TextureViewDimension.Default );

	/// <inheritdoc/>
	public override void Clear( GpuTexture texture, Color color )
	{
		var t = (BackendTexture)texture;

		// In a DeferUploads scope (the mirror applying a frame's changes: a map's capture cubes, made at once), with the
		// scope's list rather than a list each
		var deferred = Gpu.DeferredList;
		var list = deferred ?? Gpu.BeginCommandList( QueueType.Graphics, "Clear texture" );
		list.Barrier( TextureBarrier.Transition( t.Handle, t.Layout, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.MemoryWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
		list.ClearTexture( t.Handle, new System.Numerics.Vector4( color.r, color.g, color.b, color.a ) );
		list.Barrier( TextureBarrier.Transition( t.Handle, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead ) );
		t.Layout = ImageLayout.ShaderRead;
		if ( deferred is not null ) return;

		// In order with what's submitted after it, as MakeReadable: not waited for, which held the main thread for the GPU
		if ( BatchSubmits ) Gpu.SubmitLater( list );
		else Gpu.Submit( list );
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

	/// <summary>
	/// Someone else's buffer on this device (the engine's copy of a game's buffer) as a <see cref="GpuBuffer"/> draws can
	/// bind by attribute. Disposing it leaves the buffer alone; its owner keeps it alive while it's bound.
	/// </summary>
	public GpuBuffer BorrowBuffer( BufferHandle handle, int elementCount, int elementSize, string name )
	{
		var size = Math.Max( (long)elementCount * elementSize, 16 );
#pragma warning disable CA2000 // Returned, and disposing it frees nothing
		return new GpuBuffer( new BackendBuffer( this, handle, size, name, owned: false ), Math.Max( elementCount, 1 ), Math.Max( elementSize, 1 ) );
#pragma warning restore CA2000
	}

	/// <summary>Whether <paramref name="buffer"/> is bound as <paramref name="handle"/>.</summary>
	public static bool Binds( GpuBuffer buffer, BufferHandle handle ) => buffer?.Resource is BackendBuffer backing && backing.Handle.Equals( handle );

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
	readonly Dictionary<string, System.Threading.Tasks.Task<Borealis.Source2.World.WorldFile>> lightmapReads = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Read the lightmaps of the map in <paramref name="folder"/> on a worker (its world file, each texture read and
	/// decoded), so <see cref="MapLightmapParameters"/> only makes them: a map's are large, and decoding them held up its load.
	/// </summary>
	public void ReadMapLightmapsAhead( string folder )
	{
		if ( string.IsNullOrEmpty( folder ) ) return;
		lock ( mapLightmaps )
		{
			if ( mapLightmaps.ContainsKey( folder ) || lightmapReads.ContainsKey( folder ) ) return;
			lightmapReads[folder] = System.Threading.Tasks.Task.Run( () =>
			{
				try
				{
					// The world file too, kept for where they're made: parsing it took 100 ms of the first frame after a load
					var world = Borealis.Source2.World.WorldFile.Load( Content, $"{folder}/world.vwrld" );
					System.Threading.Tasks.Parallel.ForEach( world.Lightmaps, path => ReadTextureAhead( path ) );
					return world;
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or KeyNotFoundException )
				{
					// Read where they're made, which reports it
					return null;
				}
			} );
		}
	}

	/// <summary>
	/// Whether <see cref="MapLightmapParameters"/> waits for each lightmap write, as it did, rather than deferring them into
	/// the frame's submission. For comparisons.
	/// </summary>
	internal static bool WaitForLightmapUploads { get; set; }

	/// <summary>
	/// The lightmaps of the map in <paramref name="folder"/> (<c>maps/construct_25</c>), from its world file
	/// (<see cref="LightmapParameters"/>), read once (ahead on a worker, when <see cref="ReadMapLightmapsAhead"/> was asked);
	/// null without lightmaps or a readable world.
	/// </summary>
	public ShaderParameters MapLightmapParameters( string folder )
	{
		if ( string.IsNullOrEmpty( folder ) ) return null;
		System.Threading.Tasks.Task<Borealis.Source2.World.WorldFile> reading;
		lock ( mapLightmaps ) lightmapReads.TryGetValue( folder, out reading );
		var read = reading?.GetAwaiter().GetResult();
		lock ( mapLightmaps )
		{
			if ( mapLightmaps.TryGetValue( folder, out var cached ) ) return cached;
			lightmapReads.Remove( folder );
			ShaderParameters made = null;
			try
			{
				var world = read ?? Borealis.Source2.World.WorldFile.Load( Content, $"{folder}/world.vwrld" );

				// Their mips and slices written with the frame's next submission, not each waited for
				using ( Zones.MapLightmaps.Start() )
				using ( WaitForLightmapUploads ? default : Gpu.DeferUploads() )
					made = LightmapParameters( world.Lightmaps, world.LightmapUvScale );
				mapLightmapPaths[folder] = [.. world.Lightmaps];
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or KeyNotFoundException )
			{
				made = null;
			}

			return mapLightmaps[folder] = made;
		}
	}

	// Mirrors drawing each map's lightmaps (HoldMapLightmaps)
	readonly Dictionary<string, int> mapLightmapHolders = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, string[]> mapLightmapPaths = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>A world drawing the map in <paramref name="folder"/>'s lightmaps; <see cref="ReleaseMapLightmaps"/> when it's gone.</summary>
	public void HoldMapLightmaps( string folder )
	{
		lock ( mapLightmaps ) mapLightmapHolders[folder] = mapLightmapHolders.GetValueOrDefault( folder ) + 1;
	}

	/// <summary>Map lightmap sets freed once no world drew them, for soaks and tests.</summary>
	public int MapLightmapsReleased { get; private set; }

	/// <summary>
	/// A world that drew the map in <paramref name="folder"/>'s lightmaps has gone. When it was the last, the lightmaps and the
	/// map's other textures read by path (its light probe volumes) are destroyed (once the GPU is done with them) and read again
	/// if a world draws the map again: a map's are hundreds of MiB.
	/// </summary>
	public void ReleaseMapLightmaps( string folder )
	{
		string[] paths;
		lock ( mapLightmaps )
		{
			if ( !mapLightmapHolders.TryGetValue( folder, out var holders ) ) return;
			if ( --holders > 0 )
			{
				mapLightmapHolders[folder] = holders;
				return;
			}

			mapLightmapHolders.Remove( folder );
			mapLightmaps.Remove( folder );
			if ( !mapLightmapPaths.Remove( folder, out paths ) ) return;
		}

		var textures = new List<GpuTexture>( paths.Length );
		lock ( loadedTextures )
		{
			foreach ( var path in paths )
			{
				if ( loadedTextures.TryGetValue( path, out var texture ) && loadedTextures.Remove( path ) ) textures.Add( texture );
				textureLives.Remove( path );
			}

			// The rest of the map's own textures read by path: its light probe volumes, hundreds of MiB on a big map
			var prefix = folder + "/";
			foreach ( var path in loadedTextures.Keys.Where( p => p.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ).ToList() )
			{
				if ( loadedTextures.Remove( path, out var texture ) ) textures.Add( texture );
				textureLives.Remove( path );
			}
		}

		foreach ( var texture in textures ) texture?.Dispose();

		MapLightmapsReleased++;
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
	public override GpuTexture SheetMetadata => SheetCache ?? sheetMetadata;

	/// <summary>
	/// The sheet cache sprite shaders read sheet textures' frames from (native's layout, which the engine fills), or null for
	/// none: <see cref="SheetMetadata"/> is a placeholder then.
	/// </summary>
	public GpuTexture SheetCache { get; set; }

	/// <summary>A compiled material's texture for a shader variable (<c>g_tSkyTexture</c>), or null.</summary>
	public GpuTexture MaterialTexture( string material, string variable ) => MaterialTexture( LoadMaterial( material ), variable );

	/// <summary>
	/// <see cref="MaterialTexture(string, string)"/> without pinning the material: what's drawn outside the worlds (a water
	/// body's reflection reading the sky's cubemap) borrows it, and it goes with the worlds that hold it (their sky's). One not
	/// loaded yet is loaded unheld (<see cref="UnheldMaterial"/>), so the first world to hold it and let go frees it.
	/// </summary>
	public GpuTexture LoadedMaterialTexture( string material, string variable )
	{
		GpuMaterial loaded;
		lock ( sharedMaterials ) sharedMaterials.TryGetValue( material, out loaded );
		loaded ??= UnheldMaterial( material );
		return MaterialTexture( loaded, variable ) ?? FileTexture( material, loaded, variable );
	}

	/// <summary>
	/// A texture a material drawn as a surface names (<see cref="ConvertedMaterial.File"/>), whether its surface reads it or
	/// not (a water material's caustics, which only the CPU reads): held with the material, as the textures its draws read
	/// are, and whole, as it's used outside them. Null when the material names none for the variable.
	/// </summary>
	GpuTexture FileTexture( string path, GpuMaterial material, string variable )
	{
		if ( material is not ConvertedMaterial { File: { } file } || !file.Textures.TryGetValue( variable, out var texturePath ) || string.IsNullOrEmpty( texturePath ) ) return null;
		lock ( sharedMaterials )
		{
			var life = LifeOf( path );
			life.Textures ??= new List<string>();
			var held = life.Textures.Contains( texturePath, StringComparer.OrdinalIgnoreCase );
			if ( !held ) life.Textures.Add( texturePath );
			var texture = held ? HeldTexture( texturePath ) : LoadOnce( texturePath, streamed: false, hold: true );
			if ( texture is BackendTexture { Stream: { } stream } ) stream.Pinned = true;
			return texture;
		}
	}

	/// <summary>
	/// A material loaded as <see cref="ModelMaterial"/> loads one (its textures held by it), but neither held nor pinned: it
	/// stays until something holds it (<see cref="ModelMaterial"/>) and lets it go (<see cref="ReleaseModelMaterial"/>).
	/// </summary>
	GpuMaterial UnheldMaterial( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return LoadMaterial( path );
		lock ( sharedMaterials )
		{
			var material = ModelMaterial( path );
			LifeOf( path ).Holders--;
			return material;
		}
	}

	/// <summary>A material's texture for a shader variable, or null (<see cref="MaterialTexture(string, string)"/>).</summary>
	public GpuTexture MaterialTexture( GpuMaterial material, string variable )
	{
		if ( material is not BackendMaterial m || !m.Textures.TryGetValue( variable, out var texture ) ) return null;

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
			// Asked for by something other than a model's draws: kept for good
			LifeOf( path ).Pinned = true;
			if ( sharedMaterials.TryGetValue( path, out var material ) ) return material;
			return sharedMaterials[path] = Converted( path ) ?? Material( path, null );
		}
	}

	//
	// Material and texture lifetimes: a model's draws hold their materials (ModelMaterial), and a material made for them holds
	// the textures it read by path; once a freed model's materials are held by nothing they go, and textures nothing holds
	// with them. What's asked for any other way (LoadMaterial, LoadTexture, a material made elsewhere) is pinned and stays.
	//

	sealed class ContentLife
	{
		public int Holders;
		public bool Pinned;
		public List<string> Textures;
	}

	readonly Dictionary<string, ContentLife> materialLives = new( StringComparer.OrdinalIgnoreCase );
	readonly Dictionary<string, ContentLife> textureLives = new( StringComparer.OrdinalIgnoreCase );

	// The textures a material being made for a model reads by path (MaterialTextureFile), on the thread making it
	[ThreadStatic] static List<string> collectingTextures;

	// Under sharedMaterials' lock
	ContentLife LifeOf( string path )
	{
		if ( !materialLives.TryGetValue( path, out var life ) ) materialLives[path] = life = new ContentLife();
		return life;
	}

	/// <summary>Materials and textures freed after nothing held them, for soaks and tests.</summary>
	public int MaterialsFreed { get; private set; }

	/// <inheritdoc cref="TexturesFreed"/>
	public int TexturesFreed { get; private set; }

	// Under loadedTextures' lock: asked for by something other than a model's material, so it stays
	void PinTexture( string path )
	{
		if ( !textureLives.TryGetValue( path, out var life ) ) textureLives[path] = life = new ContentLife();
		life.Pinned = true;
	}

	/// <summary>A model's draw's material (<see cref="LoadMaterial(string)"/>), held by the model until <see cref="ReleaseModelMaterial"/>.</summary>
	public GpuMaterial ModelMaterial( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return LoadMaterial( path );
		lock ( sharedMaterials )
		{
			var life = LifeOf( path );
			life.Holders++;
			if ( sharedMaterials.TryGetValue( path, out var material ) ) return material;

			// The textures it reads by path are held by it
			var outer = collectingTextures;
			collectingTextures = life.Textures ??= new List<string>();
			try
			{
				return sharedMaterials[path] = Converted( path ) ?? Material( path, null );
			}
			finally
			{
				collectingTextures = outer;
			}
		}
	}

	/// <summary>A material a holder already holds (<see cref="ModelMaterial"/>), found again without counting it twice.</summary>
	public GpuMaterial HeldMaterial( string path )
	{
		lock ( sharedMaterials )
		{
			if ( sharedMaterials.TryGetValue( path, out var material ) ) return material;
		}

		return ModelMaterial( path );
	}

	/// <summary>A texture by path (as <see cref="LoadTexture"/> loads it), held by the caller until <see cref="ReleaseTexture"/> rather than pinned.</summary>
	public GpuTexture HoldTexture( string path ) => LoadOnce( path, streamed: false, hold: true );

	/// <summary>A texture a holder already holds (<see cref="HoldTexture"/>), found again without counting it twice.</summary>
	public GpuTexture HeldTexture( string path )
	{
		lock ( loadedTextures )
		{
			if ( loadedTextures.TryGetValue( path, out var texture ) ) return texture;
		}

		return HoldTexture( path );
	}

	/// <summary>A holder of the texture at <paramref name="path"/> (<see cref="HoldTexture"/>) lets it go: held by nothing and unpinned, it's freed.</summary>
	public void ReleaseTexture( string path ) => ReleaseTextures( [path] );

	void ReleaseTextures( IEnumerable<string> paths )
	{
		List<GpuTexture> freed = null;
		lock ( loadedTextures )
		{
			foreach ( var texture in paths )
			{
				if ( !textureLives.TryGetValue( texture, out var life ) || life.Holders == 0 ) continue;
				if ( --life.Holders > 0 || life.Pinned ) continue;
				textureLives.Remove( texture );
				if ( loadedTextures.Remove( texture, out var loaded ) ) (freed ??= new()).Add( loaded );
			}
		}

		foreach ( var texture in freed ?? [] )
		{
			texture?.Dispose();
			TexturesFreed++;
		}
	}

	/// <summary>A freed model no longer draws with the material at <paramref name="path"/>: unheld and unpinned, it goes, and its textures nothing else holds.</summary>
	public void ReleaseModelMaterial( string path )
	{
		if ( string.IsNullOrEmpty( path ) ) return;
		List<string> textures;
		IStreamDependent released;
		lock ( sharedMaterials )
		{
			if ( !materialLives.TryGetValue( path, out var life ) || life.Holders == 0 ) return;
			if ( --life.Holders > 0 || life.Pinned ) return;
			materialLives.Remove( path );
			released = sharedMaterials.GetValueOrDefault( path ) as IStreamDependent;
			sharedMaterials.Remove( path );
			textures = life.Textures;
			MaterialsFreed++;
		}

		if ( textures is not null ) ReleaseTextures( textures );

		// Anything still drawing with it binds its textures again, as defaults where they've gone, rather than their
		// destroyed images through the binding sets kept for it
		released?.TexturesChanged();
	}

	/// <summary>
	/// A compiled material as its <c>.shader</c> draws it, with its shader, values and textures: <see cref="LoadMaterial(string)"/>'s,
	/// or where a surface stands in for its shader in the scene, the surface's fallback. What the engine's drawing
	/// (<see cref="ShaderDrawer.Material"/>) takes, as it draws the <c>.shader</c>. Null when it can't be read.
	/// </summary>
	internal BackendMaterial ShaderMaterial( string path )
	{
		var loaded = LoadMaterial( path );
		var material = (loaded as SurfaceMaterial)?.Fallback as BackendMaterial ?? loaded as BackendMaterial;
		return material?.Shader is null ? null : material;
	}

	/// <summary>
	/// A material whose <c>.shader</c> has a surface standing in for it (<see cref="SurfaceShaders.Convert"/>), as that
	/// surface with the material's values; null when it has none or doesn't compile.
	/// </summary>
	GpuMaterial Converted( string path )
	{
		if ( !Surfaces.HasConversions ) return null;
		if ( (readMaterials.TryGetValue( path, out var read ) ? read : Content.TryRead( path ) is { } data ? ReadMaterial( data ) : null) is not { } file ) return null;
		var converted = Converted( path, file, () =>
		{
			lock ( sharedMaterials ) return Material( path, null );
		} );
		if ( converted is not null ) readMaterials.TryRemove( path, out _ );
		return converted;
	}

	/// <summary>
	/// <paramref name="file"/> as the surface standing in for its shader, with <paramref name="fallback"/> (its <c>.shader</c>
	/// material) for draws the surface can't make; null when it has none or doesn't compile.
	/// </summary>
	ConvertedMaterial Converted( string path, MaterialFile file, Func<BackendMaterial> fallback )
	{
		var tier = Surfaces;
		if ( tier.ConversionFor( file.Shader ) is not { } surface || tier.Layout( surface ) is not { } compiled ) return null;
		if ( !ConvertedMaterial.Accepts( compiled, file ) || tier.ConvertMaterial?.Invoke( path ) == false ) return null;
		tier.CountConverted( path );
		var converted = ConvertedMaterial.Create( compiled, WithFeatureRules( file ), path, MaterialTextureFile );

		// The .shader material, for draws the surface can't make
		converted.SetFallback( fallback );

		// Its scene attributes (overlay, NoZPrepass, ...) are the .shader material's: that material without textures
		converted.SetAttributes( () => new BackendMaterial( path, Shaders.Load( file.Shader ), file, null ) );
		return converted;
	}

	/// <summary>
	/// <paramref name="file"/> with its feature values (<c>F_*</c>) through its shader's feature rules, as native applies them
	/// and the <c>.shader</c> material reads them (<see cref="BackendMaterial.FeatureValues"/>): a surface standing in for the shader
	/// takes the same features. The file itself when the rules change nothing.
	/// </summary>
	MaterialFile WithFeatureRules( MaterialFile file )
	{
		if ( Shaders.Load( file.Shader ) is not { Features.StaticRules.Length: > 0 } shader ) return file;
		var features = BackendMaterial.FeatureValues( shader, file );
		Dictionary<string, int> ints = null;
		foreach ( var (name, value) in file.Ints )
		{
			var index = shader.FeatureIndex( name );
			if ( index < 0 || features[index] == value ) continue;
			ints ??= new Dictionary<string, int>( file.Ints, StringComparer.OrdinalIgnoreCase );
			ints[name] = features[index];
		}

		return ints is null ? file : new MaterialFile { Name = file.Name, Shader = file.Shader, Ints = ints, Floats = file.Floats, Vectors = file.Vectors, Textures = file.Textures, IntAttributes = file.IntAttributes, ShaderVersion = file.ShaderVersion };
	}

	/// <summary>
	/// A material from a material file in hand rather than in the content (one made in memory), as <see cref="LoadMaterial(string)"/>
	/// loads one: as the surface standing in for its shader when it has one. Not shared.
	/// </summary>
	internal GpuMaterial LoadMaterial( string name, MaterialFile file )
	{
		BackendMaterial Classic() => Track( new BackendMaterial( name, Shaders.Load( file.Shader ), file, Textures( file ) ) );
#pragma warning disable CA2000 // Returned: the caller owns it
		if ( Surfaces.HasConversions && Converted( name, file, Classic ) is { } converted ) return converted;
#pragma warning restore CA2000
		return Classic();
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

	/// <summary>A material of <paramref name="shader"/>'s defaults, values set on it later: one made in code from a shader.</summary>
	public GpuMaterial CreateMaterial( string name, ShaderFile shader ) => Track( new BackendMaterial( name, shader, null, null ) );

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

	/// <summary>
	/// A compiled material with <paramref name="values"/> and <paramref name="textures"/> (shader variables by name, such as
	/// <c>g_tColor</c>) over its own: a material a game copied and changed in code. Textures that aren't this device's are
	/// left as the file has them.
	/// </summary>
	public GpuMaterial LoadMaterial( string path, IReadOnlyDictionary<string, float[]> values, IReadOnlyDictionary<string, GpuTexture> textures, string name = null )
	{
		var material = Material( path, values is null ? null : new( values, StringComparer.OrdinalIgnoreCase ), name );
		SetOn( material, null, textures );
		return material;
	}

	/// <summary>
	/// Set <paramref name="values"/> and <paramref name="textures"/> on a material made by
	/// <see cref="LoadMaterial(string, IReadOnlyDictionary{string, float[]}, IReadOnlyDictionary{string, GpuTexture}, string)"/>,
	/// as code changed them: what draws with it draws with them from its next draw.
	/// </summary>
	public void SetOnMaterial( GpuMaterial material, IReadOnlyDictionary<string, float[]> values, IReadOnlyDictionary<string, GpuTexture> textures )
	{
		if ( material is BackendMaterial own ) SetOn( own, values, textures );
	}

	static void SetOn( BackendMaterial material, IReadOnlyDictionary<string, float[]> values, IReadOnlyDictionary<string, GpuTexture> textures )
	{
		foreach ( var (variable, value) in values ?? new Dictionary<string, float[]>() ) material.Set( variable, value );
		foreach ( var (variable, texture) in textures ?? new Dictionary<string, GpuTexture>() )
		{
			if ( texture is BackendTexture own ) material.SetTexture( variable, own );
		}
	}

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
		var file = readMaterials.TryRemove( path, out var read ) ? read : Content.TryRead( path ) is { } data ? ReadMaterial( data ) : MissingMaterial( path );
		if ( file is null ) return Track( new BackendMaterial( name ?? path, null, null, null, values ) );
		return Track( new BackendMaterial( name ?? path, Shaders.Load( file.Shader ), file, Textures( file ), values ) );
	}

	/// <summary>What native draws with a material that isn't there.</summary>
	public const string ErrorMaterial = "materials/error.vmat";

	/// <summary>
	/// A material named but not there draws as the error material, as native's resource system binds it; one that's there
	/// but can't be read, or no name, still draws nothing.
	/// </summary>
	MaterialFile MissingMaterial( string path )
	{
		if ( string.IsNullOrEmpty( path ) || path.TrimEnd( '_', 'c' ).Equals( ErrorMaterial, StringComparison.OrdinalIgnoreCase ) ) return null;
		return Content.TryRead( ErrorMaterial ) is { } data ? ReadMaterial( data ) : null;
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
	/// the default stream from 4. With <paramref name="normals"/>, the opaque pass's normals target follows colour, unwritten
	/// (<see cref="AddNormalsTarget"/>). In a tools pass (<paramref name="tools"/>) a colour pipeline draws with the engine's
	/// pixel stage (<see cref="ToolsViewStage"/>).
	/// </summary>
	internal PipelineHandle Pipeline( ShaderVariant variant, VertexLayout layout, Format color, Format depth, int samples, bool depthOnly, BindingLayoutHandle[] sets = null, bool materialPerDraw = false, bool ids = false, ShaderVariant stateFrom = null, bool normals = false, ToolsPass tools = ToolsPass.None )
	{
		normals &= color != Format.Unknown && !ids;
		if ( ids || color == Format.Unknown ) tools = ToolsPass.None;
		var key = new PipelineKey( variant, layout, ids ? IdsColorKey : color, depth, samples, depthOnly, sets, materialPerDraw, stateFrom, normals, tools );
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
				if ( field.Format.IsInteger() != (input.Kind != SpirvScalar.Float) ) return null;
				return new VertexAttribute( 0, 0, field.Format, field.Offset );
			}, defaultBuffer: vertexCache ? 4 : 2 );

			VertexBufferLayout[] streams = vertexCache
				? [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 4 ), new( 4 ), new( 0 )]
				: [new( layout.Stride ), new( 4, VertexStepRate.Instance ), new( 0 )];
			// The visibility-driven forward pass's id prepass: target 0 takes the authored output (its alpha drives alpha to
			// coverage) and goes nowhere, target 1 the ids (a GPU-driven pixel shader's id output)
			Format[] colors = ids ? [Format.R8Unorm, Format.R32Uint] : color == Format.Unknown ? [] : [color];
			var name = $"{variant}{(depthOnly ? " depth" : ids ? " ids" : "")}";
			var depthBias = depthOnly || color == Format.Unknown || ids;

			// A module with task and mesh stages draws with them, and no vertex input
			var info = variant.Mesh is not null && Gpu.Capabilities.MeshShaders ? variant.MeshPipelineInfo( colors, depth, samples, depthBias, name ).Value : variant.PipelineInfo( streams, attributes, colors, depth, samples, depthBias, name );
			if ( ids ) info.Blend = [BlendState.Opaque, BlendState.Opaque];
			if ( normals ) AddNormalsTarget( ref info, !depthOnly && WritesNormals( variant ) );
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

			if ( tools != ToolsPass.None ) ToolsViewStage.Apply( Shaders, ref info, tools, ToolsViews.ShaderColor( variant.Shader?.Name ) );

			// Stripped pixel shaders: depth passes of opaque materials don't need them (alpha-tested ones keep theirs)
			if ( depthOnly && color == Format.Unknown )
			{
				info.FragmentShader = default;
				info.FragmentEntry = null;
				info.FragmentConstants = null;
			}

			FitOutputs( ref info, variant.Pixel );

			// A draw never waits for it: skipped until workers have made it
			info.SkipUntilReady = true;
			using ( Zones.PipelineCreate.Start() ) pipeline = Gpu.CreateGraphicsPipeline( info );
			pipelines[key] = pipeline;
			if ( DumpPixelShaders is { } dump && info.FragmentShader.IsValid ) DumpPixelShader( dump, variant, info.FragmentConstants, materialPerDraw );
			return pipeline;
		}
	}

	/// <summary>
	/// Leave out of a pipeline's pixel stage the outputs its colour targets don't take: at a location with no target, or one of
	/// the other numeric type (integer or float). A variant's GPU-driven form writes its visibility id, a uint, at target 1
	/// for the ids prepass, where target 1 is the ids; elsewhere target 1 is the opaque pass's normals (RGB10A2) or there's
	/// none. Location 0 stays: its alpha is what alpha to coverage reads, with a target there or not.
	/// </summary>
	void FitOutputs( ref GraphicsPipelineInfo info, ShaderStage pixel )
	{
		if ( !info.FragmentShader.IsValid || pixel is null || info.FragmentShader != pixel.Module || pixel.Reflection?.Outputs is not { Count: > 0 } outputs ) return;

		var colors = info.ColorFormats ?? [];
		var unused = 0u;
		foreach ( var output in outputs )
		{
			if ( output.Location is < 1 or >= 32 ) continue;
			var fits = output.Location < colors.Length && colors[output.Location] != Format.Unknown && colors[output.Location].IsInteger() == (output.Kind != SpirvScalar.Float);
			if ( !fits ) unused |= 1u << output.Location;
		}

		if ( unused != 0 ) info.FragmentShader = Shaders.ModuleWithoutOutputs( pixel, unused );
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

	internal readonly record struct PipelineKey( ShaderVariant Variant, VertexLayout Layout, Format Color, Format Depth, int Samples, bool DepthOnly, BindingLayoutHandle[] Sets = null, bool MaterialPerDraw = false, ShaderVariant StateFrom = null, bool Normals = false, ToolsPass Tools = ToolsPass.None );

	/// <summary>
	/// <c>kForwardNormalsOutput</c> in sbox/forward_normals.slangh: a pixel stage that has it writes the opaque pass's normals
	/// and roughness at target 1 while it's set.
	/// </summary>
	internal const uint NormalsOutputId = 121;

	/// <summary>
	/// Add the opaque pass's normals target (<see cref="NormalsFormat"/>) after a pipeline's colour targets: written with
	/// <paramref name="writes"/> (an opaque forward pass whose pixel stage has <see cref="NormalsOutputId"/>, which this sets),
	/// left as it is otherwise. Each colour target keeps its blend.
	/// </summary>
	internal static void AddNormalsTarget( ref GraphicsPipelineInfo info, bool writes )
	{
		var colors = info.ColorFormats ?? [];
		var blend = new BlendState[colors.Length + 1];
		for ( int i = 0; i < colors.Length; i++ ) blend[i] = info.Blend is { Length: > 0 } b ? b[Math.Min( i, b.Length - 1 )] : BlendState.Opaque;
		blend[^1] = writes ? BlendState.Opaque : BlendState.NoWrites;
		info.ColorFormats = [.. colors, NormalsFormat];
		info.Blend = blend;
		if ( writes ) info.FragmentConstants = [.. (info.FragmentConstants ?? []).Where( c => c.Id != NormalsOutputId ), new SpecializationConstant( NormalsOutputId, 1 )];
	}

	/// <summary>
	/// Whether a variant's pixel stage writes the opaque pass's normals: a Slang pass module's that includes
	/// sbox/forward_normals.slangh (water), whose SPIR-V has <see cref="NormalsOutputId"/>. Compiled programs have none.
	/// </summary>
	static bool WritesNormals( ShaderVariant variant ) => variant.Pixel?.Reflection?.SpecializationIds?.Contains( NormalsOutputId ) == true;

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
	/// Surfaces' opaque forward passes write their normal and roughness into the pass's second target when it has one
	/// (<see cref="NormalsFormat"/>); every other pipeline drawn there leaves it as it is.
	/// </summary>
	internal override bool WritesForwardNormals => true;

	/// <summary>The opaque pass's normals target (<see cref="ScreenSpaceReflectionsLayer.NormalsFormat"/>).</summary>
	internal const Format NormalsFormat = Format.RGB10A2Unorm;

	/// <summary>
	/// Draw a map's static overlays GPU-driven (on by default; <c>-no-gpudriven-overlays</c> turns it off): those of one
	/// render order as one call, so draws of a pipeline go together whatever their material, which reorders overlays within
	/// a render order where they overlap.
	/// </summary>
	public bool GpuDrivenOverlays { get; set; } = !HasSwitch( "-no-gpudriven-overlays" );

	internal override bool DrawsOverlaysGpuDriven => GpuDrivenOverlays && DrawsAggregatesGpuDriven;

	/// <summary>
	/// Static meshes' opaque draws join the aggregates' GPU-driven call (docs/reference/borealis-gpu-driven.md). On by default:
	/// on Construct it saves GPU time but costs CPU time, mostly on worker threads, where the one GPU-driven run records what
	/// workers drew between them; <c>-no-gpudriven-meshes</c> turns it off.
	/// </summary>
	public bool GpuDrivenMeshes { get; set; } = !HasSwitch( "-no-gpudriven-meshes" );

	/// <summary>
	/// Skinned meshes' opaque draws join the static meshes' GPU-driven call (docs/reference/borealis-gpu-driven.md, "Skinned
	/// meshes"): those from the vertex cache through a geometry record that pulls their vertex ids, the rest skinned in the
	/// vertex shader from their bones. On by default with <see cref="GpuDrivenMeshes"/>; <c>-no-gpudriven-skinned</c> turns
	/// it off.
	/// </summary>
	public bool GpuDrivenSkinned { get; set; } = !HasSwitch( "-no-gpudriven-skinned" );

	internal override bool DrawsSkinnedGpuDriven => GpuDrivenSkinned && DrawsMeshesGpuDriven;

	/// <summary>
	/// Fading objects' draws (the faded list, dithered opaque under <c>D_OPAQUE_FADE</c>) draw GPU-driven too, a call of mesh-draw
	/// batches per list range with no ids or visibility lists, as the opaque list's static and skinned draws are taken. On by
	/// default with <see cref="GpuDrivenMeshes"/>; <c>-no-gpudriven-faded</c> turns it off.
	/// </summary>
	public bool GpuDrivenFaded { get; set; } = !HasSwitch( "-no-gpudriven-faded" );

	internal override bool DrawsFadedGpuDriven => GpuDrivenFaded && DrawsMeshesGpuDriven;

	/// <summary>
	/// The translucent list's draws draw GPU-driven too, as the faded list's, but in colour layers in their sorted order: a
	/// stretch of the list the path can take is one call that keeps its batches' order across pipelines
	/// (<see cref="GpuContext.GpuDrivenInOrder"/>), so only neighbours in the sort share a multi-draw. On by default with
	/// <see cref="GpuDrivenMeshes"/>; <c>-no-gpudriven-translucent</c> turns it off.
	/// </summary>
	public bool GpuDrivenTranslucent { get; set; } = !HasSwitch( "-no-gpudriven-translucent" );

	internal override bool DrawsTranslucentGpuDriven => GpuDrivenTranslucent && DrawsMeshesGpuDriven;

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
	/// The visibility-driven forward pass (docs/reference/borealis-gpu-driven.md): with <see cref="GpuDrivenCulling"/> and
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

	/// <summary>
	/// <c>borealis_hiz_cs</c>, which builds a prepass's Hi-Z and culls its second phase, when its pass module draws: null with
	/// <see cref="SlangPasses"/> off or the module missing, and prepasses draw in one phase.
	/// </summary>
	internal ShaderFile HiZShader
	{
		get
		{
			if ( !SlangPasses ) return null;
			if ( Volatile.Read( ref hizShaderLoaded ) ) return hizShader;
			lock ( visibilityViews )
			{
				if ( !hizShaderLoaded )
				{
					var shader = Shaders.Load( "shaders/borealis_hiz_cs.shader" );
					hizShader = shader is not null && Passes.ModuleOf( shader ) is not null ? shader : null;
					Volatile.Write( ref hizShaderLoaded, true );
				}

				return hizShader;
			}
		}
	}

	ShaderFile hizShader;
	bool hizShaderLoaded;

	/// <summary>
	/// Whether prepass meshlets are culled and drawn with task and mesh shaders (<c>borealis_meshlet_ids</c>) where the
	/// device has them, in place of the compute pass, its index copies and indexed draws. Off by default: it costs more GPU
	/// time than the compute pass on the Steam Deck and on the PC (docs/reference/borealis-gpu-driven.md, "Meshlets");
	/// <c>-mesh-shaders</c> turns it on. Set it before drawing.
	/// </summary>
	public bool MeshletTasks { get; set; } = HasSwitch( "-mesh-shaders" );

	/// <summary>
	/// <c>borealis_meshlet_ids</c>, which culls and draws a prepass's meshlets with task and mesh shaders, when
	/// <see cref="MeshletTasks"/> is on, the device has mesh shaders, its pass module draws with both stages and meshlets are
	/// no bigger than its mesh stage takes (64 vertices, 128 triangles); null otherwise, and the compute pass culls them.
	/// </summary>
	internal BackendMaterial MeshletIds
	{
		get
		{
			if ( !MeshletTasks || !Gpu.Capabilities.MeshShaders || MeshletCullShader is null || MeshletStore is not { } store ) return null;
			if ( store.Config.MaxVertices > 64 || store.Config.MaxTriangles > 128 ) return null;
			if ( Volatile.Read( ref meshletIdsLoaded ) ) return meshletIds;
			lock ( visibilityViews )
			{
				if ( !meshletIdsLoaded )
				{
					const string path = "shaders/borealis_meshlet_ids.shader";
					if ( Shaders.Load( path ) is { } shader && Passes.ModuleOf( shader ) is not null && MaterialFromShader( path ) is BackendMaterial material
						&& Shaders.Variant( shader, "Default", [] ) is { Mesh: not null, Task: not null } )
						meshletIds = material;
					Volatile.Write( ref meshletIdsLoaded, true );
				}

				return meshletIds;
			}
		}
	}

	BackendMaterial meshletIds;
	bool meshletIdsLoaded;

	/// <summary>
	/// Where built meshlets are kept between runs: <c>game/.source2/borealismeshlets</c> beside the engine's other caches;
	/// none with <c>-no-meshlet-cache</c> or no engine content.
	/// </summary>
	string MeshletCacheFolder() => CacheFolder( "-no-meshlet-cache", "borealismeshlets" );

	/// <summary>A cache folder in <c>game/.source2</c> beside the engine's other caches; none with <paramref name="off"/> or no engine content.</summary>
	string CacheFolder( string off, string name )
	{
		if ( HasSwitch( off ) ) return null;
		var core = Content.Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) );
		return core is null ? null : Path.Combine( Path.GetDirectoryName( core ), ".source2", name );
	}

	/// <summary>What meshlets have been built, for reports.</summary>
	public string MeshletReport => meshletStore is not { } s ? (Meshlets ? "meshlets: none (meshopt not found or no GPU-driven geometry)" : "meshlets: off")
		: $"meshlets {s.Config}: {s.Published:N0} ranges published of {s.Requested:N0} asked for ({s.Refused:N0} overlapping refused, {s.Pending:N0} pending), {s.Meshlets:N0} meshlets over {s.Triangles:N0} triangles ({s.Triangles16:N0} with 16-bit indices), built in {s.BuildTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0} ms of worker time ({s.CacheHits:N0} from the cache); pass module {(MeshletCullShader is null ? "missing" : "drawing")}; task and mesh shaders {(MeshletIds is null ? "off" : "on")}";

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
		/// <summary>Where it goes once its view has gone (its finalizer): the device frees its resources at the next frame.</summary>
		public System.Collections.Concurrent.ConcurrentQueue<VisibilityView> Retire;

		~VisibilityView() => Retire?.Enqueue( this );

		/// <summary>Every buffer it made.</summary>
		public IEnumerable<BufferHandle> Buffers() => [Mask, Stamps, .. Compacted, PrepassCompacted, PrepassCompacted16, MeshletStamps, MeshletJobs, .. HiZVisible, HiZ];

		public TextureHandle Ids, Scratch;
		public int Width, Height, Samples;
		public BufferHandle Mask, Stamps;
		public readonly BufferHandle[] Compacted = new BufferHandle[3];

		/// <summary>
		/// The prepass's kept meshlets' indices (<see cref="Meshlets"/>), made when a prepass first culls meshlets: written
		/// and drawn within a frame, after barriers on the frames before, so one is enough. Its cursor is its first word.
		/// </summary>
		public BufferHandle PrepassCompacted;

		/// <summary>The same for ranges with 16-bit indices, two to a word.</summary>
		public BufferHandle PrepassCompacted16;

		/// <summary>
		/// Per visibility fragment a 16-byte stamp (frame, where its kept triangles start in <see cref="PrepassCompacted"/>,
		/// how many): where forward's compaction scans only those. Made with <see cref="PrepassCompacted"/>; cleared once, by
		/// the first prepass after.
		/// </summary>
		public BufferHandle MeshletStamps;

		/// <summary><see cref="MeshletStamps"/> as an attribute's value, which <c>borealis_meshlet_ids</c>'s material object binds.</summary>
		public GpuBuffer MeshletStampsAttribute;

		/// <summary>Whether <see cref="MeshletStamps"/> has room for task shaders' kept triangles after the stamps (<see cref="MeshletKeptAt"/>).</summary>
		public bool MeshletKept;

		/// <summary>The meshlet pass's copy jobs: their count (also the copy dispatch's group count), 1, 1, then a job per kept meshlet.</summary>
		public BufferHandle MeshletJobs;
		public volatile bool MeshletStampsCleared;
		public bool StampsCleared;

		/// <summary>The frame (<see cref="GpuDrivenFrame"/>) its mask was last cleared in, before any call drew ids into it.</summary>
		public uint ClearedFrame;

		/// <summary>Whether its buffers and targets are made, which <see cref="VisibilityFor"/> sets last, under its lock.</summary>
		public volatile bool Ready;

		/// <summary>
		/// Indices each of <see cref="Compacted"/> holds, cursor included: room for a triangle per sample of the view's depth
		/// target (only triangles a sample shows are compacted), at most <see cref="VisibilityCompactedIndices"/>.
		/// </summary>
		public int CompactedCapacity;

		/// <summary>
		/// Indices <see cref="PrepassCompacted"/> holds and kept triangles <see cref="MeshletStamps"/> has room for: less for
		/// small views (cube capture faces); a fragment that doesn't fit draws whole.
		/// </summary>
		public int PrepassCapacity;
		public uint KeptCapacity;

		/// <summary>
		/// Two-phase Hi-Z occlusion (<see cref="HiZOcclusion"/>): a bit per visibility fragment, set by forward's compaction
		/// where a fragment has a triangle that owns a sample, in <see cref="HiZVisible"/>[<see cref="HiZCurrent"/>]; the
		/// prepass reads the other, last frame's. Made by <see cref="HiZBuffersFor"/>.
		/// </summary>
		public readonly BufferHandle[] HiZVisible = new BufferHandle[2];
		public int HiZCurrent;

		/// <summary>Whether forward wrote this frame's bits (the next frame reads them), and whether the bits read this frame are a frame's.</summary>
		public bool HiZWrote, HiZReadValid;

		/// <summary>
		/// The Hi-Z: the farthest depth (reverse-Z: least) of each 4x4 pixels' samples at level 0, then each level half the last
		/// (rounded up) of the farthest of 2x2, down to 2 texels a side, one after another as floats.
		/// </summary>
		public BufferHandle HiZ;
		public int HiZWidth, HiZHeight, HiZLevels;

		/// <summary>The frame and command list that built <see cref="HiZ"/> last: later calls of the view in the list use it.</summary>
		public uint HiZBuiltFrame;
		public object HiZBuiltBy;

		/// <summary>Every texture it made.</summary>
		public IEnumerable<TextureHandle> Textures() => [Ids, Scratch];

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

	/// <summary>Kept meshlets a prepass call's copy jobs hold (Construct Overview keeps ~62k); past it, fragments draw whole.</summary>
	internal const int MeshletJobCapacity = 1 << 18;

	/// <summary>Indices a view's prepass compacted buffer holds: Construct Overview keeps ~10M at 64x124.</summary>
	internal const int PrepassCompactedIndices = 12 << 20;

	/// <summary>
	/// Where a view's kept triangles start in its meshlet stamps (after a stamp per visibility fragment): a cursor, then
	/// <see cref="MeshletKeptCapacity"/> words, the number of each triangle task and mesh shaders kept among its draw's, for
	/// forward's compaction.
	/// </summary>
	internal const uint MeshletKeptAt = GpuDrivenTables.MaxVisibilityFragments * 16;

	/// <summary>Kept triangles a view's prepass writes in a frame; a fragment without room is scanned whole.</summary>
	internal const uint MeshletKeptCapacity = 6 << 20;

	/// <summary>Samples (pixels times samples) under which a view is small (a cube capture face) and gets smaller prepass buffers.</summary>
	internal const long SmallViewSamples = 1 << 20;

	/// <summary>Prepass indices and kept triangles a small view has room for.</summary>
	internal const int SmallPrepassIndices = 2 << 20;
	internal const uint SmallKeptCapacity = 1 << 20;

	/// <summary>
	/// What shadow views' meshlet passes draw from (<see cref="ShadowMeshlets"/>): kept meshlets' indices, 32-bit and 16-bit,
	/// the copy jobs and a stamps buffer no shadow call writes (its fragments have no visibility). Shared by every shadow call:
	/// each sets the cursors back before its meshlet pass, after the calls before it drew, so a call has all the room.
	/// </summary>
	internal sealed class ShadowMeshletBuffers
	{
		public BufferHandle Compacted, Compacted16, Jobs, Stamps;
		public int Capacity;
	}

	/// <summary>Indices a shadow call's kept meshlets have room for, in each index size; a fragment past it draws whole.</summary>
	internal const int ShadowMeshletIndices = 4 << 20;

	ShadowMeshletBuffers shadowMeshletBuffers;

	/// <summary>The shadow views' meshlet buffers (<see cref="ShadowMeshletBuffers"/>), made the first time.</summary>
	internal ShadowMeshletBuffers ShadowMeshletBuffersNow()
	{
		if ( Volatile.Read( ref shadowMeshletBuffers ) is { } known ) return known;
		using var zone = Zones.VisibilityMake.Start();

		// Made without the lock, as a view's prepass buffers are, and kept unless another thread made them meanwhile
		var made = new ShadowMeshletBuffers
		{
			Capacity = ShadowMeshletIndices,
			Compacted = Gpu.CreateBuffer( new BufferInfo { Size = ShadowMeshletIndices * 4L, Usage = BufferUsage.Index | BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis shadow meshlet triangles" } ),
			Compacted16 = Gpu.CreateBuffer( new BufferInfo { Size = ShadowMeshletIndices * 2L, Usage = BufferUsage.Index | BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis shadow meshlet triangles 16" } ),
			Jobs = Gpu.CreateBuffer( new BufferInfo { Size = 16 + MeshletJobCapacity * 16L, Usage = BufferUsage.Storage | BufferUsage.Indirect | BufferUsage.CopyDestination, Name = "Borealis shadow meshlet copy jobs" } ),
			Stamps = Gpu.CreateBuffer( new BufferInfo { Size = 16, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis shadow meshlet stamps" } ),
		};

		lock ( visibilityViews )
		{
			if ( shadowMeshletBuffers is { } other )
			{
				foreach ( var buffer in (BufferHandle[])[made.Compacted, made.Compacted16, made.Jobs, made.Stamps] ) Gpu.Destroy( buffer );
				return other;
			}

			Volatile.Write( ref shadowMeshletBuffers, made );
			return made;
		}
	}

	/// <summary>Indices a view's compacted buffers need for its depth target (<see cref="VisibilityView.CompactedCapacity"/>).</summary>
	internal static int CompactedIndicesFor( in TextureInfo depth )
	{
		if ( depth.Width == 0 ) return VisibilityCompactedIndices;
		var samples = (long)depth.Width * depth.Height * Math.Max( depth.Samples, 1 );
		return (int)Math.Clamp( 3 * samples + VisibilityView.CompactedCursorIndices, 1 << 16, VisibilityCompactedIndices );
	}

	/// <summary>GPU-driven frames a view's visibility resources stay made without being used; then they're freed.</summary>
	internal const uint IdleVisibilityFrames = 600;

	/// <summary>Hi-Z level 0's size for a depth target this size: a texel per 4x4 pixels.</summary>
	internal static (int Width, int Height, int Levels, int Texels) HiZLayout( int width, int height )
	{
		var (w, h) = ((width + 3) / 4, (height + 3) / 4);
		var (levels, texels) = (1, w * h);
		for ( var (x, y) = (w, h); (x > 2 || y > 2) && levels < 16; levels++ )
		{
			(x, y) = ((x + 1) / 2, (y + 1) / 2);
			texels += x * y;
		}

		return (w, h, levels, texels);
	}

	/// <summary>
	/// A view's Hi-Z buffers (<see cref="VisibilityView.HiZVisible"/>, <see cref="VisibilityView.HiZ"/>), made the first time
	/// and the Hi-Z again when the view's size changed. On the frame's context, before its calls (<c>BeginGpuDrivenView</c>),
	/// which clears them. Whether they're new.
	/// </summary>
	internal bool HiZBuffersFor( VisibilityView state )
	{
		var layout = HiZLayout( state.Width, state.Height );
		if ( state.HiZVisible[0].IsValid && state.HiZ.IsValid && state.HiZWidth == layout.Width && state.HiZHeight == layout.Height ) return false;
		lock ( visibilityViews )
		{
			var made = false;
			for ( int i = 0; i < state.HiZVisible.Length; i++ )
			{
				if ( state.HiZVisible[i].IsValid ) continue;
				state.HiZVisible[i] = Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityFragments / 8, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis Hi-Z visible fragments" } );
				visibilityBuffers.Add( state.HiZVisible[i] );
				made = true;
			}

			if ( state.HiZ.IsValid && visibilityBuffers.Remove( state.HiZ ) ) Gpu.Destroy( state.HiZ );
			state.HiZ = Gpu.CreateBuffer( new BufferInfo { Size = Math.Max( layout.Texels, 1 ) * 4L, Usage = BufferUsage.Storage, Name = "Borealis Hi-Z" } );
			visibilityBuffers.Add( state.HiZ );
			(state.HiZWidth, state.HiZHeight, state.HiZLevels) = (layout.Width, layout.Height, layout.Levels);
			return made;
		}
	}

	/// <summary>
	/// A view's prepass compacted buffer (<see cref="VisibilityView.PrepassCompacted"/>), with its meshlet stamps, made the
	/// first time.
	/// </summary>
	internal BufferHandle PrepassCompactedFor( VisibilityView state )
	{
		if ( state.PrepassCompacted.IsValid ) return state.PrepassCompacted;
		using var zone = Zones.VisibilityMake.Start();

		// Made without the lock (the driver's allocations take milliseconds, and other views' calls take the lock), then
		// kept unless another thread made them meanwhile. Room for task shaders' kept triangles only where they draw; less
		// for small views
		var small = state.Width > 0 && (long)state.Width * state.Height * Math.Max( state.Samples, 1 ) < SmallViewSamples;
		var prepassCapacity = small ? SmallPrepassIndices : PrepassCompactedIndices;
		var keptCapacity = small ? SmallKeptCapacity : MeshletKeptCapacity;
		var meshletKept = MeshletIds is not null;
		var stampBytes = meshletKept ? MeshletKeptAt + 4 + keptCapacity * 4L : MeshletKeptAt;
		var stamps = Gpu.CreateBuffer( new BufferInfo { Size = stampBytes, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis meshlet stamps" } );
		var jobs = Gpu.CreateBuffer( new BufferInfo { Size = 16 + MeshletJobCapacity * 16L, Usage = BufferUsage.Storage | BufferUsage.Indirect | BufferUsage.CopyDestination, Name = "Borealis meshlet copy jobs" } );
		var compacted16 = Gpu.CreateBuffer( new BufferInfo { Size = prepassCapacity * 2L, Usage = BufferUsage.Index | BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis prepass meshlet triangles 16" } );
		var buffer = Gpu.CreateBuffer( new BufferInfo { Size = prepassCapacity * 4L, Usage = BufferUsage.Index | BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis prepass meshlet triangles" } );

		lock ( visibilityViews )
		{
			if ( state.PrepassCompacted.IsValid )
			{
				foreach ( var made in (BufferHandle[])[stamps, jobs, compacted16, buffer] ) Gpu.Destroy( made );
				return state.PrepassCompacted;
			}

			(state.PrepassCapacity, state.KeptCapacity, state.MeshletKept) = (prepassCapacity, keptCapacity, meshletKept);
			(state.MeshletStamps, state.MeshletJobs, state.PrepassCompacted16) = (stamps, jobs, compacted16);
			visibilityBuffers.Add( jobs );
			visibilityBuffers.Add( compacted16 );
			visibilityBuffers.Add( stamps );

			// Freed with the view's other buffers (visibilityBuffers), not through the wrapper
#pragma warning disable CA2000
			state.MeshletStampsAttribute = new GpuBuffer( new BackendBuffer( this, stamps, stampBytes, "Borealis meshlet stamps" ), (int)(stampBytes / 4), 4 );
#pragma warning restore CA2000
			visibilityBuffers.Add( buffer );
			var cleared = ClearMadeAfterClear( state, compacted: false, prepass: buffer );
			state.PrepassCompacted = buffer;

			// Said cleared only once every buffer is in place: recording threads read them without the lock once it's set
			if ( cleared ) state.MeshletStampsCleared = true;
			return buffer;
		}
	}

	/// <summary>
	/// Clear a view's visibility buffers made after this frame cleared the view's (<c>BackendContext.BeginGpuDrivenView</c>)
	/// as that clear would have: this frame's calls go on to use them, and their cursors and stamps would hold whatever the
	/// memory last held (a target that grew, a child view drawn at another size inside a cube capture, a prepass's meshlet
	/// buffers made at its first use). In a list of its own, submitted now, ahead of the lists recording with them. Whether
	/// it did; the caller marks the meshlet stamps cleared once the view holds every buffer.
	/// </summary>
	bool ClearMadeAfterClear( VisibilityView state, bool compacted, BufferHandle prepass )
	{
		if ( state.ClearedFrame != GpuDrivenFrame ) return false;

		var list = Gpu.BeginCommandList( QueueType.Graphics, "Borealis visibility made mid-frame" );
		if ( compacted )
		{
			foreach ( var buffer in state.Compacted ) list.FillBuffer( buffer, VisibilityView.CompactedCursorIndices, 0, 4 );
		}

		if ( prepass.IsValid )
		{
			list.FillBuffer( prepass, VisibilityView.CompactedCursorIndices, 0, 4 );
			list.FillBuffer( state.PrepassCompacted16, VisibilityView.CompactedCursorIndices, 0, 4 );
			list.FillBuffer( state.MeshletStamps, 0 );
		}

		list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.AllCommands, MemoryAccess.MemoryRead | MemoryAccess.MemoryWrite );
		Gpu.Submit( list );
		VisibilityClearedMidFrame++;
		return true;
	}

	/// <summary>Views' visibility buffers made after their frame's clear and cleared when made, for the tests.</summary>
	public int VisibilityClearedMidFrame { get; private set; }

	readonly ConditionalWeakTable<object, VisibilityView> visibilityViews = new();
	readonly List<BufferHandle> visibilityBuffers = new();
	readonly List<TextureHandle> visibilityTextures = new();

	// Views that have gone (a camera's renders after its scene unloaded): their visibility resources, freed at the next frame
	readonly System.Collections.Concurrent.ConcurrentQueue<VisibilityView> retiredVisibility = new();

	/// <summary>Views' visibility resources freed after their views went, for soaks and tests.</summary>
	public int VisibilityViewsFreed { get; private set; }

	/// <summary>Views' visibility resources freed after the views went unused, for soaks and tests.</summary>
	public int VisibilityViewsIdled { get; private set; }

	/// <summary>
	/// Free the visibility resources of views unused for <see cref="IdleVisibilityFrames"/> (a capture's faces between
	/// captures, a camera that stopped drawing): they're made again if the view draws again.
	/// </summary>
	void FreeIdleVisibility()
	{
		lock ( visibilityViews )
		{
			foreach ( var (_, state) in visibilityViews )
			{
				if ( !state.Ready || GpuDrivenFrame - state.ClearedFrame < IdleVisibilityFrames ) continue;
				FreeVisibility( state );
				VisibilityViewsIdled++;
			}
		}
	}

	// A view's visibility resources, destroyed once the GPU is done with them; the state is made again when next asked for.
	// Under the visibility lock
	void FreeVisibility( VisibilityView state )
	{
		foreach ( var buffer in state.Buffers() )
		{
			if ( buffer.IsValid && visibilityBuffers.Remove( buffer ) ) Gpu.Destroy( buffer );
		}

		foreach ( var texture in state.Textures() )
		{
			if ( texture.IsValid && visibilityTextures.Remove( texture ) ) Gpu.Destroy( texture );
		}

		state.Ready = false;
		state.Mask = state.Stamps = state.PrepassCompacted = state.PrepassCompacted16 = state.MeshletStamps = state.MeshletJobs = state.HiZ = default;
		state.HiZVisible.AsSpan().Clear();
		state.HiZWidth = state.HiZHeight = state.HiZLevels = 0;
		state.HiZWrote = state.HiZReadValid = false;
		(state.HiZBuiltFrame, state.HiZBuiltBy) = (0, null);
		state.Compacted.AsSpan().Clear();
		state.Ids = state.Scratch = default;
		state.MeshletStampsAttribute = null;
		state.Width = state.Height = state.Samples = 0;
		state.CompactedCapacity = state.PrepassCapacity = 0;
		state.KeptCapacity = 0;
		state.StampsCleared = state.MeshletStampsCleared = false;
		state.ClearedFrame = 0;
	}

	void FreeRetiredVisibility()
	{
		if ( retiredVisibility.IsEmpty ) return;
		lock ( visibilityViews )
		{
			while ( retiredVisibility.TryDequeue( out var gone ) )
			{
				FreeVisibility( gone );
				VisibilityViewsFreed++;
			}
		}
	}

	/// <summary>
	/// Whether a view's visibility resources are made on a worker the first time it asks, while loading asynchronously
	/// (<see cref="AsyncModels"/>; on): its calls draw without visibility until they're ready, as on its first frame, instead
	/// of a recording thread making them while the frame waits (a map's new views took ~90 ms of the frames after its load).
	/// Off makes them where asked. For comparisons.
	/// </summary>
	internal static bool MakeVisibilityAhead { get; set; } = true;

	// Views whose resources are being made on a worker
	readonly HashSet<object> visibilityMaking = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// A view's visibility resources, made the first time and remade when its depth target changes size; null while they're
	/// made on a worker the first time (<see cref="MakeVisibilityAhead"/>).
	/// </summary>
	internal VisibilityView VisibilityFor( object view, TextureInfo depth )
	{
		// Made already, for a depth target of this size: found without a lock, as every GPU-driven call of the view asks
		if ( visibilityViews.TryGetValue( view, out var made ) && made.Ready && (depth.Width == 0 || made.Width == depth.Width && made.Height == depth.Height && made.Samples == depth.Samples) ) return made;

		if ( MakeVisibilityAhead && AsyncModels && depth.Width != 0 && (made is null || !made.Ready) )
		{
			lock ( visibilityViews )
			{
				if ( !visibilityMaking.Add( view ) ) return null;
			}

			System.Threading.ThreadPool.UnsafeQueueUserWorkItem( static state => state.Device.MakeVisibility( state.View, state.Depth ), (Device: this, View: view, Depth: depth), preferLocal: false );
			return null;
		}

		using var zone = Zones.VisibilityMake.Start();
		lock ( visibilityViews )
		{
			var state = visibilityViews.GetValue( view, _ => new VisibilityView { Retire = retiredVisibility } );
			if ( !state.Mask.IsValid )
			{
				(state.Mask, state.Stamps) = (VisibilityMask(), VisibilityStamps());
				visibilityBuffers.Add( state.Mask );
				visibilityBuffers.Add( state.Stamps );
			}

			// Room for every sample's triangle: made with the view, and made again when its target grows past it
			var compacted = CompactedIndicesFor( depth );
			if ( !state.Compacted[0].IsValid || compacted > state.CompactedCapacity )
			{
				for ( int i = 0; i < state.Compacted.Length; i++ )
				{
					if ( state.Compacted[i].IsValid && visibilityBuffers.Remove( state.Compacted[i] ) ) Gpu.Destroy( state.Compacted[i] );
					state.Compacted[i] = VisibilityCompacted( compacted );
					visibilityBuffers.Add( state.Compacted[i] );
				}

				state.CompactedCapacity = compacted;
				ClearMadeAfterClear( state, compacted: true, prepass: default );
			}

			if ( depth.Width != 0 && (state.Width != depth.Width || state.Height != depth.Height || state.Samples != depth.Samples) )
			{
				foreach ( var old in (TextureHandle[])[state.Ids, state.Scratch] )
				{
					if ( !old.IsValid ) continue;
					visibilityTextures.Remove( old );
					Gpu.Destroy( old );
				}

				(state.Scratch, state.Ids) = VisibilityTargets( depth );
				visibilityTextures.Add( state.Scratch );
				visibilityTextures.Add( state.Ids );
				(state.Width, state.Height, state.Samples) = (depth.Width, depth.Height, depth.Samples);
			}

			state.Ready = true;
			return state;
		}
	}

	/// <summary>
	/// On a worker, a view's visibility resources made the first time (<see cref="MakeVisibilityAhead"/>), then handed to its
	/// state under the lock, ready for its next frame.
	/// </summary>
	void MakeVisibility( object view, TextureInfo depth )
	{
		try
		{
			using var zone = Zones.VisibilityMake.Start();
			var compacted = CompactedIndicesFor( depth );
			var (mask, stamps) = (VisibilityMask(), VisibilityStamps());
			var buffers = new BufferHandle[3];
			for ( int i = 0; i < buffers.Length; i++ ) buffers[i] = VisibilityCompacted( compacted );
			var (scratch, ids) = VisibilityTargets( depth );

			VisibilityView state;
			lock ( visibilityViews )
			{
				state = visibilityViews.GetValue( view, _ => new VisibilityView { Retire = retiredVisibility } );
				if ( state.Ready || state.Mask.IsValid )
				{
					// Made meanwhile where it was asked
					foreach ( var buffer in (BufferHandle[])[mask, stamps, .. buffers] ) Gpu.Destroy( buffer );
					Gpu.Destroy( scratch );
					Gpu.Destroy( ids );
					return;
				}

				(state.Mask, state.Stamps) = (mask, stamps);
				visibilityBuffers.Add( mask );
				visibilityBuffers.Add( stamps );
				buffers.CopyTo( state.Compacted, 0 );
				visibilityBuffers.AddRange( buffers );
				state.CompactedCapacity = compacted;
				(state.Scratch, state.Ids) = (scratch, ids);
				visibilityTextures.Add( scratch );
				visibilityTextures.Add( ids );
				(state.Width, state.Height, state.Samples) = (depth.Width, depth.Height, depth.Samples);
				state.Ready = true;
			}

			// Its meshlet prepass's buffers too, which its first prepass call with meshlets would make
			if ( meshletStore is not null ) PrepassCompactedFor( state );
		}
		catch ( GpuException )
		{
			// Out of memory: asked again where it's drawn, which reports it
		}
		finally
		{
			lock ( visibilityViews ) visibilityMaking.Remove( view );
		}
	}

	// The triangle mask: copied out only for the debug figure
	BufferHandle VisibilityMask() => Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityBits / 8, Usage = BufferUsage.Storage | BufferUsage.CopyDestination | (Figure is not null ? BufferUsage.CopySource : 0), Name = "Borealis visibility mask" } );

	BufferHandle VisibilityStamps() => Gpu.CreateBuffer( new BufferInfo { Size = GpuDrivenTables.MaxVisibilityFragments * 4L, Usage = BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis visibility stamps" } );

	BufferHandle VisibilityCompacted( int indices ) => Gpu.CreateBuffer( new BufferInfo { Size = indices * 4L, Usage = BufferUsage.Index | BufferUsage.Storage | BufferUsage.CopyDestination, Name = "Borealis visibility triangles" } );

	// Target 0 of the id prepass (the authored output, which only its alpha to coverage is wanted from) and the ids
	(TextureHandle Scratch, TextureHandle Ids) VisibilityTargets( in TextureInfo depth )
	{
		var scratch = Gpu.CreateTexture( new TextureInfo { Width = depth.Width, Height = depth.Height, Depth = 1, Layers = 1, Mips = 1, Samples = depth.Samples, Dimension = TextureDimension.Texture2D, Format = Format.R8Unorm, Usage = TextureUsage.ColorTarget, Name = "Borealis visibility scratch" } );
		var ids = Gpu.CreateTexture( new TextureInfo
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
		return (scratch, ids);
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
		FreeRetiredVisibility();
		if ( GpuDrivenFrame % 60 == 0 ) FreeIdleVisibility();
		FreeUnheldModels();
		gpuDrivenTables?.BeginFrame();
		if ( meshletStore is { } meshlets ) meshlets.Loading = Loading;
		if ( lodStore is { } lods ) lods.Loading = Loading;
		meshletStore?.BeginFrame();
		lodStore?.BeginFrame();
		GpuDrivenFrame++;
		loadFrame++;
		TimeFrame();
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
		// Views' visibility being made on workers is finished first
		while ( true )
		{
			lock ( visibilityViews )
			{
				if ( visibilityMaking.Count == 0 ) break;
			}

			System.Threading.Thread.Sleep( 1 );
		}

		Gpu.WaitIdle();
		Poll();
		materialCache?.Dispose();
		surfaces?.Dispose();
		ResidentCulls.Dispose( Gpu );
		meshletStore?.Dispose();
		lodStore?.Dispose();
		gpuDrivenTables?.Dispose();
		lock ( visibilityViews )
		{
			foreach ( var buffer in visibilityBuffers ) Gpu.Destroy( buffer );
			foreach ( var texture in visibilityTextures ) Gpu.Destroy( texture );
			if ( shadowMeshletBuffers is { } shadow )
			{
				foreach ( var buffer in (BufferHandle[])[shadow.Compacted, shadow.Compacted16, shadow.Jobs, shadow.Stamps] ) Gpu.Destroy( buffer );
				shadowMeshletBuffers = null;
			}

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
