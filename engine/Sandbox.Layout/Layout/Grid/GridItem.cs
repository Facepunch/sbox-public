namespace Sandbox.Layout;

internal enum TrackEstimate : byte
{
	BaseSize,
	MaxDefiniteValue,
}

/// <summary>
/// An in-flow grid item: its resolved placement plus cached intrinsic contributions used by track sizing.
/// </summary>
internal sealed class GridItem
{
	public LayoutNode Node;
	public int SourceOrder;

	/// <summary>Placement in origin-zero lines.</summary>
	public GridSpan Row;
	public GridSpan Column;

	/// <summary>Placement as indices into the interleaved track vectors (gutter index at start/end lines).</summary>
	public GridSpan RowIndexes;
	public GridSpan ColumnIndexes;

	public Align AlignSelf;
	public Align JustifySelf;

	public float Baseline = Num.Undefined;
	public float BaselineShim;

	public bool CrossesFlexibleRow;
	public bool CrossesFlexibleColumn;
	public bool CrossesIntrinsicRow;
	public bool CrossesIntrinsicColumn;

	// Caches, invalidated between sizing passes.
	public bool HasGridAreaSizeCache;
	public LayoutSize GridAreaSizeCache;
	public float MinContentContributionCacheWidth = Num.Undefined;
	public float MinContentContributionCacheHeight = Num.Undefined;
	public float MaxContentContributionCacheWidth = Num.Undefined;
	public float MaxContentContributionCacheHeight = Num.Undefined;
	public float MinimumContributionCacheWidth = Num.Undefined;
	public float MinimumContributionCacheHeight = Num.Undefined;

	public LayoutStyle Style => Node.Style;

	/// <summary>Items are pooled per thread; <see cref="ReturnAll"/> hands a list's items back.</summary>
	public static GridItem Rent(
		LayoutNode node,
		int sourceOrder,
		GridSpan column,
		GridSpan row,
		Align alignSelf,
		Align justifySelf )
	{
		var item = GridPool<GridItem>.Rent();
		item.Node = node;
		item.SourceOrder = sourceOrder;
		item.Column = column;
		item.Row = row;
		item.RowIndexes = default;
		item.ColumnIndexes = default;
		item.AlignSelf = alignSelf;
		item.JustifySelf = justifySelf;
		item.Baseline = Num.Undefined;
		item.BaselineShim = 0;
		item.CrossesFlexibleRow = false;
		item.CrossesFlexibleColumn = false;
		item.CrossesIntrinsicRow = false;
		item.CrossesIntrinsicColumn = false;
		item.ClearCaches();
		return item;
	}

	/// <summary>Returns every item in the list to the pool and clears the list.</summary>
	public static void ReturnAll( List<GridItem> items )
	{
		foreach ( var item in items )
		{
			item.Node = null;
			GridPool<GridItem>.Return( item );
		}
		items.Clear();
	}

	public GridSpan Placement( Dimension axis ) => axis == Dimension.Width ? Column : Row;
	public GridSpan PlacementIndexes( Dimension axis ) => axis == Dimension.Width ? ColumnIndexes : RowIndexes;
	public int Span( Dimension axis ) => Placement( axis ).Span;
	public bool CrossesFlexibleTrack( Dimension axis ) => axis == Dimension.Width ? CrossesFlexibleColumn : CrossesFlexibleRow;
	public bool CrossesIntrinsicTrack( Dimension axis ) => axis == Dimension.Width ? CrossesIntrinsicColumn : CrossesIntrinsicRow;

	/// <summary>Range of track-vector indices the item spans, excluding the gutters at its start and end lines.</summary>
	public (int Start, int End) TrackRangeExcludingLines( Dimension axis )
	{
		var indexes = PlacementIndexes( axis );
		return (indexes.Start + 1, indexes.End);
	}

	public bool HasAutoBlockMargin => Style.GetMargin( Edge.Top ).IsAuto
		|| Style.GetMargin( Edge.Bottom ).IsAuto
		|| Style.GetMargin( Edge.Vertical ).IsAuto
		|| Style.GetMargin( Edge.All ).IsAuto;

	public bool HasCyclicBlockSizeDependency => Style.Height.IsPercent && (CrossesIntrinsicRow || CrossesFlexibleRow);

