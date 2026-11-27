using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Draws <see cref="AggregateObject"/>s as native's <c>CAggregateSceneObjectDesc</c> does. Each view culls an aggregate's
/// fragments on the CPU, then its visible fragments are one multi-draw, occlusion culled fragment by fragment in the main
/// view. Aggregates are opaque, so they draw wherever the opaque runs do.
/// </summary>
internal sealed partial class AggregateRenderFeature : RenderFeature
{
	/// <summary>
	/// An aggregate's fragments visible in a view: their args in the frame's args buffer, and their occlusion cull run's, or -1.
	/// </summary>
	internal readonly record struct Batch( AggregateObject Aggregate, int ObjectIndex, int FirstArg, int Count, int CullArg, long Triangles, ulong Key, int TextureSize );

	internal sealed class Batches
	{
		public readonly List<Batch> List = new();

		/// <summary>Whether the device draws them GPU-driven: then a layer's batches record as one run, so one multi-draw per bucket.</summary>
		public bool OneRun;

		/// <summary>The resident aggregates the view draws besides <see cref="List"/> (AggregateRenderFeature.Resident.cs), or null.</summary>
		public ResidentList Resident;

		/// <summary>The frame <see cref="Resident"/> and <see cref="OneRun"/> were worked out in (<see cref="PrepareResident"/>).</summary>
		public int ResidentGeneration = -1;
	}

	/// <summary>
	/// The args of every view's batches this frame, uploaded once in <see cref="Setup"/>.
	/// </summary>
	GpuBuffer.IndirectDrawIndexedArguments[] args = new GpuBuffer.IndirectDrawIndexedArguments[256];
	int argCount;
	int argGeneration = -1;
	readonly UploadRing<GpuBuffer.IndirectDrawIndexedArguments> argsRing = new( "Borealis AggregateArgs", GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments );

	GpuMaterial depthOnlyMaterial, depthOnlyTwoSided;
	bool depthOnlyLoaded;
	TransformBuffer transforms;

	static readonly Comparison<Batch> ByKey = ( a, b ) => a.Key.CompareTo( b.Key );

	/// <summary>
	/// Collects the main view's fragments for occlusion culling. Null when not culling, and never set for shadow views.
	/// </summary>
	internal OcclusionCuller Occlusion { get; set; }

	public override Type ObjectType => typeof( AggregateObject );

	internal override bool DrawsShadows => true;

	internal override bool CastsShadow( RenderObject obj ) => obj.LayerMatch == LayerMatch.None && obj is AggregateObject { CastShadows: true };

	/// <summary>
	/// A view's batches, after <see cref="Prepare"/> - for the tests.
	/// </summary>
	internal List<Batch> BatchesFor( ViewPass pass ) => pass.State<Batches>( this ).List;

	/// <summary>
	/// The frame's args so far - for the tests.
	/// </summary>
	internal ReadOnlySpan<GpuBuffer.IndirectDrawIndexedArguments> Args => args.AsSpan( 0, argCount );

