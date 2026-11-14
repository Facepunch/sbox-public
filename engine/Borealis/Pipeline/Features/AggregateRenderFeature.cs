using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// Draws <see cref="AggregateObject"/>s as native's <c>CAggregateSceneObjectDesc</c> does. Each view culls an aggregate's
/// fragments on the CPU, then its visible fragments are one multi-draw, occlusion culled fragment by fragment in the main
/// view. Aggregates are opaque, so they draw wherever the opaque runs do.
/// </summary>
internal sealed class AggregateRenderFeature : RenderFeature
{
	/// <summary>
	/// An aggregate's fragments visible in a view: their args in the frame's args buffer, and their occlusion cull run's, or -1.
	/// </summary>
	internal readonly record struct Batch( AggregateObject Aggregate, int FirstArg, int Count, int CullArg, long Triangles, ulong Key, int TextureSize );

	internal sealed class Batches
	{
		public readonly List<Batch> List = new();

		/// <summary>Whether the device draws them GPU-driven: then a layer's batches record as one run, so one multi-draw per bucket.</summary>
		public bool OneRun;
	}

	/// <summary>
	/// The args of every view's batches this frame, uploaded once in <see cref="Setup"/>.
	/// </summary>
	GpuBuffer.IndirectDrawIndexedArguments[] args = new GpuBuffer.IndirectDrawIndexedArguments[256];
	int argCount;
	int argGeneration = -1;
	readonly UploadRing<GpuBuffer.IndirectDrawIndexedArguments> argsRing = new( "Borealis AggregateArgs", GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments );

	GpuMaterial depthOnlyMaterial;
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
		// The frame's transforms, whose instance ids a GPU-driven draw reads on the CPU (the same buffer for every view)
		this.transforms = transforms;

		var state = pass.State<Batches>( this );
		var batches = state.List;
		batches.Clear();
		state.OneRun = Occlusion is null && RenderHost.Current?.Device is { DrawsAggregatesGpuDriven: true };

		// The frame's views all write into one args buffer
		if ( argGeneration != transforms.Generation )
		{
			argGeneration = transforms.Generation;
			argCount = 0;
		}

		var visible = pass.Visible( this );
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

		// The GPU culls each fragment of what it draws GPU-driven, and this device reads no texture sizes: every fragment's
		// args, for a batch it leaves to the classic path, without testing or sizing them one by one
		var gpuCulls = state.OneRun && RenderHost.Current.Device.CullsAggregatesGpuDriven;

		foreach ( var index in CollectionsMarshal.AsSpan( visible ) )
		{
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
				batches.Add( new Batch( aggregate, first, argCount - first, -1, indices / 3, SortKey( aggregate ), TextureSizes.All ) );
				continue;
			}

			// One texture size for all of it, the largest any fragment left needs
			var objectFallback = textureSizes.ForObject( objectCenters[index], objectExtents[index] ).Fallback;
			var textureSize = 0.0f;
			for ( int i = 0; i < fragments.Length; i++ )
			{
				if ( shadow && (fragments[i].Flags & FragmentFlags.DoNotCastShadows) != 0 ) continue;
				if ( !frustum.Intersects( centers[i], extents[i] ) ) continue;
				if ( size.Culls( centers[i], extents[i] ) ) continue;

				args[argCount++] = fragments[i].Args;
				indices += fragments[i].Args.IndexCount;
				textureSize = MathF.Max( textureSize, textureSizes.ForFragment( fragments[i].UvDensity, centers[i], extents[i], viewCamera, objectFallback ) );
			}

			var count = argCount - first;
			if ( count == 0 ) continue;

			WriteTransforms( aggregate, index, versions[index], transforms );
			var cull = occlusion is not null && aggregate.OcclusionCulling ? occlusion.CommitFragments( args.AsSpan( first, count ), centers, extents ) : -1;
			batches.Add( new Batch( aggregate, first, count, cull, indices / 3, SortKey( aggregate ), TextureSizes.Clamp( textureSize ) ) );
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
	static void WriteTransforms( AggregateObject aggregate, int index, int version, TransformBuffer transforms )
	{
		if ( aggregate.TransformsGeneration == transforms.Generation ) return;
		aggregate.TransformsGeneration = transforms.Generation;

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

			var instance = transforms.AddInstance( slot );
			if ( i == 0 ) aggregate.FirstInstance = instance;
		}
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
	/// Upload the frame's args, after every view has prepared.
	/// </summary>
	internal override void Setup( GpuContext context, RenderWorld world, ViewPass view )
	{
		// On the main thread: draws may record on worker threads, where materials can't be made
		depthOnlyMaterial ??= RenderHost.Current.Device.LoadMaterial( MeshRenderFeature.DepthOnlyMaterial );

		var buffer = argsRing.Upload( context, args.AsSpan( 0, argCount ) );
		if ( argCount > 0 )
			context.BufferBarrier( buffer, GpuStage.Transfer, GpuAccess.TransferWrite, GpuStage.DrawIndirect, GpuAccess.IndirectCommandRead );
	}

