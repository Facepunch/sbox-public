namespace Sandbox.Layout;

/// <summary>
/// The track sizing algorithm (css-grid-1 §11.5 – §11.8): initialise track sizes, resolve intrinsic sizes
/// from item contributions, maximise, expand flexible tracks, stretch auto tracks.
/// </summary>
internal static partial class GridLayout
{
	private enum TrackFilter : byte
	{
		Any,
		MinIndefinite,
		MinContentBased,
		MinMaxContent,
		MaxIndefinite,
		MaxContentAlike,
		MaxIntrinsic,
		MaxContentOrFitContent,
		AutoMin,
	}

	private enum TrackLimit : byte
	{
		GrowthLimit,
		FitContentLimitedGrowthLimit,
		FitContentLimit,
		Infinite,
	}

	private enum TrackSize : byte
	{
		BaseSize,
		GrowthLimitOrBase,
	}

	// Item orderings used by the sizing passes, one per axis so no closure captures the axis.
	private static readonly Comparison<GridItem> IntrinsicSizingOrderWidth = static ( a, b ) => CompareForIntrinsicSizing( a, b, Dimension.Width );
	private static readonly Comparison<GridItem> IntrinsicSizingOrderHeight = static ( a, b ) => CompareForIntrinsicSizing( a, b, Dimension.Height );
	private static readonly Comparison<GridItem> PlacementStartOrderWidth = static ( a, b ) => a.Column.Start.CompareTo( b.Column.Start );
	private static readonly Comparison<GridItem> PlacementStartOrderHeight = static ( a, b ) => a.Row.Start.CompareTo( b.Row.Start );

	/// <summary>Non-flex-crossing items by ascending span (then placement), then flex-crossing items.</summary>
	private static int CompareForIntrinsicSizing( GridItem a, GridItem b, Dimension axis )
	{
		var aCrossesFlexibleTrack = a.CrossesFlexibleTrack( axis );
		var bCrossesFlexibleTrack = b.CrossesFlexibleTrack( axis );
		if ( aCrossesFlexibleTrack != bCrossesFlexibleTrack )
		{
			return aCrossesFlexibleTrack ? 1 : -1;
		}

		var spanCompare = a.Span( axis ).CompareTo( b.Span( axis ) );
		if ( spanCompare != 0 )
		{
			return spanCompare;
		}

		return a.Placement( axis ).Start.CompareTo( b.Placement( axis ).Start );
	}

	private enum IntrinsicContributionType
	{
		Minimum,
		Maximum,
	}

	/// <summary>Available space along an axis: a definite length, or an intrinsic sizing request.</summary>
	private readonly record struct AvailableSpace( SizingMode Mode, float Size )
	{
		public bool IsDefinite => Mode == SizingMode.StretchFit || Mode == SizingMode.FitContent;
		public bool IsMinContent => Mode == SizingMode.MinContent;
		public bool IsMaxContent => Mode == SizingMode.MaxContent;
		public static AvailableSpace Definite( float size ) => new( SizingMode.StretchFit, size );
		public static readonly AvailableSpace MaxContent = new( SizingMode.MaxContent, Num.Undefined );
		public static readonly AvailableSpace MinContent = new( SizingMode.MinContent, Num.Undefined );

		public float ComputeFreeSpace( float usedSpace )
		{
			return Mode switch
			{
				SizingMode.MaxContent => float.PositiveInfinity,
				SizingMode.MinContent => 0.0f,
				_ => Size - usedSpace,
			};
		}
	}

	private sealed class TrackSizingContext
	{
		public LayoutNode Node;
		public Direction Direction;
		public int Depth;
		public uint GenerationCount;
		public Dimension Axis;
		public List<GridTrack> AxisTracks;
		public List<GridTrack> OtherAxisTracks;
		public LayoutSize InnerNodeSize;
		public TrackEstimate OtherAxisEstimate;

		public LayoutSize GridAreaSize( GridItem item )
		{
			if ( item.HasGridAreaSizeCache )
			{
				return item.GridAreaSizeCache;
			}

			var size = item.GridAreaSize( Axis, AxisTracks, OtherAxisTracks, InnerNodeSize, OtherAxisEstimate );
			item.GridAreaSizeCache = size;
			item.HasGridAreaSizeCache = true;
			return size;
		}

		public float MinContentContribution( GridItem item )
		{
			var cached = Axis == Dimension.Width ? item.MinContentContributionCacheWidth : item.MinContentContributionCacheHeight;
			var gridArea = GridAreaSize( item );
			var margins = item.MarginAxisSums( gridArea.Width );
			if ( Num.IsUndefined( cached ) )
			{
				var available = gridArea;
				available[Axis] = Num.Undefined;
				cached = MeasureItem( item, Axis, gridArea, available, SizingMode.MinContent, Direction, Depth, GenerationCount );
				if ( Axis == Dimension.Width )
				{
					item.MinContentContributionCacheWidth = cached;
				}
				else
				{
					item.MinContentContributionCacheHeight = cached;
				}
			}
			return cached + margins[Axis];
		}

		public float MaxContentContribution( GridItem item )
		{
			var cached = Axis == Dimension.Width ? item.MaxContentContributionCacheWidth : item.MaxContentContributionCacheHeight;
			var gridArea = GridAreaSize( item );
			var margins = item.MarginAxisSums( gridArea.Width );
			if ( Num.IsUndefined( cached ) )
			{
				var available = gridArea;
				available[Axis] = Num.Undefined;
				cached = MeasureItem( item, Axis, gridArea, available, SizingMode.MaxContent, Direction, Depth, GenerationCount );
				if ( Axis == Dimension.Width )
				{
					item.MaxContentContributionCacheWidth = cached;
				}
				else
				{
					item.MaxContentContributionCacheHeight = cached;
				}
			}
			return cached + margins[Axis];
		}

		public float MinimumContribution( GridItem item )
		{
			var cached = Axis == Dimension.Width ? item.MinimumContributionCacheWidth : item.MinimumContributionCacheHeight;
			var gridArea = GridAreaSize( item );
			var margins = item.MarginAxisSums( gridArea.Width );
			if ( Num.IsUndefined( cached ) )
			{
				cached = ComputeMinimumContribution( item, gridArea );
				if ( Axis == Dimension.Width )
				{
					item.MinimumContributionCacheWidth = cached;
				}
				else
				{
					item.MinimumContributionCacheHeight = cached;
				}
			}
			return cached + margins[Axis];
		}