	/// <summary>
	/// Cull each visible aggregate's fragments against the view (<c>CAggregateSceneObjectDesc::GeneratePrimitives</c>), write
	/// the args of those left, and the fragments' transforms once a frame.
	/// </summary>
	internal override void Prepare( RenderWorld world, ViewPass pass, TransformBuffer transforms )
	{
		using var zone = Zones.AggregatePrepare.Start();

		// The frame's transforms, whose instance ids a GPU-driven draw reads on the CPU (the same buffer for every view)
		this.transforms = transforms;

		var state = pass.State<Batches>( this );
		var batches = state.List;
		batches.Clear();
		PrepareResident( world, pass, transforms );

		var visible = pass.Visible( this );

		// The GPU culls each fragment of what it draws GPU-driven: every fragment's args, for a batch it leaves to the classic
		// path, without testing or sizing them one by one; a worker sizes them, and resident meshes, for texture streaming
		// (AggregateRenderFeature.Sizes.cs)
		var gpuCulls = state.OneRun && RenderHost.Current.Device.CullsAggregatesGpuDriven;
		var gpuCulledSize = gpuCulls && (visible.Count > 0 || state.Resident is not null) ? GpuCulledSize( pass, world, visible, state.Resident ) : 0;
		if ( visible.Count == 0 ) return;

		var objects = world.Objects;
		var frustum = pass.View.Frustum;
		var size = SizeCull.ForPass( pass );
		var shadow = pass.IsShadow;
		var textureSizes = new TextureSizes( pass.View, pass.Root );
		var viewCamera = pass.View.Position;
		var objectCenters = world.BoundsCenter;
		var versions = world.EntryVersions;
		var objectExtents = world.BoundsExtents;
		var occlusion = !shadow && Occlusion is { CullsFragments: true } culler ? culler : null;
		var holds = state.Resident?.Holds;

		foreach ( var index in CollectionsMarshal.AsSpan( visible ) )
		{
			// Drawn from the view's resident list
			if ( holds is not null && index < holds.Length && holds[index] ) continue;

			var aggregate = (AggregateObject)objects[index];
			if ( !aggregate.Visible || (!shadow && !aggregate.GameLayers) ) continue;

			var fragments = aggregate.Fragments;
			var centers = aggregate.Centers;
			var extents = aggregate.Extents;
			if ( argCount + fragments.Length > args.Length ) Array.Resize( ref args, Math.Max( argCount + fragments.Length, args.Length * 2 ) );

			var first = argCount;
			long indices = 0;
			if ( gpuCulls )
			{
				for ( int i = 0; i < fragments.Length; i++ )
				{
					if ( shadow && (fragments[i].Flags & FragmentFlags.DoNotCastShadows) != 0 ) continue;
					args[argCount++] = fragments[i].Args;
					indices += fragments[i].Args.IndexCount;
				}

				if ( argCount == first ) continue;
				WriteTransforms( aggregate, index, versions[index], transforms );
				batches.Add( new Batch( aggregate, index, first, argCount - first, -1, indices / 3, SortKey( aggregate ), gpuCulledSize ) );
				continue;
			}

			// One texture size for all of it, the largest any fragment left needs
			var objectFallback = textureSizes.ForObject( objectCenters[index], objectExtents[index] ).Fallback;
			var textureSize = 0.0f;
			for ( int i = 0; i < fragments.Length; i++ )
			{
				if ( shadow && (fragments[i].Flags & FragmentFlags.DoNotCastShadows) != 0 ) continue;
				if ( !textureSizes.TrySizeFragment( frustum, size, fragments[i].UvDensity, centers[i], extents[i], viewCamera, objectFallback, out var requested ) ) continue;

				args[argCount++] = fragments[i].Args;
				indices += fragments[i].Args.IndexCount;
				textureSize = MathF.Max( textureSize, requested );
			}

			var count = argCount - first;
			if ( count == 0 ) continue;

			WriteTransforms( aggregate, index, versions[index], transforms );
			var cull = occlusion is not null && aggregate.OcclusionCulling ? occlusion.CommitFragments( args.AsSpan( first, count ), centers, extents ) : -1;
			batches.Add( new Batch( aggregate, index, first, count, cull, indices / 3, SortKey( aggregate ), TextureSizes.Clamp( textureSize ) ) );
		}

		// Similar materials one after another, so each bind reuses what the last set (CBaseSceneObjectDesc::BindMaterial)
		batches.Sort( ByKey );
	}

	/// <summary>
	/// Every fragment's transform, with instance ids in fragment order, once a frame: a draw's base instance is its fragment's
	/// index (<c>CAggregateSceneObjectDesc::FindOrAllocateFragmentTransforms</c>). The transforms stay in the aggregate's run of
	/// kept entries between frames, rewritten when its entry <paramref name="version"/> moves (its tint or the world's
	/// change of it); its first frame drawn, they're frame entries.
	/// </summary>
	static int WriteTransforms( AggregateObject aggregate, int index, int version, TransformBuffer transforms )
	{
		var firstInstance = transforms.AggregateFirstInstance( index );
		if ( firstInstance >= 0 ) return firstInstance;

		firstInstance = transforms.InstanceCount;

		var tint = aggregate.Tint;
		var fragments = aggregate.Fragments;
		var first = transforms.KeptRunFor( index, aggregate, fragments.Length, version, out var write );
		for ( int i = 0; i < fragments.Length; i++ )
		{
			int slot;
			if ( first >= 0 )
			{
				slot = first + i;
				if ( write )
				{
					var drawTint = fragments[i].DrawTint;
					transforms.WriteKept( slot, fragments[i].Transform, new Color( tint.r * drawTint.x, tint.g * drawTint.y, tint.b * drawTint.z, tint.a ) );
				}
			}
			else
			{
				var drawTint = fragments[i].DrawTint;
				slot = transforms.Add( fragments[i].Transform, new Color( tint.r * drawTint.x, tint.g * drawTint.y, tint.b * drawTint.z, tint.a ) );
			}

			transforms.AddInstance( slot );
		}

		transforms.SetAggregateFirstInstance( index, firstInstance );
		return firstInstance;
	}

	/// <summary>
	/// Similarity (24 bits), then material (16), as <see cref="MeshRenderFeature"/>'s opaque runs sort.
	/// </summary>
	static ulong SortKey( AggregateObject aggregate )
	{
		var similarity = aggregate.Mesh.SimilarityForLod( 0 );
		similarity ^= similarity >> 24 ^ similarity >> 48;

		var draws = aggregate.Mesh.DrawsForLod( 0 );
		var material = (uint)RuntimeHelpers.GetHashCode( draws.Length > 0 ? draws[0].Material : null );
		material = (material ^ material >> 16) & 0xFFFF;

		return (similarity & 0xFFFFFF) << 40 | (ulong)material << 24;
	}

