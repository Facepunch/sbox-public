using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Features;

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
	internal readonly record struct Batch( AggregateObject Aggregate, int FirstArg, int Count, int CullArg, long Triangles, ulong Key );

	internal sealed class Batches
	{
		public readonly List<Batch> List = new();
	}

	/// <summary>
	/// The args of every view's batches this frame, uploaded once in <see cref="Setup"/>.
	/// </summary>
	GpuBuffer.IndirectDrawIndexedArguments[] args = new GpuBuffer.IndirectDrawIndexedArguments[256];
	int argCount;
	int argGeneration = -1;
	readonly UploadRing<GpuBuffer.IndirectDrawIndexedArguments> argsRing = new( "SceneRenderer AggregateArgs", GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.IndirectDrawArguments );

	Material depthOnlyMaterial;

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
		var batches = pass.State<Batches>( this ).List;
		batches.Clear();

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
		var size = new SizeCull( pass.Root );
		var shadow = pass.IsShadow;
		var occlusion = !shadow && Occlusion is { CullsFragments: true } culler ? culler : null;

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
			for ( int i = 0; i < fragments.Length; i++ )
			{
				if ( shadow && (fragments[i].Flags & AggregateFragmentFlags.DoNotCastShadows) != 0 ) continue;
				if ( !frustum.Intersects( centers[i], extents[i] ) ) continue;
				if ( size.Culls( centers[i], extents[i] ) ) continue;

				args[argCount++] = fragments[i].Args;
				indices += fragments[i].Args.IndexCount;
			}

			var count = argCount - first;
			if ( count == 0 ) continue;

			WriteTransforms( aggregate, transforms );
			var cull = occlusion is not null && aggregate.OcclusionCulling ? occlusion.CommitFragments( args.AsSpan( first, count ), centers, extents ) : -1;
			batches.Add( new Batch( aggregate, first, count, cull, indices / 3, SortKey( aggregate ) ) );
		}

		// Similar materials one after another, so each bind reuses what the last set (CBaseSceneObjectDesc::BindMaterial)
		batches.Sort( ByKey );
	}

	/// <summary>
	/// Every fragment's transform, once a frame, with instance ids in fragment order: a draw's base instance is its fragment's
	/// index (<c>CAggregateSceneObjectDesc::FindOrAllocateFragmentTransforms</c>).
	/// </summary>
	static void WriteTransforms( AggregateObject aggregate, TransformBuffer transforms )
	{
		if ( aggregate.SlotGeneration == transforms.Generation ) return;
		aggregate.SlotGeneration = transforms.Generation;

		var tint = aggregate.Tint;
		var fragments = aggregate.Fragments;
		for ( int i = 0; i < fragments.Length; i++ )
		{
			var drawTint = fragments[i].DrawTint;
			var slot = transforms.Add( fragments[i].Transform, new Color( tint.r * drawTint.x, tint.g * drawTint.y, tint.b * drawTint.z, tint.a ) );
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
	internal override void Setup( RenderContext context, RenderWorld world, ViewPass view )
	{
		// On the main thread: draws may record on worker threads, where materials can't be made
		depthOnlyMaterial ??= Material.Load( MeshRenderFeature.DepthOnlyMaterial );

		var buffer = argsRing.Upload( context, args.AsSpan( 0, argCount ) );
		if ( argCount > 0 )
			context.BufferBarrier( buffer, RenderBarrierPipelineStageFlags_t.TransferBit, RenderBarrierAccessFlags_t.TransferWriteBit, RenderBarrierPipelineStageFlags_t.DrawIndirectBit, RenderBarrierAccessFlags_t.IndirectCommandReadBit );
	}

	/// <summary>
	/// A batch for each aggregate, in every layer that draws the opaque runs.
	/// </summary>
	internal override int RunCount( ViewPass view, MeshLayer layer ) => (layer.Runs & MeshRuns.Opaque) != 0 ? view.State<Batches>( this ).List.Count : 0;

	/// <summary>
	/// Draw each batch as one multi-draw: its own args, or the occlusion cull's for the layer's phase.
	/// </summary>
	internal override void Draw( RenderContext context, ViewPass view, MeshLayer layer, int first, int count, ref RenderStats stats )
	{
		var batches = CollectionsMarshal.AsSpan( view.State<Batches>( this ).List ).Slice( first, count );

		var depth = layer.Depth;
		var normals = depth && layer.WritesNormals && view.View.DepthNormals && !view.IsShadow;
		var mode = layer.ShaderMode;
		var shadow = layer.LayerType == SceneLayerType.Shadow;
		var depthBias = shadow ? view.DepthBias : 0;
		var slopeBias = shadow ? view.SlopeScaledDepthBias : 0;

		// As MeshRenderFeature: the early prepass culls only with last frame's chain, the late one draws only culled batches
		var phase = context.Occlusion is { } occlusion && (layer.OcclusionPhase != OcclusionPhase.Early || occlusion.CullsEarly) ? layer.OcclusionPhase : OcclusionPhase.None;
		var culledOnly = layer.OcclusionPhase == OcclusionPhase.Late;

		foreach ( ref readonly var batch in batches )
		{
			if ( culledOnly && batch.CullArg < 0 ) continue;

			var aggregate = batch.Aggregate;
			var mesh = aggregate.Mesh;
			var draws = mesh.DrawsForLod( 0 );
			if ( draws.Length == 0 ) continue;

			// Every fragment shares the first draw's buffers and material
			ref readonly var draw = ref draws[0];
			var material = MaterialFor( draw, depth && !normals, layer.ForceDepthFastPath, out var stripPixelShader );
			if ( material is null ) continue;

			// Lightmaps light every draw or none of them (CAggregateSceneObjectDesc::SetBakedLightingAttributes)
			context.SetObjectAttributes( null, !depth && aggregate.Lightmapped ? aggregate.LightmapAttributes : null );

			var drawn = batch.CullArg >= 0 && phase != OcclusionPhase.None
				? context.DrawModelFragmentsCulled( material, mode, mesh, draw, phase, aggregate.FirstInstance, batch.CullArg, batch.Count, depthBias, slopeBias, stripPixelShader )
				: context.DrawModelFragments( material, mode, mesh, draw, aggregate.FirstInstance, argsRing.Current, batch.FirstArg, batch.Count, depthBias, slopeBias, stripPixelShader );
			if ( !drawn ) continue;

			stats.DrawnTriangles += batch.Triangles;
			if ( shadow )
			{
				stats.ShadowDraws++;
				continue;
			}

			if ( depth )
			{
				stats.DepthDraws++;
				continue;
			}

			stats.Draws++;
			stats.Instances += batch.Count;
			stats.Triangles += batch.Triangles;
		}

		context.SetObjectAttributes( null );
	}

	/// <summary>
	/// The depth-only material on the fast path, else the draw's own, with the pixel shader stripped for an opaque depth pass
	/// (<c>CSceneSystem::ShouldOverrideDepthMaterial</c>, <c>CAggregateSceneObjectDesc::DrawArrayExt</c>).
	/// </summary>
	Material MaterialFor( in RenderMesh.Draw draw, bool depth, bool forceDepthOnly, out bool stripPixelShader )
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