		/// <summary>
		/// The item's minimum contribution (§6.6 automatic minimum size): its preferred size if definite, else
		/// its min size, else - for scroll containers - zero, else its content-based minimum when it spans an
		/// auto-min track (and no flexible track unless it spans just one).
		/// </summary>
		private float ComputeMinimumContribution( GridItem item, LayoutSize gridArea )
		{
			var node = item.Node;
			var style = node.Style;
			var contentBox = style.BoxSizing == BoxSizing.ContentBox;
			var paddingAndBorderRow = LayoutAlgorithm.PaddingAndBorderForAxis( node, FlexDirection.Row, Direction, gridArea.Width );
			var paddingAndBorderColumn = LayoutAlgorithm.PaddingAndBorderForAxis( node, FlexDirection.Column, Direction, gridArea.Width );

			// Preferred size, transferring through the aspect ratio.
			var width = node.HasDefiniteLength( Dimension.Width, gridArea.Width ) ? node.GetResolvedDimension( Direction, Dimension.Width, gridArea.Width, gridArea.Width ) : Num.Undefined;
			var height = node.HasDefiniteLength( Dimension.Height, gridArea.Height )
				? node.GetResolvedDimension( Direction, Dimension.Height, gridArea.Height, gridArea.Width )
				: Num.Undefined;
			ApplyAspectRatio( style.AspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn, ref width, ref height );
			var preferred = Axis == Dimension.Width ? width : height;
			if ( Num.IsDefined( preferred ) )
			{
				return preferred;
			}

			// Min size, also transferred.
			var minWidth = style.ResolvedMinDimension( Direction, Dimension.Width, gridArea.Width, gridArea.Width );
			var minHeight = style.ResolvedMinDimension( Direction, Dimension.Height, gridArea.Height, gridArea.Width );
			ApplyAspectRatio( style.AspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn, ref minWidth, ref minHeight );
			var min = Axis == Dimension.Width ? minWidth : minHeight;
			if ( Num.IsDefined( min ) )
			{
				return min;
			}

			if ( item.IsScrollContainer )
			{
				return 0.0f;
			}

			var (start, end) = item.TrackRangeExcludingLines( Axis );
			var spansAutoMinTrack = false;
			var spansFlexibleTrack = false;
			for ( int i = start; i < end; i++ )
			{
				if ( AxisTracks[i].Min.IsAuto )
				{
					spansAutoMinTrack = true;
				}

				if ( AxisTracks[i].IsFlexible )
				{
					spansFlexibleTrack = true;
				}
			}
			var onlySpanOneTrack = end - start == 1;
			var useContentBasedMinimum = spansAutoMinTrack && (onlySpanOneTrack || !spansFlexibleTrack);

			if ( !useContentBasedMinimum )
			{
				return 0.0f;
			}

			// Min-content contribution, measured with the grid area as available space.
			var cached = Axis == Dimension.Width ? item.MinContentContributionCacheWidth : item.MinContentContributionCacheHeight;
			if ( Num.IsUndefined( cached ) )
			{
				var available = gridArea;
				available[Axis] = Num.Undefined;
				cached = MeasureItem( item, Axis, gridArea, available, SizingMode.MinContent, Direction, Depth, GenerationCount );
				if ( Axis == Dimension.Width )
				{
					item.MinContentContributionCacheWidth = cached;
				}
				else
				{
					item.MinContentContributionCacheHeight = cached;
				}
			}

			var minimumContribution = cached;

			// Limited by the sum of fixed max track sizing functions when all spanned tracks have one.
			var limit = SpannedFixedTrackLimit( item, InnerNodeSize[Axis] );
			if ( Num.IsDefined( limit ) )
			{
				minimumContribution = MathF.Min( minimumContribution, limit );
			}

			return minimumContribution;
		}

		/// <summary>Sum of the spanned tracks' definite max sizing functions (fit-content(x) counts as x), or undefined.</summary>
		public float SpannedTrackLimit( GridItem item, float axisInnerSize )
		{
			var (start, end) = item.TrackRangeExcludingLines( Axis );
			float sum = 0;
			for ( int i = start; i < end; i++ )
			{
				var limit = AxisTracks[i].MaxDefiniteLimit( axisInnerSize );
				if ( Num.IsUndefined( limit ) )
				{
					return Num.Undefined;
				}

				sum += limit;
			}
			return sum;
		}

		/// <summary>Sum of the spanned tracks' definite max sizing functions (fit-content excluded), or undefined.</summary>
		public float SpannedFixedTrackLimit( GridItem item, float axisInnerSize )
		{
			var (start, end) = item.TrackRangeExcludingLines( Axis );
			float sum = 0;
			for ( int i = start; i < end; i++ )
			{
				var limit = AxisTracks[i].MaxDefiniteValue( axisInnerSize );
				if ( Num.IsUndefined( limit ) )
				{
					return Num.Undefined;
				}

				sum += limit;
			}
			return sum;
		}
	}

	private static void ApplyAspectRatio(
		float aspectRatio,
		bool contentBox,
		float paddingAndBorderRow,
		float paddingAndBorderColumn,
		ref float width,
		ref float height )
	{
		if ( Num.IsUndefined( aspectRatio ) )
		{
			return;
		}

		if ( Num.IsDefined( width ) && Num.IsUndefined( height ) )
		{
			height = LayoutAlgorithm.WidthToHeight(
				width,
				aspectRatio,
				contentBox,
				paddingAndBorderRow,
				paddingAndBorderColumn );
		}
		else if ( Num.IsUndefined( width ) && Num.IsDefined( height ) )
		{
			width = LayoutAlgorithm.HeightToWidth(
				height,
				aspectRatio,
				contentBox,
				paddingAndBorderRow,
				paddingAndBorderColumn );
		}
	}

	// -----------------------------------------------------------------------------------------------
	// §11.5 – §11.8
	// -----------------------------------------------------------------------------------------------