	/// <summary>
	/// Upload the frame's args, after every view has prepared, and make the resident lists' device batches the frame's layers
	/// will draw (<see cref="MakeDeviceBatches"/>).
	/// </summary>
	internal override void Setup( GpuContext context, RenderWorld world, ViewPass view )
	{
		// On the main thread: draws may record on worker threads, where materials can't be made
		if ( !depthOnlyLoaded )
		{
			depthOnlyMaterial = MeshRenderFeature.LoadDepthOnly( RenderHost.Current.Device );
			depthOnlyTwoSided = MeshRenderFeature.LoadDepthOnlyTwoSided( RenderHost.Current.Device, depthOnlyMaterial );
			depthOnlyLoaded = true;
		}
		if ( statsSlot == 0 ) statsSlot = RenderHost.Current.Device.AcquireCullStats();

		var buffer = argsRing.UploadWithPrefix( context, args.AsSpan( 0, argCount ), argPrefixCount, residentArgsVersion, out var uploaded );
		if ( uploaded )
			context.BufferBarrier( buffer, GpuStage.Transfer, GpuAccess.TransferWrite, GpuStage.DrawIndirect, GpuAccess.IndirectCommandRead );

		// After the mesh feature's setup (features set up in the order they were added), as resident mesh groups' batches take
		// its depth-only material
		MakeDeviceBatches();
	}

	/// <summary>The device slot this feature's GPU culls count what they let through into (<see cref="GpuDevice.AcquireCullStats"/>), or 0.</summary>
	int statsSlot;

	/// <summary>Add what the GPU culls of this feature's views let through, read back a few frames late, to a frame's stats.</summary>
	internal void AddCulledCounts( ref RenderStats stats )
	{
		if ( statsSlot != 0 ) RenderHost.Current.Device.CullCounts( statsSlot ).AddTo( ref stats );
	}

	/// <summary>
	/// A batch for each aggregate, in every layer that draws the opaque runs; GPU-driven, the runs they and the mesh draws split
	/// into, and none when there's nothing at all to draw (no batch, no mesh draw, no resident list), as in an empty scene.
	/// </summary>
	internal override int RunCount( ViewPass view, MeshLayer layer )
	{
		if ( (layer.Runs & MeshRuns.Opaque) == 0 ) return 0;
		var state = view.State<Batches>( this );
		if ( !state.OneRun ) return state.List.Count;

		var items = state.List.Count + MeshDraws( view ).Count;
		return items == 0 && state.Resident is not { DeviceCount: > 0 } ? 0 : Chunks( items );
	}

	/// <summary>
	/// GPU-driven runs a layer's batches and mesh draws split into, which workers record side by side, each one call with
	/// buckets of its own: one per <see cref="ItemsPerChunk"/> items, at most <see cref="MaxChunks"/>.
	/// </summary>
	static int Chunks( int items ) => Math.Clamp( items / ItemsPerChunk, 1, MaxChunks );

	const int ItemsPerChunk = 256;

	/// <summary>At most this many GPU-driven runs a layer: 1, or what <c>-gpudriven-chunks N</c> asks for, for measuring.</summary>
	static readonly int MaxChunks = ChunksSwitch();

	static int ChunksSwitch()
	{
		var args = Environment.GetCommandLineArgs();
		var at = Array.FindIndex( args, a => a.Equals( "-gpudriven-chunks", StringComparison.OrdinalIgnoreCase ) );
		return at >= 0 && at + 1 < args.Length && int.TryParse( args[at + 1], out var chunks ) ? Math.Clamp( chunks, 1, 8 ) : 1;
	}

	/// <summary>Run <paramref name="chunk"/> of <paramref name="chunks"/>' first item, of <paramref name="items"/>: they split them evenly.</summary>
	static int ChunkStart( int chunk, int chunks, int items ) => (int)((long)chunk * items / chunks);

	/// <summary>Each GPU-driven run as heavy as the batches and draws it holds.</summary>
	internal override void RunWeights( ViewPass view, MeshLayer layer, Span<int> weights )
	{
		var state = view.State<Batches>( this );
		if ( !state.OneRun )
		{
			weights.Fill( 1 );
			return;
		}

		var items = state.List.Count + MeshDraws( view ).Count;
		for ( int c = 0; c < weights.Length; c++ ) weights[c] = Math.Max( ChunkStart( c + 1, weights.Length, items ) - ChunkStart( c, weights.Length, items ), 1 );

		// A resident list costs the first run about a multi-draw per bucket, not a batch each; and setup makes its batches for
		// the layer's depth kind
		if ( state.Resident is { } resident )
		{
			weights[0] += resident.DeviceCount / ResidentWeightDivisor;
			WantDeviceBatches( state, layer );
		}
	}

