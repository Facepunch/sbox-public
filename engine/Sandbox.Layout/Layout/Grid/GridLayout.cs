namespace Sandbox.Layout;

/// <summary>
/// CSS grid layout (<c>display: grid</c>). Places items into tracks (§8), sizes the tracks (§11), aligns
/// tracks within the container (§10.5) and items within their areas (§10), and positions absolutely
/// positioned children against their grid area (§9).
/// </summary>
internal static partial class GridLayout
{
	internal static void Compute(
		LayoutNode node,
		float availableWidth,
		float availableHeight,
		Direction direction,
		SizingMode widthSizingMode,
		SizingMode heightSizingMode,
		float ownerWidth,
		float ownerHeight,
		bool performLayout,
		int depth,
		uint generationCount,
		float marginAxisRow,
		float marginAxisColumn )
	{
		ref var layout = ref node.Layout;
		var style = node.Style;
		var isRtl = direction == Direction.RTL;

		var paddingLeft = layout.Padding( PhysicalEdge.Left );
		var paddingRight = layout.Padding( PhysicalEdge.Right );
		var paddingTop = layout.Padding( PhysicalEdge.Top );
		var paddingBottom = layout.Padding( PhysicalEdge.Bottom );
		var borderLeft = layout.Border( PhysicalEdge.Left );
		var borderRight = layout.Border( PhysicalEdge.Right );
		var borderTop = layout.Border( PhysicalEdge.Top );
		var borderBottom = layout.Border( PhysicalEdge.Bottom );
		var pbRow = paddingLeft + paddingRight + borderLeft + borderRight;
		var pbColumn = paddingTop + paddingBottom + borderTop + borderBottom;

		// ---------------------------------------------------------------------------------------------
		// Container sizes: known outer size (stretch or definite style), min/max, available grid space
		// ---------------------------------------------------------------------------------------------

		var minWidth = style.ResolvedMinDimension( direction, Dimension.Width, ownerWidth, ownerWidth );
		var maxWidth = style.ResolvedMaxDimension( direction, Dimension.Width, ownerWidth, ownerWidth );
		var minHeight = style.ResolvedMinDimension( direction, Dimension.Height, ownerHeight, ownerWidth );
		var maxHeight = style.ResolvedMaxDimension( direction, Dimension.Height, ownerHeight, ownerWidth );

		var styleWidth = node.HasDefiniteLength( Dimension.Width, ownerWidth ) ? node.GetResolvedDimension( direction, Dimension.Width, ownerWidth, ownerWidth ) : Num.Undefined;
		var styleHeight = node.HasDefiniteLength( Dimension.Height, ownerHeight ) ? node.GetResolvedDimension( direction, Dimension.Height, ownerHeight, ownerWidth ) : Num.Undefined;

		var outerWidth = widthSizingMode == SizingMode.StretchFit ? availableWidth - marginAxisRow : styleWidth;
		var outerHeight = heightSizingMode == SizingMode.StretchFit ? availableHeight - marginAxisColumn : styleHeight;
		if ( Num.IsDefined( outerWidth ) )
		{
			outerWidth = MathF.Max( Clamp( outerWidth, minWidth, maxWidth ), pbRow );
		}

		if ( Num.IsDefined( outerHeight ) )
		{
			outerHeight = MathF.Max( Clamp( outerHeight, minHeight, maxHeight ), pbColumn );
		}

		var availableGridSpaceWidth = ResolveAvailableGridSpace( widthSizingMode, availableWidth - marginAxisRow, outerWidth, minWidth, maxWidth, pbRow );
		var availableGridSpaceHeight = ResolveAvailableGridSpace( heightSizingMode, availableHeight - marginAxisColumn, outerHeight, minHeight, maxHeight, pbColumn );

		var innerMinWidth = Num.IsDefined( minWidth ) ? minWidth - pbRow : Num.Undefined;
		var innerMaxWidth = Num.IsDefined( maxWidth ) ? maxWidth - pbRow : Num.Undefined;
		var innerMinHeight = Num.IsDefined( minHeight ) ? minHeight - pbColumn : Num.Undefined;
		var innerMaxHeight = Num.IsDefined( maxHeight ) ? maxHeight - pbColumn : Num.Undefined;

		var innerNodeSize = new LayoutSize( Num.IsDefined( outerWidth ) ? outerWidth - pbRow : Num.Undefined, Num.IsDefined( outerHeight ) ? outerHeight - pbColumn : Num.Undefined );

		var alignContent = style.AlignContent == Align.Auto ? Justify.Stretch : ToJustify( style.AlignContent );
		var justifyContent = style.JustifyContent;
		var alignItems = style.AlignItems == Align.Auto ? Align.Stretch : style.AlignItems;
		var justifyItems = style.JustifyItems == Align.Auto ? Align.Stretch : style.JustifyItems;

		var childBuffer = GridPool<List<LayoutNode>>.Rent();
		childBuffer.Clear();
		List<GridItem> items = null;
		CellOccupancyMatrix matrix = null;
		List<GridTrack> columns = null;
		List<GridTrack> rows = null;
		try
		{
			var children = node.GetLayoutChildren( childBuffer );
			LayoutAlgorithm.PropagateBaselineSensitivity( node, children );

			// ---------------------------------------------------------------------------------------------
			// Explicit grid, named lines, placement
			// ---------------------------------------------------------------------------------------------

			// Auto-repeat resolves against the definite size; failing that the max or min size.
			var autoFitWidth = Num.IsDefined( outerWidth ) ? outerWidth : Num.IsDefined( maxWidth ) ? maxWidth : minWidth;
			var autoFitHeight = Num.IsDefined( outerHeight ) ? outerHeight : Num.IsDefined( maxHeight ) ? maxHeight : minHeight;
			if ( Num.IsDefined( autoFitWidth ) )
			{
				autoFitWidth = MathF.Max( Clamp( autoFitWidth, minWidth, maxWidth ), pbRow ) - pbRow;
			}

			if ( Num.IsDefined( autoFitHeight ) )
			{
				autoFitHeight = MathF.Max( Clamp( autoFitHeight, minHeight, maxHeight ), pbColumn ) - pbColumn;
			}

			var columnGap = style.ComputeColumnGap();
			var rowGap = style.ComputeRowGap();

			var (columnAutoRepetitions, explicitColumnCount) = ComputeExplicitGridSize(
				style.GridTemplateColumns,
				columnGap,
				autoFitWidth,
				Num.IsDefined( outerWidth ) || Num.IsDefined( maxWidth ) );
			var (rowAutoRepetitions, explicitRowCount) = ComputeExplicitGridSize(
				style.GridTemplateRows,
				rowGap,
				autoFitHeight,
				Num.IsDefined( outerHeight ) || Num.IsDefined( maxHeight ) );

			var columnNames = new NamedLines( style.GridTemplateColumns, columnAutoRepetitions, explicitColumnCount );
			var rowNames = new NamedLines( style.GridTemplateRows, rowAutoRepetitions, explicitRowCount );

			var (estimatedColumnCounts, estimatedRowCounts) = ComputeGridSizeEstimate(
				explicitColumnCount,
				explicitRowCount,
				children,
				columnNames,
				rowNames );

			items = GridPool<List<GridItem>>.Rent();
			matrix = CellOccupancyMatrix.Rent( estimatedColumnCounts, estimatedRowCounts );
			PlaceGridItems( matrix, items, children, style.GridAutoFlow, alignItems, justifyItems, columnNames, rowNames );

			var finalColumnCounts = matrix.TrackCounts( Dimension.Width );
			var finalRowCounts = matrix.TrackCounts( Dimension.Height );

			columns = GridPool<List<GridTrack>>.Rent();
			rows = GridPool<List<GridTrack>>.Rent();
			InitializeGridTracks(
				columns,
				finalColumnCounts,
				style.GridTemplateColumns,
				style.GridAutoColumnItems,
				columnGap,
				columnAutoRepetitions,
				matrix,
				Dimension.Width );
			InitializeGridTracks( rows, finalRowCounts, style.GridTemplateRows, style.GridAutoRowItems, rowGap, rowAutoRepetitions, matrix, Dimension.Height );
			CellOccupancyMatrix.Return( matrix );
			matrix = null;

			foreach ( var item in items )
			{
				item.ColumnIndexes = new GridSpan(
					finalColumnCounts.LineToTrackVecIndex( item.Column.Start ),
					finalColumnCounts.LineToTrackVecIndex( item.Column.End ) );
				item.RowIndexes = new GridSpan( finalRowCounts.LineToTrackVecIndex( item.Row.Start ), finalRowCounts.LineToTrackVecIndex( item.Row.End ) );

				var (cs, ce) = item.TrackRangeExcludingLines( Dimension.Width );
				for ( int i = cs; i < ce; i++ )
				{
					if ( columns[i].IsFlexible )
					{
						item.CrossesFlexibleColumn = true;
					}

					if ( columns[i].HasIntrinsicSizingFunction )
					{
						item.CrossesIntrinsicColumn = true;
					}
				}
				var (rs, re) = item.TrackRangeExcludingLines( Dimension.Height );
				for ( int i = rs; i < re; i++ )
				{
					if ( rows[i].IsFlexible )
					{
						item.CrossesFlexibleRow = true;
					}

					if ( rows[i].HasIntrinsicSizingFunction )
					{
						item.CrossesIntrinsicRow = true;
					}
				}
			}

			var hasBaselineAlignedItem = false;
			foreach ( var item in items )
			{
				if ( item.ParticipatesInBaselineAlignment )
				{
					hasBaselineAlignedItem = true;
				}
			}

			// Children with display: none take no part but still need zeroed layouts.
			foreach ( var child in children )
			{
				child.ProcessDimensions();
				if ( child.Style.Display == Display.None )
				{
					LayoutAlgorithm.ZeroOutLayoutRecursively( child );
					child.HasNewLayout = true;
					child.SetDirty( false );
				}
			}

			// ---------------------------------------------------------------------------------------------
			// Track sizing: columns, then rows, then re-runs when contributions changed
			// ---------------------------------------------------------------------------------------------

			TrackSizingAlgorithm(
				node,
				direction,
				depth,
				generationCount,
				Dimension.Width,
				innerMinWidth,
				innerMaxWidth,
				justifyContent,
				alignContent,
				availableGridSpaceWidth,
				innerNodeSize,
				columns,
				rows,
				items,
				TrackEstimate.MaxDefiniteValue,
				hasBaselineAlignedItem );

			var initialColumnSum = SumBaseSizes( columns );
			if ( Num.IsUndefined( innerNodeSize.Width ) )
			{
				innerNodeSize.Width = initialColumnSum;
			}

			foreach ( var item in items )
			{
				item.HasGridAreaSizeCache = false;
			}

			TrackSizingAlgorithm(
				node,
				direction,
				depth,
				generationCount,
				Dimension.Height,
				innerMinHeight,
				innerMaxHeight,
				alignContent,
				justifyContent,
				availableGridSpaceHeight,
				innerNodeSize,
				rows,
				columns,
				items,
				TrackEstimate.BaseSize,
				false );

			var initialRowSum = SumBaseSizes( rows );
			if ( Num.IsUndefined( innerNodeSize.Height ) )
			{
				innerNodeSize.Height = initialRowSum;
			}

			var containerWidth = Num.IsDefined( outerWidth ) ? outerWidth : MathF.Max( Clamp( initialColumnSum + pbRow, minWidth, maxWidth ), pbRow );
			var containerHeight = Num.IsDefined( outerHeight ) ? outerHeight : MathF.Max( Clamp( initialRowSum + pbColumn, minHeight, maxHeight ), pbColumn );
			var contentWidth = MathF.Max( 0.0f, containerWidth - pbRow );
			var contentHeight = MathF.Max( 0.0f, containerHeight - pbColumn );

			// Percentage tracks in an indefinite axis resolve against the now-known container size.
			if ( !availableGridSpaceWidth.IsDefinite )
			{
				foreach ( var column in columns )
				{
					var min = GridTrack.ResolvedPercentageSize( column.Min, contentWidth );
					var max = GridTrack.ResolvedPercentageSize( column.Max, contentWidth );
					column.BaseSize = Clamp( column.BaseSize, min, max );
				}
			}
			if ( !availableGridSpaceHeight.IsDefinite )
			{
				foreach ( var row in rows )
				{
					var min = GridTrack.ResolvedPercentageSize( row.Min, contentHeight );
					var max = GridTrack.ResolvedPercentageSize( row.Max, contentHeight );
					row.BaseSize = Clamp( row.BaseSize, min, max );
				}
			}

			var hasPercentageColumn = false;
			foreach ( var column in columns )
			{
				if ( column.UsesPercentage )
				{
					hasPercentageColumn = true;
				}
			}

			var hasPercentageRow = false;
			foreach ( var row in rows )
			{
				if ( row.UsesPercentage )
				{
					hasPercentageRow = true;
				}
			}

			var parentWidthIndefinite = !availableGridSpaceWidth.IsDefinite;
			var rerunColumnSizing = parentWidthIndefinite && hasPercentageColumn;
			var intrinsicColumnContributionChanged = false;

			if ( !rerunColumnSizing )
			{
				// Items crossing intrinsic columns may have changed size now that rows are known.
				foreach ( var item in items )
				{
					if ( !item.CrossesIntrinsicColumn )
					{
						continue;
					}
					var gridAreaSize = item.GridAreaSize( Dimension.Width, columns, rows, innerNodeSize, TrackEstimate.BaseSize );
					var available = gridAreaSize;
					available.Width = Num.Undefined;
					var newMinContent = MeasureItem( item, Dimension.Width, gridAreaSize, available, SizingMode.MinContent, direction, depth, generationCount );
					var changed = !Num.OptionalEquals( newMinContent, item.MinContentContributionCacheWidth );
					item.GridAreaSizeCache = gridAreaSize;
					item.HasGridAreaSizeCache = true;
					item.MinContentContributionCacheWidth = newMinContent;
					item.MaxContentContributionCacheWidth = Num.Undefined;
					item.MinimumContributionCacheWidth = Num.Undefined;
					if ( changed )
					{
						intrinsicColumnContributionChanged = true;
					}
				}
				rerunColumnSizing = intrinsicColumnContributionChanged;
			}
			else
			{
				foreach ( var item in items )
				{
					item.HasGridAreaSizeCache = false;
					item.ClearAxisCaches( Dimension.Width );
				}
			}

			var intrinsicRowContributionChanged = false;
			if ( rerunColumnSizing )
			{
				TrackSizingAlgorithm(
					node,
					direction,
					depth,
					generationCount,
					Dimension.Width,
					innerMinWidth,
					innerMaxWidth,
					justifyContent,
					alignContent,
					availableGridSpaceWidth,
					innerNodeSize,
					columns,
					rows,
					items,
					TrackEstimate.BaseSize,
					hasBaselineAlignedItem );

				var parentHeightIndefinite = !availableGridSpaceHeight.IsDefinite;
				var rerunRowSizing = parentHeightIndefinite && hasPercentageRow;

				if ( !rerunRowSizing )
				{
					foreach ( var item in items )
					{
						if ( !item.CrossesIntrinsicRow )
						{
							continue;
						}
						var gridAreaSize = item.GridAreaSize( Dimension.Height, rows, columns, innerNodeSize, TrackEstimate.BaseSize );
						var available = gridAreaSize;
						available.Height = Num.Undefined;
						var newMinContent = MeasureItem( item, Dimension.Height, gridAreaSize, available, SizingMode.MinContent, direction, depth, generationCount );
						var changed = !Num.OptionalEquals( newMinContent, item.MinContentContributionCacheHeight );
						item.GridAreaSizeCache = gridAreaSize;
						item.HasGridAreaSizeCache = true;
						item.MinContentContributionCacheHeight = newMinContent;
						item.MaxContentContributionCacheHeight = Num.Undefined;
						item.MinimumContributionCacheHeight = Num.Undefined;
						if ( changed )
						{
							intrinsicRowContributionChanged = true;
						}
					}
					rerunRowSizing = intrinsicRowContributionChanged;
				}
				else
				{
					foreach ( var item in items )
					{
						item.HasGridAreaSizeCache = false;
						item.ClearAxisCaches( Dimension.Height );
					}
				}

				if ( rerunRowSizing )
				{
					TrackSizingAlgorithm(
						node,
						direction,
						depth,
						generationCount,
						Dimension.Height,
						innerMinHeight,
						innerMaxHeight,
						alignContent,
						justifyContent,
						availableGridSpaceHeight,
						innerNodeSize,
						rows,
						columns,
						items,
						TrackEstimate.BaseSize,
						false );
				}
			}

			if ( (intrinsicColumnContributionChanged && !hasPercentageColumn) || (intrinsicRowContributionChanged && !hasPercentageRow) )
			{
				if ( intrinsicColumnContributionChanged && !hasPercentageColumn )
				{
					containerWidth = Num.IsDefined( outerWidth ) ? outerWidth : MathF.Max( Clamp( SumBaseSizes( columns ) + pbRow, minWidth, maxWidth ), pbRow );
					contentWidth = MathF.Max( 0.0f, containerWidth - pbRow );
				}
				if ( intrinsicRowContributionChanged && !hasPercentageRow )
				{
					containerHeight = Num.IsDefined( outerHeight ) ? outerHeight : MathF.Max( Clamp( SumBaseSizes( rows ) + pbColumn, minHeight, maxHeight ), pbColumn );
					contentHeight = MathF.Max( 0.0f, containerHeight - pbColumn );
				}
			}

			layout.SetMeasuredDimension( Dimension.Width, containerWidth );
			layout.SetMeasuredDimension( Dimension.Height, containerHeight );
			layout.HadOverflow = SumBaseSizes( columns ) > contentWidth + 0.0001f || SumBaseSizes( rows ) > contentHeight + 0.0001f;

			if ( !performLayout )
			{
				return;
			}

			// A content-sized axis is now definite; flexible tracks resolve against it one final time (this is
			// where a flex factor sum below 1 leaves free space, css-grid-1 §11.7.1).
			if ( Num.IsUndefined( outerWidth ) && HasFlexibleTrack( columns ) )
			{
				innerNodeSize.Width = contentWidth;
				foreach ( var item in items )
				{
					item.ClearCaches();
				}
				TrackSizingAlgorithm(
					node,
					direction,
					depth,
					generationCount,
					Dimension.Width,
					innerMinWidth,
					innerMaxWidth,
					justifyContent,
					alignContent,
					AvailableSpace.Definite( contentWidth ),
					innerNodeSize,
					columns,
					rows,
					items,
					TrackEstimate.BaseSize,
					hasBaselineAlignedItem );
			}
			if ( Num.IsUndefined( outerHeight ) && HasFlexibleTrack( rows ) )
			{
				innerNodeSize.Height = contentHeight;
				foreach ( var item in items )
				{
					item.ClearCaches();
				}
				TrackSizingAlgorithm(
					node,
					direction,
					depth,
					generationCount,
					Dimension.Height,
					innerMinHeight,
					innerMaxHeight,
					alignContent,
					justifyContent,
					AvailableSpace.Definite( contentHeight ),
					innerNodeSize,
					rows,
					columns,
					items,
					TrackEstimate.BaseSize,
					false );
			}

			// ---------------------------------------------------------------------------------------------
			// Track alignment
			// ---------------------------------------------------------------------------------------------

			AlignTracks( contentWidth, paddingLeft, paddingRight, borderLeft, borderRight, columns, justifyContent, isRtl );
			AlignTracks( contentHeight, paddingTop, paddingBottom, borderTop, borderBottom, rows, alignContent, false );

			// ---------------------------------------------------------------------------------------------
			// Item layout
			// ---------------------------------------------------------------------------------------------

			items.Sort( SourceOrderComparison );

			foreach ( var item in items )
			{
				var top = rows[item.RowIndexes.Start + 1].Offset;
				var bottom = rows[item.RowIndexes.End].Offset;
				var left = isRtl ? columns[item.ColumnIndexes.End - 1].Offset : columns[item.ColumnIndexes.Start + 1].Offset;
				var right = isRtl ? columns[item.ColumnIndexes.Start].Offset : columns[item.ColumnIndexes.End].Offset;

				AlignAndPositionItem( node, item.Node, left, top, right, bottom, justifyItems, alignItems, item.BaselineShim, direction, containerWidth, depth, generationCount );
			}

			// ---------------------------------------------------------------------------------------------
			// Absolutely positioned children: the grid area of their placement, or the padding box
			// ---------------------------------------------------------------------------------------------

			foreach ( var child in children )
			{
				if ( child.Style.Display == Display.None || child.Style.PositionType != PositionType.Absolute ) continue;

				var column = ToOriginZero(
					child.Style.GridColumnStart,
					child.Style.GridColumnEnd,
					columnNames,
					finalColumnCounts.Explicit ).ResolveAbsolutelyPositionedGridTracks();
				var row = ToOriginZero( child.Style.GridRowStart, child.Style.GridRowEnd, rowNames, finalRowCounts.Explicit ).ResolveAbsolutelyPositionedGridTracks();

				int? columnStart = column.Start.HasValue
					? NullIfNegative( finalColumnCounts.TryLineToTrackVecIndex( column.Start.Value ) )
					: null;
				int? columnEnd = column.End.HasValue
					? NullIfNegative( finalColumnCounts.TryLineToTrackVecIndex( column.End.Value ) )
					: null;
				int? rowStart = row.Start.HasValue ? NullIfNegative( finalRowCounts.TryLineToTrackVecIndex( row.Start.Value ) ) : null;
				int? rowEnd = row.End.HasValue ? NullIfNegative( finalRowCounts.TryLineToTrackVecIndex( row.End.Value ) ) : null;

				float areaLeft, areaRight;
				if ( isRtl )
				{
					areaLeft = columnEnd.HasValue ? RtlLineAsEndEdge( columns, columnEnd.Value ) : borderLeft;
					areaRight = columnStart.HasValue ? RtlLineAsStartEdge( columns, columnStart.Value ) : containerWidth - borderRight;
				}
				else
				{
					areaLeft = columnStart.HasValue ? LineAsStartEdge( columns, columnStart.Value ) : borderLeft;
					areaRight = columnEnd.HasValue ? LineAsEndEdge( columns, columnEnd.Value ) : containerWidth - borderRight;
				}
				var areaTop = rowStart.HasValue ? LineAsStartEdge( rows, rowStart.Value ) : borderTop;
				var areaBottom = rowEnd.HasValue ? LineAsEndEdge( rows, rowEnd.Value ) : containerHeight - borderBottom;

				AlignAndPositionItem( node, child, areaLeft, areaTop, areaRight, areaBottom, justifyItems, alignItems, 0.0f, direction, containerWidth, depth, generationCount );
			}

		}
		finally
		{
			if ( matrix is not null ) CellOccupancyMatrix.Return( matrix );
			ReleaseGridState( items, columns, rows, childBuffer );
		}
	}