	private static void TrackSizingAlgorithm(
		LayoutNode node,
		Direction direction,
		int depth,
		uint generationCount,
		Dimension axis,
		float axisMinSize,
		float axisMaxSize,
		Justify axisAlignment,
		Justify otherAxisAlignment,
		AvailableSpace availableGridSpaceAxis,
		LayoutSize innerNodeSize,
		List<GridTrack> axisTracks,
		List<GridTrack> otherAxisTracks,
		List<GridItem> items,
		TrackEstimate otherAxisEstimate,
		bool hasBaselineAlignedItem )
	{
		var percentageBasis = Num.IsDefined( innerNodeSize[axis] ) ? innerNodeSize[axis] : axisMinSize;

		// 11.4 Initialise track sizes
		foreach ( var track in axisTracks )
		{
			track.BaseSize = Num.UnwrapOrDefault( track.MinDefiniteValue( percentageBasis ), 0.0f );
			track.GrowthLimit = Num.UnwrapOrDefault( track.MaxDefiniteValue( percentageBasis ), float.PositiveInfinity );
			if ( track.GrowthLimit < track.BaseSize )
			{
				track.GrowthLimit = track.BaseSize;
			}
		}

		if ( hasBaselineAlignedItem )
		{
			ResolveItemBaselines( node, direction, depth, generationCount, axis, items, innerNodeSize );
		}

		// Shortcut: all tracks definite and equal
		var allFixed = true;
		foreach ( var track in axisTracks )
		{
			if ( track.BaseSize != track.GrowthLimit || Num.IsUndefined( track.MinDefiniteValue( percentageBasis ) ) )
			{
				allFixed = false;
				break;
			}
		}
		if ( allFixed )
		{
			return;
		}

		// Gutters in the other axis widened by content distribution are part of the grid area estimate.
		var otherAxis = axis == Dimension.Width ? Dimension.Height : Dimension.Width;
		var gutterAdjustment = ComputeAlignmentGutterAdjustment(
			otherAxisAlignment,
			innerNodeSize[otherAxis],
			otherAxisEstimate,
			otherAxisTracks );
		if ( otherAxisTracks.Count > 3 )
		{
			for ( int i = 2; i < otherAxisTracks.Count - 1; i += 2 )
			{
				otherAxisTracks[i].ContentAlignmentAdjustment = gutterAdjustment;
			}
		}

		var ctx = GridPool<TrackSizingContext>.Rent();
		ctx.Node = node;
		ctx.Direction = direction;
		ctx.Depth = depth;
		ctx.GenerationCount = generationCount;
		ctx.Axis = axis;
		ctx.AxisTracks = axisTracks;
		ctx.OtherAxisTracks = otherAxisTracks;
		ctx.InnerNodeSize = innerNodeSize;
		ctx.OtherAxisEstimate = otherAxisEstimate;

		try
		{
			// 11.5 Resolve intrinsic track sizes
			ResolveIntrinsicTrackSizes( ctx, items, availableGridSpaceAxis );

			// 11.6 Maximise tracks
			MaximiseTracks( axisTracks, innerNodeSize[axis], availableGridSpaceAxis );

			// For the purpose of the final two steps, the available space is the inner node size if known.
			var availableForExpansion = Num.IsDefined( innerNodeSize[axis] )
				? AvailableSpace.Definite( innerNodeSize[axis] )
				: (availableGridSpaceAxis.IsMinContent ? AvailableSpace.MinContent : AvailableSpace.MaxContent);

			// 11.7 Expand flexible tracks
			ExpandFlexibleTracks( ctx, items, axisMinSize, axisMaxSize, availableForExpansion );

			// 11.8 Stretch auto tracks
			if ( axisAlignment == Justify.Stretch )
			{
				StretchAutoTracks( axisTracks, axisMinSize, availableForExpansion );
			}
		}
		finally
		{
			ctx.Node = null;
			ctx.AxisTracks = null;
			ctx.OtherAxisTracks = null;
			GridPool<TrackSizingContext>.Return( ctx );
		}
	}

	/// <summary>
	/// Extra space each inner gutter of an axis will receive from content distribution (space-between /
	/// space-around / space-evenly), so grid area estimates in the other axis stay accurate.
	/// </summary>
	private static float ComputeAlignmentGutterAdjustment(
		Justify alignment,
		float axisInnerNodeSize,
		TrackEstimate estimate,
		List<GridTrack> tracks )
	{
		if ( tracks.Count <= 1 )
		{
			return 0.0f;
		}

		int outerGutterWeight = alignment switch
		{
			Justify.FlexStart or Justify.FlexEnd or Justify.Center => 1,
			Justify.Stretch or Justify.SpaceBetween => 0,
			Justify.SpaceAround or Justify.SpaceEvenly => 1,
			_ => 0,
		};
		int innerGutterWeight = alignment switch
		{
			Justify.SpaceBetween => 1,
			Justify.SpaceAround => 2,
			Justify.SpaceEvenly => 1,
			_ => 0,
		};

		if ( innerGutterWeight == 0 || Num.IsUndefined( axisInnerNodeSize ) )
		{
			return 0.0f;
		}

		float trackSizeSum = 0;
		foreach ( var track in tracks )
		{
			var size = estimate == TrackEstimate.BaseSize ? track.BaseSize : track.MaxDefiniteValue( axisInnerNodeSize );
			if ( Num.IsUndefined( size ) )
			{
				return 0.0f;
			}

			trackSizeSum += size;
		}

		var freeSpace = MathF.Max( 0.0f, axisInnerNodeSize - trackSizeSum );
		var weightedTrackCount = ((tracks.Count - 3) / 2) * innerGutterWeight + 2 * outerGutterWeight;
		return weightedTrackCount > 0 ? (freeSpace / weightedTrackCount) * innerGutterWeight : 0.0f;
	}