	/// <summary>
	/// The mesh feature, whose static opaque draws a view hands this one when the device draws GPU-driven
	/// (<see cref="MeshRenderFeature.GpuDrivenItems"/>), so they go in the same buckets as the aggregates.
	/// </summary>
	internal MeshRenderFeature Meshes { get; set; }

	/// <summary>
	/// Drawing a 3D skybox, which shows only where the main world's depth left sky: its resident meshes don't fade their LODs.
	/// Its prepass still draws ids and its forward pass lists, which cut the skybox's own overdraw.
	/// </summary>
	internal bool Skybox { get; set; }

	static readonly List<MeshRenderFeature.DrawItem> NoDraws = new();

	List<MeshRenderFeature.DrawItem> MeshDraws( ViewPass view ) => Meshes?.GpuDrivenItems( view ) ?? NoDraws;

	/// <summary>
	/// Draw each batch as one multi-draw: its own args, or the occlusion cull's for the layer's phase.
	/// </summary>
	internal override void Draw( GpuContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		using var zone = Zones.GpuDrivenDraw.Start();
		var state = view.State<Batches>( this );
		var batches = state.OneRun ? default : CollectionsMarshal.AsSpan( state.List ).Slice( first, count );

		// GPU-driven, runs [first, first + count) of the layer's batches then mesh draws, split evenly
		ReadOnlySpan<MeshRenderFeature.DrawItem> meshDraws = default;
		if ( state.OneRun )
		{
			var all = CollectionsMarshal.AsSpan( MeshDraws( view ) );
			var items = state.List.Count + all.Length;
			var chunks = Chunks( items );
			var (from, to) = (ChunkStart( first, chunks, items ), ChunkStart( first + count, chunks, items ));
			var aggregates = state.List.Count;
			batches = CollectionsMarshal.AsSpan( state.List )[Math.Min( from, aggregates )..Math.Min( to, aggregates )];
			meshDraws = all[(Math.Max( from, aggregates ) - aggregates)..(Math.Max( to, aggregates ) - aggregates)];
		}

		var depth = layer.Depth;
		var mode = layer.ShaderMode;
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		var depthBias = shadow ? view.DepthBias : 0;
		var slopeBias = shadow ? view.SlopeScaledDepthBias : 0;

		// As MeshRenderFeature: the early prepass culls only with last frame's chain, the late one draws only culled batches
		var phase = context.Occlusion is { } occlusion && (layer.OcclusionPhase != OcclusionPhase.Early || occlusion.CullsEarly) ? layer.OcclusionPhase : OcclusionPhase.None;
		var culledOnly = layer.OcclusionPhase == OcclusionPhase.Late;


		// The device's GPU-driven path first, for what it can take: batches drawn from its own args, not the culler's
		var gpuDriven = context.GpuDriven && phase == OcclusionPhase.None && !culledOnly && transforms is not null;
		var resident = gpuDriven && first == 0 ? state.Resident : null;
		var residentCount = resident?.DeviceCount ?? 0;
		var total = gpuDriven ? residentCount + batches.Length + meshDraws.Length : 0;
		var rented = total > 256 ? System.Buffers.ArrayPool<bool>.Shared.Rent( total ) : null;
		Span<bool> allDrawn = rented is null ? stackalloc bool[total] : rented.AsSpan( 0, total );
		var accepted = allDrawn[residentCount..];
		if ( gpuDriven )
		{
			allDrawn.Clear();
			// The frame copy frame-buffer readers among the mesh draws read, taken as the mesh feature takes it before its first
			// reader (where the layer copies at all)
			if ( !depth && !shadow && (ReadsFrameBuffer( view, meshDraws ) || (first == 0 && state.Resident is { ReadsFrameBuffer: true })) ) context.CopyFrameBuffer();

			// The view's resident list first, in the first run, in the same call: the device keeps what it makes of it
			var counted = DrawGpuDriven( context, view, resident, residentCount, batches, meshDraws, mode, layer, depthBias, slopeBias, allDrawn, ref stats );

			for ( int i = counted; i < batches.Length; i++ )
			{
				if ( !accepted[i] ) continue;
				CountDrawn( batches[i], shadow, depth, ref stats );
			}
		}

		// The mesh draws it didn't take, the mesh feature's way
		if ( !meshDraws.IsEmpty )
		{
			var left = System.Buffers.ArrayPool<MeshRenderFeature.DrawItem>.Shared.Rent( meshDraws.Length );
			var leftCount = 0;
			for ( int i = 0; i < meshDraws.Length; i++ )
			{
				if ( gpuDriven && accepted[batches.Length + i] ) CountMeshDrawn( view, meshDraws[i], shadow, depth, layer.LayerType == MeshLayerType.Translucent, ref stats );
				else left[leftCount++] = meshDraws[i];
			}

			Meshes.DrawGpuDrivenLeft( context, view, left.AsSpan( 0, leftCount ), layer, ref stats );
			System.Buffers.ArrayPool<MeshRenderFeature.DrawItem>.Shared.Return( left );
		}

		for ( int b = 0; b < batches.Length; b++ )
		{
			ref readonly var batch = ref batches[b];
			if ( gpuDriven && accepted[b] ) continue;
			if ( culledOnly && batch.CullArg < 0 ) continue;

			var aggregate = batch.Aggregate;
			var mesh = aggregate.Mesh;
			var draws = mesh.DrawsForLod( 0 );
			if ( draws.Length == 0 ) continue;

			// Every fragment shares the first draw's buffers and material
			ref readonly var draw = ref draws[0];
			var material = MaterialFor( draw, depth, layer.ForceDepthFastPath, out var stripPixelShader );
			if ( material is null ) continue;
			if ( context.Census ) context.CensusNote = gpuDriven ? "aggregate: the device's GPU-driven path left it" : !context.GpuDriven ? "aggregate: GPU-driven off" : phase != OcclusionPhase.None || culledOnly ? "aggregate: occlusion culled" : "aggregate: no transforms";

			// Lightmaps light every draw or none of them (CAggregateSceneObjectDesc::SetBakedLightingAttributes)
			context.SetObjectAttributes( null, !depth && aggregate.Lightmapped ? aggregate.LightmapAttributes : null );

			var firstInstance = transforms.AggregateFirstInstance( batch.ObjectIndex );
			var drawn = batch.CullArg >= 0 && phase != OcclusionPhase.None
				? context.DrawModelFragmentsCulled( material, mode, mesh, draw, phase, firstInstance, batch.CullArg, batch.Count, depthBias, slopeBias, stripPixelShader, batch.TextureSize )
				: DrawFragmentsInView( context, view, shadow, material, mode, mesh, draw, aggregate, firstInstance, batch.FirstArg, batch.Count, depthBias, slopeBias, stripPixelShader, batch.TextureSize );
			if ( !drawn ) continue;

			CountDrawn( batch, shadow, depth, ref stats );
		}

		if ( rented is not null ) System.Buffers.ArrayPool<bool>.Shared.Return( rented );
		context.SetObjectAttributes( null );
	}