	/// <summary>
	/// A batch for each aggregate, in every layer that draws the opaque runs.
	/// </summary>
	internal override int RunCount( ViewPass view, MeshLayer layer )
	{
		if ( (layer.Runs & MeshRuns.Opaque) == 0 ) return 0;
		var state = view.State<Batches>( this );
		return state.OneRun ? Chunks( state.List.Count + MeshDraws( view ).Count ) : state.List.Count;
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
	}

	/// <summary>
	/// The mesh feature, whose static opaque draws a view hands this one when the device draws GPU-driven
	/// (<see cref="MeshRenderFeature.GpuDrivenItems"/>), so they go in the same buckets as the aggregates.
	/// </summary>
	internal MeshRenderFeature Meshes { get; set; }

	static readonly List<MeshRenderFeature.DrawItem> NoDraws = new();

	List<MeshRenderFeature.DrawItem> MeshDraws( ViewPass view ) => Meshes?.GpuDrivenItems( view ) ?? NoDraws;

	/// <summary>
	/// Draw each batch as one multi-draw: its own args, or the occlusion cull's for the layer's phase.
	/// </summary>
	internal override void Draw( GpuContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
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
		var gpuDriven = context.GpuDriven && phase == OcclusionPhase.None && !culledOnly && transforms is not null ? System.Buffers.ArrayPool<bool>.Shared.Rent( batches.Length + meshDraws.Length ) : null;
		if ( gpuDriven is not null )
		{
			var drawn = gpuDriven.AsSpan( 0, batches.Length + meshDraws.Length );
			drawn.Clear();
			// The frame copy frame-buffer readers among the mesh draws read, taken as the mesh feature takes it before its first
			// reader (where the layer copies at all)
			if ( !depth && !shadow && ReadsFrameBuffer( view, meshDraws ) ) context.CopyFrameBuffer();
			DrawGpuDriven( context, view, batches, meshDraws, mode, depth, layer.ForceDepthFastPath, depthBias, slopeBias, drawn );

			for ( int i = 0; i < batches.Length; i++ )
			{
				if ( !drawn[i] ) continue;
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
				if ( gpuDriven is not null && gpuDriven[batches.Length + i] ) CountMeshDrawn( view, meshDraws[i], shadow, depth, layer.LayerType == MeshLayerType.Translucent, ref stats );
				else left[leftCount++] = meshDraws[i];
			}

			Meshes.DrawGpuDrivenLeft( context, view, left.AsSpan( 0, leftCount ), layer, ref stats );
			System.Buffers.ArrayPool<MeshRenderFeature.DrawItem>.Shared.Return( left );
		}

		for ( int b = 0; b < batches.Length; b++ )
		{
			ref readonly var batch = ref batches[b];
			if ( gpuDriven is not null && gpuDriven[b] ) continue;
			if ( culledOnly && batch.CullArg < 0 ) continue;

			var aggregate = batch.Aggregate;
			var mesh = aggregate.Mesh;
			var draws = mesh.DrawsForLod( 0 );
			if ( draws.Length == 0 ) continue;

			// Every fragment shares the first draw's buffers and material
			ref readonly var draw = ref draws[0];
			var material = MaterialFor( draw, depth, layer.ForceDepthFastPath, out var stripPixelShader );
			if ( material is null ) continue;
			if ( context.Census ) context.CensusNote = gpuDriven is not null ? "aggregate: the device's GPU-driven path left it" : !context.GpuDriven ? "aggregate: GPU-driven off" : phase != OcclusionPhase.None || culledOnly ? "aggregate: occlusion culled" : "aggregate: no transforms";

			// Lightmaps light every draw or none of them (CAggregateSceneObjectDesc::SetBakedLightingAttributes)
			context.SetObjectAttributes( null, !depth && aggregate.Lightmapped ? aggregate.LightmapAttributes : null );

			var drawn = batch.CullArg >= 0 && phase != OcclusionPhase.None
				? context.DrawModelFragmentsCulled( material, mode, mesh, draw, phase, aggregate.FirstInstance, batch.CullArg, batch.Count, depthBias, slopeBias, stripPixelShader, batch.TextureSize )
				: context.DrawModelFragments( material, mode, mesh, draw, aggregate.FirstInstance, argsRing.Current, batch.FirstArg, batch.Count, depthBias, slopeBias, stripPixelShader, batch.TextureSize );
			if ( !drawn ) continue;

			CountDrawn( batch, shadow, depth, ref stats );
		}

		if ( gpuDriven is not null ) System.Buffers.ArrayPool<bool>.Shared.Return( gpuDriven );
		context.SetObjectAttributes( null );
	}