	public bool ParticipatesInBaselineAlignment => AlignSelf == Align.Baseline && !HasAutoBlockMargin && !HasCyclicBlockSizeDependency;

	public void ClearCaches()
	{
		HasGridAreaSizeCache = false;
		MinContentContributionCacheWidth = MinContentContributionCacheHeight = Num.Undefined;
		MaxContentContributionCacheWidth = MaxContentContributionCacheHeight = Num.Undefined;
		MinimumContributionCacheWidth = MinimumContributionCacheHeight = Num.Undefined;
	}

	public void ClearAxisCaches( Dimension axis )
	{
		if ( axis == Dimension.Width )
		{
			MinContentContributionCacheWidth = MaxContentContributionCacheWidth = MinimumContributionCacheWidth = Num.Undefined;
		}
		else
		{
			MinContentContributionCacheHeight = MaxContentContributionCacheHeight = MinimumContributionCacheHeight = Num.Undefined;
		}
	}

	public bool IsScrollContainer => Style.Overflow != Overflow.Visible;

	// -----------------------------------------------------------------------------------------------
	// Sizes derived from the grid area
	// -----------------------------------------------------------------------------------------------

	/// <summary>
	/// The size of the item's grid area for percentage resolution and stretching: in <paramref name="axis"/>
	/// only definite when every spanned track has a fixed min == max; in the other axis the estimate
	/// (base sizes once that axis has been sized).
	/// </summary>
	public LayoutSize GridAreaSize(
		Dimension axis,
		List<GridTrack> axisTracks,
		List<GridTrack> otherAxisTracks,
		LayoutSize innerNodeSize,
		TrackEstimate otherAxisEstimate )
	{
		var size = LayoutSize.Undefined;

		var (start, end) = TrackRangeExcludingLines( axis );
		float axisSum = 0;
		var axisDefinite = true;
		for ( int i = start; i < end; i++ )
		{
			var track = axisTracks[i];
			var min = track.MinDefiniteValue( innerNodeSize[axis] );
			var max = track.MaxDefiniteValue( innerNodeSize[axis] );
			if ( Num.IsUndefined( min ) || Num.IsUndefined( max ) || min != max )
			{
				axisDefinite = false;
				break;
			}
			axisSum += track.BaseSize;
		}
		if ( axisDefinite )
		{
			size[axis] = axisSum;
		}

		var other = axis == Dimension.Width ? Dimension.Height : Dimension.Width;
		var (oStart, oEnd) = TrackRangeExcludingLines( other );
		float otherSum = 0;
		var otherDefinite = true;
		for ( int i = oStart; i < oEnd; i++ )
		{
			var estimate = otherAxisEstimate == TrackEstimate.BaseSize
				? otherAxisTracks[i].BaseSize
				: otherAxisTracks[i].MaxDefiniteValue( innerNodeSize[other] );
			if ( Num.IsUndefined( estimate ) )
			{
				otherDefinite = false;
				break;
			}
			otherSum += estimate + otherAxisTracks[i].ContentAlignmentAdjustment;
		}
		if ( otherDefinite )
		{
			size[other] = otherSum;
		}

		return size;
	}

	/// <summary>Sum of margins in each axis (auto = 0), percentages against the grid area width, plus the baseline shim vertically.</summary>
	public LayoutSize MarginAxisSums( float gridAreaWidth )
	{
		var left = Node.Style.ComputeMargin( PhysicalEdge.Left, Direction.LTR );
		var right = Node.Style.ComputeMargin( PhysicalEdge.Right, Direction.LTR );
		var top = Node.Style.ComputeMargin( PhysicalEdge.Top, Direction.LTR );
		var bottom = Node.Style.ComputeMargin( PhysicalEdge.Bottom, Direction.LTR );

		// Horizontal percentage margins resolve to 0 for intrinsic contributions.
		static float Horizontal( StyleLength length ) => length.IsPoints ? length.Value : 0;
		float Vertical( StyleLength length ) => length.IsAuto ? 0 : Num.UnwrapOrDefault( length.Resolve( gridAreaWidth ), 0 );

		return new LayoutSize(
			Horizontal( left ) + Horizontal( right ),
			Vertical( top ) + Vertical( bottom ) + BaselineShim );
	}
}