	/// <summary>Whether any of the mesh draws reads the frame buffer copy (<see cref="MeshRenderFeature.ReadersTaken"/>).</summary>
	/// <summary>Whether a prepared view's resident list draws a frame-buffer reader, which may be anywhere on its screen.</summary>
	internal bool ResidentReadsFrameBuffer( ViewPass view ) => view.State<Batches>( this ).Resident is { ReadsFrameBuffer: true };

	bool ReadsFrameBuffer( ViewPass view, ReadOnlySpan<MeshRenderFeature.DrawItem> meshDraws )
	{
		if ( meshDraws.IsEmpty || !Meshes.ReadersTaken( view ) ) return false;
		foreach ( var item in meshDraws )
		{
			if ( Meshes.ReadsFrameBuffer( view, item ) ) return true;
		}

		return false;
	}

	/// <summary>Count a mesh draw the GPU-driven path took, as the mesh feature counts its draws.</summary>
	void CountMeshDrawn( ViewPass view, MeshRenderFeature.DrawItem item, bool shadow, bool depth, bool translucentLayer, ref RenderStats stats )
	{
		var (triangles, instances) = Meshes.GpuDrivenCounts( view, item );
		stats.DrawnTriangles += triangles;
		if ( shadow )
		{
			stats.ShadowDraws++;
			return;
		}

		if ( depth )
		{
			stats.DepthDraws++;
			return;
		}

		if ( translucentLayer ) stats.TranslucentDraws++;
		stats.Draws++;
		stats.Instances += instances;
		stats.Triangles += triangles;
	}

	static void CountDrawn( in Batch batch, bool shadow, bool depth, ref RenderStats stats )
	{
		stats.DrawnTriangles += batch.Triangles;
		if ( shadow )
		{
			stats.ShadowDraws++;
			return;
		}

		if ( depth )
		{
			stats.DepthDraws++;
			return;
		}

		stats.Draws++;
		stats.Instances += batch.Count;
		stats.Triangles += batch.Triangles;
	}