	private static readonly Comparison<GridItem> SourceOrderComparison = static ( a, b ) => a.SourceOrder.CompareTo( b.SourceOrder );

	private static void ReleaseGridState( List<GridItem> items, List<GridTrack> columns, List<GridTrack> rows, List<LayoutNode> childBuffer )
	{
		if ( items is not null )
		{
			GridItem.ReturnAll( items );
			GridPool<List<GridItem>>.Return( items, items.Capacity );
		}
		if ( columns is not null )
		{
			GridTrack.ReturnAll( columns );
			GridPool<List<GridTrack>>.Return( columns, columns.Capacity );
		}
		if ( rows is not null )
		{
			GridTrack.ReturnAll( rows );
			GridPool<List<GridTrack>>.Return( rows, rows.Capacity );
		}
		childBuffer.Clear();
		GridPool<List<LayoutNode>>.Return( childBuffer, childBuffer.Capacity );
	}

	private static int? NullIfNegative( int value ) => value < 0 ? null : value;

	private static float LineAsStartEdge( List<GridTrack> tracks, int index ) => index + 1 < tracks.Count ? tracks[index + 1].Offset : tracks[index].Offset;
	private static float LineAsEndEdge( List<GridTrack> tracks, int index ) => index == 0 ? (tracks.Count > 1 ? tracks[1].Offset : tracks[0].Offset) : tracks[index].Offset;
	private static float RtlLineAsStartEdge( List<GridTrack> tracks, int index )
	{
		return tracks.Count > index + 1
			? tracks[index].Offset
			: index == 0 ? tracks[0].Offset : tracks[index - 1].Offset;
	}
	private static float RtlLineAsEndEdge( List<GridTrack> tracks, int index ) => index == 0 ? tracks[0].Offset : tracks[index - 1].Offset;

