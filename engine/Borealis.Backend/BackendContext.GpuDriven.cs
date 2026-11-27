using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// GPU-driven drawing (<see cref="BackendDevice.GpuDriven"/>, docs/reference/borealis-gpu-driven.md): a layer's aggregate
/// batches are sorted into buckets by what a multi-draw can't vary - the shader's GPU-driven variant, pipeline state
/// and object attributes - and each bucket is one pipeline and set bind and one indirect multi-draw per index page. What
/// the classic path binds per draw (vertex and index buffers, material constants and textures, the instance's transform)
/// comes from <see cref="GpuDrivenTables"/> by instance index instead.
/// </summary>
internal sealed partial class BackendContext
{
	static readonly StringToken GpuDrivenConstantsName = new( "BorealisGpuDriven_t" );

	/// <summary>The table slots <c>borealis_gpu_driven.fxc</c>'s <c>BorealisGpuDriven_t</c> holds (<see cref="HandFilledConstants"/>).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct GpuDrivenConstants
	{
		public uint Instances, Draws, Geometry, Materials;

		/// <summary>Where each instance's first vertex bit is, a word per instance (wraps); the instance records where there are no ids.</summary>
		public uint VertexBases;
		/// <summary>The lighting table's slot: a draw's lighting record is its flags' top 24 bits, in 16 bytes.</summary>
		public uint Lighting;
		public uint Pad1, Pad2;
	}

	/// <summary><c>borealis_gpu_driven_cull_cs</c>'s and <c>borealis_gpu_driven_compact_cs</c>'s <c>BorealisGpuDrivenCull_t</c> (<see cref="HandFilledConstants"/>).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct GpuDrivenCullConstants
	{
		/// <summary>Byte offsets in the frame's region: the view record (then a batch record each), the instance records, the commands.</summary>
		public uint Input, Batches, Instances, Commands;

		/// <summary>The first instance record's index, the fragment and draw tables' bindless slots, the multi-draws' byte offset (0 to keep every slot).</summary>
		public uint FirstInstance, Fragments, Draws, Runs;

		/// <summary>
		/// The frame's stamp; not 0 where forward's compaction sets fragments' Hi-Z visible bits; the generated LOD table's bindless slot (none without one); the compacted buffer's
		/// indices; the fragment words' and the listed fragments' byte offsets; a prepass that culls meshlets: the meshlet run
		/// table's bindless slot, else none; the draw records the LOD table covers.
		/// </summary>
		public uint Frame, Visibility, LodTable, Capacity, FragmentWords, Listed, MeshletRuns, LodCount;

		/// <summary>The meshlet store's corner runs' bindless slot (each draw record's lowest first corner and how many values from it), which compaction narrows a fragment's vertex bits to; none without a store.</summary>
		public uint Corners;

		/// <summary>
		/// This frame's stamp for LOD fades: time in ticks of 1/256 s (bits 0-13) and frame (bits 14-23) (<see cref="BackendDevice.LodFadeNow"/>);
		/// a fade's length, in ticks (<see cref="BackendDevice.LodFades"/>).
		/// </summary>
		public uint LodFadeNow, LodFadeTicks, Pad0;
	}

	/// <summary><c>borealis_meshlet_cull_cs</c>'s <c>BorealisMeshletCull_t</c> (<see cref="HandFilledConstants"/>).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct MeshletCullConstants
	{
		/// <summary>Byte offsets in the region: the listed fragments, the instance records, the commands; the first instance record's index.</summary>
		public uint Listed, Instances, Commands, FirstInstance;

		/// <summary>The multi-draws' byte offset; the draw and meshlet run tables' bindless slots; the compacted buffer's indices.</summary>
		public uint Runs, Draws, RunTable, Capacity;

		/// <summary>Instance 0's fragment word's byte offset (none without ids); the depth target's samples; the pass (0 test, 1 place, 2 copy); the frame's stamp.</summary>
		public uint FragmentWords, Samples, Pass, Frame;

		/// <summary>The viewport's width and height, and the meshlet bits the view's buffer holds.</summary>
		public System.Numerics.Vector4 Viewport;

		/// <summary>World to clip, a row per clip coordinate.</summary>
		public System.Numerics.Vector4 ClipX, ClipY, ClipZ, ClipW;
	}

	/// <summary><c>borealis_hiz_cs</c>'s <c>BorealisHiZ_t</c> (<see cref="HandFilledConstants"/>).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct HiZConstants
	{
		/// <summary>The pass (0 levels from the depth, 1 levels from <see cref="Source"/>, 2 the second phase's cull), level 0's size and the levels.</summary>
		public uint Pass, Width, Height, Levels;

		/// <summary>The level read; the depth target's size and samples.</summary>
		public uint Source, DepthWidth, DepthHeight, Samples;

		/// <summary>Byte offsets in the region: the candidates, the second phase's multi-draws, the first and second phases' listed fragments.</summary>
		public uint Candidates, Runs, Listed, Listed2;

		/// <summary>The instance records' and commands' byte offsets, the first instance record's index, instance 0's fragment word's byte offset.</summary>
		public uint Instances, Commands, FirstInstance, FragmentWords;

		/// <summary>The frame's stamp; 1: test against the Hi-Z, 2: write meshlet stamps.</summary>
		public uint Frame, Flags, Pad0, Pad1;

		/// <summary>The viewport's width and height and its depth range, which the depth the Hi-Z holds was drawn into.</summary>
		public System.Numerics.Vector4 Viewport;

		/// <summary>World to clip, a row per clip coordinate.</summary>
		public System.Numerics.Vector4 ClipX, ClipY, ClipZ, ClipW;
	}

	/// <summary><c>borealis_gpu_driven_mark_cs</c>'s <c>BorealisGpuDrivenMark_t</c> (<see cref="HandFilledConstants"/>).</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct GpuDrivenMarkConstants
	{
		/// <summary>The ids target's size and samples, and the vertex bits in use.</summary>
		public uint Width, Height, Samples, Bits;

		/// <summary>
		/// With normals from the ids (whose target keeps earlier calls' ids): the call's frame chunk slot, first instance record
		/// and how many, so only its own ids are marked; 0 instances marks every id.
		/// </summary>
		public uint CallSlot, CallFirst, CallInstances, Pad3;
	}

	/// <summary>A bucket: what one multi-draw shares, and the batches in it.</summary>
	internal sealed class GpuDrivenBucket
	{
		public ShaderVariant Variant;
		public BackendMaterial Material;

		/// <summary>A surface's GPU-driven pass and program, for a bucket of surface materials (<see cref="Variant"/> and <see cref="Material"/> are null then).</summary>
		public SurfaceProgram.PassObjects Surface;
		public SurfaceProgram SurfaceProgram;

		/// <summary>A surface bucket whose vertices need only their positions (it doesn't displace them): the ids prepass may pull those alone.</summary>
		public bool SurfacePositionsOnly;

		/// <summary>A surface bucket's materials' resolved render state (<see cref="SurfaceMaterial.RenderState"/>).</summary>
		public Sandbox.ShaderCompiler.SurfaceRenderState SurfaceState;

		/// <summary>A surface bucket's materials' feature values, which its pipelines are specialised for; null for none.</summary>
		public SurfaceFeatures SurfaceFeatures;
		public ShaderParameters Attributes;
		public bool DepthOnly, NoColor;
		public OverlayStencil Stencil;

		/// <summary>Its attributes but for their per-draw lighting (<see cref="BackendParameters.LightingFreeKey"/>): what it's found by.</summary>
		public long AttributesKey;

		/// <summary>The classic variant of its first material, when it's depth-only: what decides whether positions alone will do.</summary>
		public ShaderVariant Classic;

		/// <summary>With <see cref="BackendDevice.GpuDrivenMaterialBuckets"/>, the one pixel material record its draws share; else 0.</summary>
		public uint PixelMaterial;
		/// <summary>Its batches, with the index page each draws from: sorted by page, a bucket is one multi-draw per page.</summary>
		public List<(int Page, int Batch)> Batches = new();
		public int Commands;

		/// <summary>Every fragment of its batches' aggregates: its commands when they're culled on the GPU.</summary>
		public int Fragments;

		/// <summary>
		/// Compacted slots its fragments' meshlets can take, by index size (<see cref="MeshletChunksOf"/>): what its compacted
		/// multi-draws hold when the call culls meshlets.
		/// </summary>
		public int MeshletChunks32, MeshletChunks16;

		/// <summary>The visibility-driven prepass draws it with ids (<see cref="BackendContext.DrawIds"/>).</summary>
		public bool Ids;

		/// <summary>
		/// Its batches are skinned meshes' (<see cref="GpuContext.GpuDrivenBatch.Skinned"/>), whose vertices aren't their bind pose under the
		/// instance's transform: never drawn from positions alone (the ids shader, shadow meshlets), so never with static ones.
		/// </summary>
		public bool Skinned;

		/// <summary>Its batches' own object attributes (<see cref="GpuContext.GpuDrivenBatch.OwnAttributes"/>), one set per bucket; null for none.</summary>
		public ShaderParameters Own;

		/// <summary>With <see cref="Ids"/>: its variant has no pixel shader, so <c>borealis_gpu_driven_ids</c> draws it.</summary>
		public bool IdsShader;

		/// <summary>
		/// With <see cref="IdsShader"/> in a prepass that culls meshlets: <c>borealis_meshlet_ids</c>'s task and mesh shaders cull
		/// and draw its fragments' meshlets (<see cref="BackendDevice.MeshletIds"/>), from a task run in place of its compacted multi-draws.
		/// </summary>
		public bool MeshTasks;

		/// <summary>An ids bucket with aggregate fragments the prepass can send to the meshlet shaders.</summary>
		public bool PrepassMeshlets;

		/// <summary>
		/// In a shadow call that culls meshlets: a depth-only bucket whose vertices need only their positions, whose fragments'
		/// meshlets inside the view <c>borealis_meshlet_cull_cs</c> draws (flag 1 in its batch records, as an ids bucket's).
		/// </summary>
		public bool ShadowMeshlets;

		/// <summary>While a census runs: its number in the frame (-1 until it draws), shader name and distinct materials.</summary>
		public int CensusId;
		public string CensusShader;
		public int CensusMaterials;

		/// <summary>A copy of what it draws with, without its batches, for a plan (<see cref="GpuDrivenPlan"/>).</summary>
		public GpuDrivenBucket Kept()
		{
			var kept = (GpuDrivenBucket)MemberwiseClone();
			kept.Batches = new();
			return kept;
		}
	}

	/// <summary>The current call's buckets: the context's own, or a plan's and then its own while it draws from one.</summary>
	List<GpuDrivenBucket> gpuDrivenBuckets;

	readonly List<GpuDrivenBucket> ownBuckets = new(), combinedBuckets = new();

	/// <summary>The first bucket a batch may join: those before it are a resident prefix's.</summary>
	int openBucket;

	/// <summary>Each batch's lighting record (<see cref="GpuDrivenTables.Lighting"/>), by its index in the call.</summary>
	uint[] batchLighting = [];
	static readonly StringToken BakedLightingToken = new( "BakedLightingConstantBuffer_t" );
	static readonly StringToken ProbeVolumeToken = new( "PerBatchLightProbeVolumeConstantBuffer_t" );
	int gpuDrivenBucketCount;

	internal override bool GpuDriven => device.DrawsAggregatesGpuDriven;

	/// <summary>Count a batch left to the classic path, for the report (<see cref="BackendDevice.GpuDrivenLeftClassic"/>).</summary>
	void LeftClassic<TMode>( ShaderFile shader, TMode mode, string why ) => LeftClassic( shader.Name, mode, why );

	/// <summary>Count a batch of a shader or surface by name left to the classic path, for the report. Generic, so a mode's token isn't boxed when nothing counts.</summary>
	void LeftClassic<TMode>( string shader, TMode mode, string why )
	{
		if ( Census ) device.Census?.Left( $"{DrawCensus.ShaderName( shader )} {mode} ({why})" );
		if ( !device.TimeDraws ) return;
		device.GpuDrivenLeftClassic.AddOrUpdate( $"{shader} {mode} ({why})", 1, static ( _, n ) => n + 1 );
	}

	internal override void DrawGpuDriven( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> args, TransformBuffer transforms, int depthBias, float slopeScaledDepthBias, Span<bool> drawn, in GpuDrivenCull? cull = null )
	{
		GpuCountedBatches = 0;
		if ( !GpuDriven || recording is null || batches.IsEmpty || !PrepareGpuDrivenTargets() ) return;

		// A call whose first batches are a resident list's: those drawn from what an earlier call of their kind made of them,
		// when nothing it was made from has changed (GpuDrivenPlan), and the rest bucketed after them as usual
		var prefix = cull is { Plan: not null } resident && UsesPlans ? Math.Clamp( resident.PlanBatches, 0, batches.Length ) : 0;
		GpuDrivenPlan plan = null;
		if ( prefix > 0 )
		{
			using ( Zones.GpuDrivenPlanFind.Start() ) plan = CurrentPlan( shaderMode, batches[..prefix], cull.Value );
		}

		if ( plan is not null )
		{
			// Culled already, by the setup's resident cull
			bool residentDrawn;
			using ( Zones.GpuDrivenResidentDraw.Start() ) residentDrawn = DrawResidentCulled( shaderMode, batches, args, transforms, depthBias, slopeScaledDepthBias, drawn, cull.Value, plan, prefix );
			if ( residentDrawn )
			{
				System.Threading.Interlocked.Increment( ref device.GpuDrivenPlanHits );
				return;
			}

			if ( DrawBucketed( shaderMode, batches, args, transforms, depthBias, slopeScaledDepthBias, drawn, cull, plan, prefix ) )
			{
				System.Threading.Interlocked.Increment( ref device.GpuDrivenPlanHits );
				return;
			}

			// Its other batches can't be culled on the GPU: the whole call the usual way
			drawn.Clear();
			prefix = 0;
		}

		DrawBucketed( shaderMode, batches, args, transforms, depthBias, slopeScaledDepthBias, drawn, cull, null, prefix );
	}

	/// <summary>
	/// Read the attachment formats for bucketing without starting a rendering scope. Culling and compaction don't
	/// consume these attachments; the raster runs open the scope and apply its clear after that compute work.
	/// </summary>
	bool PrepareGpuDrivenTargets()
	{
		var r = recording;
		if ( r.Rendering || suspendScopes || resumeFirst || device.Figure is not null || !boundValid ) return EnsureScope();

		var color = bound.Color as BackendTexture;
		var depth = bound.Depth as BackendTexture;
		if ( color is null && depth is null ) return false;
		r.Color = color;
		r.Depth = depth;
		r.Normals = color is null ? null : bound.Normals as BackendTexture;
		r.Slice = bound.DepthSlice;
		r.Srgb = bound.SrgbWrite;
		return true;
	}

	/// <summary>
	/// The buckets of a call's batches, then its culling and multi-draws. With <paramref name="plan"/>, its first
	/// <paramref name="prefix"/> batches come from the plan, in its buckets, and the rest go in buckets of their own after
	/// them; false, having drawn nothing, when those can't be culled on the GPU. Without one, a call with a resident
	/// prefix still buckets it apart from the rest, and keeps what it made of it as the plan for its kind.
	/// </summary>
	bool DrawBucketed( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> args, TransformBuffer transforms, int depthBias, float slopeScaledDepthBias, Span<bool> drawn, in GpuDrivenCull? cull, GpuDrivenPlan plan, int prefix )
	{
		using var zone = Zones.GpuDrivenBucketed.Start();
		var start = device.TimeDraws ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
		gpuDrivenBuckets = ownBuckets;
		var tables = device.GpuDrivenTables;
		var cache = device.MaterialCache;
		var resolver = Resolver( cache );
		var r = recording;
		var color = r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var depth = r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth );
		var stencil = depth.HasStencil() ? OverlayStencil : OverlayStencil.None;
		var samples = (r.Color ?? r.Depth).Samples;

		// Which bucket each batch goes in: its material's GPU-driven variant under its object attributes. A plan's buckets
		// come first, as it made them; no batch after them joins one
		gpuDrivenBucketCount = 0;
		openBucket = 0;
		var totalCommands = 0;
		var totalFragments = 0;
		var prefixBuckets = 0;
		if ( plan is not null )
		{
			combinedBuckets.Clear();
			combinedBuckets.AddRange( plan.Buckets );
			combinedBuckets.AddRange( ownBuckets );
			gpuDrivenBuckets = combinedBuckets;
			gpuDrivenBucketCount = openBucket = prefixBuckets = plan.Buckets.Count;
			totalCommands = plan.Commands;
			totalFragments = plan.Fragments;
			plan.Drawn.AsSpan().CopyTo( drawn );
		}