	/// <summary>
	/// Hand the device every batch at once for its GPU-driven path (<see cref="GpuContext.DrawGpuDriven"/>), with the
	/// material each would bind here.
	/// </summary>
	int DrawGpuDriven( GpuContext context, ViewPass view, ResidentList resident, int residentCount, ReadOnlySpan<Batch> batches, ReadOnlySpan<MeshRenderFeature.DrawItem> meshDraws, StringToken mode, MeshLayer layer, int depthBias, float slopeBias, Span<bool> drawn, ref RenderStats stats )
	{
		var depth = layer.Depth;
		var forceDepthOnly = layer.ForceDepthFastPath;
		var total = drawn.Length;
		if ( total == 0 ) return 0;

		// A resident list alone is handed over as the list's own batches, without a copy; with batches after it, in an array
		// of the list's that already starts with them (TakeCombined), so the call writes only its own
		var alone = resident is not null && batches.IsEmpty && meshDraws.IsEmpty;
		var kind = DeviceKind( depth, forceDepthOnly );
		var deviceBatches = resident is not null ? DeviceBatches( resident, depth, forceDepthOnly ) : default;
		var combined = !alone && resident is not null ? TakeCombined( resident, kind, deviceBatches, total ) : null;
		var list = alone ? null : combined?.Batches ?? System.Buffers.ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Rent( total );
		var count = 0;
		if ( !alone )
		{
			count = residentCount;
			for ( int i = 0; i < batches.Length; i++ ) list[count++] = DeviceBatch( batches[i], depth, forceDepthOnly );

			// Then the mesh draws, each its instances
			foreach ( var item in meshDraws ) Meshes.WriteGpuDrivenBatch( view, item, depth, forceDepthOnly, out list[count++] );
		}

		context.DrawGpuDriven( mode, alone ? deviceBatches : list.AsSpan( 0, count ), args.AsSpan( 0, argCount ), transforms, depthBias, slopeBias, drawn, CullFor( view, depth, resident, resident?.Version ?? 0, residentCount ) );

		// Batches the device counted what survived of (not mesh draws, which the CPU culled): the caller counts the rest
		var counted = Math.Min( context.GpuCountedBatches, residentCount + batches.Length );
		if ( resident is not null ) ResidentDrawn( context, view, layer, resident, alone ? deviceBatches : list.AsSpan( 0, residentCount ), drawn[..residentCount], Math.Min( counted, residentCount ), mode, depthBias, slopeBias, ref stats );
		if ( combined is not null ) ReturnCombined( resident, kind, combined, count );
		else if ( list is not null )
		{
			list.AsSpan( 0, count ).Clear();
			System.Buffers.ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Return( list );
		}

		return Math.Max( counted - residentCount, 0 );
	}

	/// <summary>A batch as the device takes it, with the material it would bind here; default when it has nothing to draw.</summary>
	GpuContext.GpuDrivenBatch DeviceBatch( in Batch batch, bool depth, bool forceDepthOnly )
	{
		var draws = batch.Aggregate.Mesh.DrawsForLod( 0 );
		var strip = false;
		var material = draws.Length > 0 ? MaterialFor( draws[0], depth, forceDepthOnly, out strip ) : null;
		if ( material is null ) return default;

		var attributes = !depth && batch.Aggregate.Lightmapped ? batch.Aggregate.LightmapAttributes : null;
		return new GpuContext.GpuDrivenBatch( material, batch.Aggregate.Mesh, draws[0], attributes, strip, batch.FirstArg, batch.Count, transforms.AggregateFirstInstance( batch.ObjectIndex ), batch.Triangles, batch.Aggregate, TextureSize: batch.TextureSize );
	}

	/// <summary>The tests Prepare made per fragment, for the device to make on the GPU over every fragment instead.</summary>
	GpuContext.GpuDrivenCull CullFor( ViewPass view, bool depth, object plan, int planVersion, int planBatches )
	{
		var frustum = view.View.Frustum;
		var root = view.Root;
		var texels = view.LodTexelsPerUnit > 0;
		return new GpuContext.GpuDrivenCull
		{
			// Resident meshes: culled as the CPU's collect culls them, their LOD picked as Classify picks it
			KeepsSmallMeshes = view.KeepsSmallCasters,
			LodCamera = texels ? default : new Vector4( root.Position, MathF.Tan( root.FieldOfView.DegreeToRadian() * 0.5f ) ),
			LodWidth = texels ? view.LodTexelsPerUnit : root.Viewport.Width,
			LodTexels = texels,
			GeneratedLods = texels || !view.IsShadow && !view.View.Capture && ReferenceEquals( view.View, root ),
			FadeView = !texels && !view.IsShadow && !view.View.Capture && !Skybox && ReferenceEquals( view.View, root ) ? view.View : null,
			Left = frustum.Plane( 0 ),
			Right = frustum.Plane( 1 ),
			Bottom = frustum.Plane( 2 ),
			Top = frustum.Plane( 3 ),
			Near = frustum.Plane( 4 ),
			Far = frustum.Plane( 5 ),
			WorldToClip = view.View.WorldToProjection,
			ViewportSize = new Vector2( view.View.Viewport.Width, view.View.Viewport.Height ),
			CameraAndThreshold = SizeCull.ForPass( view ).CameraAndThreshold,
			Shadow = view.IsShadow,
			View = view,
			// Not for captures: a probe's faces would hold visibility runs for all they see, leaving the views that list none. A 3D
			// skybox keeps them: its own overdraw is what they cut, and it can fill most of a wide view (Construct's overview)
			Ids = depth && !view.IsShadow && !view.View.Capture,
			// Nor in a geometric debug view: its tools pass draws every triangle, not only those that own a sample
			Visibility = !depth && !view.IsShadow && !view.View.Capture && ToolsViews.PassOf( view.View ) == ToolsPass.None,
			Plan = plan,
			PlanVersion = planVersion,
			PlanBatches = planBatches,

			// The far cascade's cache and what's drawn over it: the GPU takes their list's casters by when each last moved
			Filter = plan is ResidentList && view.IsShadow ? ResidentFilterFor( view.StaticFilter ) : GpuContext.ResidentFilter.None,
			SettledStamp = GpuContext.StampWord( view.SettledStamp ),
			RestlessStamp = GpuContext.StampWord( view.RestlessStamp ),
			StatsSlot = statsSlot,
		};
	}