	private static void ResolveItemBaselines(
		LayoutNode node,
		Direction direction,
		int depth,
		uint generationCount,
		Dimension axis,
		List<GridItem> items,
		LayoutSize innerNodeSize )
	{
		var otherAxis = axis == Dimension.Width ? Dimension.Height : Dimension.Width;
		items.Sort( otherAxis == Dimension.Width ? PlacementStartOrderWidth : PlacementStartOrderHeight );

		int index = 0;
		while ( index < items.Count )
		{
			var currentRow = items[index].Placement( otherAxis ).Start;
			int end = index;
			while ( end < items.Count && items[end].Placement( otherAxis ).Start == currentRow )
			{
				end++;
			}

			int baselineCount = 0;
			for ( int i = index; i < end; i++ )
			{
				if ( items[i].ParticipatesInBaselineAlignment )
				{
					baselineCount++;
				}
			}

			if ( baselineCount > 1 )
			{
				float rowMaxBaseline = float.NegativeInfinity;
				for ( int i = index; i < end; i++ )
				{
					var item = items[i];
					if ( !item.ParticipatesInBaselineAlignment )
					{
						continue;
					}

					// Lay the item out at its own size (min-content where indefinite) to find its first baseline.
					var known = KnownDimensions( item, LayoutSize.Undefined, direction );
					var itemMargins = item.MarginAxisSums( Num.Undefined );
					var availableWidth = Num.IsDefined( known.Width ) ? known.Width + itemMargins.Width : Num.Undefined;
					var availableHeight = Num.IsDefined( known.Height ) ? known.Height + itemMargins.Height : Num.Undefined;
					LayoutAlgorithm.CalculateLayoutInternal(
						item.Node,
						availableWidth,
						availableHeight,
						direction,
						Num.IsDefined( availableWidth ) ? SizingMode.StretchFit : SizingMode.MinContent,
						Num.IsDefined( availableHeight ) ? SizingMode.StretchFit : SizingMode.MinContent,
						innerNodeSize.Width, innerNodeSize.Height, true, depth, generationCount );

					var height = item.Node.Layout.MeasuredDimension( Dimension.Height );
					var baseline = LayoutAlgorithm.CalculateBaseline( item.Node );
					if ( item.IsScrollContainer )
					{
						baseline = MathF.Max( 0, MathF.Min( baseline, height ) );
					}

					var marginTop = Num.UnwrapOrDefault( item.Style.ComputeMargin( PhysicalEdge.Top, direction ).Resolve( innerNodeSize.Width ), 0 );
					item.Baseline = baseline + marginTop;
					rowMaxBaseline = MathF.Max( rowMaxBaseline, item.Baseline );
				}

				for ( int i = index; i < end; i++ )
				{
					var item = items[i];
					if ( item.ParticipatesInBaselineAlignment )
					{
						item.BaselineShim = rowMaxBaseline - item.Baseline;
					}
				}
			}

			index = end;
		}
	}