		try
		{
			if ( batchLighting.Length < batches.Length ) Array.Resize( ref batchLighting, Math.Max( batches.Length, batchLighting.Length * 2 ) );

			// Culled on the GPU when every batch is a whole aggregate whose fragments' transforms are in order
			var gpuCull = device.GpuDrivenCulling && cull.HasValue && CullShader is not null;
			var vertexCache = (attributes.GetCombo( VertexAnimationCombo ) ?? 0) != 0;
			int? vertexCacheWas = null;

			// An id prepass whose object motion the ids resolve reads (only a view whose frame reads motion): skinned depth draws
			// write their ids with depth_only's pixel stage (D_BOREALIS_IDS)
			var skinnedIds = device.ObjectMotionIds && cull is { Ids: true, View: { } idsView } && r.Color is null && device.ExistingVisibility( idsView ) is { } idsVisibility && device.WantsObjectMotion( idsVisibility );
			var idsCombo = false;
			ShaderParameters lastAttributes = null, lastOwn = null;
			var lastAttributesSet = false;
			var lastLighting = 0u;
			var lastKey = 0L;
			var inOrder = GpuDrivenInOrder && !cull.HasValue && plan is null;
			var stopped = false;
			var commandsBefore = 0;
			for ( int i = plan is null ? 0 : prefix; i < batches.Length; i++ )
			{
				// The batches after a resident prefix go in buckets of their own
				if ( i == prefix && plan is null && prefix > 0 ) openBucket = prefixBuckets = gpuDrivenBucketCount;

				ref readonly var batch = ref batches[i];
				var source = batch.Mesh?.Geometry as IBackendGeometry;
				DrawGeometry? orderedGeometry = null;

				// In order: nothing after a batch left to the caller; else only the last bucket, on its index page
				if ( inOrder )
				{
					if ( stopped || (i > 0 && totalCommands == commandsBefore && batches[i - 1].Count > 0) )
					{
						stopped = true;
						continue;
					}

					commandsBefore = totalCommands;
					openBucket = Math.Max( gpuDrivenBucketCount - 1, 0 );
					if ( gpuDrivenBucketCount > 0 && gpuDrivenBuckets[gpuDrivenBucketCount - 1].Batches is { Count: > 0 } last
						&& source is not null )
					{
						orderedGeometry = GeometryOf( batch, source );
						if ( orderedGeometry.Value.Pooled is { } inOrderPooled && inOrderPooled.IndexPageSlot != last[^1].Page ) openBucket = gpuDrivenBucketCount;
					}
				}
				if ( batch.TextureSize > 0 ) device.AskTextures( batch.Material, batch.TextureSize );

				// A surface material: its own GPU-driven passes, in buckets of its own (BackendContext.Surfaces.cs)
				if ( batch.Material is SurfaceMaterial surface )
				{
					if ( tables.Churns( surface, null ) )
					{
						LeftClassic( surface.Name, shaderMode, "material records churn" );
						continue;
					}

					// A skinned one from the vertex cache with its skinned GPU-driven pass, under its own object attributes
					if ( source is not null && source.Use() && (orderedGeometry ?? GeometryOf( batch, source )).Pooled is { } surfacePooled )
					{
						SetObjectAttributes( batch.OwnAttributes, batch.ObjectAttributes );
						NextPassState( cache );
						if ( !lastAttributesSet || !ReferenceEquals( batch.ObjectAttributes, lastAttributes ) || !ReferenceEquals( batch.OwnAttributes, lastOwn ) )
						{
							(lastAttributes, lastOwn, lastAttributesSet) = (batch.ObjectAttributes, batch.OwnAttributes, true);
							lastLighting = tables.Lighting( Lookup( BakedLightingToken ) as byte[], Lookup( ProbeVolumeToken ) as byte[] );
							lastKey = AttributesKey( batch.ObjectAttributes );
						}

						batchLighting[i] = lastLighting;
						if ( SurfaceBucket( surface, shaderMode, batch.StripPixelShader || r.Color is null, batch.ObjectAttributes, lastKey, stencil, batch.Skinned, batch.VertexCache, batch.OwnAttributes ) is { } surfaceBucket )
						{
							surfaceBucket.Batches.Add( (surfacePooled.IndexPageSlot, i) );
							device.Surfaces.CountGpuDriven();
							surfaceBucket.Commands += batch.Count;
							totalCommands += batch.Count;
							var surfaceFragments = FragmentCount( batch );
							gpuCull &= surfaceFragments > 0 && (batch.Instances || batch.Owner is ResidentMeshes || transforms.InstanceSlot( batch.FirstInstance + surfaceFragments - 1 ) == transforms.InstanceSlot( batch.FirstInstance ) + surfaceFragments - 1);
							surfaceBucket.Fragments += surfaceFragments;
							CountMeshletChunks( surfaceBucket, batch, surfaceFragments, surfacePooled.IndexFormat );
							totalFragments += surfaceFragments;
						}
						else if ( batch.Skinned ) LeftClassic( surface.Name, shaderMode, batch.VertexCache ? "surface, no skinned GPU-driven pass" : "surface, no GPU-driven pass" );
					}
					else if ( batch.Skinned ) LeftClassic( surface.Name, shaderMode, batch.VertexCache ? "surface, vertex cache geometry not pooled" : "surface, geometry not pooled" );

					continue;
				}

				if ( batch.Material is not BackendMaterial { Shader: { } shader } m || source is null || !source.Use() ) continue;
				var geometry = orderedGeometry ?? GeometryOf( batch, source );
				if ( geometry.Pooled is not { } pooled )
				{
					LeftClassic( shader, shaderMode, batch.VertexCache ? "vertex cache geometry not pooled" : "geometry not pooled" );
					continue;
				}

				// A draw from the vertex cache takes the variant its classic draw would, with D_CS_VERTEX_ANIMATION: set only
				// when it changes (skinned draws come together), and put back as it was after the call
				if ( batch.VertexCache != vertexCache )
				{
					vertexCacheWas ??= attributes.GetCombo( VertexAnimationCombo ) ?? 0;
					vertexCache = batch.VertexCache;
					attributes.SetCombo( VertexAnimationCombo, vertexCache ? 1 : 0 );
				}

				if ( skinnedIds && batch.Skinned != idsCombo ) attributes.SetCombo( BorealisIdsCombo, (idsCombo = batch.Skinned) ? 1 : 0 );

				SetObjectAttributes( batch.OwnAttributes, batch.ObjectAttributes );
				NextPassState( cache );
				var mode = CachedModeName( shader, shaderMode );
				if ( mode is null ) continue;

				// Its lighting constants as the draw would read them (the object's, else the pass's), as a lighting record
				if ( batchLighting.Length < batches.Length ) Array.Resize( ref batchLighting, Math.Max( batches.Length, batchLighting.Length * 2 ) );
				// Consecutive batches mostly share their attributes (the world's lightmap, a probe volume): looked up once for them
				if ( !lastAttributesSet || !ReferenceEquals( batch.ObjectAttributes, lastAttributes ) || !ReferenceEquals( batch.OwnAttributes, lastOwn ) )
				{
					(lastAttributes, lastOwn, lastAttributesSet) = (batch.ObjectAttributes, batch.OwnAttributes, true);
					lastLighting = tables.Lighting( Lookup( BakedLightingToken ) as byte[], Lookup( ProbeVolumeToken ) as byte[] );
					lastKey = AttributesKey( batch.ObjectAttributes );
				}

				batchLighting[i] = lastLighting;

				comboLookup ??= name => LookupCombo( Token( name ) );
				specializationLookup ??= name => Integer( Lookup( Token( name ) ) );
				var variant = m.GpuDrivenVariant( device.Shaders, mode, ComboKey( m, mode ), comboLookup, specializationLookup );
				if ( variant?.Vertex?.Bytecode is null )
				{
					LeftClassic( shader, mode, "no variant" );
					continue;
				}

				// A matrix its object sets (a transform, likely new each frame) would make a material record per value
				if ( ObjectSetsMatrix( variant.Vertex ) || ObjectSetsMatrix( variant.Pixel ) )
				{
					LeftClassic( shader, mode, "object sets a matrix constant" );
					continue;
				}

				// Nor what makes a new material record all the time
				if ( tables.Churns( m, variant.Vertex ) || tables.Churns( m, variant.Pixel ) )
				{
					LeftClassic( shader, mode, "material records churn" );
					continue;
				}

				var noColor = batch.StripPixelShader || r.Color is null;
				var depthOnly = StripsPixelShader( m, variant, noColor, batch.StripPixelShader, LookupCombo( OpaqueFadeCombo ) is > 0 || idsCombo && variant.Pixel is { Bytecode: not null, GpuDriven: true } );
				if ( !depthOnly && variant.Pixel?.Bytecode is null )
				{
					LeftClassic( shader, mode, "no pixel variant" );
					continue;
				}

				// One material per bucket where pixels read it, when asked: its record's offset is then the bucket's, uniform
				var pixelMaterial = 0u;
				if ( device.GpuDrivenMaterialBuckets && !depthOnly && batch.OwnAttributes is null )
				{
					pixelMaterial = MaterialRecord( tables, m, variant.Pixel, geometry.UncompressedTangentFrame );
				}

				var bucket = FindBucket( variant, batch.ObjectAttributes, lastKey, depthOnly, noColor, pixelMaterial, m, mode, stencil, batch.Skinned, batch.OwnAttributes );
				bucket.Batches.Add( (pooled.IndexPageSlot, i) );
				bucket.Commands += batch.Count;
				totalCommands += batch.Count;

				// A mesh draw's instances are its fragments, each with its own transform slot; an aggregate's need theirs in order
				var fragmentCount = FragmentCount( batch );
				gpuCull &= fragmentCount > 0 && (batch.Instances || batch.Owner is ResidentMeshes || transforms.InstanceSlot( batch.FirstInstance + fragmentCount - 1 ) == transforms.InstanceSlot( batch.FirstInstance ) + fragmentCount - 1);
				bucket.Fragments += fragmentCount;
				CountMeshletChunks( bucket, batch, fragmentCount, pooled.IndexFormat );
				totalFragments += fragmentCount;
			}

			if ( vertexCacheWas is { } was ) attributes.SetCombo( VertexAnimationCombo, was );
			if ( idsCombo ) attributes.SetCombo( BorealisIdsCombo, 0 );
			SetObjectAttributes( null );
			if ( plan is null && prefix == batches.Length ) prefixBuckets = gpuDrivenBucketCount;
			if ( gpuDrivenBucketCount == 0 ) return true;
			if ( plan is not null && !gpuCull ) return false;
			if ( Census ) CensusBuckets( batches );

			if ( gpuCull )
			{
				DrawGpuCulled( shaderMode, batches, transforms, cull.Value, totalFragments, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, drawn, plan, prefix, prefixBuckets, totalCommands );
				if ( CountsOnDevice( cull.Value ) ) GpuCountedBatches += batches.Length;
				TimeGpuDriven( start, drawn, totalFragments );
				return true;
			}

			if ( !EnsureScope() ) return true;

			// One region this frame for every instance record (8 bytes) and command (20 bytes)
			var instanceBytes = totalCommands * GpuDrivenTables.InstanceStride;
			var commandBase = (instanceBytes + 15) & ~15;
			var frameBytes = commandBase + totalCommands * GpuDrivenTables.CommandStride;
			var (frameBuffer, frameSlot, frameOffset) = tables.Allocate( frameBytes );
			var data = Gpu.Map( frameBuffer ).Slice( (int)frameOffset, frameBytes );
			var firstInstance = (uint)(frameOffset / GpuDrivenTables.InstanceStride);
			var instances = MemoryMarshal.Cast<byte, InstanceRecord>( data[..instanceBytes] );
			var commands = MemoryMarshal.Cast<byte, GpuBuffer.IndirectDrawIndexedArguments>( data.Slice( commandBase, totalCommands * GpuDrivenTables.CommandStride ) );

			// The tables' slots as the draws bind them: read again before a run is drawn when a table grew since (a record appended
			// for this call is past the end of the buffer at the slot read before)
			var generation = tables.Generation;
			SetTableSlots( tables, frameSlot, (uint)frameOffset - firstInstance * 4 );

			var written = 0;
			for ( int b = 0; b < gpuDrivenBucketCount; b++ )
			{
				var bucket = gpuDrivenBuckets[b];

				// A bucket's one material, where it has one, is an object attribute: another bucket's moves a dynamic offset
				SetObjectAttributes( bucket.Own ?? (bucket.PixelMaterial != 0 ? device.GpuDrivenMaterialParameters( bucket.PixelMaterial ) : null), bucket.Attributes );
				NextPassState( cache );
				resolver.Begin( passEpoch );

				// The records, and each batch's commands, by index page
				var bucketStart = written;
				var currentPage = default( BufferHandle );
				var currentFormat = IndexFormat.UInt16;
				var runStart = written;
				var bound = false;

				bucket.Batches.Sort( static ( a, b ) => a.Page != b.Page ? a.Page.CompareTo( b.Page ) : a.Batch.CompareTo( b.Batch ) );
				foreach ( var (_, index) in bucket.Batches )
				{
					ref readonly var batch = ref batches[index];

					// Resident meshes draw only where the GPU picks their LOD: left undrawn here
					if ( batch.Owner is ResidentMeshes ) continue;

					var material = batch.Material;
					var geometry = GeometryOf( batch );
					var pooled = geometry.Pooled.Value;

					// Depth only, from the positions-only copy when the vertex shader reads nothing else these vertices have
					if ( bucket.Classic is not null && device.PositionStreams && pooled.PositionsRecord != 0 && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) pooled = pooled with { GeometryRecord = pooled.PositionsRecord };

					var (vertexMaterial, pixelMaterial) = Records( tables, bucket, material, geometry.UncompressedTangentFrame );
					var flags = (geometry.UncompressedTangentFrame ? 1u : 0u) | batchLighting[index] << 8;

					if ( pooled.IndexPage != currentPage )
					{
						if ( written > runStart )
						{
							RebindIfGrown( tables, ref generation, ref bound, frameSlot, (uint)frameOffset - firstInstance * 4, cache, resolver );
							DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, currentPage, currentFormat, frameBuffer, frameOffset + commandBase + runStart * GpuDrivenTables.CommandStride, written - runStart );
						}

						currentPage = pooled.IndexPage;
						currentFormat = pooled.IndexFormat;
						runStart = written;
					}

					// An owner's fragments keep their draw records by instance; anything else looks them up by key. The owner's
					// lock is held only around its records and the draw table, which takes no other lock, and is let go whatever
					// throws, so a worker drawing the same owner in another segment can't wait on it forever
					var owned = batch.Owner is null ? null : tables.DrawsOf( batch.Owner, pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, pooled.IndexPageSlot );
					var ownedTaken = false;
					try
					{
						if ( owned is not null ) System.Threading.Monitor.Enter( owned, ref ownedTaken );

						for ( int f = 0; f < batch.Count; f++ )
						{
							var arg = batch.Instances ? InstanceArguments( batch, geometry, f ) : args[batch.FirstArg + f];
							var firstIndex = pooled.FirstIndex + arg.FirstIndex;
							uint draw;
							if ( owned is null )
							{
								draw = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, arg.IndexCount, arg.BaseVertex, pooled.IndexPageSlot );
							}
							else
							{
								var slot = (int)arg.FirstInstance;
								if ( slot >= owned.Draws.Length ) Array.Resize( ref owned.Draws, Math.Max( slot + 1, owned.Draws.Length * 2 ) );

								ref var entry = ref owned.Draws[slot];
								if ( entry.IndexCount != arg.IndexCount || entry.FirstIndex != firstIndex || entry.BaseVertex != arg.BaseVertex )
									entry = new GpuDrivenTables.OwnerDraw( firstIndex, arg.IndexCount, arg.BaseVertex, tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, arg.IndexCount, arg.BaseVertex, pooled.IndexPageSlot ) );
								draw = entry.Draw;
							}

							instances[written] = new InstanceRecord { Transform = (uint)transforms.InstanceSlot( batch.FirstInstance + (int)arg.FirstInstance ), Draw = draw };
							commands[written] = new GpuBuffer.IndirectDrawIndexedArguments
							{
								IndexCount = arg.IndexCount,
								InstanceCount = 1,
								FirstIndex = firstIndex,
								BaseVertex = arg.BaseVertex,
								FirstInstance = firstInstance + (uint)written,
							};
							written++;
						}
					}
					finally
					{
						if ( ownedTaken ) System.Threading.Monitor.Exit( owned );
					}