	private static bool HasFlexibleTrack( List<GridTrack> tracks )
	{
		foreach ( var track in tracks )
		{
			if ( track.IsFlexible )
			{
				return true;
			}
		}

		return false;
	}

	private static float SumBaseSizes( List<GridTrack> tracks )
	{
		float sum = 0;
		foreach ( var track in tracks )
		{
			sum += track.BaseSize;
		}

		return sum;
	}

	private static float Clamp( float value, float min, float max )
	{
		if ( Num.IsDefined( max ) )
		{
			value = MathF.Min( value, max );
		}

		if ( Num.IsDefined( min ) )
		{
			value = MathF.Max( value, min );
		}

		return value;
	}

	private static Justify ToJustify( Align align )
	{
		return align switch
		{
			Align.FlexStart => Justify.FlexStart,
			Align.Center => Justify.Center,
			Align.FlexEnd => Justify.FlexEnd,
			Align.SpaceBetween => Justify.SpaceBetween,
			Align.SpaceAround => Justify.SpaceAround,
			Align.SpaceEvenly => Justify.SpaceEvenly,
			Align.Baseline => Justify.FlexStart,
			_ => Justify.Stretch,
		};
	}

	private static AvailableSpace ResolveAvailableGridSpace(
		SizingMode mode,
		float availableBorderBox,
		float outerSize,
		float min,
		float max,
		float paddingAndBorder )
	{
		if ( Num.IsDefined( outerSize ) )
		{
			return AvailableSpace.Definite( outerSize - paddingAndBorder );
		}

		switch ( mode )
		{
			case SizingMode.FitContent:
			case SizingMode.StretchFit:
				{
					var space = MathF.Max( Clamp( availableBorderBox, min, max ), paddingAndBorder );
					return AvailableSpace.Definite( space - paddingAndBorder );
				}
			case SizingMode.MinContent:
				return AvailableSpace.MinContent;
			default:
				return AvailableSpace.MaxContent;
		}
	}