	/// <summary>Whether any of the mesh draws reads the frame buffer copy (<see cref="MeshRenderFeature.ReadersTaken"/>).</summary>
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
	void DrawGpuDriven( GpuContext context, ViewPass view, ReadOnlySpan<Batch> batches, ReadOnlySpan<MeshRenderFeature.DrawItem> meshDraws, StringToken mode, bool depth, bool forceDepthOnly, int depthBias, float slopeBias, Span<bool> drawn )
	{
		var list = System.Buffers.ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Rent( batches.Length + meshDraws.Length );
		var count = 0;
		for ( int i = 0; i < batches.Length; i++ )
		{
			ref readonly var batch = ref batches[i];
			var draws = batch.Aggregate.Mesh.DrawsForLod( 0 );
			var strip = false;
			var material = draws.Length > 0 ? MaterialFor( draws[0], depth, forceDepthOnly, out strip ) : null;
			if ( material is null )
			{
				list[count++] = default;
				continue;
			}

			var attributes = !depth && batch.Aggregate.Lightmapped ? batch.Aggregate.LightmapAttributes : null;
			list[count++] = new GpuContext.GpuDrivenBatch( material, batch.Aggregate.Mesh, draws[0], attributes, strip, batch.FirstArg, batch.Count, batch.Aggregate.FirstInstance, batch.Triangles, batch.Aggregate );
		}

		// Then the mesh draws, each its instances
		foreach ( var item in meshDraws ) list[count++] = Meshes.GpuDrivenBatch( view, item, depth, forceDepthOnly ) ?? default;

		// The tests Prepare made per fragment, for the device to make on the GPU over every fragment instead
		var frustum = view.View.Frustum;
		var cull = new GpuContext.GpuDrivenCull
		{
			Left = frustum.Plane( 0 ),
			Right = frustum.Plane( 1 ),
			Bottom = frustum.Plane( 2 ),
			Top = frustum.Plane( 3 ),
			Near = frustum.Plane( 4 ),
			Far = frustum.Plane( 5 ),
			CameraAndThreshold = SizeCull.ForPass( view ).CameraAndThreshold,
			Shadow = view.IsShadow,
			View = view,
			Ids = depth && !view.IsShadow,
			Visibility = !depth && !view.IsShadow,
		};

		context.DrawGpuDriven( mode, list.AsSpan( 0, count ), args.AsSpan( 0, argCount ), transforms, depthBias, slopeBias, drawn, cull );
		System.Buffers.ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Return( list, clearArray: true );
	}

	/// <summary>
	/// The depth-only material on the fast path, else the draw's own, with the pixel shader stripped for an opaque depth pass
	/// (<c>CSceneSystem::ShouldOverrideDepthMaterial</c>, <c>CAggregateSceneObjectDesc::DrawArrayExt</c>).
	/// </summary>
	GpuMaterial MaterialFor( in RenderMesh.Draw draw, bool depth, bool forceDepthOnly, out bool stripPixelShader )
	{
		stripPixelShader = false;
		if ( depth && (forceDepthOnly || draw.ShadowFastPath) && depthOnlyMaterial is not null ) return depthOnlyMaterial;

		stripPixelShader = depth && !draw.Translucent && !draw.AlphaTest;
		return draw.Material;
	}

	internal override void Dispose()
	{
		argsRing.Dispose();
	}
}
