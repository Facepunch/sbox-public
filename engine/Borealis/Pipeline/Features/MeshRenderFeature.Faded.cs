using System.Buffers;

namespace Borealis.Features;

/// <summary>
/// Fading objects' draws GPU-driven (<see cref="GpuDevice.DrawsFadedGpuDriven"/>): the faded list is dithered opaque under
/// <c>D_OPAQUE_FADE</c>, each instance's fade in its tint, so its order doesn't matter. It isn't in the depth prepass, so it
/// can't join the opaque list's call (whose forward pass draws what the prepass's ids kept): each list range a layer draws goes
/// to the device as one call of mesh-draw batches, culled on the CPU as before, with no ids or visibility lists, as the static
/// overlays' do. What the path can't take, or the device leaves, draws the classic way after it.
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>Whether this context draws the faded list GPU-driven: not while occlusion culls, which takes its runs' arguments.</summary>
	bool FadedGpuDriven( GpuContext context ) => context.GpuDriven && Occlusion is null && overlayTransforms is not null && RenderHost.Current?.Device is { DrawsFadedGpuDriven: true };

	/// <summary>The faded list's <paramref name="items"/>: a GPU-driven call of those it can take, then the rest the classic way.</summary>
	void DrawFadedGpuDriven( GpuContext context, ViewPass view, ReadOnlySpan<Run> runs, ReadOnlySpan<DrawItem> items, MeshLayer layer, ref RenderStats stats, MeshRuns list )
	{
		if ( items.IsEmpty ) return;

		var skinned = RenderHost.Current?.Device is { DrawsSkinnedGpuDriven: true };
		var depth = layer.Depth;
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		var batches = ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Rent( items.Length );
		var taken = ArrayPool<int>.Shared.Rent( items.Length );
		var drawn = ArrayPool<bool>.Shared.Rent( items.Length );
		var left = ArrayPool<DrawItem>.Shared.Rent( items.Length );
		try
		{
			var count = 0;
			for ( int k = 0; k < items.Length; k++ )
			{
				ref readonly var run = ref runs[items[k].Run];
				if ( !TakesGpuDriven( run, skinned ) || run.ReadsFrameBuffer ) continue;
				if ( BatchOf( run, items[k], depth, layer.ForceDepthFastPath ) is not { } batch ) continue;
				batches[count] = batch;
				taken[count++] = k;
			}

			var drawnSpan = drawn.AsSpan( 0, count );
			drawnSpan.Clear();
			if ( count > 0 ) context.DrawGpuDriven( layer.ShaderMode, batches.AsSpan( 0, count ), default, overlayTransforms, shadow ? view.DepthBias : 0, shadow ? view.SlopeScaledDepthBias : 0, drawnSpan );

			// What it didn't take from the start of the array, what the device left from its end: the census tells them apart
			var notTaken = 0;
			var deviceLeft = 0;
			var t = 0;
			for ( int k = 0; k < items.Length; k++ )
			{
				var isTaken = t < count && taken[t] == k;
				if ( isTaken ) t++;
				if ( isTaken && drawnSpan[t - 1] )
				{
					ref readonly var run = ref runs[items[k].Run];
					var triangles = (long)(run.Mesh.DrawsForLod( run.Lod )[items[k].Draw].IndexCount / 3) * run.Count;
					stats.DrawnTriangles += triangles;
					if ( shadow ) stats.ShadowDraws++;
					else if ( depth ) stats.DepthDraws++;
					else
					{
						stats.Draws++;
						stats.Instances += run.Count;
						stats.Triangles += triangles;
					}

					continue;
				}

				if ( isTaken ) left[items.Length - ++deviceLeft] = items[k];
				else left[notTaken++] = items[k];
			}

			if ( notTaken > 0 ) DrawItems( context, view, runs, left.AsSpan( 0, notTaken ), layer, ref stats, list );
			if ( deviceLeft > 0 ) DrawItems( context, view, runs, left.AsSpan( items.Length - deviceLeft, deviceLeft ), layer, ref stats, list, left: true );
		}
		finally
		{
			ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Return( batches, clearArray: true );
			ArrayPool<int>.Shared.Return( taken );
			ArrayPool<bool>.Shared.Return( drawn );
			ArrayPool<DrawItem>.Shared.Return( left );
		}
	}
}
