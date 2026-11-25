using System.Buffers;
using System.Runtime.InteropServices;

namespace Borealis.Features;

/// <summary>
/// A map's static overlays drawn GPU-driven (<see cref="GpuDevice.DrawsOverlaysGpuDriven"/>): the overlays of one render
/// order go to the device in one call, which draws each pipeline's as one multi-draw whatever their material. Render orders
/// stay in order; within one, overlays of different pipelines no longer interleave, so where they overlap the later one
/// may no longer be on top. Runs the GPU-driven path can't take (object attributes, callbacks, skinning, frame copies)
/// draw the classic way after their render order's call. Render orders the view's resident list holds
/// (<see cref="AggregateRenderFeature.ResidentOverlays"/>) draw from it instead, in their place among the view's own.
/// </summary>
internal sealed partial class MeshRenderFeature
{
	/// <summary>The frame's transforms, from the last <see cref="Prepare"/>, which the overlays' GPU-driven call reads instances from.</summary>
	TransformBuffer overlayTransforms;

	/// <summary>
	/// Whether a layer is the static overlays' and the device draws them GPU-driven: then the layer is one run (one segment),
	/// so render order 0's overlays go in one call whatever the number of workers. Not with custom objects or callbacks
	/// among them, which record on the main thread.
	/// </summary>
	bool OverlaysOneRun( ViewPass view, MeshLayer layer, RunLists lists ) => layer.Runs == MeshRuns.StaticOverlay && Occlusion is null && overlayTransforms is not null
		&& RenderHost.Current?.Device is { DrawsOverlaysGpuDriven: true } && (lists.Custom & MeshRuns.StaticOverlay) == 0 && (lists.Callbacks & MeshRuns.StaticOverlay) == 0
		&& (lists.StaticOverlay.Count > 0 || Aggregates?.ResidentOverlays( view ) is { Count: > 0 }) && lists.OverlayOrders.Count == lists.StaticOverlay.Count;

	/// <summary>The overlays' one run weighs as many draws as it holds, so the frame's segments and its parallel threshold see its work.</summary>
	internal override void RunWeights( ViewPass view, MeshLayer layer, Span<int> weights )
	{
		var lists = view.State<RunLists>( this );
		if ( weights.Length == 1 && OverlaysOneRun( view, layer, lists ) )
		{
			// A resident list's batch costs the run less than one of the view's own, as the aggregates weigh theirs
			var listed = 0;
			if ( Aggregates?.ResidentOverlays( view ) is { } orders )
			{
				foreach ( var order in orders ) listed += order.Device.Length;
			}

			weights[0] = Math.Max( lists.ItemsByBit[System.Numerics.BitOperations.TrailingZeroCount( (uint)MeshRuns.StaticOverlay )].Count + listed / AggregateRenderFeature.ResidentWeightDivisor, 1 );
			return;
		}

		weights.Fill( 1 );
	}

	/// <summary>
	/// Each run's render order, from the sorted bucket it was emitted from: <see cref="Emit"/> makes runs of consecutive
	/// entries, so a run's first entry's key holds its order (<see cref="AddToLists"/>).
	/// </summary>
	static void RenderOrders( Bucket bucket, List<Run> runs, List<int> orders )
	{
		orders.Clear();
		var entry = 0;
		foreach ( var run in CollectionsMarshal.AsSpan( runs ) )
		{
			if ( entry >= bucket.Count )
			{
				orders.Clear();
				return;
			}

			orders.Add( (int)(uint)(bucket.Keys[entry] >> 32) ^ int.MinValue );
			entry += Math.Max( run.Count, 1 );
		}
	}

