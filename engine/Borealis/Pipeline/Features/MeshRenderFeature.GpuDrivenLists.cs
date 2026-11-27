using System.Buffers;

namespace Borealis.Features;

/// <summary>
/// The faded, translucent and refraction lists' draws GPU-driven (<see cref="GpuDevice.DrawsFadedGpuDriven"/>,
/// <see cref="GpuDevice.DrawsTranslucentGpuDriven"/>, which the refraction list follows). None is in the depth prepass, so none
/// can join the opaque list's call (whose forward pass draws what the prepass's ids kept): each list range a layer draws goes to the device as calls of
/// mesh-draw batches, culled on the CPU as before, with no ids or visibility lists, as the static overlays' do. The runs taken
/// are the opaque list's (skinned ones with <see cref="GpuDevice.DrawsSkinnedGpuDriven"/>); frame-buffer readers only in the
/// translucent list's colour layers, which copy the frame for them, and in the refraction list, which is all readers.
/// <list type="bullet">
/// <item>The faded list is dithered opaque under <c>D_OPAQUE_FADE</c>, each instance's fade in its tint, so its order doesn't
/// matter: one call, then what it didn't take or the device left, the classic way.</item>
/// <item>The translucent list blends in its sorted order in colour layers: each stretch of runs the path takes is one call in
/// order (<see cref="GpuContext.GpuDrivenInOrder"/>), so neighbours in the sort of one pipeline share a multi-draw, and what
/// the device leaves of it, with the runs between stretches, draws the classic way in its place. Refractive glass and other
/// frame-buffer readers are in the stretches: the frame is copied once, at the first reader (<see cref="GpuContext.CopyFrameBuffer"/>),
/// so a stretch ends before it and what's drawn before it is in the copy, and every reader after it reads that one copy, as
/// the classic way draws them. In shadows (depth only) as the faded list.</item>
/// <item>The refraction list (<see cref="RefractionStencilLayer"/>: the readers' depth, all with depth_only) as the faded
/// list: one call, depth only, so its order doesn't matter.</item>
/// </list>
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>Whether this context draws <paramref name="list"/> GPU-driven: not while occlusion culls, which takes its runs' arguments.</summary>
	bool ListGpuDriven( GpuContext context, MeshRuns list )
	{
		if ( list is not (MeshRuns.Faded or MeshRuns.Translucent or MeshRuns.Refraction) || !context.GpuDriven || Occlusion is not null || overlayTransforms is null ) return false;
		var device = RenderHost.Current?.Device;
		return list == MeshRuns.Faded ? device is { DrawsFadedGpuDriven: true } : device is { DrawsTranslucentGpuDriven: true };
	}

	/// <summary>
	/// Whether a run of the faded or translucent list can be drawn GPU-driven: as the opaque list's are taken, frame-buffer
	/// readers only with <paramref name="readers"/> (the translucent list's colour layers, in order, and the refraction list).
	/// </summary>
	static bool ListTakes( in Run run, bool skinned, bool readers, bool shadow ) => TakesGpuDriven( run, skinned, shadow ) && (readers || !run.ReadsFrameBuffer);

	/// <summary>A faded or translucent list's <paramref name="items"/>: GPU-driven calls of those the path takes, the rest the classic way.</summary>
	void DrawListGpuDriven( GpuContext context, ViewPass view, ReadOnlySpan<Run> runs, ReadOnlySpan<DrawItem> items, MeshLayer layer, ref RenderStats stats, MeshRuns list )
	{
		if ( items.IsEmpty ) return;

		var skinned = RenderHost.Current?.Device is { DrawsSkinnedGpuDriven: true };
		var inOrder = list == MeshRuns.Translucent && !layer.Depth;
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		var batches = ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Rent( items.Length );
		var taken = ArrayPool<int>.Shared.Rent( items.Length );
		var drawn = ArrayPool<bool>.Shared.Rent( items.Length );
		var left = ArrayPool<DrawItem>.Shared.Rent( items.Length );
		try
		{
			if ( !inOrder )
			{
				DrawCall( context, view, runs, items, layer, ref stats, list, skinned, false, list == MeshRuns.Refraction && !NoGpuDrivenReaders, batches, taken, drawn, left );
				return;
			}

			// Stretches the path takes, one call each, and the runs between them the classic way, in the list's order. The first
			// reader a stretch meets starts a stretch of its own, the frame copied before it (once a layer: a classic reader
			// before it may have made it already)
			var readers = !NoGpuDrivenReaders;
			var copied = false;
			for ( int i = 0; i < items.Length; )
			{
				var j = i;
				while ( j < items.Length && !ListTakes( runs[items[j].Run], skinned, readers, shadow ) ) j++;
				if ( j > i ) DrawItems( context, view, runs, items[i..j], layer, ref stats, list );

				i = j;
				while ( j < items.Length && ListTakes( runs[items[j].Run], skinned, readers, shadow ) )
				{
					if ( !copied && runs[items[j].Run].ReadsFrameBuffer )
					{
						if ( j > i ) break;
						context.CopyFrameBuffer();
						copied = true;
					}

					j++;
				}

				if ( j > i ) DrawCall( context, view, runs, items[i..j], layer, ref stats, list, skinned, true, readers, batches, taken, drawn, left );
				i = j;
			}
		}
		finally
		{
			ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Return( batches, clearArray: true );
			ArrayPool<int>.Shared.Return( taken );
			ArrayPool<bool>.Shared.Return( drawn );
			ArrayPool<DrawItem>.Shared.Return( left );
		}
	}

	/// <summary>
	/// One GPU-driven call of <paramref name="items"/>, then what it didn't draw the classic way. In order, the call takes
	/// the items up to the first without a batch and the device draws a prefix of them, so what's left is the items from the
	/// first it didn't draw, drawn after it in their order. With <paramref name="readers"/>, frame-buffer readers are taken,
	/// the copy made before the call.
	/// </summary>
	void DrawCall( GpuContext context, ViewPass view, ReadOnlySpan<Run> runs, ReadOnlySpan<DrawItem> items, MeshLayer layer, ref RenderStats stats, MeshRuns list, bool skinned, bool inOrder, bool readers,
		GpuContext.GpuDrivenBatch[] batches, int[] taken, bool[] drawn, DrawItem[] left )
	{
		var depth = layer.Depth;
		var forceDepthOnly = layer.ForceDepthFastPath;
		var shadow = layer.LayerType == MeshLayerType.Shadow;
		var count = 0;
		for ( int k = 0; k < items.Length; k++ )
		{
			ref readonly var run = ref runs[items[k].Run];
			if ( !ListTakes( run, skinned, readers, shadow ) || !TryBatchOf( run, items[k], depth, forceDepthOnly, out batches[count] ) )
			{
				if ( inOrder ) break;
				continue;
			}

			taken[count++] = k;
		}

		var drawnSpan = drawn.AsSpan( 0, count );
		drawnSpan.Clear();
		if ( count > 0 )
		{
			context.GpuDrivenInOrder = inOrder;
			try
			{
				context.DrawGpuDriven( layer.ShaderMode, batches.AsSpan( 0, count ), default, overlayTransforms, shadow ? view.DepthBias : 0, shadow ? view.SlopeScaledDepthBias : 0, drawnSpan );
			}
			finally
			{
				context.GpuDrivenInOrder = false;
			}
		}

		// What the call didn't take from the start of the array, what the device left from its end: the census tells them
		// apart. In order, everything from the first item not drawn, in order
		var notTaken = 0;
		var deviceLeft = 0;
		var t = 0;
		var stoppedAt = -1;
		for ( int k = 0; k < items.Length; k++ )
		{
			var isTaken = t < count && taken[t] == k;
			if ( isTaken ) t++;
			if ( isTaken && drawnSpan[t - 1] && stoppedAt < 0 )
			{
				ref readonly var run = ref runs[items[k].Run];
				var triangles = (long)(run.Mesh.DrawsForLod( run.Lod )[items[k].Draw].IndexCount / 3) * run.Count;
				stats.DrawnTriangles += triangles;
				if ( shadow ) stats.ShadowDraws++;
				else if ( depth ) stats.DepthDraws++;
				else
				{
					if ( layer.LayerType == MeshLayerType.Translucent ) stats.TranslucentDraws++;
					stats.Draws++;
					stats.Instances += run.Count;
					stats.Triangles += triangles;
				}

				continue;
			}

			if ( inOrder )
			{
				stoppedAt = stoppedAt < 0 ? k : stoppedAt;
				continue;
			}

			if ( isTaken ) left[items.Length - ++deviceLeft] = items[k];
			else left[notTaken++] = items[k];
		}

		if ( stoppedAt >= 0 ) DrawItems( context, view, runs, items[stoppedAt..], layer, ref stats, list, left: true );
		if ( notTaken > 0 ) DrawItems( context, view, runs, left.AsSpan( 0, notTaken ), layer, ref stats, list );
		if ( deviceLeft > 0 ) DrawItems( context, view, runs, left.AsSpan( items.Length - deviceLeft, deviceLeft ), layer, ref stats, list, left: true );
	}
}