	// -----------------------------------------------------------------------------------------------
	// Item measurement
	// -----------------------------------------------------------------------------------------------

	/// <summary>
	/// The size an item's box takes along <paramref name="axis"/> under an intrinsic constraint, given
	/// what's known about its grid area. Excludes margins.
	/// </summary>
	private static float MeasureItem(
		GridItem item,
		Dimension axis,
		LayoutSize gridAreaSize,
		LayoutSize availableSpace,
		SizingMode axisMode,
		Direction direction,
		int depth,
		uint generationCount )
	{
		var node = item.Node;
		var known = KnownDimensions( item, gridAreaSize, direction );
		var margins = item.MarginAxisSums( gridAreaSize.Width );

		float availableWidth, availableHeight;
		SizingMode widthMode, heightMode;

		ResolveMeasureAxis( Dimension.Width, axis, known.Width, availableSpace.Width, margins.Width, axisMode, out availableWidth, out widthMode );
		ResolveMeasureAxis( Dimension.Height, axis, known.Height, availableSpace.Height, margins.Height, axisMode, out availableHeight, out heightMode );

		// max-width / max-height cap the space content may be measured against.
		LayoutAlgorithm.ConstrainMaxSizeForMode( node, direction, FlexDirection.Row, gridAreaSize.Width, gridAreaSize.Width, ref widthMode, ref availableWidth );
		LayoutAlgorithm.ConstrainMaxSizeForMode( node, direction, FlexDirection.Column, gridAreaSize.Height, gridAreaSize.Width, ref heightMode, ref availableHeight );

		LayoutAlgorithm.CalculateLayoutInternal(
			node,
			availableWidth,
			availableHeight,
			direction,
			widthMode,
			heightMode,
			gridAreaSize.Width,
			gridAreaSize.Height,
			false,
			depth,
			generationCount );

		return node.Layout.MeasuredDimension( axis );
	}