	/// <summary>Which of its resident list's casters a view keeps by when each last moved.</summary>
	static GpuContext.ResidentFilter ResidentFilterFor( StaticFilter filter ) => filter switch
	{
		StaticFilter.Settled => GpuContext.ResidentFilter.Settled,
		StaticFilter.SettledNoStatic => GpuContext.ResidentFilter.SettledNoStatic,
		StaticFilter.Unsettled => GpuContext.ResidentFilter.Unsettled,
		_ => GpuContext.ResidentFilter.None,
	};

	/// <summary>A resident list's weight in its run: a batch of it costs the run about this many times less than one of the view's own.</summary>
	internal const int ResidentWeightDivisor = 16;

	/// <summary>
	/// Draw one render order's resident static overlays as one call that keeps their order (<see cref="GpuContext.GpuDrivenCull.KeepsOrder"/>):
	/// culled on the GPU, a command per overlay in list order. Overlays the device leaves go undrawn this frame and the
	/// view's own way from the next.
	/// </summary>
	internal void DrawOverlayOrder( GpuContext context, ViewPass view, OverlayOrder order, MeshLayer layer, ref RenderStats stats )
	{
		var batches = order.Device;
		if ( batches.Length == 0 ) return;

		var rented = System.Buffers.ArrayPool<bool>.Shared.Rent( batches.Length );
		var drawn = rented.AsSpan( 0, batches.Length );
		drawn.Clear();
		var cull = CullFor( view, false, order, order.Version, batches.Length );
		cull.View = null;
		cull.Visibility = false;
		cull.GeneratedLods = false;
		cull.KeepsOrder = true;
		context.DrawGpuDriven( layer.ShaderMode, batches, default, transforms, 0, 0, drawn, cull );
		var counted = context.GpuCountedBatches;

		if ( drawn.IndexOf( false ) < 0 )
		{
			if ( counted < batches.Length )
			{
				stats.DrawnTriangles += order.Triangles;
				stats.Draws += batches.Length;
				stats.Instances += order.Fragments;
				stats.Triangles += order.Triangles;
			}
		}
		else
		{
			for ( int i = 0; i < batches.Length; i++ )
			{
				if ( drawn[i] )
				{
					if ( i >= counted ) stats.Draws++;
				}
				else if ( batches[i].Owner is GpuContext.ResidentMeshes owner ) MeshesLeft( order.Groups, owner );
			}
		}

		System.Buffers.ArrayPool<bool>.Shared.Return( rented );
	}

	/// <summary>
	/// Count what the device drew of a view's resident list (<see cref="GpuContext.GpuDrivenCull.Plan"/>) and draw what it left
	/// the classic way, from the frame's arguments.
	/// </summary>
	void ResidentDrawn( GpuContext context, ViewPass view, MeshLayer layer, ResidentList list, ReadOnlySpan<GpuContext.GpuDrivenBatch> batches, ReadOnlySpan<bool> drawn, int counted, StringToken mode, int depthBias, float slopeBias, ref RenderStats stats )
	{
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		var depth = layer.Depth;
		if ( drawn.IndexOf( false ) < 0 )
		{
			// All of it: counted on the device, or the list's totals
			if ( counted >= batches.Length ) return;

			stats.DrawnTriangles += list.Triangles;
			if ( shadow ) stats.ShadowDraws += batches.Length;
			else if ( depth ) stats.DepthDraws += batches.Length;
			else
			{
				stats.Draws += batches.Length;
				stats.Instances += list.Fragments;
				stats.Triangles += list.Triangles;
			}

			return;
		}

		var own = CollectionsMarshal.AsSpan( list.Batches );
		for ( int i = 0; i < own.Length; i++ )
		{
			if ( drawn[i] )
			{
				if ( i >= counted ) CountDrawn( own[i], shadow, depth, ref stats );
				continue;
			}

			ref readonly var batch = ref batches[i];
			if ( batch.Material is null ) continue;
			if ( context.Census ) context.CensusNote = "aggregate: the device's GPU-driven path left it (resident)";
			context.SetObjectAttributes( null, batch.ObjectAttributes );
			if ( DrawFragmentsInView( context, view, shadow, batch.Material, mode, batch.Mesh, batch.Draw, own[i].Aggregate, batch.FirstInstance, batch.FirstArg, batch.Count, depthBias, slopeBias, batch.StripPixelShader, batch.TextureSize ) )
				CountDrawn( own[i], shadow, depth, ref stats );
		}

		// Resident meshes: those the device left go undrawn this frame and stay with the views' own batches after it
		for ( int i = own.Length; i < batches.Length; i++ )
		{
			if ( drawn[i] )
			{
				if ( i < counted ) continue;
				if ( shadow ) stats.ShadowDraws++;
				else if ( depth ) stats.DepthDraws++;
				else stats.Draws++;
				continue;
			}

			if ( batches[i].Material is not null && batches[i].Owner is GpuContext.ResidentMeshes owner ) ResidentMeshesLeft( list, owner );
		}

		context.SetObjectAttributes( null );
	}