	private static void ResolveIntrinsicTrackSizes( TrackSizingContext ctx, List<GridItem> items, AvailableSpace axisAvailableGridSpace )
	{
		var axis = ctx.Axis;
		var axisTracks = ctx.AxisTracks;
		var axisInnerNodeSize = ctx.InnerNodeSize[axis];

		// Items are processed in batches: non-flex-crossing items by ascending span, then flex-crossing items.
		items.Sort( axis == Dimension.Width ? IntrinsicSizingOrderWidth : IntrinsicSizingOrderHeight );

		int batchStart = 0;
		while ( batchStart < items.Count )
		{
			var first = items[batchStart];
			var batchSpan = first.Span( axis );
			var isFlex = first.CrossesFlexibleTrack( axis );
			int batchEnd = batchStart + 1;
			if ( isFlex )
			{
				batchEnd = items.Count;
			}
			else
			{
				while ( batchEnd < items.Count && !items[batchEnd].CrossesFlexibleTrack( axis ) && items[batchEnd].Span( axis ) <= batchSpan ) batchEnd++;
			}

			var batch = new ListSlice<GridItem>( items, batchStart, batchEnd );
			batchStart = batchEnd;

			// 11.5.1: Items that don't span a flexible track and span exactly one track.
			if ( !isFlex && batchSpan == 1 )
			{
				foreach ( var item in batch )
				{
					var trackIndex = item.PlacementIndexes( axis ).Start + 1;
					var track = axisTracks[trackIndex];

					float newBaseSize;
					switch ( track.Min.Kind )
					{
						case TrackBreadthKind.MinContent:
							newBaseSize = MathF.Max( track.BaseSize, ctx.MinContentContribution( item ) );
							break;
						case TrackBreadthKind.Percent:
							newBaseSize = Num.IsUndefined( axisInnerNodeSize ) ? MathF.Max( track.BaseSize, ctx.MinContentContribution( item ) ) : track.BaseSize;
							break;
						case TrackBreadthKind.MaxContent:
							newBaseSize = MathF.Max( track.BaseSize, ctx.MaxContentContribution( item ) );
							break;
						case TrackBreadthKind.Auto:
							{
								float space;
								if ( (axisAvailableGridSpace.IsMinContent || axisAvailableGridSpace.IsMaxContent) && !item.IsScrollContainer )
								{
									// Under an intrinsic constraint the automatic minimum is limited by the track's fixed max.
									var axisMinimumSize = ctx.MinimumContribution( item );
									var axisMinContentSize = ctx.MinContentContribution( item );
									var limit = track.MaxDefiniteLimit( axisInnerNodeSize );
									space = MathF.Max( Num.IsDefined( limit ) ? MathF.Min( axisMinContentSize, limit ) : axisMinContentSize, axisMinimumSize );
								}
								else
								{
									space = ctx.MinimumContribution( item );
								}
								newBaseSize = MathF.Max( track.BaseSize, space );
								break;
							}
						default:
							newBaseSize = track.BaseSize;
							break;
					}

					var growthLimitMinContent = !item.IsScrollContainer ? ctx.MinContentContribution( item ) : Num.Undefined;
					var growthLimitMaxContent = ctx.MaxContentContribution( item );
					var growthLimitIntrinsicMinContent = ctx.MinContentContribution( item );

					track.BaseSize = newBaseSize;

					if ( track.IsFitContent )
					{
						if ( Num.IsDefined( growthLimitMinContent ) ) track.GrowthLimitPlannedIncrease = MathF.Max( track.GrowthLimitPlannedIncrease, growthLimitMinContent );
						var fitContentLimit = track.FitContentLimit( axisInnerNodeSize );
						var maxContent = MathF.Min( growthLimitMaxContent, fitContentLimit );
						track.GrowthLimitPlannedIncrease = MathF.Max( track.GrowthLimitPlannedIncrease, maxContent );
					}
					else if ( track.MaxIsMaxContentAlike || (track.Max.Kind == TrackBreadthKind.Percent && Num.IsUndefined( axisInnerNodeSize )) )
					{
						track.GrowthLimitPlannedIncrease = MathF.Max( track.GrowthLimitPlannedIncrease, growthLimitMaxContent );
					}
					else if ( track.MaxIsIntrinsic )
					{
						track.GrowthLimitPlannedIncrease = MathF.Max( track.GrowthLimitPlannedIncrease, growthLimitIntrinsicMinContent );
					}
				}

				foreach ( var track in axisTracks )
				{
					if ( track.GrowthLimitPlannedIncrease > 0.0f )
					{
						track.GrowthLimit = track.GrowthLimit == float.PositiveInfinity ? track.GrowthLimitPlannedIncrease : MathF.Max( track.GrowthLimit, track.GrowthLimitPlannedIncrease );
					}
					track.InfinitelyGrowable = false;
					track.GrowthLimitPlannedIncrease = 0.0f;
					if ( track.GrowthLimit < track.BaseSize ) track.GrowthLimit = track.BaseSize;
				}

				continue;
			}

			// 11.5.2 step 1: minimum contributions -> base sizes of tracks with intrinsic min sizing functions
			foreach ( var item in batch )
			{
				if ( !item.CrossesIntrinsicTrack( axis ) ) continue;

				float space;
				if ( (axisAvailableGridSpace.IsMinContent || axisAvailableGridSpace.IsMaxContent) && !item.IsScrollContainer )
				{
					var axisMinimumSize = ctx.MinimumContribution( item );
					var axisMinContentSize = ctx.MinContentContribution( item );
					var limit = ctx.SpannedTrackLimit( item, axisInnerNodeSize );
					var limitedMinContent = MathF.Max( Num.IsDefined( limit ) ? MathF.Min( axisMinContentSize, limit ) : axisMinContentSize, axisMinimumSize );
					if ( isFlex )
					{
						var (s, e) = item.TrackRangeExcludingLines( axis );
						float inflexibleSizes = 0;
						float flexFactorSum = 0;
						for ( int i = s; i < e; i++ )
						{
							if ( axisTracks[i].IsFlexible ) flexFactorSum += axisTracks[i].FlexFactor;
							else inflexibleSizes += axisTracks[i].BaseSize;
						}
						var scale = MathF.Min( flexFactorSum, 1.0f );
						var excess = MathF.Max( limitedMinContent - inflexibleSizes, 0.0f );
						space = MathF.Max( axisMinimumSize, inflexibleSizes + excess * scale );
					}
					else
					{
						space = limitedMinContent;
					}
				}
				else
				{
					space = ctx.MinimumContribution( item );
				}

				if ( space > 0.0f )
				{
					var (s, e) = item.TrackRangeExcludingLines( axis );
					var limit = item.IsScrollContainer ? TrackLimit.FitContentLimitedGrowthLimit : TrackLimit.GrowthLimit;
					DistributeItemSpaceToBaseSize( isFlex, space, axisTracks, s, e, TrackFilter.MinIndefinite, limit, IntrinsicContributionType.Minimum, axisInnerNodeSize );
				}
			}
			FlushPlannedBaseSizeIncreases( axisTracks );

			// step 2: min-content contributions -> tracks with min-content or max-content min sizing functions
			foreach ( var item in batch )
			{
				var space = ctx.MinContentContribution( item );
				if ( space > 0.0f )
				{
					var (s, e) = item.TrackRangeExcludingLines( axis );
					var limit = item.IsScrollContainer ? TrackLimit.FitContentLimitedGrowthLimit : TrackLimit.GrowthLimit;
					DistributeItemSpaceToBaseSize( isFlex, space, axisTracks, s, e, TrackFilter.MinContentBased, limit, IntrinsicContributionType.Minimum, axisInnerNodeSize );
				}
			}
			FlushPlannedBaseSizeIncreases( axisTracks );

			// step 3: under a max-content constraint, max-content contributions -> tracks with auto or max-content min sizing functions
			if ( axisAvailableGridSpace.IsMaxContent )
			{
				foreach ( var item in batch )
				{
					var axisMaxContentSize = ctx.MaxContentContribution( item );
					var limit = ctx.SpannedTrackLimit( item, axisInnerNodeSize );
					var space = Num.IsDefined( limit ) ? MathF.Min( axisMaxContentSize, limit ) : axisMaxContentSize;
					var (s, e) = item.TrackRangeExcludingLines( axis );
					if ( isFlex )
					{
						float inflexibleSizes = 0;
						float flexFactorSum = 0;
						for ( int i = s; i < e; i++ )
						{
							if ( axisTracks[i].IsFlexible ) flexFactorSum += axisTracks[i].FlexFactor;
							else inflexibleSizes += axisTracks[i].BaseSize;
						}
						var scale = MathF.Min( flexFactorSum, 1.0f );
						space = inflexibleSizes + MathF.Max( space - inflexibleSizes, 0.0f ) * scale;
					}

					if ( space > 0.0f )
					{
						var anyMaxContentMin = false;
						for ( int i = s; i < e; i++ ) if ( axisTracks[i].Min.IsMaxContent ) anyMaxContentMin = true;

						if ( anyMaxContentMin )
						{
							DistributeItemSpaceToBaseSize( isFlex, space, axisTracks, s, e, TrackFilter.MinMaxContent, TrackLimit.Infinite, IntrinsicContributionType.Maximum, axisInnerNodeSize );
						}
						else
						{
							DistributeItemSpaceToBaseSize(
								isFlex,
								space,
								axisTracks,
								s,
								e,
								TrackFilter.AutoMin,
								TrackLimit.FitContentLimitedGrowthLimit,
								IntrinsicContributionType.Maximum,
								axisInnerNodeSize );
						}
					}
				}
				FlushPlannedBaseSizeIncreases( axisTracks );
			}

			// step 4: max-content contributions -> tracks with max-content min sizing functions
			foreach ( var item in batch )
			{
				var space = ctx.MaxContentContribution( item );
				if ( space > 0.0f )
				{
					var (s, e) = item.TrackRangeExcludingLines( axis );
					DistributeItemSpaceToBaseSize( isFlex, space, axisTracks, s, e, TrackFilter.MinMaxContent, TrackLimit.GrowthLimit, IntrinsicContributionType.Maximum, axisInnerNodeSize );
				}
			}
			FlushPlannedBaseSizeIncreases( axisTracks );

			foreach ( var track in axisTracks )
			{
				if ( track.GrowthLimit < track.BaseSize ) track.GrowthLimit = track.BaseSize;
			}

			// steps 5 & 6: growth limits (skipped for items spanning flexible tracks)
			if ( !isFlex )
			{
				foreach ( var item in batch )
				{
					var space = ctx.MinContentContribution( item );
					if ( space > 0.0f )
					{
						var (s, e) = item.TrackRangeExcludingLines( axis );
						DistributeItemSpaceToGrowthLimit( space, axisTracks, s, e, TrackFilter.MaxIndefinite, axisInnerNodeSize );
					}
				}
				FlushPlannedGrowthLimitIncreases( axisTracks, true );

				foreach ( var item in batch )
				{
					var space = ctx.MaxContentContribution( item );
					if ( space > 0.0f )
					{
						var (s, e) = item.TrackRangeExcludingLines( axis );
						DistributeItemSpaceToGrowthLimit( space, axisTracks, s, e, TrackFilter.MaxContentAlike, axisInnerNodeSize );
					}
				}
				FlushPlannedGrowthLimitIncreases( axisTracks, false );
			}
		}

		// Any track still with an infinite growth limit gets its base size as growth limit.
		foreach ( var track in axisTracks )
		{
			if ( track.GrowthLimit == float.PositiveInfinity ) track.GrowthLimit = track.BaseSize;
		}
	}