	private static void ResolveMeasureAxis(
		Dimension thisAxis,
		Dimension measuredAxis,
		float known,
		float available,
		float margins,
		SizingMode axisMode,
		out float availableWithMargins,
		out SizingMode mode )
	{
		if ( Num.IsDefined( known ) )
		{
			availableWithMargins = known + margins;
			mode = SizingMode.StretchFit;
		}
		else if ( thisAxis == measuredAxis )
		{
			availableWithMargins = Num.Undefined;
			mode = axisMode;
		}
		else if ( Num.IsDefined( available ) )
		{
			availableWithMargins = MathF.Max( 0, available );
			mode = SizingMode.FitContent;
		}
		else
		{
			availableWithMargins = Num.Undefined;
			mode = axisMode == SizingMode.MinContent ? SizingMode.MinContent : SizingMode.MaxContent;
		}
	}

	/// <summary>
	/// The item's definite sizes given its grid area: preferred sizes, aspect ratio transfer, stretching into
	/// a definite area (when margins aren't auto), clamped by min/max.
	/// </summary>
	private static LayoutSize KnownDimensions( GridItem item, LayoutSize gridAreaSize, Direction direction )
	{
		var node = item.Node;
		var style = node.Style;
		var contentBox = style.BoxSizing == BoxSizing.ContentBox;
		var margins = item.MarginAxisSums( gridAreaSize.Width );
		var paddingAndBorderRow = LayoutAlgorithm.PaddingAndBorderForAxis( node, FlexDirection.Row, direction, gridAreaSize.Width );
		var paddingAndBorderColumn = LayoutAlgorithm.PaddingAndBorderForAxis( node, FlexDirection.Column, direction, gridAreaSize.Width );
		var aspectRatio = style.AspectRatio;

		var width = node.HasDefiniteLength( Dimension.Width, gridAreaSize.Width )
			? node.GetResolvedDimension( direction, Dimension.Width, gridAreaSize.Width, gridAreaSize.Width )
			: Num.Undefined;
		var height = node.HasDefiniteLength( Dimension.Height, gridAreaSize.Height )
			? node.GetResolvedDimension( direction, Dimension.Height, gridAreaSize.Height, gridAreaSize.Width )
			: Num.Undefined;

		var marginLeftAuto = style.ComputeMargin( PhysicalEdge.Left, direction ).IsAuto;
		var marginRightAuto = style.ComputeMargin( PhysicalEdge.Right, direction ).IsAuto;
		var marginTopAuto = style.ComputeMargin( PhysicalEdge.Top, direction ).IsAuto;
		var marginBottomAuto = style.ComputeMargin( PhysicalEdge.Bottom, direction ).IsAuto;

		if ( Num.IsUndefined( width ) && !marginLeftAuto && !marginRightAuto && item.JustifySelf == Align.Stretch && Num.IsDefined( gridAreaSize.Width ) )
		{
			width = gridAreaSize.Width - margins.Width;
		}

		ApplyAspectRatio( aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn, ref width, ref height );

		if ( Num.IsUndefined( height ) && !marginTopAuto && !marginBottomAuto && item.AlignSelf == Align.Stretch && Num.IsDefined( gridAreaSize.Height ) )
		{
			height = gridAreaSize.Height - margins.Height;
		}

		ApplyAspectRatio( aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn, ref width, ref height );

		if ( Num.IsDefined( width ) ) width = LayoutAlgorithm.BoundAxisWithinMinAndMax( node, direction, FlexDirection.Row, width, gridAreaSize.Width, gridAreaSize.Width );
		if ( Num.IsDefined( height ) ) height = LayoutAlgorithm.BoundAxisWithinMinAndMax( node, direction, FlexDirection.Column, height, gridAreaSize.Height, gridAreaSize.Width );

		return new LayoutSize( width, height );
	}