	/// <summary>
	/// A batch's fragments on the classic path, from args that hold every one of them untested: the GPU culls what it draws
	/// GPU-driven (<see cref="Prepare"/>, resident lists), but a batch it leaves (a material with no GPU-driven variant) would
	/// otherwise draw fragments the view can't see. Here the view's frustum takes those out, the survivors drawn a run of
	/// consecutive args at a time. A shadow view's args leave out what casts none, so they don't line up with the fragments:
	/// those draw as they are. False when nothing was drawn.
	/// </summary>
	bool DrawFragmentsInView( GpuContext context, ViewPass view, bool shadow, GpuMaterial material, StringToken mode, RenderMesh mesh, in RenderMesh.Draw draw, AggregateObject aggregate, int firstInstance, int firstArg, int count, int depthBias, float slopeBias, bool stripPixelShader, int textureSize )
	{
		var centers = aggregate.Centers;
		if ( shadow || !CullsLeftFragments || count != centers.Length )
			return context.DrawModelFragments( material, mode, mesh, draw, firstInstance, argsRing.Current, firstArg, count, depthBias, slopeBias, stripPixelShader, textureSize );

		var extents = aggregate.Extents;
		var frustum = view.View.Frustum;
		var drew = false;
		var run = -1;
		for ( int i = 0; i <= count; i++ )
		{
			if ( i < count && frustum.Intersects( centers[i], extents[i] ) )
			{
				if ( run < 0 ) run = i;
				continue;
			}

			if ( run < 0 ) continue;
			drew |= context.DrawModelFragments( material, mode, mesh, draw, firstInstance, argsRing.Current, firstArg + run, i - run, depthBias, slopeBias, stripPixelShader, textureSize );
			run = -1;
		}

		return drew;
	}

	/// <summary>
	/// Whether <see cref="DrawFragmentsInView"/> frustum-culls a left batch's fragments (on by default); BorealisLab's
	/// <c>-noleftcull</c> variants turn it off to measure it.
	/// </summary>
	internal static bool CullsLeftFragments { get; set; } = true;

	/// <summary>
	/// The depth-only material on the fast path, else the draw's own, with the pixel shader stripped for an opaque depth pass
	/// (<c>CSceneSystem::ShouldOverrideDepthMaterial</c>, <c>CAggregateSceneObjectDesc::DrawArrayExt</c>).
	/// </summary>
	GpuMaterial MaterialFor( in RenderMesh.Draw draw, bool depth, bool forceDepthOnly, out bool stripPixelShader )
	{
		stripPixelShader = false;
		if ( depth && forceDepthOnly && depthOnlyMaterial is not null ) return depthOnlyMaterial;
		if ( depth && (draw.ShadowFastPath || draw.Material is { DepthOnlyNow: true }) && MeshRenderFeature.DepthOnlyFor( draw.Material, depthOnlyMaterial, depthOnlyTwoSided ) is { } depthOnly ) return depthOnly;

		stripPixelShader = depth && !draw.Translucent && !draw.AlphaTest;
		return draw.Material;
	}

	internal override void Dispose()
	{
		argsRing.Dispose();

		// The device keeps each resident group's fragment records and vertex bits until its owner is let go; a map's
		// systems go with it, and the next map's groups reuse them
		foreach ( var owner in meshOwners.Values ) ReleaseOwner( owner.Owner );
		meshOwners.Clear();
		if ( statsSlot != 0 ) RenderHost.Current?.Device?.ReleaseCullStats( statsSlot );
		statsSlot = 0;

		// The streaming device takes sizes from every system's for as long as it holds them, and its last answer names
		// materials of the world this one drew
		if ( sizes is not null ) RenderHost.Current?.Device?.RemoveTextureSizes( sizes );
		sizes = null;
	}
}