	private static void FlushPlannedBaseSizeIncreases( List<GridTrack> tracks )
	{
		foreach ( var track in tracks )
		{
			track.BaseSize += track.BaseSizePlannedIncrease;
			track.BaseSizePlannedIncrease = 0.0f;
		}
	}

	private static void FlushPlannedGrowthLimitIncreases( List<GridTrack> tracks, bool setInfinitelyGrowable )
	{
		foreach ( var track in tracks )
		{
			if ( track.GrowthLimitPlannedIncrease > 0.0f )
			{
				track.GrowthLimit = track.GrowthLimit == float.PositiveInfinity ? track.BaseSize + track.GrowthLimitPlannedIncrease : track.GrowthLimit + track.GrowthLimitPlannedIncrease;
				track.InfinitelyGrowable = setInfinitelyGrowable;
			}
			else
			{
				track.InfinitelyGrowable = false;
			}
			track.GrowthLimitPlannedIncrease = 0.0f;
		}
	}

	/// <summary>
	/// 11.5.1 "Distribute extra space": grow the base sizes of affected tracks in [start, end) to
	/// accommodate an item's contribution.
	/// </summary>
	private static void DistributeItemSpaceToBaseSize(
		bool isFlex,
		float space,
		List<GridTrack> tracks,
		int start,
		int end,
		TrackFilter trackIsAffected,
		TrackLimit trackLimit,
		IntrinsicContributionType contributionType,
		float axisInnerNodeSize )
	{
		var useFlexFactor = false;
		if ( isFlex )
		{
			float flexFactorSum = 0;
			for ( int i = start; i < end; i++ )
			{
				if ( tracks[i].IsFlexible && MatchesFilter( tracks[i], trackIsAffected, axisInnerNodeSize ) )
				{
					flexFactorSum += tracks[i].FlexFactor;
				}
			}

			useFlexFactor = flexFactorSum > 0.0f;
		}

		DistributeItemSpaceToBaseSizeInner(
			space,
			tracks,
			start,
			end,
			trackIsAffected,
			isFlex,
			useFlexFactor,
			trackLimit,
			contributionType,
			axisInnerNodeSize );
	}

	private static void DistributeItemSpaceToBaseSizeInner(
		float space,
		List<GridTrack> tracks,
		int start,
		int end,
		TrackFilter trackIsAffected,
		bool requireFlexible,
		bool useFlexFactor,
		TrackLimit trackLimit,
		IntrinsicContributionType contributionType,
		float axisInnerNodeSize )
	{
		var anyAffected = false;
		for ( int i = start; i < end; i++ )
		{
			if ( IsAffected( tracks[i], axisInnerNodeSize, trackIsAffected, requireFlexible, null ) )
			{
				anyAffected = true;
			}
		}

		if ( space == 0.0f || !anyAffected )
		{
			return;
		}

		float trackSizes = 0;
		for ( int i = start; i < end; i++ )
		{
			trackSizes += tracks[i].BaseSize;
		}

		var extraSpace = MathF.Max( 0.0f, space - trackSizes );

		const float threshold = 0.000001f;

		// 1. Distribute up to the tracks' limits.
		extraSpace = DistributeSpaceUpToLimits(
			extraSpace,
			tracks,
			start,
			end,
			axisInnerNodeSize,
			trackIsAffected,
			requireFlexible,
			null,
			useFlexFactor,
			TrackSize.BaseSize,
			trackLimit );

		// 2. Distribute any remaining space beyond the limits to tracks with intrinsic / max-content max sizing functions.
		if ( extraSpace > threshold )
		{
			var filter = contributionType == IntrinsicContributionType.Minimum ? TrackFilter.MaxIntrinsic : TrackFilter.MaxContentOrFitContent;

			int count = 0;
			for ( int i = start; i < end; i++ )
			{
				if ( IsAffected( tracks[i], axisInnerNodeSize, trackIsAffected, requireFlexible, filter ) )
				{
					count++;
				}
			}

			TrackFilter? secondFilter = count == 0 ? null : filter;

			DistributeSpaceUpToLimits(
				extraSpace,
				tracks,
				start,
				end,
				axisInnerNodeSize,
				trackIsAffected,
				requireFlexible,
				secondFilter,
				useFlexFactor,
				TrackSize.BaseSize,
				TrackLimit.FitContentLimit );
		}

		// 3. Record the planned increase (max of per-item increases).
		for ( int i = start; i < end; i++ )
		{
			var track = tracks[i];
			if ( track.ItemIncurredIncrease > track.BaseSizePlannedIncrease )
			{
				track.BaseSizePlannedIncrease = track.ItemIncurredIncrease;
			}

			track.ItemIncurredIncrease = 0.0f;
		}
	}

	private static void DistributeItemSpaceToGrowthLimit(
		float space,
		List<GridTrack> tracks,
		int start,
		int end,
		TrackFilter trackIsAffected,
		float axisInnerNodeSize )
	{
		int affectedCount = 0;
		for ( int i = start; i < end; i++ )
		{
			if ( MatchesFilter( tracks[i], trackIsAffected, axisInnerNodeSize ) )
			{
				affectedCount++;
			}
		}

		if ( space == 0.0f || affectedCount == 0 )
		{
			return;
		}

		float trackSizes = 0;
		for ( int i = start; i < end; i++ )
		{
			trackSizes += tracks[i].GrowthLimit == float.PositiveInfinity
				? tracks[i].BaseSize
				: tracks[i].GrowthLimit;
		}

		var extraSpace = MathF.Max( 0.0f, space - trackSizes );

		int growableCount = 0;
		for ( int i = start; i < end; i++ )
		{
			var track = tracks[i];
			if ( MatchesFilter( track, trackIsAffected, axisInnerNodeSize )
				&& (track.InfinitelyGrowable
					|| track.FitContentLimitedGrowthLimit( axisInnerNodeSize ) == float.PositiveInfinity) )
			{
				growableCount++;
			}
		}

		if ( growableCount > 0 )
		{
			var increase = extraSpace / growableCount;
			for ( int i = start; i < end; i++ )
			{
				var track = tracks[i];
				if ( MatchesFilter( track, trackIsAffected, axisInnerNodeSize )
					&& (track.InfinitelyGrowable
						|| track.FitContentLimitedGrowthLimit( axisInnerNodeSize ) == float.PositiveInfinity) )
				{
					track.ItemIncurredIncrease = increase;
				}
			}
		}
		else
		{
			DistributeSpaceUpToLimits(
				extraSpace,
				tracks,
				start,
				end,
				axisInnerNodeSize,
				trackIsAffected,
				false,
				null,
				false,
				TrackSize.GrowthLimitOrBase,
				TrackLimit.FitContentLimit );
		}

		for ( int i = start; i < end; i++ )
		{
			var track = tracks[i];
			if ( track.ItemIncurredIncrease > track.GrowthLimitPlannedIncrease )
			{
				track.GrowthLimitPlannedIncrease = track.ItemIncurredIncrease;
			}

			track.ItemIncurredIncrease = 0.0f;
		}
	}