	// -----------------------------------------------------------------------------------------------
	// Alignment (§10)
	// -----------------------------------------------------------------------------------------------

	/// <summary>Positions tracks within the container, distributing free space per justify/align-content.</summary>
	private static void AlignTracks(
		float contentBoxSize,
		float paddingStart,
		float paddingEnd,
		float borderStart,
		float borderEnd,
		List<GridTrack> tracks,
		Justify alignment,
		bool reversed )
	{
		float usedSize = 0;
		foreach ( var track in tracks )
		{
			usedSize += track.BaseSize;
		}
		var freeSpace = contentBoxSize - usedSize;
		var origin = paddingStart + borderStart;

		int numTracks = 0;
		for ( int i = 1; i < tracks.Count; i += 2 )
		{
			if ( !tracks[i].IsCollapsed )
			{
				numTracks++;
			}
		}

		// Fallback alignment (css-align-3 §5.1)
		var keyword = alignment;
		if ( numTracks <= 1 || freeSpace <= 0.0f )
		{
			keyword = keyword switch
			{
				Justify.Stretch or Justify.SpaceBetween => Justify.FlexStart,
				Justify.SpaceAround or Justify.SpaceEvenly => Justify.Center,
				_ => keyword,
			};
		}

		if ( reversed )
		{
			// In a right-to-left axis "start" (and stretch's leftover space) sits at the right edge.
			keyword = keyword switch
			{
				Justify.FlexStart or Justify.Stretch => Justify.FlexEnd,
				Justify.FlexEnd => Justify.FlexStart,
				_ => keyword,
			};
		}

		var emptyGridOffset = numTracks == 0 ? AlignmentOffset( freeSpace, numTracks, keyword, true ) : 0.0f;
		var totalOffset = origin + emptyGridOffset;
		var seenTrack = false;

		void Position( int i, GridTrack track )
		{
			var isGutter = i % 2 == 0;
			var isTrack = !isGutter && !track.IsCollapsed;
			var isFirst = isTrack && !seenTrack;
			var offset = isTrack ? AlignmentOffset( freeSpace, numTracks, keyword, isFirst ) : 0.0f;
			track.Offset = totalOffset + offset;
			totalOffset = totalOffset + offset + track.BaseSize;
			if ( isTrack )
			{
				seenTrack = true;
			}
		}

		if ( reversed )
		{
			for ( int i = 0; i < tracks.Count; i++ )
			{
				Position( i, tracks[tracks.Count - 1 - i] );
			}
		}
		else
		{
			for ( int i = 0; i < tracks.Count; i++ )
			{
				Position( i, tracks[i] );
			}
		}
	}

	private static float AlignmentOffset( float freeSpace, int numItems, Justify alignment, bool isFirst )
	{
		if ( isFirst )
		{
			return alignment switch
			{
				Justify.FlexEnd => freeSpace,
				Justify.Center => freeSpace / 2.0f,
				Justify.SpaceAround => freeSpace >= 0.0f ? (freeSpace / numItems) / 2.0f : freeSpace / 2.0f,
				Justify.SpaceEvenly => freeSpace >= 0.0f ? freeSpace / (numItems + 1) : freeSpace / 2.0f,
				_ => 0.0f,
			};
		}

		freeSpace = MathF.Max( freeSpace, 0.0f );
		return alignment switch
		{
			Justify.SpaceBetween => freeSpace / (numItems - 1),
			Justify.SpaceAround => freeSpace / numItems,
			Justify.SpaceEvenly => freeSpace / (numItems + 1),
			_ => 0.0f,
		};
	}

