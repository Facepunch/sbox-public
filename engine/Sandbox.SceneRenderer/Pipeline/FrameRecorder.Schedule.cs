namespace Sandbox.SceneRenderer;

internal sealed partial class FrameRecorder
{
	sealed class ComputeBatch( RenderContext frame ) : IDisposable
	{
		public readonly RenderContext Context = new( frame );
		public int From, Count;

		public void Dispose() => Context.Dispose();
	}

	readonly List<ComputeBatch> _computeBatches = [];
	int _computeBatchCount;

	void PlanSegments( bool parallel, bool asyncCompute )
	{
		var runs = 0;
		for ( int w = 0; w < workCount; w++ )
			runs += work[w].Runs;

		Parallel = parallel && runs >= ParallelRunThreshold;
		var maxSegments = Parallel ? MaxRecordThreads : 1;
		_computeBatchCount = 0;
		for ( int l = 0; l < layerCount; )
		{
			if ( !CanUseCompute( l ) || !parallel || !asyncCompute || !RenderContext.SupportsAsyncCompute )
			{
				AddGraphicsLayer( l++, maxSegments );
				continue;
			}

			var frame = layers[l].Frame;
			var computeEnd = l + 1;
			var accessed = layers[l].Reads | layers[l].Writes;
			while ( computeEnd < layerCount && layers[computeEnd].Frame == frame && CanUseCompute( computeEnd ) )
			{
				// Handoffs can clear outputs before dispatch. Do not clear a resource an earlier compute
				// layer still uses; start another batch after that work has finished instead.
				if ( (layers[computeEnd].Writes & accessed) != 0 ) break;
				accessed |= layers[computeEnd].Reads | layers[computeEnd].Writes;
				computeEnd++;
			}

			// Keep the serial list as the dependency order. A graphics producer before the next compute batch
			// is submitted before its handoff; unknown access and child frames also end this overlap window.
			var end = computeEnd;
			while ( end < layerCount && layers[end].Frame == frame && !CanUseCompute( end ) && !HasUntrackedAccess( end ) )
				end++;

			PlanComputeBatch( l, computeEnd, end, maxSegments );
			l = end;
		}
	}

	bool HasUntrackedAccess( int index ) => ((layers[index].Reads | layers[index].Writes) & FrameResources.Untracked) != 0;

	bool CanUseCompute( int index ) => layers[index].Layer.SupportsAsyncCompute && !layers[index].Layer.IsShared && !HasUntrackedAccess( index );

	void PlanComputeBatch( int from, int computeEnd, int end, int maxSegments )
	{
		var frame = layers[from].Frame;

		// Queue order handles accesses within a branch. Graphics only waits for the last conflicting compute pass.
		// A reader of an intermediate result splits that producer from later independent compute work.
		var canOverlap = false;
		var acquiredLayer = -1;
		for ( int l = computeEnd; l < end; l++ )
		{
			ref var graphics = ref layers[l];
			graphics.WaitForLayer = -1;
			for ( int c = from; c < computeEnd; c++ )
			{
				ref var compute = ref layers[c];
				if ( (compute.Writes & (graphics.Reads | graphics.Writes)) != 0 || (compute.Reads & graphics.Writes) != 0 )
					graphics.WaitForLayer = c;
			}

			if ( graphics.Layer.IsShared && graphics.WorkFrom == graphics.WorkTo ) continue;

			if ( graphics.WaitForLayer > acquiredLayer )
			{
				acquiredLayer = graphics.WaitForLayer;
				layers[acquiredLayer].SplitAfter = true;
			}

			canOverlap |= acquiredLayer < computeEnd - 1;
		}

		if ( !canOverlap )
		{
			for ( int l = from; l < end; l++ )
				AddGraphicsLayer( l, maxSegments );

			return;
		}

		frame.AsyncCompute = true;

		var firstCompute = segmentCount;
		for ( int l = from; l < computeEnd; l++ )
		{
			if ( l == from || layers[l - 1].SplitAfter )
			{
				AddSegment( SegmentKind.Layers, frame, l, true );
				segments[segmentCount - 1].Async = true;
				segments[segmentCount - 1].Context.AsyncCompute = true;
			}
			else
			{
				segments[segmentCount - 1].Count++;
			}

			layers[l].Segment = segmentCount - 1;
		}

		var lastCompute = segmentCount - 1;

		if ( _computeBatchCount == _computeBatches.Count )
			_computeBatches.Add( new( frame.System.FrameContext ) );

		var batch = _computeBatches[_computeBatchCount];
		batch.From = from;
		batch.Count = computeEnd - from;
		segments[firstCompute].HandOff = _computeBatchCount++;

		var acquired = firstCompute - 1;
		for ( int l = computeEnd; l < end; l++ )
		{
			var dependency = layers[l].WaitForLayer;
			var waitFor = dependency >= 0 ? layers[dependency].Segment : -1;
			var first = segmentCount;
			AddGraphicsLayer( l, maxSegments, separate: waitFor > acquired );
			if ( waitFor > acquired && segmentCount > first )
			{
				AcquireCompute( first, acquired + 1, waitFor );
				acquired = waitFor;
			}
		}

		// Complete outstanding compute before the next handoff, untracked work or another frame. The next
		// graphics layer can share this context, including a draw layer that follows a discarded empty layer.
		if ( acquired < lastCompute )
		{
			AddSegment( SegmentKind.Layers, frame, end, true, count: 0 );
			AcquireCompute( segmentCount - 1, acquired + 1, lastCompute );
		}
	}

	void AcquireCompute( int consumer, int from, int producer )
	{
		segments[consumer].TakeBackFrom = from;
		segments[consumer].WaitFor = producer;
		segments[producer].Signal = true;
	}

	void AddGraphicsLayer( int index, int maxSegments, bool separate = false )
	{
		ref var planned = ref layers[index];
		if ( planned.Layer.IsShared )
		{
			// Fold a pending compute join into the first draw instead of submitting an empty graphics context.
			if ( planned.WorkFrom < planned.WorkTo && segmentCount > 0
				&& segments[segmentCount - 1] is { Kind: SegmentKind.Layers, Count: 0 } join && join.Frame == planned.Frame )
			{
				var first = --segmentCount;
				AddDrawSegments( planned.Frame, planned.WorkFrom, planned.WorkTo, maxSegments );
				AcquireCompute( first, join.TakeBackFrom, join.WaitFor );
			}
			else
			{
				AddDrawSegments( planned.Frame, planned.WorkFrom, planned.WorkTo, maxSegments );
			}
		}
		else if ( !separate && segmentCount > 0 && segments[segmentCount - 1] is { Kind: SegmentKind.Layers, Async: false } last
			&& last.Frame == planned.Frame && last.Index + last.Count == index )
		{
			segments[segmentCount - 1].Count++;
		}
		else
		{
			AddSegment( SegmentKind.Layers, planned.Frame, index, true );
		}
	}
}