	/// <summary>
	/// Distributes space to the affected tracks in proportion, freezing tracks as they hit their limit.
	/// Increases land in <see cref="GridTrack.ItemIncurredIncrease"/>. Returns the space left over.
	/// </summary>
	private static bool IsAffected(
		GridTrack track,
		float axisInnerNodeSize,
		TrackFilter filter,
		bool requireFlexible,
		TrackFilter? secondFilter )
	{
		if ( requireFlexible && !track.IsFlexible )
		{
			return false;
		}

		if ( !MatchesFilter( track, filter, axisInnerNodeSize ) )
		{
			return false;
		}

		return secondFilter is null || MatchesFilter( track, secondFilter.Value, axisInnerNodeSize );
	}

	private static bool MatchesFilter( GridTrack track, TrackFilter filter, float axisInnerNodeSize )
	{
		return filter switch
		{
			TrackFilter.Any => true,
			TrackFilter.MinIndefinite => Num.IsUndefined( track.MinDefiniteValue( axisInnerNodeSize ) ),
			TrackFilter.MinContentBased => track.MinIsMinOrMaxContent,
			TrackFilter.MinMaxContent => track.Min.IsMaxContent,
			TrackFilter.MaxIndefinite => !track.MaxHasDefiniteValue( axisInnerNodeSize ),
			TrackFilter.MaxContentAlike => track.MaxIsMaxContentAlike || (track.Max.Kind == TrackBreadthKind.Percent && Num.IsUndefined( axisInnerNodeSize )),
			TrackFilter.MaxIntrinsic => track.MaxIsIntrinsic,
			TrackFilter.MaxContentOrFitContent => track.MaxIsMaxOrFitContent,
			TrackFilter.AutoMin => track.Min.IsAuto && !track.MaxIsMinContent,
			_ => false,
		};
	}

	private static float GetTrackSize( GridTrack track, TrackSize size )
	{
		return size == TrackSize.BaseSize || track.GrowthLimit == float.PositiveInfinity ? track.BaseSize : track.GrowthLimit;
	}

	private static float GetTrackLimit( GridTrack track, TrackLimit limit, float axisInnerNodeSize )
	{
		return limit switch
		{
			TrackLimit.GrowthLimit => track.GrowthLimit,
			TrackLimit.FitContentLimitedGrowthLimit => track.FitContentLimitedGrowthLimit( axisInnerNodeSize ),
			TrackLimit.FitContentLimit => track.FitContentLimit( axisInnerNodeSize ),
			_ => float.PositiveInfinity,
		};
	}

	private static float DistributeSpaceUpToLimits(
		float spaceToDistribute,
		List<GridTrack> tracks,
		int start,
		int end,
		float axisInnerNodeSize,
		TrackFilter trackIsAffected,
		bool requireFlexible,
		TrackFilter? secondFilter,
		bool useFlexFactor,
		TrackSize affectedProperty,
		TrackLimit trackLimit )
	{
		const float threshold = 0.01f;
		var maxIterations = (end - start) + 1;

		for ( int iteration = 0; iteration < maxIterations; iteration++ )
		{
			if ( spaceToDistribute <= threshold )
			{
				break;
			}

			float proportionSum = 0;
			var minIncreaseLimit = float.PositiveInfinity;
			for ( int i = start; i < end; i++ )
			{
				var track = tracks[i];
				if ( !IsAffected( track, axisInnerNodeSize, trackIsAffected, requireFlexible, secondFilter ) )
				{
					continue;
				}

				var currentSize = GetTrackSize( track, affectedProperty );
				var limit = GetTrackLimit( track, trackLimit, axisInnerNodeSize );
				if ( currentSize + track.ItemIncurredIncrease >= limit )
				{
					continue;
				}

				var proportion = useFlexFactor ? track.FlexFactor : 1.0f;
				proportionSum += proportion;
				minIncreaseLimit = MathF.Min(
					minIncreaseLimit,
					(limit - currentSize - track.ItemIncurredIncrease) / proportion );
			}

			if ( proportionSum == 0.0f )
			{
				break;
			}

			var iterationIncrease = MathF.Min( minIncreaseLimit, spaceToDistribute / proportionSum );

			for ( int i = start; i < end; i++ )
			{
				var track = tracks[i];
				if ( !IsAffected( track, axisInnerNodeSize, trackIsAffected, requireFlexible, secondFilter ) )
				{
					continue;
				}

				var increase = iterationIncrease * (useFlexFactor ? track.FlexFactor : 1.0f);
				if ( increase > 0.0f
					&& GetTrackSize( track, affectedProperty ) + track.ItemIncurredIncrease + increase
						<= GetTrackLimit( track, trackLimit, axisInnerNodeSize ) + threshold )
				{
					track.ItemIncurredIncrease += increase;
					spaceToDistribute -= increase;
				}
			}
		}

		return spaceToDistribute;
	}