	/// <summary>
	/// Sizes an item for its grid area (stretch / aspect ratio / min-max), lays it out and positions it
	/// according to justify-self / align-self, auto margins and (for absolute items) insets.
	/// </summary>
	private static void AlignAndPositionItem(
		LayoutNode container,
		LayoutNode node,
		float areaLeft,
		float areaTop,
		float areaRight,
		float areaBottom,
		Align containerJustifyItems,
		Align containerAlignItems,
		float baselineShim,
		Direction direction,
		float containerWidth,
		int depth,
		uint generationCount )
	{
		var style = node.Style;
		var isRtl = direction == Direction.RTL;
		var areaWidth = areaRight - areaLeft;
		var areaHeight = areaBottom - areaTop;
		var position = style.PositionType;
		var isAbsolute = position == PositionType.Absolute;
		var contentBox = style.BoxSizing == BoxSizing.ContentBox;
		var aspectRatio = style.AspectRatio;

		var paddingAndBorderRow = LayoutAlgorithm.PaddingAndBorderForAxis( node, FlexDirection.Row, direction, areaWidth );
		var paddingAndBorderColumn = LayoutAlgorithm.PaddingAndBorderForAxis( node, FlexDirection.Column, direction, areaWidth );

		// Insets (absolute and relative items)
		var left = style.ComputePosition( PhysicalEdge.Left, direction );
		var right = style.ComputePosition( PhysicalEdge.Right, direction );
		var top = style.ComputePosition( PhysicalEdge.Top, direction );
		var bottom = style.ComputePosition( PhysicalEdge.Bottom, direction );
		var insetLeft = left.IsAuto ? Num.Undefined : left.Resolve( areaWidth );
		var insetRight = right.IsAuto ? Num.Undefined : right.Resolve( areaWidth );
		var insetTop = top.IsAuto ? Num.Undefined : top.Resolve( areaHeight );
		var insetBottom = bottom.IsAuto ? Num.Undefined : bottom.Resolve( areaHeight );

		// Margins (auto = undefined)
		var marginLeftStyle = style.ComputeMargin( PhysicalEdge.Left, direction );
		var marginRightStyle = style.ComputeMargin( PhysicalEdge.Right, direction );
		var marginTopStyle = style.ComputeMargin( PhysicalEdge.Top, direction );
		var marginBottomStyle = style.ComputeMargin( PhysicalEdge.Bottom, direction );
		var marginLeft = marginLeftStyle.IsAuto ? Num.Undefined : Num.UnwrapOrDefault( marginLeftStyle.Resolve( areaWidth ), 0 );
		var marginRight = marginRightStyle.IsAuto ? Num.Undefined : Num.UnwrapOrDefault( marginRightStyle.Resolve( areaWidth ), 0 );
		var marginTop = marginTopStyle.IsAuto ? Num.Undefined : Num.UnwrapOrDefault( marginTopStyle.Resolve( areaWidth ), 0 );
		var marginBottom = marginBottomStyle.IsAuto ? Num.Undefined : Num.UnwrapOrDefault( marginBottomStyle.Resolve( areaWidth ), 0 );

		var inherentWidth = node.HasDefiniteLength( Dimension.Width, areaWidth ) ? node.GetResolvedDimension( direction, Dimension.Width, areaWidth, areaWidth ) : Num.Undefined;
		var inherentHeight = node.HasDefiniteLength( Dimension.Height, areaHeight ) ? node.GetResolvedDimension( direction, Dimension.Height, areaHeight, areaWidth ) : Num.Undefined;
		ApplyAspectRatio( aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn, ref inherentWidth, ref inherentHeight );

		// Default alignment: stretch unless the item has a definite size (or aspect ratio, vertically).
		var justifySelf = style.JustifySelf != Align.Auto
			? style.JustifySelf
			: containerJustifyItems != Align.Auto
				? containerJustifyItems
				: Num.IsDefined( inherentWidth ) ? Align.FlexStart : Align.Stretch;
		var alignSelf = style.AlignSelf != Align.Auto
			? style.AlignSelf
			: containerAlignItems != Align.Auto
				? containerAlignItems
				: Num.IsDefined( inherentHeight ) || Num.IsDefined( aspectRatio )
					? Align.FlexStart
					: Align.Stretch;
		if ( isAbsolute )
		{
			if ( style.JustifySelf == Align.Auto && containerJustifyItems == Align.Auto )
			{
				justifySelf = Align.FlexStart;
			}

			if ( style.AlignSelf == Align.Auto && containerAlignItems == Align.Auto )
			{
				alignSelf = Align.FlexStart;
			}
		}

		var areaMinusMarginsWidth = areaWidth - Num.UnwrapOrDefault( marginLeft, 0 ) - Num.UnwrapOrDefault( marginRight, 0 );
		var areaMinusMarginsHeight = areaHeight - Num.UnwrapOrDefault( marginTop, 0 ) - Num.UnwrapOrDefault( marginBottom, 0 ) - baselineShim;

		var width = inherentWidth;
		if ( Num.IsUndefined( width ) )
		{
			if ( isAbsolute && Num.IsDefined( insetLeft ) && Num.IsDefined( insetRight ) )
			{
				width = MathF.Max( areaMinusMarginsWidth - insetLeft - insetRight, 0.0f );
			}
			else if ( Num.IsDefined( marginLeft )
				&& Num.IsDefined( marginRight )
				&& justifySelf == Align.Stretch
				&& !isAbsolute )
			{
				width = areaMinusMarginsWidth;
			}
		}

		var height = inherentHeight;
		ApplyAspectRatio( aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn, ref width, ref height );

		if ( Num.IsUndefined( height ) )
		{
			if ( isAbsolute && Num.IsDefined( insetTop ) && Num.IsDefined( insetBottom ) )
			{
				height = MathF.Max( areaMinusMarginsHeight - insetTop - insetBottom, 0.0f );
			}
			else if ( Num.IsDefined( marginTop )
				&& Num.IsDefined( marginBottom )
				&& alignSelf == Align.Stretch
				&& !isAbsolute )
			{
				height = areaMinusMarginsHeight;
			}
		}

		ApplyAspectRatio( aspectRatio, contentBox, paddingAndBorderRow, paddingAndBorderColumn, ref width, ref height );

		// Clamp by min/max (min size floors at padding + border).
		if ( Num.IsDefined( width ) )
		{
			width = LayoutAlgorithm.BoundAxis( node, FlexDirection.Row, direction, width, areaWidth, areaWidth );
		}

		if ( Num.IsDefined( height ) )
		{
			height = LayoutAlgorithm.BoundAxis( node, FlexDirection.Column, direction, height, areaHeight, areaWidth );
		}

		var xMargins = Num.UnwrapOrDefault( marginLeft, 0 ) + Num.UnwrapOrDefault( marginRight, 0 );
		var yMargins = Num.UnwrapOrDefault( marginTop, 0 ) + Num.UnwrapOrDefault( marginBottom, 0 );

		// Lay the item out: definite sizes stretch-fit, the rest fit into the area.
		var availableWidth = Num.IsDefined( width ) ? width + xMargins : MathF.Max( 0, areaMinusMarginsWidth ) + xMargins;
		var availableHeight = Num.IsDefined( height ) ? height + yMargins : MathF.Max( 0, areaMinusMarginsHeight ) + yMargins;
		var widthMode = Num.IsDefined( width ) ? SizingMode.StretchFit : SizingMode.FitContent;
		var heightMode = Num.IsDefined( height ) ? SizingMode.StretchFit : SizingMode.FitContent;
		LayoutAlgorithm.ConstrainMaxSizeForMode( node, direction, FlexDirection.Row, areaWidth, areaWidth, ref widthMode, ref availableWidth );
		LayoutAlgorithm.ConstrainMaxSizeForMode( node, direction, FlexDirection.Column, areaHeight, areaWidth, ref heightMode, ref availableHeight );

		if ( isAbsolute && (Num.IsUndefined( width ) || Num.IsUndefined( height )) )
		{
			// Absolute items shrink-to-fit; measure first so min/max can be applied to the result.
			LayoutAlgorithm.CalculateLayoutInternal( node, availableWidth, availableHeight, direction, widthMode, heightMode, areaWidth, areaHeight, false, depth, generationCount );
			if ( Num.IsUndefined( width ) )
			{
				width = node.Layout.MeasuredDimension( Dimension.Width );
			}

			if ( Num.IsUndefined( height ) )
			{
				height = node.Layout.MeasuredDimension( Dimension.Height );
			}

			availableWidth = width + xMargins;
			availableHeight = height + yMargins;
			widthMode = heightMode = SizingMode.StretchFit;
		}

		LayoutAlgorithm.CalculateLayoutInternal( node, availableWidth, availableHeight, direction, widthMode, heightMode, areaWidth, areaHeight, true, depth, generationCount );

		var finalWidth = node.Layout.MeasuredDimension( Dimension.Width );
		var finalHeight = node.Layout.MeasuredDimension( Dimension.Height );

		var (x, resolvedMarginLeft, resolvedMarginRight) = AlignItemWithinArea(
			areaLeft,
			areaRight,
			justifySelf,
			finalWidth,
			position,
			insetLeft,
			insetRight,
			marginLeft,
			marginRight,
			0.0f,
			isRtl );
		var (y, resolvedMarginTop, resolvedMarginBottom) = AlignItemWithinArea(
			areaTop,
			areaBottom,
			alignSelf,
			finalHeight,
			position,
			insetTop,
			insetBottom,
			marginTop,
			marginBottom,
			baselineShim,
			false );

		node.Layout.SetPosition( PhysicalEdge.Left, x );
		node.Layout.SetPosition( PhysicalEdge.Top, y );
		node.Layout.SetPosition( PhysicalEdge.Right, containerWidth - x - finalWidth );
		node.Layout.SetPosition( PhysicalEdge.Bottom, 0 );
		node.Layout.SetMargin( PhysicalEdge.Left, resolvedMarginLeft );
		node.Layout.SetMargin( PhysicalEdge.Right, resolvedMarginRight );
		node.Layout.SetMargin( PhysicalEdge.Top, resolvedMarginTop );
		node.Layout.SetMargin( PhysicalEdge.Bottom, resolvedMarginBottom );
	}