	/// <summary>
	/// Every static overlay of the view, render order by render order: one its resident list holds from the list, else a
	/// GPU-driven call of the view's own, then what that didn't take the classic way.
	/// </summary>
	void DrawOverlays( GpuContext context, ViewPass view, RunLists lists, MeshLayer layer, ref RenderStats stats )
	{
		using var zone = Zones.OverlayDraw.Start();
		var bit = System.Numerics.BitOperations.TrailingZeroCount( (uint)MeshRuns.StaticOverlay );
		var runs = CollectionsMarshal.AsSpan( lists.ByBit[bit] );
		ReadOnlySpan<DrawItem> items = CollectionsMarshal.AsSpan( lists.ItemsByBit[bit] );
		var orders = lists.OverlayOrders;
		if ( !context.GpuDriven )
		{
			DrawItems( context, view, runs, items, layer, ref stats, MeshRuns.StaticOverlay );
			return;
		}

		var listed = Aggregates?.ResidentOverlays( view );
		var nextListed = 0;
		var listedCount = listed?.Count ?? 0;
		if ( items.IsEmpty )
		{
			while ( nextListed < listedCount ) Aggregates.DrawOverlayOrder( context, view, listed[nextListed++], layer, ref stats );
			return;
		}

		var batches = ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Rent( items.Length );
		var taken = ArrayPool<int>.Shared.Rent( items.Length );
		var drawn = ArrayPool<bool>.Shared.Rent( items.Length );
		var left = ArrayPool<DrawItem>.Shared.Rent( items.Length );
		try
		{
			for ( int i = 0; i < items.Length; )
			{
				// One render order's overlays
				var order = orders[items[i].Run];
				var j = i + 1;
				while ( j < items.Length && orders[items[j].Run] == order ) j++;

				// The listed render orders up to it. The view's own overlays of a listed one draw after the list, as they would
				// after the order's call
				var alsoListed = false;
				while ( nextListed < listedCount && listed[nextListed].RenderOrder <= order )
				{
					alsoListed |= listed[nextListed].RenderOrder == order;
					Aggregates.DrawOverlayOrder( context, view, listed[nextListed++], layer, ref stats );
				}

				var count = 0;
				for ( int k = i; k < j; k++ )
				{
					if ( OverlayBatch( runs, items[k], layer ) is not { } batch ) continue;
					batches[count] = batch;
					taken[count++] = k;
				}

				var drawnSpan = drawn.AsSpan( 0, count );
				drawnSpan.Clear();
				if ( count > 0 ) context.DrawGpuDriven( layer.ShaderMode, batches.AsSpan( 0, count ), default, overlayTransforms, 0, 0, drawnSpan );

				// Ones the device took in a listed order should have been in the list: a change it hasn't seen, so the next
				// frame's lists are made again
				if ( alsoListed && drawnSpan.IndexOf( true ) >= 0 ) Aggregates.OverlaysStale();

				// Then what it didn't take, the classic way, in their order
				var leftCount = 0;
				var t = 0;
				for ( int k = i; k < j; k++ )
				{
					var isTaken = t < count && taken[t] == k;
					if ( isTaken && drawnSpan[t++] )
					{
						ref readonly var run = ref runs[items[k].Run];
						var triangles = (long)(run.Mesh.DrawsForLod( run.Lod )[items[k].Draw].IndexCount / 3) * run.Count;
						stats.DrawnTriangles += triangles;
						stats.Draws++;
						stats.Instances += run.Count;
						stats.Triangles += triangles;
						continue;
					}

					left[leftCount++] = items[k];
				}

				if ( leftCount > 0 ) DrawItems( context, view, runs, left.AsSpan( 0, leftCount ), layer, ref stats, MeshRuns.StaticOverlay );
				i = j;
			}

			// The listed render orders after the view's own
			while ( nextListed < listedCount ) Aggregates.DrawOverlayOrder( context, view, listed[nextListed++], layer, ref stats );
		}
		finally
		{
			ArrayPool<GpuContext.GpuDrivenBatch>.Shared.Return( batches, clearArray: true );
			ArrayPool<int>.Shared.Return( taken );
			ArrayPool<bool>.Shared.Return( drawn );
			ArrayPool<DrawItem>.Shared.Return( left );
		}
	}

	/// <summary>An overlay draw as a GPU-driven batch of its run's instances, with the material and lighting <see cref="DrawItems"/> would use; null when it needs the classic path.</summary>
	GpuContext.GpuDrivenBatch? OverlayBatch( ReadOnlySpan<Run> runs, DrawItem item, MeshLayer layer )
	{
		ref readonly var run = ref runs[item.Run];
		if ( run.Custom is not null || run.Callbacks is not null || run.Skinned || run.Deformed || run.Morphable || run.ReadsFrameBuffer || run.CullDraw >= 0 || run.Attributes is not null ) return null;

		ref readonly var draw = ref run.Mesh.DrawsForLod( run.Lod )[item.Draw];
		var material = MaterialFor( run, draw, false, layer.ForceDepthFastPath, out var strip );
		if ( material is null ) return null;

		var probe = run.Probe is null || !RenderMesh.NeedsLightProbe( material ) ? null : run.Probe;
		var lighting = draw.Lightmapped ? run.Lightmap : probe;
		var first = run.FirstInstance + (run.Tinted ? item.Draw * run.Count : 0);
		return new GpuContext.GpuDrivenBatch( material, run.Mesh, draw, lighting, strip, -1, run.Count, first, (long)(draw.IndexCount / 3) * run.Count, null, Instances: true );
	}
}