					drawn[index] = true;
				}

				if ( written > runStart )
				{
					RebindIfGrown( tables, ref generation, ref bound, frameSlot, (uint)frameOffset - firstInstance * 4, cache, resolver );
					DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, currentPage, currentFormat, frameBuffer, frameOffset + commandBase + runStart * GpuDrivenTables.CommandStride, written - runStart );
				}

				bucket.Batches.Clear();
			}

			SetObjectAttributes( null );
			r.Binder = null;
			TimeGpuDriven( start, drawn, written );
			return true;
		}
		finally
		{
			// The context's own buckets again, keeping any made beyond them while a plan's were in the list
			if ( ReferenceEquals( gpuDrivenBuckets, combinedBuckets ) )
			{
				for ( int i = prefixBuckets + ownBuckets.Count; i < combinedBuckets.Count; i++ ) ownBuckets.Add( combinedBuckets[i] );
				combinedBuckets.Clear();
				gpuDrivenBuckets = ownBuckets;
			}
		}
	}

	static readonly StringToken VertexAnimationCombo = new( "D_CS_VERTEX_ANIMATION" );
	static readonly StringToken BorealisIdsCombo = new( "D_BOREALIS_IDS" );

	/// <summary>The GPU-driven constants with the tables' slots as they are now.</summary>
	void SetTableSlots( GpuDrivenTables tables, int frameSlot, uint vertexBases )
	{
		var slots = tables.Slots;
		SetConstants( GpuDrivenConstantsName, new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials, VertexBases = vertexBases, Lighting = (uint)slots.Lighting } );
	}

	/// <summary>A table grew since <paramref name="generation"/>: the constants take the new slots, and the bucket binds again with them.</summary>
	void RebindIfGrown( GpuDrivenTables tables, ref int generation, ref bool bound, int frameSlot, uint vertexBases, MaterialObjectCache cache, PassResolver resolver )
	{
		if ( tables.Generation == generation ) return;
		generation = tables.Generation;
		SetTableSlots( tables, frameSlot, vertexBases );
		NextPassState( cache );
		resolver.Begin( passEpoch );
		bound = false;
	}

	/// <summary>What a batch draws from: its draw's geometry, or its vertex-cache form for a skinned draw from the cache.</summary>
	static DrawGeometry GeometryOf( in GpuDrivenBatch batch ) => GeometryOf( batch, (IBackendGeometry)batch.Mesh.Geometry );

	static DrawGeometry GeometryOf( in GpuDrivenBatch batch, IBackendGeometry source )
	{
		if ( batch.VertexCache && source is ISkinnedGeometry skinned && skinned.VertexCacheFor( batch.Draw.Mesh, batch.Draw.DrawCall ) is { } cache ) return cache.Geometry;
		return source.For( batch.Draw.Mesh, batch.Draw.DrawCall );
	}

	/// <summary>A batch's fragments: a mesh draw's instances, an aggregate's fragments, or resident meshes.</summary>
	static int FragmentCount( in GpuDrivenBatch batch ) => batch.Instances ? batch.Count : batch.Owner switch
	{
		AggregateObject aggregate => aggregate.Fragments.Length,
		ResidentMeshes meshes => meshes.Count,
		_ => 0,
	};

	/// <summary>Instance <paramref name="instance"/> of a mesh draw's batch, as the arguments an aggregate's fragment has.</summary>
	static GpuBuffer.IndirectDrawIndexedArguments InstanceArguments( in GpuDrivenBatch batch, in DrawGeometry geometry, int instance ) => new()
	{
		IndexCount = (uint)batch.Draw.IndexCount,
		InstanceCount = 1,
		FirstIndex = (uint)batch.Draw.StartIndex,
		BaseVertex = geometry.BaseVertex,
		FirstInstance = (uint)instance,
	};

	/// <summary>
	/// The timings of a <see cref="DrawGpuDriven"/>: a bucket is a bind and its first multi-draw; the rest of its multi-draws
	/// (one per index page) draws; its commands fragment draws. The time is the whole call's, put on the first bind.
	/// </summary>
	void TimeGpuDriven( long start, Span<bool> drawn, int commands )
	{
		if ( device.TimeDraws )
		{
			var ticks = System.Diagnostics.Stopwatch.GetTimestamp() - start;
			for ( int b = 0; b < gpuDrivenBucketCount; b++ )
			{
				device.DrawTimes.Add( b == 0 ? ticks : 0, bind: true, drawn: true );
				device.DrawTimes.AddIndirect( gpuDrivenBuckets[b].Commands );
			}

			for ( int i = gpuDrivenBucketCount; i < gpuDrivenRuns; i++ ) device.DrawTimes.Add( 0, bind: false, drawn: true );

			var drawnBatches = 0;
			foreach ( var d in drawn ) drawnBatches += d ? 1 : 0;
			device.DrawTimes.AddGpuDriven( drawnBatches, gpuDrivenBucketCount, gpuDrivenRuns, commands );
		}

		gpuDrivenRuns = 0;
	}

	ShaderFile CullShader => device.GpuDrivenCullShader;

	ShaderDrawer cullDrawer;
	ShaderAttributes cullAttributes;
	byte[] cullConstants;
	readonly List<(int Bucket, BufferHandle Page, IndexFormat Format, int First, int Count)> cullRuns = new();

	static class CullAttribute
	{
		public static readonly int Frame = AttributeIds.Of( "BorealisCullFrame" );
		public static readonly int Stats = AttributeIds.Of( "BorealisCullStats" );
		public static readonly int Levels = AttributeIds.Of( "BorealisCullLevels" );
		public static readonly int LodFade = AttributeIds.Of( LodFadeAttribute );
		public static readonly int Stamps = AttributeIds.Of( "BorealisCullStamps" );
		public static readonly int Mask = AttributeIds.Of( "BorealisCullMask" );
		public static readonly int Compacted = AttributeIds.Of( "BorealisCullCompacted" );
		public static readonly int HiZVisible = AttributeIds.Of( "BorealisHiZVisible" );
		public static readonly int MeshletStamps = AttributeIds.Of( "BorealisMeshletStamps" );
		public static readonly int MeshletCompacted = AttributeIds.Of( "BorealisMeshletCompacted" );
		public static readonly int MeshletCompacted16 = AttributeIds.Of( "BorealisMeshletCompacted16" );
		public static readonly int CullConstants = AttributeIds.Of( "BorealisGpuDrivenCull_t" );
		public static readonly int Ids = AttributeIds.Of( "BorealisIds" );
		public static readonly int Msaa = AttributeIds.Of( "D_MSAA" );
		public static readonly int MarkConstants = AttributeIds.Of( "BorealisGpuDrivenMark_t" );
	}

	const int CullViewBytes = CullViewRecord.Size;
	const int CullBatchBytes = BatchRecord.Size;

	/// <summary>
	/// Draw the buckets with every fragment of each batch's aggregate culled on the GPU (<c>borealis_gpu_driven_cull_cs</c>):
	/// the CPU writes the view and a 32-byte record per batch, and a dispatch writes every fragment's instance record and
	/// command, with no instance where the frustum or size test fails. The fragments' bounds and draw records are written
	/// once per aggregate (<see cref="GpuDrivenTables.FragmentsOf"/>). With <see cref="BackendDevice.GpuDrivenVisibility"/>,
	/// a view's depth prepass also draws ids and marks the triangles that own a sample, and its forward pass draws only
	/// those (<see cref="DrawIds"/>).
	/// </summary>
	void DrawGpuCulled( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, TransformBuffer transforms, in GpuDrivenCull cull, int totalFragments, Format color, Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver, Span<bool> drawn, GpuDrivenPlan plan, int prefix, int prefixBuckets, int totalCommands )
	{
		var tables = device.GpuDrivenTables;
		var cache = device.MaterialCache;
		var r = recording;

		// A plan's buckets have its records; the rest have their batches
		var batchCount = plan is not null ? plan.BatchRecords.Length / (CullBatchBytes / 4) : 0;
		var instanceFragments = 0;
		for ( int b = plan is not null ? prefixBuckets : 0; b < gpuDrivenBucketCount; b++ )
		{
			batchCount += gpuDrivenBuckets[b].Batches.Count;
			foreach ( var (_, index) in gpuDrivenBuckets[b].Batches )
			{
				if ( batches[index].Instances ) instanceFragments += batches[index].Count;
			}
		}

		// The visibility-driven forward pass: the view's ids and lists, compacting only
		var (compact, shaders, visibility, ids, lists) = CullState( cull );
		var meshlets = compact && MeshletsFor( cull, ids );
		var meshTasks = meshlets && MeshTasksNow( cull, compact, ids, visibility );
		var listing = lists || meshlets;

		// One region: the view, the batches, the multi-draws (first slot and visible count, when compacting; a run of
		// compacted triangles per bucket after its own), the listed fragments, mesh draws'
		// instances' transform slots, a resident filter's moved words, then a slot per fragment for its instance record
		// (8 bytes), its fragment (4, for ids) and command (20), and with lists or meshlets the compacted slots after them
		// (CompactSlots)
		var slotCount = totalFragments;
		if ( listing )
		{
			slotCount += plan?.CompactUsed ?? 0;
			for ( int b = plan is not null ? prefixBuckets : 0; b < gpuDrivenBucketCount; b++ )
			{
				var (slots32, slots16) = CompactSlots( gpuDrivenBuckets[b], meshlets, lists );
				slotCount += slots32 + slots16;
			}
		}

		var hiz = !meshTasks && HiZFor( cull, visibility, totalFragments ) && ids;
		var hizFirstSlot = hiz ? slotCount : 0;
		slotCount += hizFirstSlot;

		// At most a multi-draw per batch (a run per index page, each with a batch at least), and two more per bucket: its
		// compacted triangles and their 16-bit meshlets, or its task run and tail
		var runCapacity = batchCount + 2 * gpuDrivenBucketCount;
		var runsOffset = CullViewBytes + batchCount * CullBatchBytes;
		var listedOffset = (runsOffset + (compact ? runCapacity * MultiDraw.Size : 0) + 15) & ~15;
		var transformSlotsOffset = listedOffset + (listing ? ListedArguments.Size + totalFragments * ListedFragment.Size : 0);
		var filter = cull.Filter != ResidentFilter.None && !listing;
		var movedOffset = transformSlotsOffset + ((instanceFragments * 4 + 15) & ~15);
		var instanceOffset = movedOffset + (filter ? (totalFragments * 4 + 15) & ~15 : 0);
		var fragmentOffset = instanceOffset + slotCount * GpuDrivenTables.InstanceStride;
		var commandOffset = (fragmentOffset + (ids ? slotCount * 4 : 0) + 15) & ~15;
		var frameBytes = commandOffset + slotCount * GpuDrivenTables.CommandStride;

		// A prepass culled in two phases: the Hi-Z tail after the rest
		var hizTail = (frameBytes + 15) & ~15;
		if ( hiz ) frameBytes = hizTail + HiZTailBytes( runCapacity * MultiDraw.Size, totalFragments );
		var deviceRegion = DeviceCullRegions && !hiz;
		var (frameBuffer, frameSlot, frameOffset) = deviceRegion ? tables.AllocateDeviceCull( frameBytes ) : tables.Allocate( frameBytes );
		var headBytes = listedOffset + (listing ? ListedArguments.Size : 0);
		var tailBytes = instanceOffset - transformSlotsOffset;
		var upload = deviceRegion ? r.List.AllocateUpload( headBytes + tailBytes, 16 ) : default;
		var data = deviceRegion ? upload.Data : Gpu.Map( frameBuffer ).Slice( (int)frameOffset, frameBytes );
		var stagedTransforms = deviceRegion ? headBytes : transformSlotsOffset;
		var firstInstance = (uint)((frameOffset + instanceOffset) / GpuDrivenTables.InstanceStride);
		var (compacted, compacted16) = lists ? (visibility.Compacted, default) : meshlets ? MeshletCompacted( visibility ) : default;

		if ( listing )
		{
			// The compaction's dispatch: a group per listed fragment, counted up by the culling (the meshlet passes: a group
			// per chunk of each), and where its entries start past the arguments
			MemoryMarshal.Write( data.Slice( listedOffset, ListedArguments.Size ), new ListedArguments { GroupsX = 0, GroupsY = 1, GroupsZ = 1, Entries = 0 } );
		}
		var batchRecords = MemoryMarshal.Cast<byte, uint>( data.Slice( CullViewBytes, batchCount * CullBatchBytes ) );
		var cullBatches = MemoryMarshal.Cast<uint, BatchRecord>( batchRecords );
		var transformSlots = MemoryMarshal.Cast<byte, uint>( data.Slice( stagedTransforms, instanceFragments * 4 ) );
		var slotsWritten = 0;
		var movedWords = MemoryMarshal.Cast<byte, uint>( data.Slice( stagedTransforms + (movedOffset - transformSlotsOffset), filter ? totalFragments * 4 : 0 ) );
		var movedBase = (uint)(frameOffset + movedOffset);
		var movedWritten = 0;

		// Each batch's record and its fragments' slots, bucket by bucket and page by page, and the multi-draws they make; a
		// batch drawn as it was last frame takes its records from the view's memo
		var memo = device.GpuDrivenRecordMemo && cull.View is not null ? device.RecordMemosFor( cull.View ).For( shaderMode.Value, r.Color is null, ids, lists ) : null;
		var memoTaken = false;
		var memoHits = 0;
		GpuDrivenRecordMemo.Slot unkept = default;
		var written = plan?.Written ?? 0;
		var compactWritten = totalFragments + (plan?.CompactUsed ?? 0);
		var record = plan is not null ? plan.BatchRecords.Length / (CullBatchBytes / 4) : 0;

		// A resident prefix's buckets come first: what a plan checks of them, and where their records and multi-draws end. A
		// call that keeps its order has a slot per fragment, the same each frame, so its plan holds them as they are
		var checks = plan is null && prefixBuckets > 0 && cull.Plan is not null && UsesPlans && (compact || cull.KeepsOrder) ? new (GpuMaterial, ShaderParameters, ShaderVariant, int, int)[prefixBuckets] : null;
		var (prefixRecords, prefixWritten, prefixCompact, prefixRuns) = (0, 0, 0, 0);
		try
		{
			if ( memo is not null ) System.Threading.Monitor.TryEnter( memo, ref memoTaken );
			cullRuns.Clear();
			if ( plan is not null )
			{
				// The plan's records and multi-draws, as the call that made it wrote them
				plan.BatchRecords.CopyTo( batchRecords );
				cullRuns.AddRange( plan.Runs );

				// A resident filter's moved words, which its records' sixth word finds
				if ( filter && plan.Moved is { } moved )
				{
					moved.CopyTo( movedWords );
					movedWritten = moved.Length;
					for ( int i = 0; i < plan.MovedAt.Length; i++ )
					{
						if ( plan.MovedAt[i] >= 0 ) cullBatches[i].Moved = movedBase + (uint)plan.MovedAt[i] * 4;
					}
				}

				// Forward's compacted triangles come from this frame's compacted buffer, which turns over each frame, after
				// this call's own slots; a meshlet call's 16-bit runs from its 16-bit buffer
				for ( int i = 0; i < cullRuns.Count; i++ )
				{
					if ( plan.Compacted[i] ) cullRuns[i] = cullRuns[i] with { Page = cullRuns[i].Format == IndexFormat.UInt16 && compacted16.IsValid ? compacted16 : compacted, First = cullRuns[i].First - plan.CompactBase + totalFragments };
				}

				// The ids shader, which this context may not have made yet
				if ( ids && shaders.Ids is not null ) idsShaderVariant ??= Variant( shaders.Ids, "Default" );
			}

			for ( int b = plan is null ? 0 : prefixBuckets; b < gpuDrivenBucketCount; b++ )
			{
				var bucket = gpuDrivenBuckets[b];
				SetObjectAttributes( bucket.Own, bucket.Attributes );
				NextPassState( cache );

				// The prepass draws ids with each bucket's own variant, whose pixel shader writes them where it covers; a depth
				// bucket without one (opaque depth_only) with borealis_gpu_driven_ids, when its vertices need only their positions
				var ownPixel = bucket.Surface is not null ? bucket.Surface.Pass.Pixel is not null : bucket.Variant.Pixel?.Bytecode is not null && bucket.Variant.Pixel.GpuDriven;
				if ( ids && !ownPixel && shaders.Ids is not null ) idsShaderVariant ??= Variant( shaders.Ids, "Default" );
				bucket.IdsShader = ids && !ownPixel && !bucket.Skinned && (bucket.Classic is not null || bucket.SurfacePositionsOnly) && shaders.Ids is not null && idsShaderVariant is not null;
				bucket.Ids = ids && (ownPixel || bucket.IdsShader);
				var bucketLists = lists && !bucket.DepthOnly;

				// A shadow call's depth-only buckets whose vertex shader reads only positions (as the ids prepass's do): their
				// meshlets' boxes bound what they draw. Alpha-tested and translucent ones keep their own pass and draw whole
				// Its draws' meshlets: aggregates ask for theirs as their records are written, a model's static meshes (instances,
				// resident meshes) here, once per draw, for shadow views alone. A bucket of neither draws as before, without a
				// meshlet pass
				bucket.ShadowMeshlets = meshlets && !ids && bucket.Classic is not null && !bucket.Skinned;
				var withMeshlets = false;
				foreach ( var (_, index) in bucket.Batches )
				{
					if ( !bucket.ShadowMeshlets ) break;
					ref readonly var batch = ref batches[index];
					var geometry = GeometryOf( batch );
					if ( !ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) bucket.ShadowMeshlets = false;
					if ( batch.Owner is AggregateObject && !batch.Instances ) withMeshlets = true;
					else if ( batch.Mesh.Geometry is BackendModel model && geometry.Pooled is { } pooledMesh )
					{
						RequestMeshlets( device.MeshletStore, pooledMesh, model, batch.Draw, geometry.BaseVertex );
						withMeshlets = true;
					}
				}

				bucket.ShadowMeshlets &= withMeshlets;

				var currentPage = default( BufferHandle );
				var currentFormat = IndexFormat.UInt16;
				var runStart = written;

				bucket.Batches.Sort( static ( a, b ) => a.Page != b.Page ? a.Page.CompareTo( b.Page ) : a.Batch.CompareTo( b.Batch ) );
				var hasAggregate = false;
				foreach ( var (_, index) in bucket.Batches )
				{
					if ( !bucket.Ids ) break;
					ref readonly var batch = ref batches[index];
					hasAggregate |= !batch.Instances && batch.Owner is AggregateObject;
					if ( bucket.IdsShader && bucket.Classic is not null && !ReadsOnlyPositions( bucket.Classic, GeometryOf( batch ).Layout ) )
						bucket.Ids = bucket.IdsShader = false;
					if ( hasAggregate && !bucket.IdsShader ) break;
				}

				bucket.PrepassMeshlets = meshlets && bucket.Ids && hasAggregate;
				var bucketCompact = bucketLists || bucket.PrepassMeshlets || bucket.ShadowMeshlets;

				// Its meshlets culled and drawn by task and mesh shaders: a task group per listed fragment, as many as a task draw takes
				bucket.MeshTasks = meshTasks && bucket.PrepassMeshlets && bucket.IdsShader && bucket.Fragments <= MaxTaskGroups;

				// What its stages' material records read of the attributes: the same for each of its batches. A depth-only
				// bucket has a pixel record only when it draws ids with its own pixel shader, which its depth pass strips
				var pixelRecord = !bucket.DepthOnly || bucket.Ids && !bucket.IdsShader;
				var vertexAttributes = bucket.Surface is null && bucket.Variant.Vertex is { } vertexStage ? AttributeHash( vertexStage ) : 0;
				var pixelAttributes = bucket.Surface is null && pixelRecord && bucket.Variant.Pixel is { } pixelStage ? AttributeHash( pixelStage ) : 0;
				var stages = (object)bucket.Surface ?? bucket.Variant;

				// What a plan of this call checks of the bucket before it's used again
				if ( checks is not null && b < prefixBuckets )
				{
					if ( bucket.Batches.Count == 0 ) checks = null;
					else checks[b] = (batches[bucket.Batches[0].Batch].Material, batches[bucket.Batches[0].Batch].ObjectAttributes, bucket.Variant, vertexAttributes, pixelAttributes);
				}

				foreach ( var (_, index) in bucket.Batches )
				{
					ref readonly var batch = ref batches[index];
					var material = batch.Material;
					var geometry = GeometryOf( batch );
					var pooled = geometry.Pooled.Value;
					var flags = (geometry.UncompressedTangentFrame ? 1u : 0u) | (pooled.IndexFormat == IndexFormat.UInt32 ? 2u : 0u) | batchLighting[index] << 8;

					// Depth only, from the positions-only copy when the vertex shader reads nothing else these vertices have
					var positions = bucket.Classic is not null && (device.PositionStreams || bucket.IdsShader) && pooled.PositionsRecord != 0;

					if ( pooled.IndexPage != currentPage )
					{
						if ( written > runStart ) cullRuns.Add( (b, currentPage, currentFormat, runStart, written - runStart) );
						currentPage = pooled.IndexPage;
						currentFormat = pooled.IndexFormat;
						runStart = written;
					}

					ref var cullBatch = ref cullBatches[record];
					uint visibilityFirst, vertexBase, kind;
					int fragmentCount;
					if ( batch.Instances )
					{
						// A mesh draw's instances: its draw record, and each instance's transform slot in the frame's region; the
						// caller culled them, and each has a run of vertex bits of its own, a stride apart, made for this frame
						if ( positions && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) pooled = pooled with { GeometryRecord = pooled.PositionsRecord };
						var (vertexMaterial, pixelMaterial) = Records( tables, bucket, material, geometry.UncompressedTangentFrame, vertexAttributes, pixelAttributes );
						fragmentCount = batch.Count;
						cullBatch.First = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, pooled.FirstIndex + (uint)batch.Draw.StartIndex, (uint)batch.Draw.IndexCount, geometry.BaseVertex, pooled.IndexPageSlot );
						cullBatch.Transform = (uint)(frameOffset + transformSlotsOffset + slotsWritten * 4);
						for ( int k = 0; k < fragmentCount; k++ ) transformSlots[slotsWritten++] = (uint)transforms.InstanceSlot( batch.FirstInstance + k );

						var stride = (uint)pooled.VertexCount;
						(visibilityFirst, vertexBase) = visibility is not null && stride < 1u << 24
							? tables.FrameVisibility( (batch.FirstInstance, batch.Mesh, batch.Draw.Mesh, batch.Draw.DrawCall), fragmentCount, pooled.VertexCount )
							: (GpuDrivenTables.NoVisibility, GpuDrivenTables.NoVisibility);
						kind = 4u | stride << 8;
					}
					else if ( batch.Owner is ResidentMeshes meshes )
					{
						// Resident meshes: their group's records (a header with the model's LODs, then each mesh's bounds, LOD
						// scale and transform slot), this draw's record, and a run of vertex bits each, made once
						if ( positions && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ) pooled = pooled with { GeometryRecord = pooled.PositionsRecord };
						var (vertexMaterial, pixelMaterial) = Records( tables, bucket, material, geometry.UncompressedTangentFrame, vertexAttributes, pixelAttributes );
						fragmentCount = meshes.Count;
						// A tinted model's draws each have their own entries, so records of their own
						var keys = meshes.VisibilityKeys[batch.Lod];
						var tinted = meshes.Tinted && (uint)batch.FirstArg < (uint)keys.Length;
						var slotOffset = tinted ? meshes.TintOffsets[batch.Lod] + batch.FirstArg : 0;
						if ( meshes.FadesLods ) device.WantLodFade();
						cullBatch.First = tables.FragmentsOf( tinted ? keys[batch.FirstArg] : meshes, 0, 0, 0, 0, fragmentCount + 1, (Meshes: meshes, Offset: slotOffset), static ( s, bytes ) => WriteResidentMeshes( s.Meshes, s.Offset, bytes ), releasable: true );
						cullBatch.Transform = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, pooled.FirstIndex + (uint)batch.Draw.StartIndex, (uint)batch.Draw.IndexCount, geometry.BaseVertex, pooled.IndexPageSlot );

						// Meshes that differ in their lighting: a draw record each, listed in the fragment table (bit 31)
						if ( batch.MeshLighting is { } meshLighting && meshLighting.Length == fragmentCount && (uint)batch.FirstArg < (uint)keys.Length )
							cullBatch.Transform = MeshLightingDraws( tables, keys[batch.FirstArg], meshLighting, bucket.Attributes, pooled, vertexMaterial, pixelMaterial, flags & 0xff, pooled.FirstIndex + (uint)batch.Draw.StartIndex, (uint)batch.Draw.IndexCount, geometry.BaseVertex ) | MeshDrawList;

						// A model without LODs of its own: its draw's generated levels, which the culls pick per mesh (not an
						// overlay's, which keeps the surface under it, nor a material's that places its vertices itself)
						if ( device.MeshLods && device.LodStore is { } lods && !cull.KeepsOrder && material.PlacesVertices != true && batch.Mesh.Geometry is BackendModel { GeneratesLevels: true } lodModel && lodModel.File.LodCount <= 1 )
						{
							var lodColor = r.Color is not null && !bucket.DepthOnly;
							if ( lodModel.FirstLodRequest( batch.Draw.Mesh, batch.Draw.DrawCall, lodColor ) ) RequestLods( lods, pooled, lodModel, batch.Draw, geometry.BaseVertex, lodColor );
						}

						var stride = (uint)pooled.VertexCount;
						(visibilityFirst, vertexBase) = visibility is not null && stride < 1u << 24 && (long)stride * fragmentCount < int.MaxValue && (uint)batch.FirstArg < (uint)keys.Length
							? tables.Visibility( keys[batch.FirstArg], fragmentCount, (int)stride * fragmentCount )
							: (GpuDrivenTables.NoVisibility, GpuDrivenTables.NoVisibility);
						// Bit 7: this draw cross-fades its LODs (its material dithers them), only below LOD 8 so the bit is free
						kind = 8u | (uint)batch.Lod << 4 | (meshes.FadesLods && batch.Lod < 8 && material.DithersLodFade ? 0x80u : 0) | stride << 8;
					}
					else
					{
						var aggregate = (AggregateObject)batch.Owner;
						fragmentCount = aggregate.Fragments.Length;

						// Its records as last frame's, when everything they were looked up by is the same; a ringing material's are
						// this frame's copies (GpuDrivenTables.Rings), looked up afresh
						ref var kept = ref memoTaken ? ref memo.At( record ) : ref unkept;
						var version = MaterialVersion( material );
						var visible = visibility is not null;
						var epoch = tables.VisibilityEpoch;
						var recordEpoch = tables.RecordEpoch;
						if ( memoTaken && ReferenceEquals( kept.Material, material ) && kept.Version == version && !tables.Rings( material ) && ReferenceEquals( kept.Stages, stages ) && kept.DepthOnly == bucket.DepthOnly && kept.PixelRecord == pixelRecord
							&& kept.VertexAttributes == vertexAttributes && kept.PixelAttributes == pixelAttributes && ReferenceEquals( kept.Owner, aggregate ) && kept.Fragments == fragmentCount
							&& kept.Flags == flags && kept.Pooled == pooled && ReferenceEquals( kept.Layout, geometry.Layout ) && ReferenceEquals( kept.Classic, bucket.Classic ) && kept.Positions == positions && kept.Visibility == visible && kept.VisibilityEpoch == epoch && kept.RecordEpoch == recordEpoch )
						{
							memoHits++;
						}
						else
						{
							var used = positions && ReadsOnlyPositions( bucket.Classic, geometry.Layout ) ? pooled with { GeometryRecord = pooled.PositionsRecord } : pooled;
							var (vertexMaterial, pixelMaterial) = Records( tables, bucket, material, geometry.UncompressedTangentFrame, vertexAttributes, pixelAttributes );
							var fragmentRecords = tables.FragmentsOf( aggregate, used.GeometryRecord, vertexMaterial, pixelMaterial, flags, fragmentCount,
								(Tables: tables, Aggregate: aggregate, Pooled: used, Vertex: vertexMaterial, Pixel: pixelMaterial, Flags: flags, Meshlets: device.MeshletStore, Model: batch.Mesh.Geometry as BackendModel, batch.Draw.Mesh, batch.Draw.DrawCall,
									Lods: material.PlacesVertices != true && batch.Mesh.Geometry is BackendModel { GeneratesLevels: true } ? device.LodStore : null, Color: r.Color is not null && !bucket.DepthOnly),
								static ( s, bytes ) =>
								{
									WriteFragments( s.Tables, s.Aggregate, s.Pooled, s.Vertex, s.Pixel, s.Flags, bytes );
									if ( s.Meshlets is not null && s.Model is not null ) RequestMeshlets( s.Meshlets, s.Aggregate, s.Pooled, s.Model, s.Mesh, s.DrawCall );
									if ( s.Lods is not null && s.Model is not null ) RequestLods( s.Lods, s.Aggregate, s.Pooled, s.Model, s.Mesh, s.DrawCall, s.Color );
								}, releasable: true, indexPage: used.IndexPageSlot );
							var (first, firstVertex) = visible ? tables.Visibility( aggregate, fragmentCount, pooled.VertexCount ) : (GpuDrivenTables.NoVisibility, GpuDrivenTables.NoVisibility);
							kept = new GpuDrivenRecordMemo.Slot
							{
								Material = material,
								Version = version,
								Stages = stages,
								DepthOnly = bucket.DepthOnly,
								PixelRecord = pixelRecord,
								VertexAttributes = vertexAttributes,
								PixelAttributes = pixelAttributes,
								Owner = aggregate,
								Fragments = fragmentCount,
								Flags = flags,
								Pooled = pooled,
								Layout = geometry.Layout,
								Classic = bucket.Classic,
								Positions = positions,
								Visibility = visible,
								Vertex = vertexMaterial,
								Pixel = pixelMaterial,
								FragmentRecords = fragmentRecords,
								VisibilityFirst = first,
								VertexBase = firstVertex,
								VisibilityEpoch = epoch,
								RecordEpoch = recordEpoch,
							};
						}

						cullBatch.First = kept.FragmentRecords;
						cullBatch.Transform = (uint)transforms.InstanceSlot( batch.FirstInstance );
						(visibilityFirst, vertexBase) = (kept.VisibilityFirst, kept.VertexBase);
						kind = 0;
					}

					cullBatch.Count = (uint)fragmentCount;
					cullBatch.Run = compact ? (uint)cullRuns.Count : (uint)written;
					cullBatch.FirstVisibility = vertexBase != GpuDrivenTables.NoVisibility ? visibilityFirst : GpuDrivenTables.NoVisibility;
					cullBatch.Moved = filter ? WriteMoved( batch, fragmentCount, movedWords, movedBase, ref movedWritten ) : 0;
					cullBatch.Flags = (bucket.Ids || bucket.ShadowMeshlets ? 1u : 0u) | (bucketLists ? 2u : 0u) | kind;
					cullBatch.FirstVertexBit = vertexBase;
					record++;
					written += fragmentCount;
					drawn[index] = true;
				}

				if ( written > runStart ) cullRuns.Add( (b, currentPage, currentFormat, runStart, written - runStart) );

				// Kept meshlets drawn by task and mesh shaders: a task run, a slot per fragment the culling lists (the task
				// shader reads each slot's instance record), then a run of 1 and 1 after its count, so that the count and it make
				// the task draw's group counts. The records' sixth word names the task run, with TaskRun set
				if ( bucketCompact && bucket.MeshTasks )
				{
					var taskRun = cullRuns.Count;
					cullRuns.Add( (b, default, IndexFormat.UInt32, compactWritten, TaskRunCount( bucket.Fragments )) );
					cullRuns.Add( (b, default, IndexFormat.UInt32, 1, TaskTail) );
					compactWritten += bucket.Fragments;
					for ( int i = record - bucket.Batches.Count; i < record; i++ ) cullBatches[i].Moved = (uint)taskRun | TaskRunBit;
				}

				// Compacted triangles: one more multi-draw for the bucket, from the compacted index buffer
				else if ( bucketCompact )
				{
					var compactRun = cullRuns.Count;
					var (slots32, slots16) = CompactSlots( bucket, meshlets, lists );
					cullRuns.Add( (b, compacted, IndexFormat.UInt32, compactWritten, slots32) );
					compactWritten += slots32;

					// Meshlets of ranges with 16-bit indices: the next multi-draw, from the 16-bit compacted buffer
					if ( meshlets )
					{
						cullRuns.Add( (b, compacted16, IndexFormat.UInt16, compactWritten, slots16) );
						compactWritten += slots16;
					}
					for ( int i = record - bucket.Batches.Count; i < record; i++ ) cullBatches[i].Moved = (uint)compactRun;
				}

				if ( checks is not null && b == prefixBuckets - 1 ) (prefixRecords, prefixWritten, prefixCompact, prefixRuns) = (record, written, compactWritten - totalFragments, cullRuns.Count);
			}
		}
		finally
		{
			if ( memoTaken ) System.Threading.Monitor.Exit( memo );
		}

		if ( memoHits > 0 ) System.Threading.Interlocked.Add( ref device.GpuDrivenRecordMemoHits, memoHits );
		if ( checks is not null ) KeepPlan( shaderMode, batches[..prefix], cull, color, depth, samples, totalFragments, checks, batchRecords[..(prefixRecords * (CullBatchBytes / 4))], drawn[..prefix], compacted, prefixWritten, prefixCompact, prefixRuns, filter ? movedWords : default, movedBase );
		if ( device.GpuDrivenRecordLog is { } log )
		{
			lock ( log ) log.Add( batchRecords.ToArray() );
		}

		SetObjectAttributes( null );

		// Each multi-draw's first slot, and its count from 0, which the culling counts up; a task run's tail 1 and 1
		var runs = MemoryMarshal.Cast<byte, MultiDraw>( data.Slice( runsOffset, compact ? cullRuns.Count * MultiDraw.Size : 0 ) );
		for ( int i = 0; i < runs.Length; i++ ) runs[i] = new MultiDraw { FirstSlot = (uint)cullRuns[i].First, Count = cullRuns[i].Count == TaskTail ? 1u : 0u };

		// The view record, with the Hi-Z tail's candidates where the prepass is culled in two phases
		var hizOffsets = hiz ? WriteHiZTail( data, hizTail, runs, hizFirstSlot ) : default;
		WriteCullView( cull, data, r.Color is null, hiz ? HiZBitsSlot( visibility ) : uint.MaxValue, hiz ? (uint)(frameOffset + hizOffsets.Candidates) : 0 );

		if ( deviceRegion )
		{
			CloseScope( flushClears: false );
			// The listed entries and output slots are GPU-written. Copy just the header and CPU inputs.
			r.List.CopyBuffer( upload.Buffer, upload.Offset, frameBuffer, frameOffset, listing ? headBytes : upload.Data.Length );
			if ( listing && tailBytes != 0 ) r.List.CopyBuffer( upload.Buffer, upload.Offset + headBytes, frameBuffer, frameOffset + transformSlotsOffset, tailBytes );
			BarrierCullInputs( r.List );
		}

		// The view's mask and stamps were cleared before the frame's segments (BeginGpuDrivenView)
		var region = new CulledRegion( frameBuffer, frameSlot, frameOffset, runsOffset, listedOffset, instanceOffset, fragmentOffset, commandOffset, firstInstance, compacted, compacted16, hizOffsets.Runs2, hizOffsets.Listed2, hizOffsets.Candidates, hizFirstSlot );
		DrawCulledRegion( cull, region, batchCount, compact, shaders, visibility, ids, lists, meshlets, depth, samples, depthBias, slopeScaledDepthBias, resolver, totalFragments, dispatch: true );
	}

	// The census, figure and Hi-Z counts read regions back through the CPU's mapping.
	bool DeviceCullRegions => !Gpu.Adapter.Integrated && !Census && device.Figure is null;

	static void BarrierCullInputs( CommandList list )
		=> list.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader | PipelineStage.DrawIndirect, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.IndirectRead );

	/// <summary>
	/// Where a GPU-culled call's region is in the frame's chunk, and its byte offsets in it: multi-draws, listed fragments,
	/// instance records, fragment words, commands; its first instance record's index; this frame's compacted buffer.
	/// </summary>
	internal readonly record struct CulledRegion( BufferHandle Buffer, int Slot, long Offset, int Runs, int Listed, int Instances, int FragmentWords, int Commands, uint FirstInstance, BufferHandle Compacted, BufferHandle Compacted16 = default,
		int Runs2 = 0, int Listed2 = 0, int Candidates = 0, int HiZFirstSlot = 0 )
	{
		/// <summary>Whether its prepass is culled in two phases: its view deferred candidates to the Hi-Z tail (<see cref="WriteHiZTail"/>).</summary>
		public bool HiZ => Candidates != 0;
	}

	/// <summary>
	/// Cull a call's region (<paramref name="dispatch"/>; otherwise the setup's resident cull did), compact forward's listed
	/// fragments, draw the prepass's ids and mark them, then draw <see cref="cullRuns"/> from the region's commands.
	/// </summary>
	void DrawCulledRegion( in GpuDrivenCull cull, in CulledRegion region, int batchCount, bool compact, (BackendMaterial Ids, ShaderFile Mark, ShaderFile Compact) shaders, BackendDevice.VisibilityView visibility, bool ids, bool lists, bool meshlets,
		Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver, int totalFragments, bool dispatch )
	{
		var r = recording;
		var tables = device.GpuDrivenTables;
		var cache = device.MaterialCache;
		var (frameBuffer, frameSlot, frameOffset) = (region.Buffer, region.Slot, region.Offset);
		var (runsOffset, listedOffset, instanceOffset, fragmentOffset, commandOffset) = (region.Runs, region.Listed, region.Instances, region.FragmentWords, region.Commands);
		var (firstInstance, compacted) = (region.FirstInstance, region.Compacted);
		var slots = tables.Slots;

		// Call state enables visibility, but the buckets decide whether there's any work for each pass.
		var (idsBuckets, listBuckets, computeMeshlets, taskMeshlets) = CullPaths( ids, lists, meshlets );
		var surfaceVisibility = lists;
		// The opt-in Hi-Z/figure/whole-ids paths also expose this work to diagnostics.
		ids &= idsBuckets || region.HiZ || device.IdsWhole || device.Figure is not null;
		lists &= listBuckets || device.HiZOcclusion || device.Figure is not null;
		var meshletPass = computeMeshlets || taskMeshlets;

		// Outside the rendering scope: the culling, the compaction, the meshlet pass and the ids' own rendering scope. A call
		// the setup culled for that draws no ids, lists or meshlets stays in the scope
		if ( dispatch || ids || lists || meshletPass ) CloseScope( flushClears: false );

		// A resident raster-only call already has commands. Only the cull and list compaction read these attributes.
		if ( dispatch || lists )
		{
			cullDrawer ??= new ShaderDrawer( device );
			cullAttributes ??= new ShaderAttributes();
			cullConstants ??= new byte[Marshal.SizeOf<GpuDrivenCullConstants>()];
			var lods = device.LodStore;
			var lodTable = lods?.Table ?? (-1, 0u);

			// Forward's compaction sets the visible bits of the view's next prepass (two-phase Hi-Z occlusion)
			var hizBits = lists && device.HiZOcclusion && visibility is not null && visibility.HiZVisible[visibility.HiZCurrent].IsValid;
			if ( hizBits ) visibility.HiZWrote = true;
			var fades = device.LodFades();
			MemoryMarshal.Write( cullConstants, new GpuDrivenCullConstants
			{
				Input = (uint)frameOffset,
				Batches = (uint)batchCount,
				Instances = (uint)(frameOffset + instanceOffset),
				Commands = (uint)(frameOffset + commandOffset),
				FirstInstance = firstInstance,
				Fragments = (uint)tables.FragmentSlot,
				Draws = (uint)slots.Draws,
				Runs = compact ? (uint)(frameOffset + runsOffset) : 0,
				Frame = device.GpuDrivenFrame,
				Capacity = visibility is { CompactedCapacity: > 0 } ? (uint)visibility.CompactedCapacity : BackendDevice.VisibilityCompactedIndices,
				Visibility = hizBits ? 1u : 0u,
				FragmentWords = (uint)(frameOffset + fragmentOffset) - firstInstance * 4,
				Listed = lists || meshletPass ? (uint)(frameOffset + listedOffset) : 0,
				MeshletRuns = meshletPass || lists && MeshletStampsReady( visibility ) ? (uint)device.MeshletStore.RunSlotCovering( tables.DrawRecords ) : uint.MaxValue,
				LodTable = lodTable.Slot >= 0 ? (uint)lodTable.Slot : uint.MaxValue,
				LodCount = lodTable.Records,
				Corners = lists && device.MeshletStore is { } cornerStore ? (uint)cornerStore.CornerSlotCovering( tables.DrawRecords ) : uint.MaxValue,
				LodFadeNow = device.LodFadeNow,
				LodFadeTicks = fades.Ticks,
			} );
			cullAttributes.Clear();
			cullAttributes.SetById( CullAttribute.Frame, frameBuffer );
			if ( CountsOnDevice( cull ) ) cullAttributes.SetById( CullAttribute.Stats, device.CullStats.Buffer );
			if ( lods is not null ) cullAttributes.SetById( CullAttribute.Levels, lods.State );
			if ( fades.States.IsValid ) cullAttributes.SetById( CullAttribute.LodFade, fades.States );
			if ( visibility is not null )
			{
				cullAttributes.SetById( CullAttribute.Stamps, visibility.Stamps );
				cullAttributes.SetById( CullAttribute.Mask, visibility.Mask );
				cullAttributes.SetById( CullAttribute.Compacted, lists ? compacted : visibility.Compacted );
				cullAttributes.SetById( CullAttribute.HiZVisible, hizBits ? visibility.HiZVisible[visibility.HiZCurrent] : visibility.Stamps );
				if ( MeshletStampsReady( visibility ) )
				{
					cullAttributes.SetById( CullAttribute.MeshletStamps, visibility.MeshletStamps );
					cullAttributes.SetById( CullAttribute.MeshletCompacted, visibility.PrepassCompacted );
					cullAttributes.SetById( CullAttribute.MeshletCompacted16, visibility.PrepassCompacted16 );
				}
			}

			cullAttributes.SetDataById( CullAttribute.CullConstants, cullConstants );
		}

		// View buffers append disjoint ranges until the next clear. Shared shadow buffers can be overwritten.
		if ( cull.Shadow && meshletPass ) r.List.Barrier( PipelineStage.VertexInput, MemoryAccess.IndexRead, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite );
		var timer = device.PassTimer;
		if ( dispatch )
		{
			var cullScope = timer?.Begin( r.List, ids ? "GPU-driven cull (ids)" : lists ? "GPU-driven cull (lists)" : "GPU-driven cull", PassTimer.ScopeKind.Own, count: false ) ?? -1;
			r.List.Marker( "GPU-driven cull" );
			cullDrawer.Dispatch( r.List, CullShader, cullAttributes, batchCount * 64, 1, 1 );
			r.List.Marker( "end GPU-driven cull" );
			timer?.End( r.List, cullScope );
		}

		// Forward's listed fragments: a group each, which counts its triangles that own a sample and draws them or the whole
		if ( lists )
		{
			if ( dispatch ) r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.ComputeShader, MemoryAccess.IndirectRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
			var compactScope = timer?.Begin( r.List, "GPU-driven compact", PassTimer.ScopeKind.Own, count: false ) ?? -1;
			r.List.Marker( "GPU-driven compact" );

			// The view's list of large fragments: none listed, then a group a fragment (the cull's arguments), each large one
			// listed instead; then a group a part of each listed, to count, decide and copy
			var largeList = visibility is not null ? device.CompactLargeFor( visibility ) : default;
			var large = largeList.IsValid;
			cullAttributes.SetById( Ids.BorealisCompactLarge, large ? largeList : compacted );
			if ( large )
			{
				r.List.Barrier( PipelineStage.ComputeShader | PipelineStage.DrawIndirect, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.IndirectRead, PipelineStage.Transfer, MemoryAccess.TransferWrite );
				r.List.UpdateBuffer<uint>( largeList, [0u, 1u, 1u, (uint)visibility.CompactParts, 0u, 1u, 1u, 0u, (uint)visibility.CompactLargeFragments, (uint)CompactBitsParts( visibility.CompactParts ), 0u, 0u] );
				r.List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
			}

			cullAttributes.SetComboById( CompactParts, large ? 1 : 0 );
			cullDrawer.DispatchIndirect( r.List, shaders.Compact, cullAttributes, frameBuffer, frameOffset + listedOffset );
			if ( large )
			{
				for ( int mode = 2; mode <= 4; mode++ )
				{
					r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader | PipelineStage.DrawIndirect, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.IndirectRead );
					cullAttributes.SetComboById( CompactParts, mode );
					cullDrawer.DispatchIndirect( r.List, shaders.Compact, cullAttributes, largeList, mode == 3 ? 16 : 0 );
				}

				cullAttributes.SetComboById( CompactParts, 0 );
			}

			r.List.Marker( "end GPU-driven compact" );
			timer?.End( r.List, compactScope );
		}

		// The prepass's listed fragments: a group each, which draws its meshlets that can reach a sample (buckets drawn with
		// task and mesh shaders list none)
		if ( computeMeshlets ) DispatchMeshlets( cull, region, visibility, samples, dispatch );

		// Only the culling's writes before the draws' reads of them: the draws before it needn't drain
		var readers = PipelineStage.DrawIndirect | PipelineStage.VertexInput | PipelineStage.VertexShader | (taskMeshlets ? PipelineStage.TaskShader | PipelineStage.MeshShader : 0);
		if ( dispatch || lists || meshletPass ) r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, readers, MemoryAccess.IndirectRead | MemoryAccess.IndexRead | MemoryAccess.ShaderRead );
		r.Pipeline = default;
		r.Binder = null;

		var vertexBases = ids ? (uint)(frameOffset + fragmentOffset) - firstInstance * 4 : (uint)(frameOffset + instanceOffset) - firstInstance * 4;
		SetConstants( GpuDrivenConstantsName, new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials, VertexBases = vertexBases, Lighting = (uint)slots.Lighting } );

		// The prepass's id buckets first, into the ids and depth, then the triangles they show are marked. Culled in two
		// phases: the fragments forward saw last frame, the Hi-Z from their depth, then the rest that pass it
		idsCall = default;
		if ( ids ) AddIdsCall( visibility, frameSlot, firstInstance, (fragmentOffset - instanceOffset) / GpuDrivenTables.InstanceStride, vertexBases );
		if ( ids && region.HiZ )
		{
			DrawIds( cull, visibility, shaders, frameBuffer, frameOffset, commandOffset, runsOffset, firstInstance, depth, samples, depthBias, slopeScaledDepthBias, resolver, mark: false );
			HiZSecondPhase( cull, region, visibility, samples, computeMeshlets );
			r.Pipeline = default;
			r.Binder = null;
			SetConstants( GpuDrivenConstantsName, new GpuDrivenConstants { Instances = (uint)frameSlot, Draws = (uint)slots.Draws, Geometry = (uint)slots.Geometry, Materials = (uint)slots.Materials, VertexBases = vertexBases, Lighting = (uint)slots.Lighting } );
			DrawIds( cull, visibility, shaders, frameBuffer, frameOffset, commandOffset + region.HiZFirstSlot * GpuDrivenTables.CommandStride, region.Runs2, firstInstance, depth, samples, depthBias, slopeScaledDepthBias, resolver, clearIds: false );
		}
		else if ( ids ) DrawIds( cull, visibility, shaders, frameBuffer, frameOffset, commandOffset, runsOffset, firstInstance, depth, samples, depthBias, slopeScaledDepthBias, resolver );

		// A depth-only call that drew all its runs with ids has no ordinary rendering to reopen.
		if ( (suspendScopes || clearColor || clearDepth) && !EnsureScope() ) return;

		var color = r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var bound = false;
		var lastBucket = -1;
		for ( int i = 0; i < cullRuns.Count; i++ )
		{
			var run = cullRuns[i];
			var bucket = gpuDrivenBuckets[run.Bucket];
			if ( bucket.Ids ) continue;
			if ( !r.Rendering && !EnsureScope() ) return;
			if ( run.Bucket != lastBucket )
			{
				lastBucket = run.Bucket;
				SetObjectAttributes( bucket.Own ?? (bucket.PixelMaterial != 0 ? device.GpuDrivenMaterialParameters( bucket.PixelMaterial ) : null), bucket.Attributes );
				NextPassState( cache );
				resolver.Begin( passEpoch );
				bound = false;
			}

			if ( Census ) censusCompacted = run.First >= totalFragments;
			surfaceDepthFromIds = surfaceVisibility;
			DrawRun( ref bound, bucket, color, depth, samples, depthBias, slopeScaledDepthBias, resolver, run.Page, run.Format, frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, run.Count, compact ? frameOffset + runsOffset + i * MultiDraw.Size + MultiDraw.CountOffset : -1 );
			censusCompacted = false;
			surfaceDepthFromIds = false;
		}

		// Debug only: the forward pass's multi-draws again, into the figure's targets
		if ( device.Figure is { } figure && r.Color is not null && !cull.Shadow && compact && device.FigureMaterial is { } figureMaterial )
			DrawFigure( figure, figureMaterial, visibility, frameBuffer, frameOffset, commandOffset, runsOffset, lists ? listedOffset : -1, depth, resolver );

		SetObjectAttributes( null );
		r.Binder = null;
	}

	byte[] meshletConstants;
	ShaderAttributes meshletAttributes;
	byte[] hizConstants;
	ShaderAttributes hizAttributes;

	/// <summary>
	/// Two-phase Hi-Z occlusion's middle (docs/reference/borealis-gpu-driven.md, "Hi-Z occlusion"), between a prepass's first
	/// and second ids: <c>borealis_hiz_cs</c> builds the view's Hi-Z from the depth the first phase drew, then culls the
	/// candidates the first phase's culls deferred against it, a thread each, into the second phase's multi-draws and listed
	/// fragments, and the meshlet pass culls the listed ones. Where the depth target isn't the view's own (another size, an
	/// array, a viewport off its corner), every candidate draws.
	/// </summary>
	void HiZSecondPhase( in GpuDrivenCull cull, in CulledRegion region, BackendDevice.VisibilityView visibility, int samples, bool computeMeshlets )
	{
		using var zone = Zones.HiZ.Start();
		var r = recording;
		var shader = device.HiZShader;
		var depthTexture = r.Depth;
		var test = depthTexture is not null && !depthTexture.IsArray && !depthTexture.IsCube && depthTexture.Width == visibility.Width && depthTexture.Height == visibility.Height
			&& bound.DepthSlice == 0 && bound.Viewport.Left == 0 && bound.Viewport.Top == 0 && visibility.HiZ.IsValid;
		var m = cull.WorldToClip;
		var constants = new HiZConstants
		{
			Width = (uint)visibility.HiZWidth,
			Height = (uint)visibility.HiZHeight,
			Levels = (uint)visibility.HiZLevels,
			DepthWidth = (uint)visibility.Width,
			DepthHeight = (uint)visibility.Height,
			Samples = (uint)Math.Max( samples, 1 ),
			Candidates = (uint)(region.Offset + region.Candidates),
			Runs = (uint)(region.Offset + region.Runs2),
			Listed = computeMeshlets ? (uint)(region.Offset + region.Listed) : 0,
			Listed2 = (uint)(region.Offset + region.Listed2),
			Instances = (uint)(region.Offset + region.Instances),
			Commands = (uint)(region.Offset + region.Commands),
			FirstInstance = region.FirstInstance,
			FragmentWords = (uint)(region.Offset + region.FragmentWords) - region.FirstInstance * 4,
			Frame = device.GpuDrivenFrame,
			Flags = (test ? 1u : 0u) | (MeshletStampsReady( visibility ) ? 2u : 0u),
			Viewport = new System.Numerics.Vector4( cull.ViewportSize.x, cull.ViewportSize.y, bound.MinZ, bound.MaxZ ),
			ClipX = new System.Numerics.Vector4( m.M11, m.M21, m.M31, m.M41 ),
			ClipY = new System.Numerics.Vector4( m.M12, m.M22, m.M32, m.M42 ),
			ClipZ = new System.Numerics.Vector4( m.M13, m.M23, m.M33, m.M43 ),
			ClipW = new System.Numerics.Vector4( m.M14, m.M24, m.M34, m.M44 ),
		};

		hizConstants ??= new byte[Marshal.SizeOf<HiZConstants>()];
		hizAttributes ??= new ShaderAttributes();
		cullDrawer ??= new ShaderDrawer( device );
		var frameBuffer = region.Buffer;
		void Bind( in HiZConstants values, bool fromDepth )
		{
			MemoryMarshal.Write( hizConstants, values );
			hizAttributes.Clear();
			hizAttributes.SetById( Ids.BorealisHiZ, visibility.HiZ );
			hizAttributes.SetById( Ids.BorealisCullFrame, frameBuffer );
			hizAttributes.SetById( Ids.BorealisCullStamps, visibility.Stamps );
			hizAttributes.SetById( Ids.BorealisMeshletStamps, (values.Flags & 2) != 0 ? visibility.MeshletStamps : visibility.Stamps );
			hizAttributes.SetCombo( "D_HIZ_DEPTH", fromDepth ? 1 : 0 );
			hizAttributes.SetCombo( "D_MSAA", fromDepth && samples > 1 ? 1 : 0 );
			if ( fromDepth ) hizAttributes.SetById( Ids.BorealisHiZDepth, depthTexture.Handle );
			hizAttributes.SetData( "BorealisHiZ_t", hizConstants );
		}

		// Once a frame in each list: a later call of the view in the same list (its other GPU-driven calls) tests against the
		// first's Hi-Z, which the depth since has only brought nearer, so it stays conservative
		var timer = device.PassTimer;
		var built = visibility.HiZBuiltFrame == device.GpuDrivenFrame && ReferenceEquals( visibility.HiZBuiltBy, r.List );
		if ( test && !built )
		{
			(visibility.HiZBuiltFrame, visibility.HiZBuiltBy) = (device.GpuDrivenFrame, r.List);
			// The depth the first phase drew, read by compute, a thread per pixel: levels 0 to 2 from it, then five levels a
			// dispatch from the last
			var buildScope = timer?.Begin( r.List, "Hi-Z build", PassTimer.ScopeKind.Own, count: false ) ?? -1;
			r.List.Marker( "Hi-Z build" );
			Transition( depthTexture, ImageLayout.ShaderRead );
			r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderWrite );
			constants.Pass = 0;
			Bind( constants, fromDepth: true );
			cullDrawer.Dispatch( r.List, shader, hizAttributes, visibility.Width, visibility.Height, 1 );
			for ( int source = 2; source + 1 < visibility.HiZLevels; source += 5 )
			{
				r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
				var (w, h) = (visibility.HiZWidth, visibility.HiZHeight);
				for ( int l = 0; l <= source; l++ ) (w, h) = ((w + 1) / 2, (h + 1) / 2);
				constants.Pass = 1;
				constants.Source = (uint)source;
				Bind( constants, fromDepth: false );
				cullDrawer.Dispatch( r.List, shader, hizAttributes, w, h, 1 );
			}

			Transition( depthTexture, ImageLayout.DepthTarget );
			r.List.Marker( "end Hi-Z build" );
			timer?.End( r.List, buildScope );
		}

		// After the first phase's cull and the Hi-Z's writes
		var cullScope = timer?.Begin( r.List, "Hi-Z cull", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		r.List.Marker( "Hi-Z cull" );
		r.List.Barrier( PipelineStage.ComputeShader | PipelineStage.DrawIndirect | PipelineStage.VertexInput | PipelineStage.VertexShader | PipelineStage.FragmentShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.IndirectRead | MemoryAccess.IndexRead,
			PipelineStage.ComputeShader | PipelineStage.DrawIndirect, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.IndirectRead );
		constants.Pass = 2;
		constants.Source = 0;
		Bind( constants, fromDepth: false );
		cullDrawer.DispatchIndirect( r.List, shader, hizAttributes, region.Buffer, region.Offset + region.Candidates );
		r.List.Marker( "end Hi-Z cull" );
		timer?.End( r.List, cullScope );

		// The second phase's listed fragments' meshlets, into its multi-draws
		if ( computeMeshlets )
		{
			r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.ComputeShader, MemoryAccess.IndirectRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
			DispatchMeshlets( cull, region with { Runs = region.Runs2, Listed = region.Listed2 }, visibility, samples, culledHere: false );
		}

		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.VertexInput | PipelineStage.VertexShader | PipelineStage.FragmentShader, MemoryAccess.IndirectRead | MemoryAccess.IndexRead | MemoryAccess.ShaderRead );
		device.CountHiZ( region.Buffer, region.Offset + region.Candidates, test );
	}

	/// <summary>
	/// Whether a view's meshlet bits can be read: its prepass culled meshlets since its stamps were cleared, so forward's
	/// compaction scans only the meshlets the prepass kept where it stamped them this frame.
	/// </summary>
	bool MeshletStampsReady( BackendDevice.VisibilityView visibility ) => visibility is { MeshletStampsCleared: true } && device.MeshletStore is not null
		&& visibility.PrepassCompacted.IsValid && visibility.PrepassCompacted16.IsValid && visibility.MeshletStamps.IsValid;

	/// <summary>Chunks of 1024 meshlets a listed fragment has at most (4096 meshlets), each with a compacted slot of its own.</summary>
	internal const uint MeshletChunks = 4;

	/// <summary>Meshlets in one chunk of <c>borealis_meshlet_cull_cs</c>, which draws each chunk's kept ones with a command of its own.</summary>
	const uint ChunkMeshlets = 1024;

	/// <summary>
	/// Compacted slots a fragment of <paramref name="indexCount"/> indices can take: a chunk of its meshlets each, at most
	/// <see cref="MeshletChunks"/>. Every meshlet has a triangle, so it has no more chunks than its triangles fill; one at least.
	/// </summary>
	internal static int MeshletChunksOf( uint indexCount ) => (int)Math.Clamp( (Math.Min( indexCount / 3, MeshletChunks * ChunkMeshlets ) + ChunkMeshlets - 1) / ChunkMeshlets, 1u, MeshletChunks );

	/// <summary>Count a batch's fragments' meshlet chunks (<see cref="MeshletChunksOf"/>) into its bucket, by its index size.</summary>
	void CountMeshletChunks( GpuDrivenBucket bucket, in GpuDrivenBatch batch, int fragments, IndexFormat format )
	{
		var indexCount = !batch.Instances && batch.Owner is AggregateObject aggregate ? aggregate.MaxIndexCount : (uint)Math.Max( batch.Draw.IndexCount, 0 );
		var chunks = fragments * MeshletChunksOf( indexCount );
		if ( format == IndexFormat.UInt32 ) bucket.MeshletChunks32 += chunks;
		else bucket.MeshletChunks16 += chunks;
	}

	/// <summary>
	/// Slots a bucket's compacted multi-draws hold, 32-bit and 16-bit: with meshlets, a slot per chunk its fragments' meshlets
	/// can make, by their index size (a run one at least; a 32-bit one also every fragment where forward lists compact into
	/// it); else forward's lists' one per fragment.
	/// </summary>
	static (int Slots32, int Slots16) CompactSlots( GpuDrivenBucket bucket, bool meshlets, bool lists )
		=> meshlets ? (Math.Max( lists ? bucket.Fragments : 1, bucket.MeshletChunks32 ), Math.Max( 1, bucket.MeshletChunks16 )) : (bucket.Fragments, 0);

	/// <summary><c>borealis_meshlet_cull_cs</c>'s pass that tests and places a shadow call's meshlets (side and far planes only).</summary>
	const uint ShadowMeshletPass = 2;

	/// <summary>
	/// Where a call that culls meshlets puts its kept ones' indices, 32-bit and 16-bit: a prepass in its view's prepass
	/// compacted buffers, a shadow call (no visibility) in the shadow views' shared ones.
	/// </summary>
	(BufferHandle Compacted, BufferHandle Compacted16) MeshletCompacted( BackendDevice.VisibilityView visibility )
	{
		if ( visibility is null )
		{
			var shadow = device.ShadowMeshletBuffersNow();
			return (shadow.Compacted, shadow.Compacted16);
		}

		var compacted = device.PrepassCompactedFor( visibility );
		return (compacted, visibility.PrepassCompacted16);
	}

	/// <summary>
	/// <c>borealis_meshlet_cull_cs</c> over a prepass's listed fragments, a group each (an indirect dispatch, counted up by
	/// its culling): each tests its meshlets against the view a chunk at a time and draws the kept ones from room of the
	/// chunk's own in the view's prepass compacted buffer, writing the instance record, command and ids fragment word (or
	/// the fragment whole, or nothing), stamps what a fragment of one chunk drew for its forward compaction and leaves a copy
	/// job per kept meshlet; then a copy dispatch, a group per job, copies their indices there.
	/// </summary>
	void DispatchMeshlets( in GpuDrivenCull cull, in CulledRegion region, BackendDevice.VisibilityView visibility, int samples, bool culledHere )
	{
		using var zone = Zones.MeshletCull.Start();
		var r = recording;
		var m = cull.WorldToClip;

		// A shadow call (no visibility): the shared shadow buffers, from their start; no fragment words, stamps or sample grid
		var shadow = visibility is null ? device.ShadowMeshletBuffersNow() : null;
		var (stamps, jobs) = shadow is not null ? (shadow.Stamps, shadow.Jobs) : (visibility.MeshletStamps, visibility.MeshletJobs);
		var constants = new MeshletCullConstants
		{
			Listed = (uint)(region.Offset + region.Listed),
			Instances = (uint)(region.Offset + region.Instances),
			Commands = (uint)(region.Offset + region.Commands),
			FirstInstance = region.FirstInstance,
			Runs = (uint)(region.Offset + region.Runs),
			Draws = (uint)device.GpuDrivenTables.Slots.Draws,
			RunTable = (uint)device.MeshletStore.RunSlotCovering( device.GpuDrivenTables.DrawRecords ),
			Capacity = shadow is not null ? (uint)shadow.Capacity : visibility is { PrepassCapacity: > 0 } ? (uint)visibility.PrepassCapacity : BackendDevice.PrepassCompactedIndices,
			FragmentWords = shadow is not null ? uint.MaxValue : (uint)(region.Offset + region.FragmentWords) - region.FirstInstance * 4,
			Samples = shadow is not null ? 0u : (uint)samples,
			Frame = device.GpuDrivenFrame,
			Viewport = new System.Numerics.Vector4( cull.ViewportSize.x, cull.ViewportSize.y, BackendDevice.MeshletJobCapacity, 0 ),
			ClipX = new System.Numerics.Vector4( m.M11, m.M21, m.M31, m.M41 ),
			ClipY = new System.Numerics.Vector4( m.M12, m.M22, m.M32, m.M42 ),
			ClipZ = new System.Numerics.Vector4( m.M13, m.M23, m.M33, m.M43 ),
			ClipW = new System.Numerics.Vector4( m.M14, m.M24, m.M34, m.M44 ),
		};

		meshletConstants ??= new byte[Marshal.SizeOf<MeshletCullConstants>()];
		meshletAttributes ??= new ShaderAttributes();
		var (frameBuffer, compacted, compacted16) = (region.Buffer, region.Compacted, region.Compacted16);
		void Bind()
		{
			MemoryMarshal.Write( meshletConstants, constants );
			meshletAttributes.Clear();
			meshletAttributes.SetById( Ids.BorealisCullFrame, frameBuffer );
			meshletAttributes.SetById( Ids.BorealisMeshletCompacted, compacted );
			meshletAttributes.SetById( Ids.BorealisMeshletCompacted16, compacted16 );
			meshletAttributes.SetById( Ids.BorealisMeshletStamps, stamps );
			meshletAttributes.SetById( Ids.BorealisMeshletJobs, jobs );
			meshletAttributes.SetById( Ids.g_TransformBuffer, LentInstances.Transforms );
			meshletAttributes.SetData( "BorealisMeshletCull_t", meshletConstants );
		}

		var timer = device.PassTimer;
		var scope = timer?.Begin( r.List, "GPU-driven meshlets", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		r.List.Marker( "GPU-driven meshlets" );

		// No copy jobs yet: after an earlier call's copy read them (and, sharing a shadow call's cursors, its pass counted them up)
		r.List.Barrier( PipelineStage.ComputeShader | PipelineStage.DrawIndirect, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.IndirectRead, PipelineStage.Transfer, MemoryAccess.TransferWrite );
		// Only X changes between calls; the view initializes Y and Z alongside its meshlet stamps.
		r.List.FillBuffer( jobs, 0, 0, 4 );

		// A shadow call's kept triangles from the start of the shared buffers: the calls before it have drawn from them
		if ( shadow is not null )
		{
			r.List.FillBuffer( jobs, 1, 4, 8 );
			r.List.FillBuffer( jobs, 0, 12, 4 );
			r.List.FillBuffer( compacted, BackendDevice.VisibilityView.CompactedCursorIndices, 0, 4 );
			r.List.FillBuffer( compacted16, BackendDevice.VisibilityView.CompactedCursorIndices, 0, 4 );
		}
		r.List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader | PipelineStage.DrawIndirect, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.IndirectRead );

		// After the culling that listed them, in this list or the setup's: test and place, then copy
		if ( culledHere ) r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.ComputeShader, MemoryAccess.IndirectRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		constants.Pass = shadow is not null ? ShadowMeshletPass : 0;
		Bind();
		cullDrawer.DispatchIndirect( r.List, device.MeshletCullShader, meshletAttributes, region.Buffer, region.Offset + region.Listed );
		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.DrawIndirect | PipelineStage.ComputeShader, MemoryAccess.IndirectRead | MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
		constants.Pass = 1;
		Bind();
		cullDrawer.DispatchIndirect( r.List, device.MeshletCullShader, meshletAttributes, jobs, 0 );
		r.List.Marker( "end GPU-driven meshlets" );
		timer?.End( r.List, scope );
	}

	/// <summary>Whether resident calls may be drawn from plans now: not while a census or the record log watches each call.</summary>
	bool UsesPlans => device.DrawsAggregatesResident && !Census && device.GpuDrivenRecordLog is null && CullShader is not null;

	/// <summary>
	/// What a GPU-culled call of <paramref name="cull"/>'s view does with the visibility-driven forward pass: whether it
	/// compacts, the shaders, the view's state, and whether it draws ids (a depth prepass) or lists (a pass after it). Only
	/// once the view's state was cleared this frame, before any call that writes into it; the first frame draws plainly.
	/// </summary>
	(bool Compact, (BackendMaterial Ids, ShaderFile Mark, ShaderFile Compact) Shaders, BackendDevice.VisibilityView Visibility, bool Ids, bool Lists) CullState( in GpuDrivenCull cull )
	{
		var r = recording;
		var compact = !cull.KeepsOrder && device.GpuDrivenCompaction && device.SupportsDrawIndirectCount;
		var shaders = compact && device.GpuDrivenVisibility && cull.View is not null && (cull.Ids || cull.Visibility) ? device.GpuDrivenVisibilityShaders : default;
		var visibility = shaders.Mark is not null && shaders.Compact is not null && r.Depth is not null ? device.VisibilityFor( cull.View, Gpu.InfoOf( r.Depth.Handle ) ) : null;
		var ready = visibility is not null && visibility.ClearedFrame == device.GpuDrivenFrame;

		// A prepass draws ids once the view's ids targets are there, which only prepasses that draw ids ask for
		var ids = visibility is not null && cull.Ids && r.Color is null && device.IdsTargetsFor( visibility ) && ready;
		var lists = ready && cull.Visibility;
		if ( lists ) visibility.ListsFrame = visibility.ClearedFrame;
		return (compact, shaders, visibility, ids, lists);
	}

	/// <summary>
	/// Whether a GPU-culled call culls meshlets (docs/reference/borealis-gpu-driven.md, "Meshlets"): a prepass that draws ids,
	/// or with <see cref="BackendDevice.ShadowMeshlets"/> a shadow view's depth call, with no resident filter, while
	/// <see cref="BackendDevice.Meshlets"/> is on and its pass module draws. Its culling then lists the fragments whose
	/// draws have meshlets, and <c>borealis_meshlet_cull_cs</c> draws their kept meshlets.
	/// </summary>
	bool MeshletsFor( in GpuDrivenCull cull, bool ids ) => (ids || ShadowMeshletsFor( cull )) && cull.Filter == ResidentFilter.None && device.MeshletStore is not null && device.MeshletCullShader is not null;

	/// <summary>
	/// A shadow view's depth call that may cull meshlets: one that compacts (not keeping its order), into depth alone. Casters
	/// a resident filter keeps by when they moved (the far cascade's cache) draw whole: their records' sixth word is the filter's.
	/// </summary>
	bool ShadowMeshletsFor( in GpuDrivenCull cull ) => cull.Shadow && device.ShadowMeshlets && !cull.KeepsOrder && recording?.Color is null;

	/// <summary>
	/// Whether a call that culls meshlets draws some with task and mesh shaders: <c>borealis_meshlet_ids</c> takes its buckets
	/// drawn with <c>borealis_gpu_driven_ids</c> where the device has them (<see cref="BackendDevice.MeshletIds"/>).
	/// </summary>
	bool MeshTasksFor( in GpuDrivenCull cull, bool ids ) => ids && MeshletsFor( cull, ids ) && device.MeshletIds is not null;

	/// <summary>What the call's buckets can draw or list, including buckets copied from a kept plan.</summary>
	(bool Ids, bool Lists, bool ComputeMeshlets, bool TaskMeshlets) CullPaths( bool ids, bool lists, bool meshlets )
	{
		var (hasIds, hasLists, compute, tasks) = (false, false, false, false);
		if ( ids || lists || meshlets )
		{
			for ( int b = 0; b < gpuDrivenBucketCount; b++ )
			{
				var bucket = gpuDrivenBuckets[b];
				hasIds |= bucket.Ids;
				hasLists |= !bucket.DepthOnly;
				if ( meshlets && bucket.ShadowMeshlets ) compute = true;
				if ( !meshlets || !bucket.PrepassMeshlets ) continue;
				if ( bucket.MeshTasks ) tasks = true;
				else compute = true;
			}
		}
		return (hasIds, hasLists, compute, tasks);
	}

	/// <summary>
	/// Whether a view's prepass with ids is culled in two phases (<see cref="BackendDevice.HiZOcclusion"/>): a main view (no
	/// shadow, filter or kept order) whose Hi-Z buffers are made, without task and mesh shaders, of at least
	/// <see cref="BackendDevice.HiZMinFragments"/> fragments. The call that draws it builds the Hi-Z where it can, and otherwise draws every
	/// candidate.
	/// </summary>
	bool HiZFor( in GpuDrivenCull cull, BackendDevice.VisibilityView visibility, int fragments )
		=> device.HiZOcclusion && fragments >= device.HiZMinFragments && !cull.Shadow && cull.Filter == ResidentFilter.None && !cull.KeepsOrder && device.MeshletIds is null && device.HiZShader is not null
		&& visibility is not null && visibility.HiZVisible[0].IsValid && visibility.HiZVisible[1].IsValid && visibility.HiZ.IsValid && HiZBitsSlot( visibility ) != uint.MaxValue;

	/// <summary>The bindless slot of the visible bits a view's prepass reads this frame: those its forward wrote last frame.</summary>
	uint HiZBitsSlot( BackendDevice.VisibilityView visibility ) => Gpu.BindlessIndex( visibility.HiZVisible[visibility.HiZCurrent ^ 1] ) is >= 0 and var slot ? (uint)slot : uint.MaxValue;

	/// <summary>A GPU-culled call's kind, for its plan: what decides its buckets and records besides the batches.</summary>
	GpuDrivenPlan.CallKey PlanKey( StringToken shaderMode, in GpuDrivenCull cull, Format color, Format depth, int samples )
	{
		var (compact, _, visibility, ids, lists) = CullState( cull );
		return new GpuDrivenPlan.CallKey( shaderMode.Value, color, depth, samples, recording.Color is null, ids, lists, visibility is not null, device.PositionStreams, cull.KeepsOrder, cull.Filter, MeshletsFor( cull, ids ), MeshTasksNow( cull, compact, ids, visibility ), depth.HasStencil() ? OverlayStencil : OverlayStencil.None );
	}

	/// <summary>
	/// Whether this call's id buckets draw kept meshlets with task and mesh shaders, as <see cref="DrawGpuCulled"/> decides it:
	/// the device's paths, and this view's prepass compacted and meshlets kept. A plan is only for calls that decided the same,
	/// since its task runs have no index page: a view without them (a cube capture's face, before its first frame) drew them as
	/// index runs.
	/// </summary>
	bool MeshTasksNow( in GpuDrivenCull cull, bool compact, bool ids, BackendDevice.VisibilityView visibility )
		=> compact && MeshTasksFor( cull, ids ) && visibility is not null && device.PrepassCompactedFor( visibility ).IsValid && visibility.MeshletKept;

	/// <summary>
	/// The plan an earlier call of this kind made of a resident list's <paramref name="prefix"/> (<see cref="GpuDrivenPlan"/>),
	/// when there's one for this version of the list and nothing it was made from has changed; null otherwise (a stale one is
	/// dropped), and the call makes it again.
	/// </summary>
	GpuDrivenPlan CurrentPlan( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> prefix, in GpuDrivenCull cull )
	{
		var r = recording;
		var color = r.Color is null ? Format.Unknown : FormatOfTarget( r.Color, r.Srgb );
		var depth = r.Depth is null ? Format.Unknown : BackendDevice.FormatOf( r.Depth );
		var samples = (r.Color ?? r.Depth).Samples;

		var key = PlanKey( shaderMode, cull, color, depth, samples );

		// What the next frame's resident cull culls for this view; a call that keeps its order culls itself
		if ( device.GpuDrivenResidentCull && cull.View is not null && !cull.KeepsOrder )
			device.ResidentCulls.Seen( cull.View, cull.Plan, key, device.GpuDrivenFrame, cull.Shadow ? ResidentCullState.KeptShadowFrames : ResidentCullState.KeptFrames );

		var plans = device.PlansFor( cull.Plan );
		var plan = plans.Find( key, cull.PlanVersion, prefix.Length, device.GpuDrivenTables );
		if ( plan is null ) return null;
		if ( PlanCurrent( plan, shaderMode ) ) return plan;

		plans.Drop( key );
		return null;
	}

	/// <summary>
	/// Whether a plan still draws what its call would: each material's version is the one its records were written at, and
	/// each bucket's first batch takes a variant that draws the same way under the pass's attributes now, whose stages read
	/// the same attribute values into their records. Once a check has passed in full, what its buckets read of the pass's
	/// attributes (<see cref="GpuDrivenPlanReads"/>) being the same answers for them.
	/// </summary>
	bool PlanCurrent( GpuDrivenPlan plan, StringToken shaderMode )
	{
		var tables = device.GpuDrivenTables;
		if ( tables.AnyRings )
		{
			foreach ( var (material, _) in plan.Materials ) if ( tables.Rings( material ) ) return false;
			foreach ( var (material, _) in plan.SurfaceMaterials ) if ( tables.Rings( material ) ) return false;
			foreach ( var material in plan.OtherMaterials ) if ( tables.Rings( material ) ) return false;
		}

		// The rest once a frame: a frame's calls share a plan (every face of every point light's map draws one), and the
		// records a newer version replaces are kept until the next frame begins, so the calls after a change that lands
		// mid-frame draw as the frame's earlier ones did
		var frame = device.GpuDrivenFrame;
		if ( System.Threading.Volatile.Read( ref plan.CheckedFrame ) != frame )
		{
			foreach ( var (material, version) in plan.Materials )
			{
				if ( MaterialVersion( material ) != version ) return false;
			}

			foreach ( var (material, version) in plan.SurfaceMaterials )
			{
				if ( material.Version != version ) return false;
			}

			foreach ( var (attributes, version) in plan.Attributes )
			{
				if ( attributes.Version != version ) return false;
			}

			// Its records name its models' pages: a model freed since (its pages given back) makes it again without it, and the
			// rest are noted as drawn this frame
			foreach ( var model in plan.Models )
			{
				if ( !model.Use() ) return false;
			}

			foreach ( var geometry in plan.Geometries )
			{
				if ( !geometry.Use() ) return false;
			}

			System.Threading.Volatile.Write( ref plan.CheckedFrame, frame );
		}

		var cache = device.MaterialCache;

		// The rest is the buckets', which read nothing else that changes but the pass's attributes and surfaces' programs:
		// with those the same as before a check that passed in full, they check the same
		var reads = System.Threading.Volatile.Read( ref plan.Reads );
		if ( reads is not null && PlanReadsCurrent( reads ) )
		{
			// Left as a full check leaves them
			if ( plan.Checks.Length > 0 ) NextPassState( cache );
			SetObjectAttributes( null );
			return true;
		}

		comboLookup ??= name => LookupCombo( Token( name ) );
		specializationLookup ??= name => Integer( Lookup( Token( name ) ) );
		var current = true;
		for ( int b = 0; b < plan.Checks.Length && current; b++ )
		{
			var (first, attributes, variant, vertexAttributes, pixelAttributes) = plan.Checks[b];
			var bucket = plan.Buckets[b];
			SetObjectAttributes( null, attributes );
			NextPassState( cache );

			// A surface's: the same GPU-driven pass of its program under the pass's attributes now
			if ( bucket.Surface is not null )
			{
				current = first is SurfaceMaterial surface && surface.RenderState == bucket.SurfaceState && device.Surfaces.For( surface ) is { } program && ReferenceEquals( program, bucket.SurfaceProgram )
					&& ReferenceEquals( program.GpuDrivenPass( bucket.NoColor, bucket.SurfaceState.AlphaTest, LookupCombo( LightmapCombo ) is > 0, bucket.SurfaceState.DrawsTranslucent ), bucket.Surface );
				continue;
			}

			if ( first is not BackendMaterial material )
			{
				current = false;
				break;
			}

			var mode = CachedModeName( material.Shader, shaderMode );
			var now = mode is null ? null : material.GpuDrivenVariant( device.Shaders, mode, ComboKey( material, mode ), comboLookup, specializationLookup );
			if ( !SameDrawing( now, variant ) )
			{
				current = false;
				break;
			}

			if ( !ReferenceEquals( attributes, bucket.Attributes ) )
			{
				SetObjectAttributes( null, bucket.Attributes );
				NextPassState( cache );
			}

			var pixelRecord = !bucket.DepthOnly || bucket.Ids && !bucket.IdsShader;
			current = (bucket.Variant.Vertex is { } vertexStage ? AttributeHash( vertexStage ) : 0) == vertexAttributes
				&& (pixelRecord && bucket.Variant.Pixel is { } pixelStage ? AttributeHash( pixelStage ) : 0) == pixelAttributes;
		}

		SetObjectAttributes( null );
		if ( !current ) return false;

		// What it read is kept from the second check on: a plan that doesn't pass its first isn't worked out for it
		if ( reads is null ) System.Threading.Interlocked.CompareExchange( ref plan.Reads, MakePlanReads( plan ), null );
		else reads.Keep( planReadValues.AsSpan( 0, reads.ValueCount ) );

		return true;
	}

	// The values PlanReadsCurrent read for the plan PlanCurrent checks, which a check that passes in full keeps
	long[] planReadValues = [];

	/// <summary>
	/// Whether what a plan's buckets read of the pass's attributes (<paramref name="reads"/>) is what they read before the last
	/// check that passed in full, and each surface bucket's material still draws with its program. The values read are left
	/// in <see cref="planReadValues"/>.
	/// </summary>
	bool PlanReadsCurrent( GpuDrivenPlanReads reads )
	{
		var (combos, integers, hashed) = (reads.Combos, reads.Integers, reads.Hashed);
		var count = reads.ValueCount;
		if ( planReadValues.Length < count ) planReadValues = new long[Math.Max( count, planReadValues.Length * 2 )];

		// As LookupCombo and Lookup fall back to them, and as AttributeHash hashes them; the object attributes the checks read
		// first are the plan's, whose versions are compared already
		var values = planReadValues;
		var at = 0;
		for ( int i = 0; i < combos.Length; i++ ) values[at++] = Read( attributes.GetCombo( combos[i] ) );
		for ( int i = 0; i < integers.Length; i++ ) values[at++] = Read( Integer( attributes.Get( integers[i] ) ) );
		for ( int i = 0; i < hashed.Length; i++ ) values[at++] = (uint)(attributes.Get( hashed[i] )?.GetHashCode() ?? 0);

		if ( !reads.Matches( values.AsSpan( 0, at ) ) ) return false;

		// Where SurfaceShaders.For answers with the material's program without looking further
		foreach ( var (surface, program) in reads.Surfaces )
		{
			if ( program is not { Stale: false } || !ReferenceEquals( surface.Program, program ) || !ReferenceEquals( program.Device, device ) ) return false;
		}

		return true;

		static long Read( int? value ) => value is int set ? 1L << 32 | (uint)set : 0;
	}

	/// <summary>
	/// What a plan's bucket checks (<see cref="PlanCurrent"/>) read of the pass's attributes: LightmapCombo for a surface's
	/// pass, each material's shader's dynamic combos and specialization attributes for its variant's key, and the attributes
	/// its stages' records read (<see cref="AttributeNames"/>).
	/// </summary>
	GpuDrivenPlanReads MakePlanReads( GpuDrivenPlan plan )
	{
		var seen = new HashSet<long>();
		List<StringToken> combos = new(), integers = new(), hashed = new();
		HashSet<(SurfaceMaterial, SurfaceProgram)> surfaces = new();
		for ( int b = 0; b < plan.Checks.Length; b++ )
		{
			var first = plan.Checks[b].Material;
			var bucket = plan.Buckets[b];
			if ( bucket.Surface is not null )
			{
				Add( combos, 0, LightmapCombo );
				if ( first is SurfaceMaterial surface ) surfaces.Add( (surface, bucket.SurfaceProgram) );
				continue;
			}

			if ( first is not BackendMaterial { Inputs: { } inputs } ) continue;
			foreach ( var (_, token) in inputs.Combos ) Add( combos, 0, token );
			foreach ( var (_, token) in inputs.Specialization ) Add( integers, 1, token );

			var pixelRecord = !bucket.DepthOnly || bucket.Ids && !bucket.IdsShader;
			if ( bucket.Variant.Vertex is { } vertexStage )
			{
				foreach ( var name in AttributeNames( vertexStage ) ) Add( hashed, 2, name );
			}

			if ( pixelRecord && bucket.Variant.Pixel is { } pixelStage )
			{
				foreach ( var name in AttributeNames( pixelStage ) ) Add( hashed, 2, name );
			}
		}

		return new GpuDrivenPlanReads
		{
			Combos = combos.ToArray(),
			Integers = integers.ToArray(),
			Hashed = hashed.ToArray(),
			Surfaces = [.. surfaces],
		};

		void Add( List<StringToken> list, int kind, StringToken name )
		{
			if ( seen.Add( (long)kind << 32 | name.Value ) ) list.Add( name );
		}
	}

	/// <summary>
	/// Keep what this call made of its resident prefix (<paramref name="batches"/>) as its kind's plan (<see cref="GpuDrivenPlan"/>):
	/// the first buckets, their batches' records, and the first <paramref name="runs"/> multi-draws, which end at slot
	/// <paramref name="written"/> and <paramref name="compactUsed"/> slots into the compacted ones. With a resident filter, the
	/// records' moved words (<paramref name="moved"/>, from <paramref name="movedBase"/> in the frame's region) go with it.
	/// </summary>
	void KeepPlan( StringToken shaderMode, ReadOnlySpan<GpuDrivenBatch> batches, in GpuDrivenCull cull, Format color, Format depth, int samples, int totalFragments,
		(GpuMaterial, ShaderParameters, ShaderVariant, int, int)[] checks, ReadOnlySpan<uint> batchRecords, ReadOnlySpan<bool> drawn, BufferHandle compactedBuffer, int written, int compactUsed, int runs,
		ReadOnlySpan<uint> moved, uint movedBase )
	{
		var records = batchRecords.ToArray();
		var (movedWords, movedAt) = KeptMoved( records, moved, movedBase );

		var buckets = new List<GpuDrivenBucket>( checks.Length );
		var (commands, fragments) = (0, 0);
		for ( int b = 0; b < checks.Length; b++ )
		{
			buckets.Add( gpuDrivenBuckets[b].Kept() );
			commands += gpuDrivenBuckets[b].Commands;
			fragments += gpuDrivenBuckets[b].Fragments;
		}

		var compacted = new bool[runs];
		for ( int i = 0; i < runs; i++ ) compacted[i] = compactedBuffer.IsValid && cullRuns[i].Count != TaskTail && (cullRuns[i].Page == compactedBuffer || cullRuns[i].First >= totalFragments);

		var scratch = planScratch ??= new();
		var (materials, surfaces, others, attributes, models, geometries, seen) = (scratch.Materials, scratch.Surfaces, scratch.Others, scratch.Attributes, scratch.Models, scratch.Geometries, scratch.Seen);
		(BackendMaterial, int)[] keptMaterials;
		(SurfaceMaterial, int)[] keptSurfaces;
		GpuMaterial[] keptOthers;
		(BackendParameters, int)[] keptAttributes;
		BackendModel[] keptModels;
		IBackendGeometry[] keptGeometries;
		try
		{
			foreach ( ref readonly var batch in batches )
			{
				if ( batch.Material is { } material && seen.Add( material ) )
				{
					if ( material is BackendMaterial native ) materials.Add( (native, native.Version) );
					else if ( material is SurfaceMaterial surface ) surfaces.Add( (surface, surface.Version) );
					else others.Add( material );
				}
				// A freed one's batches drew nothing. BackendMesh's default Use has no liveness or lifetime work.
				if ( batch.Mesh?.Geometry is IBackendGeometry geometry && geometry is not BackendMesh && seen.Add( geometry ) )
				{
					if ( geometry is BackendModel model )
					{
						if ( model.Use() ) models.Add( model );
					}
					else if ( geometry.Use() ) geometries.Add( geometry );
				}
				if ( batch.ObjectAttributes is BackendParameters parameters && seen.Add( parameters ) ) attributes.Add( (parameters, parameters.Version) );
				if ( batch.MeshLighting is null ) continue;
				foreach ( var meshLighting in batch.MeshLighting )
				{
					if ( meshLighting is BackendParameters each && seen.Add( each ) ) attributes.Add( (each, each.Version) );
				}
			}

			(keptMaterials, keptSurfaces, keptOthers) = (materials.ToArray(), surfaces.ToArray(), others.ToArray());
			(keptAttributes, keptModels, keptGeometries) = (attributes.ToArray(), models.ToArray(), geometries.ToArray());
		}
		finally
		{
			// A plan every frame is what this does: its lists and set are kept for the next, as long as they stay modest
			var big = seen.Count > PlanScratch.KeepUpTo;
			seen.Clear();
			materials.Clear();
			surfaces.Clear();
			others.Clear();
			attributes.Clear();
			models.Clear();
			geometries.Clear();
			if ( big ) planScratch = null;
		}

		device.PlansFor( cull.Plan ).Keep( new GpuDrivenPlan
		{
			Key = PlanKey( shaderMode, cull, color, depth, samples ),
			Version = cull.PlanVersion,
			VisibilityEpoch = device.GpuDrivenTables.VisibilityEpoch,
			RecordEpoch = device.GpuDrivenTables.RecordEpoch,
			Batches = batches.Length,
			Commands = commands,
			Fragments = fragments,
			Written = written,
			CompactUsed = compactUsed,
			CompactBase = totalFragments,
			Buckets = buckets,
			Checks = checks,
			Materials = keptMaterials,
			SurfaceMaterials = keptSurfaces,
			OtherMaterials = keptOthers,
			Attributes = keptAttributes,
			Models = keptModels,
			Geometries = keptGeometries,
			BatchRecords = records,
			Moved = movedWords,
			MovedAt = movedAt,
			Runs = CollectionsMarshal.AsSpan( cullRuns )[..runs].ToArray(),
			Compacted = compacted,
			Drawn = drawn.ToArray(),
		} );
		System.Threading.Interlocked.Increment( ref device.GpuDrivenPlanBuilds );
	}

	/// <summary>The lists and set <see cref="KeepPlan"/> gathers what a plan's batches draw with in, per thread: recording threads make plans every frame.</summary>
	sealed class PlanScratch
	{
		/// <summary>Distinct objects a plan may name before its thread lets these go.</summary>
		public const int KeepUpTo = 1 << 16;

		public readonly HashSet<object> Seen = new( ReferenceEqualityComparer.Instance );
		public readonly List<(BackendMaterial, int)> Materials = new();
		public readonly List<(SurfaceMaterial, int)> Surfaces = new();
		public readonly List<GpuMaterial> Others = new();
		public readonly List<(BackendParameters, int)> Attributes = new();
		public readonly List<BackendModel> Models = new();
		public readonly List<IBackendGeometry> Geometries = new();
	}

	[ThreadStatic] static PlanScratch planScratch;

	/// <summary>
	/// A kept plan's moved words: for each of its records whose sixth word finds moved words in the frame's region (from
	/// <paramref name="movedBase"/>), where they start in the plan's own; that word is then <see cref="GpuDrivenTables.NoVisibility"/>
	/// in its records, and set again where they're copied. Null without a resident filter.
	/// </summary>
	static (uint[] Moved, int[] MovedAt) KeptMoved( uint[] records, ReadOnlySpan<uint> moved, uint movedBase )
	{
		if ( moved.IsEmpty ) return (null, null);

		var batches = MemoryMarshal.Cast<uint, BatchRecord>( records.AsSpan() );
		var movedAt = new int[batches.Length];
		var total = 0;
		for ( int i = 0; i < batches.Length; i++ )
		{
			movedAt[i] = -1;
			if ( batches[i].Moved == GpuDrivenTables.NoVisibility ) continue;

			movedAt[i] = total;
			total += (int)batches[i].Count;
		}

		var kept = new uint[total];
		for ( int i = 0; i < batches.Length; i++ )
		{
			if ( movedAt[i] < 0 ) continue;

			var first = (int)((batches[i].Moved - movedBase) / 4);
			moved.Slice( first, (int)batches[i].Count ).CopyTo( kept.AsSpan( movedAt[i] ) );
			batches[i].Moved = GpuDrivenTables.NoVisibility;
		}

		return (kept, movedAt);
	}

	/// <summary>
	/// A batch's moved words for a resident filter, one per fragment or mesh, and their byte offset in the frame's region; or
	/// <see cref="GpuDrivenTables.NoVisibility"/> for a batch every view keeps (static, or the view's own).
	/// </summary>
	static uint WriteMoved( in GpuDrivenBatch batch, int count, Span<uint> words, uint wordsBase, ref int written )
	{
		if ( batch.Instances ) return GpuDrivenTables.NoVisibility;
		if ( batch.Owner is ResidentMeshes meshes )
		{
			if ( meshes.Moved is not { } moved || moved.Length != count ) return GpuDrivenTables.NoVisibility;
			moved.CopyTo( words[written..] );
		}
		else
		{
			if ( batch.Moved == MovedNone ) return GpuDrivenTables.NoVisibility;
			words.Slice( written, count ).Fill( batch.Moved );
		}

		var offset = wordsBase + (uint)written * 4;
		written += count;
		return offset;
	}

	/// <summary>
	/// Before the frame's segments: clear <paramref name="view"/>'s triangle mask (the bits aggregates keep, and those the
	/// last frame's mesh draws took; stale bits beyond only draw a triangle more) and, the first time, its stamps, so every
	/// GPU-driven call of its passes, whichever worker records it, marks into the same cleared mask.
	/// </summary>
	internal override void BeginGpuDrivenView( object view )
	{
		if ( !GpuDriven || !device.GpuDrivenVisibility || recording is null || device.ExistingVisibility( view ) is not { } visibility ) return;
		var frame = device.GpuDrivenFrame;
		if ( visibility.ClearedFrame == frame ) return;

		// Ids targets a prepass asked for, in place before this frame's calls and cleared below with the rest
		device.PrepareIdsTargets( visibility );
		visibility.ClearedFrame = frame;

		var tables = device.GpuDrivenTables;
		var r = recording;
		CloseScope( flushClears: false );
		// Only the view's compute, index and mesh-shader accesses can overlap reuse of its buffers.
		var meshStages = device.Gpu.Capabilities.MeshShaders ? PipelineStage.TaskShader | PipelineStage.MeshShader : 0;
		r.List.Barrier( PipelineStage.ComputeShader | PipelineStage.VertexInput | PipelineStage.Transfer | meshStages,
			MemoryAccess.ShaderWrite | MemoryAccess.TransferWrite, PipelineStage.Transfer, MemoryAccess.TransferWrite );
		r.List.FillBuffer( visibility.Mask, 0, 0, Math.Max( (tables.VisibilityBits / 8 + 3) & ~3L, 4 ) );
		var meshBits = Math.Max( tables.LastFrameVisibilityBits, 1L << 16 );
		r.List.FillBuffer( visibility.Mask, 0, GpuDrivenTables.FrameVisibilityBitBase / 8, Math.Min( (meshBits / 8 + 4) & ~3L, (GpuDrivenTables.MaxVisibilityBits - GpuDrivenTables.FrameVisibilityBitBase) / 8 ) );
		if ( !visibility.StampsCleared ) r.List.FillBuffer( visibility.Stamps, 0 );
		visibility.StampsCleared = true;

		// Two-phase Hi-Z occlusion: this frame's visible bits cleared for forward to set, last frame's read by the prepass; all
		// set where forward wrote none last frame (the first frame, or after the switch was off), so everything draws first
		if ( device.HiZOcclusion && device.HiZShader is not null && device.MeshletIds is null )
		{
			var fresh = device.HiZBuffersFor( visibility );
			visibility.HiZReadValid = visibility.HiZWrote && !fresh;
			visibility.HiZCurrent ^= 1;
			r.List.FillBuffer( visibility.HiZVisible[visibility.HiZCurrent], 0 );
			if ( !visibility.HiZReadValid ) r.List.FillBuffer( visibility.HiZVisible[visibility.HiZCurrent ^ 1], uint.MaxValue );
		}

		visibility.HiZWrote = false;

		// Normals from the ids, or a tools view of them (IdsWhole): cleared once a frame instead of by each call, so they end
		// holding every call's, readable as a call's mark leaves them; and the table of the calls that draw them
		if ( device.IdsWhole && visibility.IdsReady )
		{
			r.List.Barrier( Borealis.Gpu.TextureBarrier.Transition( visibility.Ids, ImageLayout.Undefined, ImageLayout.CopyDestination, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.Transfer, MemoryAccess.TransferWrite ) );
			r.List.ClearTexture( visibility.Ids );
			r.List.Barrier( Borealis.Gpu.TextureBarrier.Transition( visibility.Ids, ImageLayout.CopyDestination, ImageLayout.ShaderRead, PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ColorTargetOutput | PipelineStage.ComputeShader, MemoryAccess.ColorTargetRead | MemoryAccess.ColorTargetWrite | MemoryAccess.ShaderRead ) );
			if ( visibility.IdsCalls.IsValid )
			{
				r.List.FillBuffer( visibility.IdsCalls, 0 );
				visibility.IdsCallCount = 0;
			}
		}

		// This frame's compacted buffer's cursor, past itself: the forward pass's calls take their triangles' ranges from it
		r.List.FillBuffer( visibility.Compacted, BackendDevice.VisibilityView.CompactedCursorIndices, 0, 4 );
		if ( visibility.PrepassCompacted.IsValid )
		{
			r.List.FillBuffer( visibility.PrepassCompacted, BackendDevice.VisibilityView.CompactedCursorIndices, 0, 4 );
			r.List.FillBuffer( visibility.PrepassCompacted16, BackendDevice.VisibilityView.CompactedCursorIndices, 0, 4 );
			if ( !visibility.MeshletStampsCleared )
			{
				r.List.FillBuffer( visibility.MeshletStamps, 0 );
				r.List.FillBuffer( visibility.MeshletJobs, 1, 4, 8 );
			}
			visibility.MeshletStampsCleared = true;

			// The kept triangles' cursor, which task shaders count up (borealis_meshlet_ids)
			if ( visibility.MeshletKept ) r.List.FillBuffer( visibility.MeshletStamps, 0, BackendDevice.MeshletKeptAt, 4 );
		}
		r.List.Barrier( PipelineStage.Transfer, MemoryAccess.TransferWrite, PipelineStage.ComputeShader | PipelineStage.Transfer | PipelineStage.DrawIndirect | meshStages,
			MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite | MemoryAccess.TransferWrite | MemoryAccess.IndirectRead );
	}

	/// <summary>
	/// While views' ids are resolved (<see cref="BackendDevice.ResolvingIds"/>), a call that draws ids puts its region in the view's table
	/// (<see cref="BackendDevice.VisibilityView.IdsCalls"/>): the frame chunk's bindless slot, its first instance record and
	/// how many, and its fragment words' base, which an id's instance record finds its vertex base by. Outside a rendering
	/// scope; the resolve reads the table after a barrier of its own.
	/// </summary>
	void AddIdsCall( BackendDevice.VisibilityView visibility, int frameSlot, uint firstInstance, int instances, uint vertexBases )
	{
		if ( !device.ResolvingIds || !visibility.IdsCalls.IsValid ) return;
		idsCall = ((uint)frameSlot, firstInstance, (uint)instances);
		var k = System.Threading.Interlocked.Increment( ref visibility.IdsCallCount ) - 1;
		if ( k >= BackendDevice.VisibilityView.MaxIdsCalls ) return;
		Span<uint> entry = [(uint)frameSlot, firstInstance, (uint)instances, vertexBases];
		recording.List.UpdateBuffer<uint>( visibility.IdsCalls, entry, k * (long)BackendDevice.VisibilityView.IdsCallBytes );
	}

	/// <summary>The call <see cref="AddIdsCall"/> last put in the table, whose ids its mark marks (none: every id).</summary>
	(uint Slot, uint First, uint Instances) idsCall;

	static readonly StringToken FigureConstantsName = new( "BorealisGpuDrivenFigure_t" );
	static readonly ConditionalWeakTable<ShaderVariant, object[]> FigureLayouts = new();
	ShaderVariant figureVariant;

	/// <summary>
	/// <c>borealislab -capture-visibility</c>'s figure (<see cref="BackendDevice.Figure"/>): this forward pass's multi-draws
	/// again with <c>borealis_gpu_driven_figure</c>, a count per fragment into its count target, then its triangles into its
	/// colour target, flat over the view's depth with dark edges or as faint edges with no depth test. Only for a view of the
	/// figure's size and one sample.
	/// </summary>
	void DrawFigure( VisibilityFigure figure, BackendMaterial figureMaterial, BackendDevice.VisibilityView visibility, BufferHandle frameBuffer, long frameOffset, int commandOffset, int runsOffset, int listedOffset, Format depth, PassResolver resolver )
	{
		var r = recording;
		var cache = device.MaterialCache;
		var target = Gpu.InfoOf( figure.Count );
		var depthInfo = Gpu.InfoOf( r.Depth.Handle );
		if ( depthInfo.Width != target.Width || depthInfo.Height != target.Height || depthInfo.Samples != 1 ) return;
		if ( (figureVariant ??= Variant( figureMaterial, "Default" )) is null ) return;

		// Where its commands and counts are, for the figure's counts once the frame is done
		var runs = new List<(int First, long CountOffset, int Max)>();
		for ( int i = 0; i < cullRuns.Count; i++ ) runs.Add( (cullRuns[i].First, frameOffset + runsOffset + i * MultiDraw.Size + MultiDraw.CountOffset, cullRuns[i].Count) );
		figure.Passes.Add( (frameBuffer, frameOffset + commandOffset, runs, listedOffset >= 0 ? frameOffset + listedOffset : -1, -1) );
		// The ids of the view with the most fragments (the main view's, not the 3D skybox's)
		var fragments = 0;
		foreach ( var run in cullRuns ) fragments += run.Count;
		if ( visibility is not null && fragments > figure.VisibilityFragments )
		{
			figure.VisibilityFragments = fragments;
			device.FigureVisibility = (visibility.Ids, visibility.Mask, device.GpuDrivenTables.VisibilityBits, device.GpuDrivenTables.FrameVisibilityBits);
		}

		CloseScope( flushClears: false );
		var first = !figure.Cleared;
		figure.Cleared = true;

		Span<int> modes = figure.DepthTestedTriangles ? [0, 1, 2] : [0, 3];
		foreach ( var mode in modes )
		{
			// The count target for the count, the colour target for the rest (cleared before its first mode)
			var texture = mode == 0 ? figure.Count : figure.Colour;
			var clear = first && mode <= 1 || first && mode == 3;
			if ( clear ) r.List.Barrier( Borealis.Gpu.TextureBarrier.Transition( texture, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.AllCommands, MemoryAccess.None, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
			else r.List.Barrier( Borealis.Gpu.TextureBarrier.Transition( texture, ImageLayout.ColorTarget, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite | MemoryAccess.ColorTargetRead ) );
			r.List.BeginRendering(
				[new ColorTarget { Texture = texture, Load = clear ? LoadOp.Clear : LoadOp.Load, Store = StoreOp.Store, ClearColor = mode == 0 ? default : new System.Numerics.Vector4( 0.02f, 0.025f, 0.035f, 1 ) }],
				new DepthTarget { Texture = r.Depth.Handle, Layer = bound.DepthSlice, Load = LoadOp.Load, Store = StoreOp.Store } );
			var rect = bound.Viewport;
			var (width, height) = r.List.RenderSize;
			if ( rect.Width <= 0 || rect.Height <= 0 ) rect = new Rect( 0, 0, width, height );
			r.List.SetViewport( rect.Left, rect.Top, rect.Width, rect.Height, bound.MinZ, bound.MaxZ );
			r.List.SetScissor( (int)rect.Left, (int)rect.Top, (int)MathF.Ceiling( rect.Width ), (int)MathF.Ceiling( rect.Height ) );
			r.Pipeline = default;

			SetConstants( FigureConstantsName, new System.Numerics.Vector4( BitConverter.UInt32BitsToSingle( (uint)mode ), 0, 0, 0 ) );
			var lastBucket = -1;
			for ( int i = 0; i < cullRuns.Count; i++ )
			{
				var run = cullRuns[i];
				var bucket = gpuDrivenBuckets[run.Bucket];
				if ( bucket.Variant is null ) continue; // surfaces aren't in the figure
				if ( run.Bucket != lastBucket )
				{
					lastBucket = run.Bucket;
					SetObjectAttributes( bucket.Own, bucket.Attributes );
					NextPassState( cache );
					resolver.Begin( passEpoch );
					var layout = FigureLayouts.GetValue( bucket.Variant, static _ => [new object(), new object(), new object(), new object()] )[mode];
					var key = new MaterialObjectCache.Key( figureMaterial, figureVariant, layout, mode == 0 ? Format.R16Float : Format.RGBA8Unorm, depth, 1, false, true );
					var materialObject = cache.Get( key, immediate: false, (device, key, stateFrom: bucket.Variant, mode), static ( s, l ) => s.device.FigurePipeline( s.key.Variant, s.stateFrom, s.mode, s.key.Depth, l.Sets ) );
					resolver.LayerEpoch = layerEpoch;
					materialObject.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
				}

				r.List.SetIndexBuffer( run.Page, run.Format, 0 );
				r.List.DrawIndexedIndirectCount( frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, frameBuffer, frameOffset + runsOffset + i * MultiDraw.Size + MultiDraw.CountOffset, run.Count, GpuDrivenTables.CommandStride );
			}

			r.List.EndRendering();
			r.Pipeline = default;
			r.Binder = null;
		}

		SetObjectAttributes( null );
	}

	byte[] markConstants;
	ShaderVariant idsShaderVariant, meshIdsVariant;

	/// <summary><c>borealis_meshlet_ids</c>'s push constants (<c>BorealisMeshletTask_t</c>), per task draw.</summary>
	[StructLayout( LayoutKind.Sequential )]
	internal struct MeshletTaskConstants
	{
		/// <summary>World to clip, a row per clip coordinate.</summary>
		public System.Numerics.Vector4 ClipX, ClipY, ClipZ, ClipW;

		/// <summary>The viewport's width and height.</summary>
		public System.Numerics.Vector2 Viewport;

		/// <summary>The depth target's samples; the task run's byte offset in the frame buffer; the region's first instance record; the meshlet run table's bindless slot.</summary>
		public uint Samples, RunAt, FirstInstance, RunTable;

		/// <summary>The region's commands' byte offset; the frame's stamp; the kept triangles' byte offset in the meshlet stamps and how many fit.</summary>
		public uint Commands, Frame, KeptAt, KeptCapacity;

		/// <summary>The faces the pipeline culls, which the mesh shader culls first: 0 none, 1 front, 2 back.</summary>
		public uint CullMode;
	}

	/// <summary>
	/// Bytes of a compaction's list of large fragments (<c>borealis_gpu_driven_compact_cs</c>'s, <see cref="BackendDevice.CompactLargeFor"/>):
	/// 48 of dispatch arguments and capacities, a word a part and 68 words a fragment, then 256 bytes for each of the first
	/// <see cref="CompactBitsParts"/> parts (which of each thread's triangles own a sample, from the parts' count for their
	/// copy). A fragment of more triangles than a group scans at once is counted and copied by up to
	/// <see cref="CompactMaxParts"/> groups.
	/// </summary>
	internal static long CompactLargeBytes( int parts, int fragments ) => 48 + parts * 4L + fragments * (4 + CompactMaxParts) * 4L + CompactBitsParts( parts ) * 256L;
	internal static int CompactBitsParts( int parts ) => Math.Min( parts, 2048 );
	internal const uint CompactMaxParts = 64;
	/// <summary>The attribute ids the GPU-driven passes' own dispatches bind by: found once, not by name each dispatch.</summary>
	static class Ids
	{
		public static readonly int BorealisCullFrame = AttributeIds.Of( "BorealisCullFrame" );
		public static readonly int BorealisCullStats = AttributeIds.Of( "BorealisCullStats" );
		public static readonly int BorealisCullLevels = AttributeIds.Of( "BorealisCullLevels" );
		public static readonly int BorealisCullStamps = AttributeIds.Of( "BorealisCullStamps" );
		public static readonly int BorealisCullMask = AttributeIds.Of( "BorealisCullMask" );
		public static readonly int BorealisCullCompacted = AttributeIds.Of( "BorealisCullCompacted" );
		public static readonly int BorealisHiZVisible = AttributeIds.Of( "BorealisHiZVisible" );
		public static readonly int BorealisMeshletStamps = AttributeIds.Of( "BorealisMeshletStamps" );
		public static readonly int BorealisMeshletCompacted = AttributeIds.Of( "BorealisMeshletCompacted" );
		public static readonly int BorealisMeshletCompacted16 = AttributeIds.Of( "BorealisMeshletCompacted16" );
		public static readonly int BorealisCompactLarge = AttributeIds.Of( "BorealisCompactLarge" );
		public static readonly int BorealisHiZ = AttributeIds.Of( "BorealisHiZ" );
		public static readonly int BorealisHiZDepth = AttributeIds.Of( "BorealisHiZDepth" );
		public static readonly int BorealisMeshletJobs = AttributeIds.Of( "BorealisMeshletJobs" );
		public static readonly int g_TransformBuffer = AttributeIds.Of( "g_TransformBuffer" );
		public static readonly int BorealisIds = AttributeIds.Of( "BorealisIds" );
	}

	static readonly int CompactParts = AttributeIds.Of( "D_PARTS" );

	/// <summary>A batch record's sixth word's bit for a bucket drawn with task and mesh shaders: the rest is its task run.</summary>
	const uint TaskRunBit = 0x80000000u;

	/// <summary>A task run's count in <see cref="cullRuns"/> (negative, with its slots): what tells it from a multi-draw.</summary>
	static int TaskRunCount( int slots ) => -1 - slots;

	/// <summary>The count of the run after a task run, whose words are 1 and 1: the task draw's second and third group counts.</summary>
	internal const int TaskTail = int.MinValue;

	/// <summary>Task groups a task run may launch: a draw's first group count, at most 65535 where the device allows only the minimum.</summary>
	const int MaxTaskGroups = 65535;

	/// <summary>The material object key's layout for <c>borealis_gpu_driven_ids</c> drawing a variant's depth: one object per variant.</summary>
	sealed record IdsLayout( VertexLayout Layout, ShaderVariant StateFrom );
	static readonly ConditionalWeakTable<ShaderVariant, IdsLayout> IdsLayouts = new();

	// Found without making the delegate that would make one
	IdsLayout IdsLayoutOf( ShaderVariant stateFrom ) => IdsLayouts.TryGetValue( stateFrom, out var known ) ? known : IdsLayouts.GetValue( stateFrom, v => new IdsLayout( device.PulledLayout, v ) );

	/// <summary>
	/// The prepass's buckets with ids, in a rendering scope of their own: each with its own variant, whose pixel shader
	/// writes the id (the bit of its triangle's first vertex + 1) at target 1 to exactly the samples it covers, its discards
	/// and alpha to coverage included; target 0 takes the authored output, which only alpha to coverage reads. Then
	/// <c>borealis_gpu_driven_mark_cs</c> sets the bit of every id in the view's mask, for its forward pass.
	/// </summary>
	void DrawIds( in GpuDrivenCull cull, BackendDevice.VisibilityView visibility, (BackendMaterial Ids, ShaderFile Mark, ShaderFile Compact) shaders, BufferHandle frameBuffer, long frameOffset, int commandOffset, int runsOffset, uint firstInstance, Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver,
		bool clearIds = true, bool mark = true )
	{
		var r = recording;
		var cache = device.MaterialCache;
		var depthTexture = r.Depth;

		// Over the ids an earlier call of the view left unmarked (MarkLater), which this call's mark marks with its own
		if ( unmarked.Visibility is not null && !ReferenceEquals( unmarked.Visibility, visibility ) ) MarkUnmarked();
		var merged = unmarked.Visibility is not null;
		if ( merged ) clearIds = false;

		var timer = device.PassTimer;
		var idsScope = timer?.Begin( r.List, "GPU-driven ids", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		r.List.Marker( "GPU-driven ids" );
		// After any earlier ids pass of the view this frame (another call's): its writes and the mark's reads come first
		// (A second phase draws on over the first's ids, which its rendering scope kept in place.) With normals from the ids
		// every call draws on over the ids the view's clear and earlier calls left, readable after their marks
		var loadIds = !clearIds || device.IdsWhole;
		var idsBefore = !clearIds ? ImageLayout.ColorTarget : device.IdsWhole ? ImageLayout.ShaderRead : ImageLayout.Undefined;
		r.List.Barrier( Borealis.Gpu.TextureBarrier.Transition( visibility.Ids, idsBefore, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput | PipelineStage.ComputeShader, MemoryAccess.ColorTargetWrite, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite | MemoryAccess.ColorTargetRead ) );
		r.List.Barrier( Borealis.Gpu.TextureBarrier.Transition( visibility.Scratch, ImageLayout.Undefined, ImageLayout.ColorTarget, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite ) );
		// A pending depth/stencil clear belongs to the first raster scope. The ids targets have the depth target's size.
		Transition( depthTexture, ImageLayout.DepthTarget );
		r.List.BeginRendering(
			[new ColorTarget { Texture = visibility.Scratch, Load = LoadOp.DontCare, Store = StoreOp.DontCare }, new ColorTarget { Texture = visibility.Ids, Load = loadIds ? LoadOp.Load : LoadOp.Clear, Store = StoreOp.Store, ClearColor = default }],
			new DepthTarget { Texture = depthTexture.Handle, Layer = bound.DepthSlice, Load = clearDepth ? LoadOp.Clear : LoadOp.Load, Store = StoreOp.Store } );
		clearDepth = false;
		var rect = bound.Viewport;
		var (width, height) = r.List.RenderSize;
		if ( rect.Width <= 0 || rect.Height <= 0 ) rect = new Rect( 0, 0, width, height );
		r.List.SetViewport( rect.Left, rect.Top, rect.Width, rect.Height, bound.MinZ, bound.MaxZ );
		r.List.SetScissor( (int)rect.Left, (int)rect.Top, (int)MathF.Ceiling( rect.Width ), (int)MathF.Ceiling( rect.Height ) );
		r.Pipeline = default;

		var lastBucket = -1;
		var lastTask = false;
		for ( int i = 0; i < cullRuns.Count; i++ )
		{
			var run = cullRuns[i];
			var bucket = gpuDrivenBuckets[run.Bucket];
			if ( !bucket.Ids || run.Count == TaskTail ) continue;
			var censusBefore = Census ? PassTimer.Recorded.Of( r.List ) : default;

			// A task run: borealis_meshlet_ids with the bucket's culling, depth and stencil, its group counts the run's count and tail
			if ( run.Count < 0 )
			{
				if ( run.Bucket != lastBucket || !lastTask )
				{
					lastBucket = run.Bucket;
					lastTask = true;
					attributes.Set( "BorealisMeshletStamps", visibility.MeshletStampsAttribute );
					SetObjectAttributes( bucket.Own, bucket.Attributes );
					NextPassState( cache );
					resolver.Begin( passEpoch );
					var meshIds = device.MeshletIds;
					var meshVariant = meshIdsVariant ??= Variant( meshIds, "Default" );
					var stateFrom = bucket.Variant ?? idsShaderVariant;
					var key = new MaterialObjectCache.Key( meshIds, meshVariant, IdsLayoutOf( stateFrom ), device.IdsFormat, depth, samples, false, true, Stencil: bucket.Stencil );
					var meshObject = cache.Get( key, immediate: false, (device, key, stateFrom), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.device.PulledLayout, s.key.Color, s.key.Depth, s.key.Samples, false, l.Sets, ids: true, stateFrom: s.stateFrom, stencil: s.key.Stencil ) );
					resolver.LayerEpoch = layerEpoch;
					meshObject.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
					// Ids pipelines bias depth (the pass's): set it, whatever an earlier colour draw's surface left
					r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
					BackendDevice.SetStencilReference( r.List, depth, key.Stencil, stateFrom.State );
				}

				var m = cull.WorldToClip;
				r.List.PushConstants( new MeshletTaskConstants
				{
					ClipX = new System.Numerics.Vector4( m.M11, m.M21, m.M31, m.M41 ),
					ClipY = new System.Numerics.Vector4( m.M12, m.M22, m.M32, m.M42 ),
					ClipZ = new System.Numerics.Vector4( m.M13, m.M23, m.M33, m.M43 ),
					ClipW = new System.Numerics.Vector4( m.M14, m.M24, m.M34, m.M44 ),
					Viewport = new System.Numerics.Vector2( cull.ViewportSize.x, cull.ViewportSize.y ),
					Samples = (uint)samples,
					RunAt = (uint)(frameOffset + runsOffset + i * MultiDraw.Size + MultiDraw.FirstSlotOffset),
					FirstInstance = firstInstance,
					RunTable = (uint)device.MeshletStore.RunSlotCovering( device.GpuDrivenTables.DrawRecords ),
					Commands = (uint)(frameOffset + commandOffset),
					Frame = device.GpuDrivenFrame,
					KeptAt = BackendDevice.MeshletKeptAt,
					KeptCapacity = visibility.KeptCapacity,
					CullMode = (uint)((bucket.Variant ?? idsShaderVariant).State.Cull),
				} );
				r.List.DrawMeshTasksIndirect( frameBuffer, frameOffset + runsOffset + i * MultiDraw.Size + MultiDraw.CountOffset );
				gpuDrivenRuns++;
				continue;
			}

			if ( run.Bucket != lastBucket || lastTask )
			{
				lastBucket = run.Bucket;
				lastTask = false;
				SetObjectAttributes( bucket.Own ?? (bucket.PixelMaterial != 0 ? device.GpuDrivenMaterialParameters( bucket.PixelMaterial ) : null), bucket.Attributes );
				NextPassState( cache );
				resolver.Begin( passEpoch );
				MaterialObject materialObject = null;
				if ( bucket.Surface is not null && !bucket.IdsShader )
				{
					// A surface's own GPU-driven pass, writing ids beside its coverage
					BindSurfaceGpuDriven( bucket, Format.Unknown, depth, samples, ids: true );
				}
				else if ( bucket.IdsShader )
				{
					// borealis_gpu_driven_ids with the bucket's culling, depth and stencil: keyed by a layout object per bucket variant
					var stateFrom = bucket.Variant ?? idsShaderVariant;
					var idsLayout = IdsLayoutOf( stateFrom );
					var key = new MaterialObjectCache.Key( shaders.Ids, idsShaderVariant, idsLayout, device.IdsFormat, depth, samples, false, true, Stencil: bucket.Stencil );
					materialObject = cache.Get( key, immediate: false, (device, key, stateFrom), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.device.PulledLayout, s.key.Color, s.key.Depth, s.key.Samples, false, l.Sets, ids: true, stateFrom: s.stateFrom, stencil: s.key.Stencil ) );
				}
				else
				{
					var perDraw = bucket.PixelMaterial != 0;
					var layout = perDraw ? device.PulledLayoutMaterialPerDraw : device.PulledLayout;
					var key = new MaterialObjectCache.Key( bucket.Material, bucket.Variant, layout, device.IdsFormat, depth, samples, false, true, Stencil: bucket.Stencil );
					materialObject = cache.Get( key, immediate: false, (device, layout, key, perDraw), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.layout, s.key.Color, s.key.Depth, s.key.Samples, false, l.Sets, s.perDraw, ids: true, stencil: s.key.Stencil ) );
				}

				resolver.LayerEpoch = layerEpoch;
				materialObject?.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
				r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
				if ( materialObject is not null ) BackendDevice.SetStencilReference( r.List, depth, bucket.Stencil, (bucket.Variant ?? idsShaderVariant).State );
			}

			r.List.SetIndexBuffer( run.Page, run.Format, 0 );
			r.List.DrawIndexedIndirectCount( frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, frameBuffer, frameOffset + runsOffset + i * MultiDraw.Size + MultiDraw.CountOffset, run.Count, GpuDrivenTables.CommandStride );
			gpuDrivenRuns++;
			if ( Census ) CensusMultiDraw( bucket, DrawCensus.Path.BucketIds, censusBefore, frameBuffer, frameOffset + commandOffset + run.First * GpuDrivenTables.CommandStride, run.Count, frameOffset + runsOffset + i * MultiDraw.Size + MultiDraw.CountOffset );
		}

		r.List.EndRendering();
		r.List.Marker( "end GPU-driven ids" );
		timer?.End( r.List, idsScope );
		r.Pipeline = default;
		r.Binder = null;
		SetObjectAttributes( null );
		if ( !mark ) return;

		// Left for the view's next call to mark with its own, or marked here: every id when it marks an earlier call's too
		if ( markLater )
		{
			unmarked = (visibility, shaders.Mark);
			return;
		}

		unmarked = default;
		if ( merged ) System.Threading.Interlocked.Increment( ref device.MarksMerged );
		Mark( visibility, shaders.Mark, merged ? default : idsCall );
	}

	/// <summary>
	/// Whether a call's ids are left unmarked for the call after it (<see cref="BackendDevice.MergedMarks"/>), and the ids
	/// and mark shader of a call that left them.
	/// </summary>
	bool markLater;
	(BackendDevice.VisibilityView Visibility, ShaderFile Mark) unmarked;

	/// <summary>Mark the ids a call left unmarked for a call after it that drew none: outside a rendering scope.</summary>
	void MarkUnmarked()
	{
		if ( unmarked.Visibility is not { } visibility ) return;
		var shader = unmarked.Mark;
		unmarked = default;
		CloseScope( flushClears: false );
		Mark( visibility, shader, default );
	}

	/// <summary>
	/// <c>borealis_gpu_driven_mark_cs</c> over the view's ids: every distinct id of each pixel's samples sets its triangle's
	/// bit, only <paramref name="call"/>'s ids where it names one (normals from the ids, whose target keeps earlier calls').
	/// </summary>
	void Mark( BackendDevice.VisibilityView visibility, ShaderFile shader, (uint Slot, uint First, uint Instances) call )
	{
		var r = recording;
		var timer = device.PassTimer;
		r.List.Barrier( Borealis.Gpu.TextureBarrier.Transition( visibility.Ids, ImageLayout.ColorTarget, ImageLayout.ShaderRead, PipelineStage.ColorTargetOutput, MemoryAccess.ColorTargetWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead ) );
		var markConstants = this.markConstants ??= new byte[Marshal.SizeOf<GpuDrivenMarkConstants>()];
		var frameBits = device.GpuDrivenTables.FrameVisibilityBits;
		MemoryMarshal.Write( markConstants, new GpuDrivenMarkConstants
		{
			Width = (uint)visibility.Width,
			Height = (uint)visibility.Height,
			Samples = (uint)visibility.Samples,
			Bits = (uint)Math.Min( frameBits > 0 ? GpuDrivenTables.FrameVisibilityBitBase + frameBits : device.GpuDrivenTables.VisibilityBits, uint.MaxValue ),
			CallSlot = call.Slot,
			CallFirst = call.First,
			CallInstances = call.Instances,
		} );
		cullDrawer ??= new ShaderDrawer( device );
		cullAttributes ??= new ShaderAttributes();
		cullAttributes.Clear();
		cullAttributes.SetById( CullAttribute.Ids, visibility.Ids );
		cullAttributes.SetById( CullAttribute.Mask, visibility.Mask );
		cullAttributes.SetComboById( CullAttribute.Msaa, visibility.Samples > 1 ? 1 : 0 );
		cullAttributes.SetDataById( CullAttribute.MarkConstants, markConstants );
		var markScope = timer?.Begin( r.List, "GPU-driven mark", PassTimer.ScopeKind.Own, count: false ) ?? -1;
		r.List.Marker( "GPU-driven mark" );
		cullDrawer.Dispatch( r.List, shader, cullAttributes, visibility.Width, visibility.Height, 1 );
		r.List.Marker( "end GPU-driven mark" );
		timer?.End( r.List, markScope );

		// The mask and stamps for the forward pass's culling, in a later list
		r.List.Barrier( PipelineStage.ComputeShader, MemoryAccess.ShaderWrite, PipelineStage.ComputeShader, MemoryAccess.ShaderRead | MemoryAccess.ShaderWrite );
	}
	/// <summary>
	/// An aggregate's fragment records: centre and draw record, extents and fragment index (bit 31 set when it casts no
	/// shadow), making each fragment's draw record.
	/// </summary>
	/// <summary>Ask for the meshlets of each of an aggregate's fragments' index ranges, built off the frame (<see cref="Meshlets.MeshletStore"/>).</summary>
	static void RequestMeshlets( Meshlets.MeshletStore store, AggregateObject aggregate, PooledGeometry pooled, BackendModel model, int mesh, int drawCall )
	{
		if ( Meshlets.MeshletStore.SourceOf( model, mesh, drawCall, pooled.IndexPage, pooled.IndexFormat ) is not { } source ) return;
		foreach ( ref readonly var fragment in aggregate.Fragments )
		{
			var arg = fragment.Args;
			var range = new Meshlets.MeshletStore.Range( pooled.IndexPageSlot, pooled.FirstIndex + arg.FirstIndex, arg.IndexCount, arg.BaseVertex );
			store.Request( range, source with { IndexBase = source.IndexBase + (int)arg.FirstIndex, VertexBase = source.VertexBase + arg.BaseVertex } );
		}
	}

	/// <summary>
	/// Ask for the meshlets of a model's static mesh draw (an instance batch's or resident meshes' draw), once a draw: a shadow
	/// view culls them (the ids prepass lists only aggregates' fragments).
	/// </summary>
	static void RequestMeshlets( Meshlets.MeshletStore store, PooledGeometry pooled, BackendModel model, in RenderMesh.Draw draw, int baseVertex )
	{
		if ( !model.FirstMeshletRequest( draw.Mesh, draw.DrawCall ) ) return;
		if ( Meshlets.MeshletStore.SourceOf( model, draw.Mesh, draw.DrawCall, pooled.IndexPage, pooled.IndexFormat ) is not { } source ) return;
		var range = new Meshlets.MeshletStore.Range( pooled.IndexPageSlot, pooled.FirstIndex + (uint)draw.StartIndex, (uint)draw.IndexCount, baseVertex );
		store.Request( range, source with { IndexBase = source.IndexBase + draw.StartIndex, VertexBase = source.VertexBase + baseVertex } );
	}

	/// <summary>
	/// Ask for the generated levels of each of an aggregate's fragments' index ranges, built off the frame
	/// (<see cref="Lods.LodStore"/>); <paramref name="color"/> for a colour pass's records, which publish them.
	/// </summary>
	static void RequestLods( Lods.LodStore store, AggregateObject aggregate, PooledGeometry pooled, BackendModel model, int mesh, int drawCall, bool color )
	{
		if ( Meshlets.MeshletStore.SourceOf( model, mesh, drawCall, pooled.IndexPage, pooled.IndexFormat ) is not { } source ) return;
		var material = store.Labelled && (uint)mesh < (uint)model.File.Meshes.Count && (uint)drawCall < (uint)model.File.Meshes[mesh].DrawCalls.Count ? model.File.Meshes[mesh].DrawCalls[drawCall].Material : null;
		var fragments = aggregate.Fragments;
		for ( int i = 0; i < fragments.Length; i++ )
		{
			var arg = fragments[i].Args;
			var range = new Meshlets.MeshletStore.Range( pooled.IndexPageSlot, pooled.FirstIndex + arg.FirstIndex, arg.IndexCount, arg.BaseVertex );
			var label = material is null ? default : new Lods.LodStore.Label( material, aggregate.Centers[i], aggregate.Extents[i] );
			store.Request( range, source with { IndexBase = source.IndexBase + (int)arg.FirstIndex, VertexBase = source.VertexBase + arg.BaseVertex }, color, label );
		}
	}

	/// <summary>
	/// Ask for the generated levels of a model's static mesh draw (resident meshes'), built off the frame; <paramref name="color"/>
	/// for a colour pass's, which publishes them.
	/// </summary>
	static void RequestLods( Lods.LodStore store, PooledGeometry pooled, BackendModel model, in RenderMesh.Draw draw, int baseVertex, bool color )
	{
		if ( Meshlets.MeshletStore.SourceOf( model, draw.Mesh, draw.DrawCall, pooled.IndexPage, pooled.IndexFormat ) is not { } source ) return;
		var range = new Meshlets.MeshletStore.Range( pooled.IndexPageSlot, pooled.FirstIndex + (uint)draw.StartIndex, (uint)draw.IndexCount, baseVertex );
		var label = store.Labelled ? new Lods.LodStore.Label( model.File.Meshes[draw.Mesh].DrawCalls[draw.DrawCall].Material, default, default ) : default;
		store.Request( range, source with { IndexBase = source.IndexBase + draw.StartIndex, VertexBase = source.VertexBase + baseVertex }, color, label );
	}

	static void WriteFragments( GpuDrivenTables tables, AggregateObject aggregate, PooledGeometry pooled, uint vertexMaterial, uint pixelMaterial, uint flags, Span<byte> bytes )
	{
		var records = MemoryMarshal.Cast<byte, FragmentRecord>( bytes );
		var fragments = aggregate.Fragments;
		var centers = aggregate.Centers;
		var extents = aggregate.Extents;
		for ( int i = 0; i < fragments.Length; i++ )
		{
			var arg = fragments[i].Args;
			var firstIndex = pooled.FirstIndex + arg.FirstIndex;
			records[i] = new FragmentRecord
			{
				Center = centers[i],
				Draw = tables.Draw( pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, arg.IndexCount, arg.BaseVertex, pooled.IndexPageSlot ),
				Extents = extents[i],
				Instance = arg.FirstInstance | ((fragments[i].Flags & FragmentFlags.DoNotCastShadows) != 0 ? 0x80000000u : 0u),
			};
		}
	}

	/// <summary>
	/// Resident meshes' records (<c>borealis_gpu_driven_cull_cs</c>): a header with the model's LOD count, its switch
	/// distances' count (bits 8-15), whether its LODs come from the fade state (bit 16, <see cref="GpuContext.ResidentMeshes.FadesLods"/>:
	/// <c>sbox/lod_fade.slangh</c>) and switch distances 1 to 7, then per mesh its centre and LOD scale, and its extents
	/// and transform slot (plus <paramref name="slotOffset"/>, a tinted model's draw's entry in each mesh's run).
	/// </summary>
	static void WriteResidentMeshes( ResidentMeshes meshes, int slotOffset, Span<byte> bytes )
	{
		ref var header = ref MemoryMarshal.AsRef<ResidentMeshHeader>( bytes[..ResidentMeshHeader.Size] );
		var switches = meshes.SwitchDistances;
		header.Lods = (uint)meshes.LodCount | (uint)switches.Length << 8 | (meshes.FadesLods ? 1u << 16 : 0);
		var distances = header.SwitchDistances;
		for ( int i = 0; i < distances.Length; i++ ) distances[i] = i + 1 < switches.Length ? switches[i + 1] : 0;

		var records = MemoryMarshal.Cast<byte, ResidentMeshRecord>( bytes[ResidentMeshHeader.Size..] );
		for ( int i = 0; i < meshes.Count; i++ )
		{
			records[i] = new ResidentMeshRecord { Center = meshes.Centers[i], LodScale = meshes.LodScales[i], Extents = meshes.Extents[i], Transform = (uint)(meshes.Slots[i] + slotOffset) };
		}
	}

	/// <summary>A resident mesh batch's draw word with this bit is a list of a draw record per mesh in the fragment table.</summary>
	const uint MeshDrawList = 0x80000000u;

	/// <summary>
	/// A resident mesh batch's draw records when its meshes differ in their lighting: each mesh's, the batch's draw with
	/// that mesh's lighting record, as a list of words in the fragment table, made once per owner, draw, records and the
	/// lighting attributes' versions. <paramref name="restore"/> is what the call's object attributes go back to.
	/// </summary>
	uint MeshLightingDraws( GpuDrivenTables tables, object owner, ShaderParameters[] lighting, ShaderParameters restore, PooledGeometry pooled, uint vertexMaterial, uint pixelMaterial, uint flags, uint firstIndex, uint indexCount, int baseVertex )
	{
		var versions = new HashCode();
		ShaderParameters last = null;
		foreach ( var attributes in lighting )
		{
			if ( ReferenceEquals( attributes, last ) ) continue;
			versions.Add( RuntimeHelpers.GetHashCode( attributes ) );
			versions.Add( attributes is BackendParameters parameters ? parameters.Version : 0 );
			last = attributes;
		}

		var key = flags | (uint)versions.ToHashCode() << 8;
		var state = (Context: this, Tables: tables, Lighting: lighting, Restore: restore, Draw: (pooled.GeometryRecord, vertexMaterial, pixelMaterial, flags, firstIndex, indexCount, baseVertex, pooled.IndexPageSlot));
		return tables.FragmentsOf( owner, pooled.GeometryRecord, vertexMaterial, pixelMaterial, key, (lighting.Length + 7) / 8, state,
			static ( s, bytes ) => s.Context.WriteMeshLightingDraws( s.Tables, s.Lighting, s.Restore, s.Draw, MemoryMarshal.Cast<byte, uint>( bytes ) ), releasable: true, indexPage: pooled.IndexPageSlot );
	}

	void WriteMeshLightingDraws( GpuDrivenTables tables, ShaderParameters[] lighting, ShaderParameters restore, (uint Geometry, uint Vertex, uint Pixel, uint Flags, uint FirstIndex, uint IndexCount, int BaseVertex, int IndexPage) d, Span<uint> words )
	{
		var cache = device.MaterialCache;
		ShaderParameters last = null;
		var draw = 0u;
		for ( int i = 0; i < lighting.Length; i++ )
		{
			if ( i == 0 || !ReferenceEquals( lighting[i], last ) )
			{
				last = lighting[i];
				SetObjectAttributes( null, last );
				NextPassState( cache );
				var record = tables.Lighting( Lookup( BakedLightingToken ) as byte[], Lookup( ProbeVolumeToken ) as byte[] );
				draw = tables.Draw( d.Geometry, d.Vertex, d.Pixel, d.Flags | record << 8, d.FirstIndex, d.IndexCount, d.BaseVertex, d.IndexPage );
			}

			words[i] = draw;
		}

		SetObjectAttributes( null, restore );
		NextPassState( cache );
	}

	/// <summary>Multi-draws recorded by the current <see cref="DrawGpuDriven"/>, for the timings.</summary>
	int gpuDrivenRuns;

	/// <summary>A material's version, which moves on whenever its values, textures or itself change.</summary>
	static int MaterialVersion( GpuMaterial material ) => material switch { BackendMaterial m => m.Version, SurfaceMaterial s => s.Version, _ => 0 };

	/// <summary>A bucket's key for attributes: the same for attributes that differ only in what each draw's lighting record holds.</summary>
	static long AttributesKey( ShaderParameters attributes ) => attributes switch { null => 0L, BackendParameters parameters => parameters.LightingFreeKey, _ => (uint)RuntimeHelpers.GetHashCode( attributes ) | 1L << 62 };

	/// <summary>
	/// Whether two GPU-driven variants draw the same way: the same one, or the same shader, mode, combos' code,
	/// specialization constants, samplers and resolved render state. Materials whose render-state or sampler expressions
	/// evaluate differently get variants of their own (<see cref="ShaderLibrary"/> keys them by the evaluated values), but
	/// those that end in the same pipeline and sets can share a bucket: its records are laid out by the same combos.
	/// </summary>
	static bool SameDrawing( ShaderVariant a, ShaderVariant b )
	{
		if ( ReferenceEquals( a, b ) ) return true;
		if ( a is null || b is null || !ReferenceEquals( a.Shader, b.Shader ) || !ReferenceEquals( a.Mode, b.Mode ) ) return false;
		if ( !SameStage( a.Vertex, b.Vertex ) || !SameStage( a.Pixel, b.Pixel ) ) return false;
		return SameState( a.State, b.State ) && System.Linq.Enumerable.SequenceEqual( a.Resources, b.Resources );
	}

	static bool SameStage( ShaderStage a, ShaderStage b ) => a is null || b is null
		? a is null && b is null
		: ReferenceEquals( a.Dynamic, b.Dynamic ) && MemoryExtensions.SequenceEqual<SpecializationConstant>( a.Constants, b.Constants );

	static bool SameState( ShaderPipelineState a, ShaderPipelineState b ) => a.Cull == b.Cull && a.Wireframe == b.Wireframe && a.DepthClip == b.DepthClip
		&& a.DepthBiasConstant == b.DepthBiasConstant && a.DepthBiasSlope == b.DepthBiasSlope && a.DepthBiasClamp == b.DepthBiasClamp && a.DepthTest == b.DepthTest && a.DepthWrite == b.DepthWrite
		&& a.DepthCompare == b.DepthCompare && a.StencilTest == b.StencilTest && a.StencilFront == b.StencilFront && a.StencilBack == b.StencilBack
		&& a.StencilReadMask == b.StencilReadMask && a.StencilWriteMask == b.StencilWriteMask && a.StencilReference == b.StencilReference
		&& a.AlphaToCoverage == b.AlphaToCoverage && MemoryExtensions.SequenceEqual<BlendState>( a.Blend, b.Blend );

	GpuDrivenBucket FindBucket( ShaderVariant variant, ShaderParameters attributes, long key, bool depthOnly, bool noColor, uint pixelMaterial, BackendMaterial material, string mode, OverlayStencil stencil, bool skinned = false, ShaderParameters own = null )
	{
		// Attributes that differ only in their lighting share a bucket: the variants read it from each draw's lighting record
		for ( int i = openBucket; i < gpuDrivenBucketCount; i++ )
		{
			var bucket = gpuDrivenBuckets[i];
			if ( SameDrawing( bucket.Variant, variant ) && bucket.AttributesKey == key && bucket.DepthOnly == depthOnly && bucket.NoColor == noColor && bucket.Stencil == stencil && bucket.PixelMaterial == pixelMaterial && bucket.Skinned == skinned && ReferenceEquals( bucket.Own, own ) ) return bucket;
		}

		if ( gpuDrivenBucketCount == gpuDrivenBuckets.Count ) gpuDrivenBuckets.Add( new GpuDrivenBucket() );
		var made = gpuDrivenBuckets[gpuDrivenBucketCount++];
		made.Variant = variant;
		made.Surface = null;
		made.SurfaceState = default;
		made.SurfaceFeatures = null;
		made.SurfaceProgram = null;
		made.SurfacePositionsOnly = false;
		made.Attributes = attributes;
		made.AttributesKey = key;
		made.DepthOnly = depthOnly;
		made.Classic = depthOnly ? Variant( material, mode ) : null;
		made.NoColor = noColor;
		made.Stencil = stencil;
		made.PixelMaterial = pixelMaterial;
		made.Material = material;
		made.Batches.Clear();
		made.Commands = 0;
		made.Ids = false;
		made.Skinned = skinned;
		made.Own = own;
		made.Fragments = 0;
		made.MeshletChunks32 = made.MeshletChunks16 = 0;
		return made;
	}

	/// <summary>GPU-driven runs skipped for having no index page (<see cref="DrawRun"/>); none is expected.</summary>
	internal static int PagelessRunsSkipped => pagelessRuns;

	static int pagelessRuns;

	/// <summary>
	/// One multi-draw: the bucket's pipeline and sets (bound once, through any of its materials' material object: a
	/// GPU-driven variant binds no material resources), the page's indices, and its run of commands: all of them, or as
	/// many as the count at <paramref name="countOffset"/> in the same buffer when it's 0 or more.
	/// </summary>
	void DrawRun( ref bool bound, GpuDrivenBucket bucket, Format color, Format depth, int samples, int depthBias, float slopeScaledDepthBias, PassResolver resolver,
		BufferHandle page, IndexFormat format, BufferHandle commands, long offset, int count, long countOffset = -1 )
	{
		// A run with no index page (a task run, if a plan's runs ever reach a call that doesn't draw tasks) has nothing to draw
		// here: throwing would leave the list half recorded
		if ( !page.IsValid )
		{
			System.Threading.Interlocked.Increment( ref pagelessRuns );
			return;
		}

		var r = recording;
		var censusBefore = Census ? PassTimer.Recorded.Of( r.List ) : default;
		if ( !bound && bucket.Surface is not null )
		{
			BindSurfaceGpuDriven( bucket, bucket.NoColor ? Format.Unknown : color, depth, samples, ids: false );
			if ( bucket.NoColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
			else if ( bucket.SurfaceState.HasDepthBias ) r.List.SetDepthBias( bucket.SurfaceState.DepthBias, bucket.SurfaceState.SlopeDepthBias );
			bound = true;
		}

		if ( !bound )
		{
			var cache = device.MaterialCache;
			var perDraw = bucket.PixelMaterial != 0;
			var layout = perDraw ? device.PulledLayoutMaterialPerDraw : device.PulledLayout;
			var key = new MaterialObjectCache.Key( bucket.Material, bucket.Variant, layout, bucket.NoColor ? Format.Unknown : color, depth, samples, bucket.DepthOnly, true, Normals: !bucket.NoColor && color != Format.Unknown && r.Normals is not null, Tools: bucket.NoColor || color == Format.Unknown ? ToolsPass.None : ToolsViewStage.ReadyPass( device.Shaders, ToolsPass ), Stencil: bucket.Stencil );
			var materialObject = cache.Get( key, immediate: false, (device, layout, key, perDraw), static ( s, l ) => s.device.Pipeline( s.key.Variant, s.layout, s.key.Color, s.key.Depth, s.key.Samples, s.key.DepthOnly, l.Sets, s.perDraw, normals: s.key.Normals, tools: s.key.Tools, stencil: s.key.Stencil ) );
			resolver.LayerEpoch = layerEpoch;
			materialObject.Bind( r.List, ref r.Pipeline, passEpoch, resolver );
			if ( bucket.NoColor ) r.List.SetDepthBias( depthBias, slopeScaledDepthBias );
			else if ( bucket.Variant?.State.HasDepthBias == true ) r.List.SetDepthBias( bucket.Variant.State.DepthBiasConstant, bucket.Variant.State.DepthBiasSlope, bucket.Variant.State.DepthBiasClamp );
			BackendDevice.SetStencilReference( r.List, depth, key.Stencil, bucket.Variant?.State );
			bound = true;
		}

		r.List.SetIndexBuffer( page, format, 0 );
		var scope = BeginBucketScope( bucket );
		if ( countOffset >= 0 ) r.List.DrawIndexedIndirectCount( commands, offset, commands, countOffset, count, GpuDrivenTables.CommandStride );
		else r.List.DrawIndexedIndirect( commands, offset, count, GpuDrivenTables.CommandStride );
		device.PassTimer?.End( r.List, scope );
		gpuDrivenRuns++;
		if ( Census ) CensusMultiDraw( bucket, DrawCensus.Path.Bucket, censusBefore, commands, offset, count, countOffset );
	}

	/// <summary>
	/// A stage's material record: its constants as the classic path fills its constant buffer (the material's values,
	/// this context's attributes, the draw's tangent format, the shader's defaults), then each material texture's bindless
	/// slot. Made once per material, stage and attribute values.
	/// </summary>
	uint MaterialRecord( GpuDrivenTables tables, BackendMaterial material, ShaderStage stage, bool uncompressedTangentFrame ) => stage is null ? 0 : MaterialRecord( tables, material, stage, uncompressedTangentFrame, AttributeHash( stage ) );

	/// <summary><see cref="MaterialRecord(GpuDrivenTables, BackendMaterial, ShaderStage, bool)"/> with the stage's <see cref="AttributeHash"/> already worked out.</summary>
	uint MaterialRecord( GpuDrivenTables tables, BackendMaterial material, ShaderStage stage, bool uncompressedTangentFrame, int attributeHash )
	{
		if ( stage is null ) return 0;

		var key = new GpuDrivenTables.MaterialRecordKey( material, material.Version, stage, uncompressedTangentFrame, attributeHash );
		if ( tables.TryMaterial( key, out var known ) ) return known;

		var size = Math.Max( stage.MaterialBuffer.Size, 4 );
		foreach ( var (_, offset) in stage.RecordTextures ) size = Math.Max( size, offset + 4 );

		return tables.Material( key, size, (Context: this, Material: material, Stage: stage, Uncompressed: uncompressedTangentFrame), static ( s, record ) => s.Context.WriteMaterialRecord( s.Material, s.Stage, s.Uncompressed, record ) );
	}

	/// <summary>A stage's attribute-sourced constants as <see cref="AttributeHash"/> reads them, and its last hash.</summary>
	sealed class StageAttributes
	{
		public StringToken[] Tokens;
		public bool Layered;
		public int LayerNames = int.MinValue;
		public long PassEpoch = -1, LayerEpoch = -1;
		public int Hash;
	}

	readonly Dictionary<ShaderStage, StageAttributes> attributeHashes = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// What a stage's attribute-sourced constants read now, as part of a material record's key: the same for every
	/// material, so it's worked out once per stage and pass state, and again per object attributes only where they can set
	/// one of its constants' attributes.
	/// </summary>
	int AttributeHash( ShaderStage stage )
	{
		var known = StageAttributesOf( stage );

		// Whether an object's attributes can set one (without material objects, which keep the names objects set, assumed so)
		var layerNames = device.MaterialObjects ? device.MaterialCache.LayerNames : int.MinValue + 1;
		if ( known.LayerNames != layerNames )
		{
			var layered = !device.MaterialObjects;
			foreach ( var token in known.Tokens ) layered |= device.MaterialCache.IsLayerName( token.Value );
			(known.Layered, known.LayerNames, known.PassEpoch) = (layered, layerNames, -1);
		}

		var layer = known.Layered ? layerEpoch : 0;
		if ( known.PassEpoch == passEpoch && known.LayerEpoch == layer ) return known.Hash;

		var hash = new HashCode();
		foreach ( var token in known.Tokens ) hash.Add( Lookup( token ) );
		(known.PassEpoch, known.LayerEpoch, known.Hash) = (passEpoch, layer, hash.ToHashCode());
		return known.Hash;
	}

	/// <summary>The attributes <see cref="AttributeHash"/> hashes for a stage, in order.</summary>
	StringToken[] AttributeNames( ShaderStage stage ) => StageAttributesOf( stage ).Tokens;

	StageAttributes StageAttributesOf( ShaderStage stage )
	{
		if ( !attributeHashes.TryGetValue( stage, out var known ) ) attributeHashes[stage] = known = new StageAttributes { Tokens = AttributeNamesOf( stage ) };
		return known;
	}

	/// <summary>
	/// A stage's attribute-sourced constants' attributes but the tangent format, which a draw's geometry decides: looked up
	/// once, as every material record's key reads them, where walking all its constants and naming each was most of the cost.
	/// </summary>
	static StringToken[] AttributeNamesOf( ShaderStage stage )
	{
		List<StringToken> found = null;
		var constants = stage.MaterialConstants;
		for ( int i = 0; i < constants.Count; i++ )
		{
			var variable = constants[i].Variable;
			if ( variable.Source != VariableSource.Attribute || variable.Attribute == "UncompressedTangentFrame" ) continue;
			(found ??= []).Add( Token( variable.Attribute ) );
		}

		return found?.ToArray() ?? [];
	}

	readonly Dictionary<ShaderStage, StringToken[]> matrixAttributes = new( ReferenceEqualityComparer.Instance );

	/// <summary>
	/// Whether the object attributes set one of a stage's attribute-sourced matrix constants (skin.shader's
	/// <c>EyeOcclusionViewProj</c>): material records are append-only and keyed by attribute values, so a per-object
	/// transform there would add records every frame. Its draws stay classic; unset (the shader's default) or set by the
	/// pass, it's in the record like any other attribute constant.
	/// </summary>
	bool ObjectSetsMatrix( ShaderStage stage )
	{
		if ( stage is null || layerEpoch == 0 ) return false;
		if ( !matrixAttributes.TryGetValue( stage, out var names ) )
		{
			List<StringToken> found = null;
			var constants = stage.MaterialConstants;
			for ( int i = 0; i < constants.Count; i++ )
			{
				var variable = constants[i].Variable;
				if ( variable.Source != VariableSource.Attribute || variable.Type is not (VariableType.Matrix3x3 or VariableType.Matrix3x4 or VariableType.Matrix4x4) ) continue;
				(found ??= []).Add( Token( variable.Attribute ) );
			}

			matrixAttributes[stage] = names = found?.ToArray() ?? [];
		}

		for ( int i = 0; i < names.Length; i++ )
		{
			if ( LayerValue( names[i], out _ ) is not null ) return true;
		}

		return false;
	}

	void WriteMaterialRecord( BackendMaterial material, ShaderStage stage, bool uncompressedTangentFrame, Span<byte> record )
	{
		var constants = Math.Min( stage.MaterialBuffer.Size, record.Length );
		if ( constants > 0 )
			WriteMaterialConstants( material, stage, uncompressedTangentFrame, record[..constants] );

		foreach ( var (variable, offset) in stage.RecordTextures )
		{
			material.Textures.TryGetValue( variable.Name, out var texture );
			var srgb = variable.RenderStateId == 1;
			int slot;
			if ( texture is { IsValid: true } && Gpu.BindlessIndex( texture.Handle ) is >= 0 and var index )
			{
				// SrgbRead( true ) textures read the sRGB view, one after the linear one, where the format has one
				slot = index + (srgb && Gpu.InfoOf( texture.Handle ).Format.HasSrgb() ? 1 : 0);
			}
			else
			{
				slot = device.MissingTextureViewIndex( srgb );
			}

			BinaryPrimitives.WriteInt32LittleEndian( record[offset..], slot );
		}
	}
}