	private static (float Start, float MarginStart, float MarginEnd) AlignItemWithinArea(
		float areaStart,
		float areaEnd,
		Align alignment,
		float size,
		PositionType position,
		float insetStart,
		float insetEnd,
		float marginStart,
		float marginEnd,
		float baselineShim,
		bool reversed )
	{
		var nonAutoMarginStart = Num.UnwrapOrDefault( marginStart, 0 ) + baselineShim;
		var nonAutoMarginEnd = Num.UnwrapOrDefault( marginEnd, 0 );
		var areaSize = MathF.Max( areaEnd - areaStart, 0.0f );
		var freeSpace = MathF.Max( areaSize - size - nonAutoMarginStart - nonAutoMarginEnd, 0.0f );
		var autoMarginCount = (Num.IsUndefined( marginStart ) ? 1 : 0) + (Num.IsUndefined( marginEnd ) ? 1 : 0);
		var autoMarginSize = autoMarginCount > 0 ? freeSpace / autoMarginCount : 0.0f;
		var resolvedMarginStart = Num.UnwrapOrDefault( marginStart, autoMarginSize ) + baselineShim;
		var resolvedMarginEnd = Num.UnwrapOrDefault( marginEnd, autoMarginSize );

		float alignmentOffset;
		switch ( alignment )
		{
			case Align.FlexEnd:
				alignmentOffset = reversed ? resolvedMarginStart : areaSize - size - resolvedMarginEnd;
				break;
			case Align.Center:
				alignmentOffset = (areaSize - size + resolvedMarginStart - resolvedMarginEnd) / 2.0f;
				break;
			default:
				alignmentOffset = reversed ? areaSize - size - resolvedMarginEnd : resolvedMarginStart;
				break;
		}

		float offsetWithinArea;
		if ( position == PositionType.Absolute )
		{
			if ( Num.IsDefined( insetStart ) && Num.IsDefined( insetEnd ) ) offsetWithinArea = reversed ? areaSize - insetEnd - size - nonAutoMarginEnd : insetStart + nonAutoMarginStart;
			else if ( Num.IsDefined( insetStart ) ) offsetWithinArea = insetStart + nonAutoMarginStart;
			else if ( Num.IsDefined( insetEnd ) ) offsetWithinArea = areaSize - insetEnd - size - nonAutoMarginEnd;
			else offsetWithinArea = alignmentOffset;
		}
		else
		{
			offsetWithinArea = alignmentOffset;
		}

		var start = areaStart + offsetWithinArea;
		if ( position == PositionType.Relative )
		{
			var relative = reversed
				? (Num.IsDefined( insetEnd ) ? -insetEnd : Num.IsDefined( insetStart ) ? insetStart : 0)
				: (Num.IsDefined( insetStart ) ? insetStart : Num.IsDefined( insetEnd ) ? -insetEnd : 0);
			start += relative;
		}

		return (start, resolvedMarginStart, resolvedMarginEnd);
	}
}