	private static void MaximiseTracks( List<GridTrack> axisTracks, float axisInnerNodeSize, AvailableSpace axisAvailableGridSpace )
	{
		float usedSpace = 0;
		foreach ( var t in axisTracks ) usedSpace += t.BaseSize;
		var freeSpace = axisAvailableGridSpace.ComputeFreeSpace( usedSpace );

		if ( freeSpace == float.PositiveInfinity )
		{
			foreach ( var t in axisTracks ) t.BaseSize = t.GrowthLimit;
		}
		else if ( freeSpace > 0.0f )
		{
			DistributeSpaceUpToLimits(
				freeSpace,
				axisTracks,
				0,
				axisTracks.Count,
				axisInnerNodeSize,
				TrackFilter.Any,
				false,
				null,
				false,
				TrackSize.BaseSize,
				TrackLimit.FitContentLimitedGrowthLimit );
			foreach ( var t in axisTracks )
			{
				t.BaseSize += t.ItemIncurredIncrease;
				t.ItemIncurredIncrease = 0.0f;
			}
		}
	}

	private static void ExpandFlexibleTracks( TrackSizingContext ctx, List<GridItem> items, float axisMinSize, float axisMaxSize, AvailableSpace availableSpace )
	{
		var axisTracks = ctx.AxisTracks;
		var axis = ctx.Axis;

		float flexFraction;
		if ( availableSpace.IsDefinite )
		{
			float usedSpace = 0;
			foreach ( var t in axisTracks ) usedSpace += t.BaseSize;
			var freeSpace = availableSpace.Size - usedSpace;
			flexFraction = freeSpace <= 0.0f ? 0.0f : FindSizeOfFr( axisTracks, 0, axisTracks.Count, availableSpace.Size );
		}
		else if ( availableSpace.IsMinContent )
		{
			flexFraction = 0.0f;
		}
		else
		{
			// Max-content: the largest of each flexible track's base size per flex factor, and each
			// flex-spanning item's max-content contribution divided by the fr of the tracks it spans.
			float trackFraction = 0;
			foreach ( var t in axisTracks )
			{
				if ( !t.IsFlexible ) continue;
				var factor = t.FlexFactor;
				trackFraction = MathF.Max( trackFraction, factor > 1.0f ? t.BaseSize / factor : t.BaseSize );
			}

			float itemFraction = 0;
			foreach ( var item in items )
			{
				if ( !item.CrossesFlexibleTrack( axis ) ) continue;
				var (s, e) = item.TrackRangeExcludingLines( axis );

				// Contribution measured without any grid area constraints.
				var cached = axis == Dimension.Width ? item.MaxContentContributionCacheWidth : item.MaxContentContributionCacheHeight;
				if ( Num.IsUndefined( cached ) )
				{
					cached = MeasureItem( item, axis, LayoutSize.Undefined, LayoutSize.Undefined, SizingMode.MaxContent, ctx.Direction, ctx.Depth, ctx.GenerationCount );
					if ( axis == Dimension.Width ) item.MaxContentContributionCacheWidth = cached; else item.MaxContentContributionCacheHeight = cached;
				}
				itemFraction = MathF.Max( itemFraction, FindSizeOfFr( axisTracks, s, e, cached ) );
			}

			flexFraction = MathF.Max( trackFraction, itemFraction );

			float hypotheticalGridSize = 0;
			foreach ( var t in axisTracks )
			{
				hypotheticalGridSize += t.IsFlexible ? MathF.Max( t.BaseSize, t.FlexFactor * flexFraction ) : t.BaseSize;
			}

			var min = Num.UnwrapOrDefault( axisMinSize, 0.0f );
			var max = Num.UnwrapOrDefault( axisMaxSize, float.PositiveInfinity );
			if ( hypotheticalGridSize < min ) flexFraction = FindSizeOfFr( axisTracks, 0, axisTracks.Count, min );
			else if ( hypotheticalGridSize > max ) flexFraction = FindSizeOfFr( axisTracks, 0, axisTracks.Count, max );
		}

		foreach ( var t in axisTracks )
		{
			if ( t.IsFlexible ) t.BaseSize = MathF.Max( t.BaseSize, t.FlexFactor * flexFraction );
		}
	}

	/// <summary>11.7.1 "Find the size of an fr".</summary>
	private static float FindSizeOfFr( List<GridTrack> tracks, int start, int end, float spaceToFill )
	{
		if ( spaceToFill == 0.0f ) return 0.0f;

		var hypotheticalFrSize = float.PositiveInfinity;
		var maxIterations = (end - start) + 1;

		for ( int iteration = 0; iteration < maxIterations; iteration++ )
		{
			float usedSpace = 0;
			float naiveFlexFactorSum = 0;
			for ( int i = start; i < end; i++ )
			{
				var t = tracks[i];
				if ( t.IsFlexible && t.FlexFactor * hypotheticalFrSize >= t.BaseSize ) naiveFlexFactorSum += t.FlexFactor;
				else usedSpace += t.BaseSize;
			}

			var leftoverSpace = spaceToFill - usedSpace;
			var flexFactor = MathF.Max( naiveFlexFactorSum, 1.0f );
			var previous = hypotheticalFrSize;
			hypotheticalFrSize = leftoverSpace / flexFactor;

			var valid = true;
			for ( int i = start; i < end; i++ )
			{
				var t = tracks[i];
				if ( !t.IsFlexible ) continue;
				if ( !(t.FlexFactor * hypotheticalFrSize >= t.BaseSize || t.FlexFactor * previous < t.BaseSize) )
				{
					valid = false;
					break;
				}
			}

			if ( valid ) break;
		}

		return hypotheticalFrSize;
	}

	private static void StretchAutoTracks( List<GridTrack> axisTracks, float axisMinSize, AvailableSpace availableSpace )
	{
		int autoCount = 0;
		foreach ( var t in axisTracks ) if ( t.MaxIsAuto ) autoCount++;
		if ( autoCount == 0 ) return;

		float usedSpace = 0;
		foreach ( var t in axisTracks ) usedSpace += t.BaseSize;

		var freeSpace = availableSpace.IsDefinite
			? availableSpace.ComputeFreeSpace( usedSpace )
			: (Num.IsDefined( axisMinSize ) ? axisMinSize - usedSpace : 0.0f);

		if ( freeSpace > 0.0f )
		{
			var extra = freeSpace / autoCount;
			foreach ( var t in axisTracks ) if ( t.MaxIsAuto ) t.BaseSize += extra;
		}
	}
}
/*
 * Portions of the grid implementation and the block/grid conformance fixtures derive from
 * Taffy: https://github.com/DioxusLabs/taffy
 * Fixture revision: ac2b86929d35b7e0f1d24919595b89b4ce89baa4.
 * This source file is embedded in Sandbox.Layout.dll to retain the notice in binary distributions.
 *
 * MIT License
 * Copyright (c) 2018 Visly Inc.
 * Copyright (c) 2026 Taffy Authors
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */
