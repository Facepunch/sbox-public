namespace Sandbox.Layout;

/// <summary>
/// Explicit grid sizing (css-grid-1 §7.2, including auto-fill / auto-fit), named line resolution (§8.3) and
/// the auto-placement algorithm (§8.5).
/// </summary>
internal static partial class GridLayout
{
	internal const int MaxGridTracks = 10_000;

	private enum AutoRepeatStrategy
	{
		MaxRepetitionsThatDoNotOverflow,
		MinRepetitionsThatDoOverflow,
	}

	/// <summary>
	/// Number of tracks in the explicit grid for one axis and how many times any auto-repeat repeats.
	/// </summary>
	internal static (int AutoRepetitions, int TrackCount) ComputeExplicitGridSize(
		TrackList template,
		StyleLength gap,
		float containerInnerSize,
		bool containerSizeIsDefinite )
	{
		if ( template.IsNone )
		{
			return (0, 0);
		}

		var components = template.ComponentItems;
		int nonAutoRepeatingTrackCount = 0;
		int autoRepetitionCount = 0;
		var allFixed = true;
		TrackTemplateComponent autoRepeat = null;
		int autoRepeatInsertionPoint = 0;

		foreach ( var component in components )
		{
			if ( component.TrackItems.Length == 0 )
			{
				return (0, 0);
			}

			if ( component.IsAutoRepeat )
			{
				autoRepetitionCount++;
				autoRepeat ??= component;
			}
			else
			{
				var count = (int)Math.Min( (long)component.TrackItems.Length * component.RepeatCount, MaxGridTracks );
				nonAutoRepeatingTrackCount = Math.Min( nonAutoRepeatingTrackCount + count, MaxGridTracks );
				if ( autoRepeat is null )
				{
					autoRepeatInsertionPoint = Math.Min( autoRepeatInsertionPoint + count, MaxGridTracks );
				}
			}

			foreach ( var track in component.TrackItems )
			{
				if ( !HasFixedComponent( track ) )
				{
					allFixed = false;
				}
			}
		}

		nonAutoRepeatingTrackCount = Math.Min( nonAutoRepeatingTrackCount, MaxGridTracks );

		var templateIsValid = autoRepetitionCount == 0 || (autoRepetitionCount == 1 && allFixed);
		if ( !templateIsValid )
		{
			return (0, 0);
		}

		if ( autoRepetitionCount == 0 )
		{
			return (0, nonAutoRepeatingTrackCount);
		}

		var repetitionTrackCount = autoRepeat.TrackItems.Length;

		int numRepetitions;
		if ( Num.IsUndefined( containerInnerSize ) )
		{
			numRepetitions = 1;
		}
		else
		{
			float TrackDefiniteValue( TrackSizingFunction function )
			{
				var max = function.IsFitContent ? Num.Undefined : function.Max.ResolveFixed( containerInnerSize );
				var min = function.Min.ResolveFixed( containerInnerSize );
				if ( Num.IsDefined( max ) )
				{
					return Num.IsDefined( min ) ? MathF.Max( max, min ) : max;
				}

				return Num.UnwrapOrDefault( min, 0 );
			}

			float nonRepeatingUsedSpace = 0;
			foreach ( var component in components )
			{
				if ( component.IsAutoRepeat )
				{
					continue;
				}

				float sum = 0;
				foreach ( var track in component.TrackItems )
				{
					sum += TrackDefiniteValue( track );
				}

				nonRepeatingUsedSpace += sum * component.RepeatCount;
			}

			var gapSize = Num.UnwrapOrDefault( gap.Resolve( containerInnerSize ), 0 );
			float perRepetitionTrackUsedSpace = 0;
			foreach ( var track in autoRepeat.TrackItems )
			{
				perRepetitionTrackUsedSpace += TrackDefiniteValue( track );
			}

			var firstRepetitionAndNonRepeatingUsedSpace = nonRepeatingUsedSpace + perRepetitionTrackUsedSpace
				+ Math.Max( nonAutoRepeatingTrackCount + repetitionTrackCount - 1, 0 ) * gapSize;

			if ( firstRepetitionAndNonRepeatingUsedSpace > containerInnerSize )
			{
				numRepetitions = 1;
			}
			else
			{
				var perRepetitionGapUsedSpace = repetitionTrackCount * gapSize;
				var perRepetitionUsedSpace = perRepetitionTrackUsedSpace + perRepetitionGapUsedSpace;
				var numRepetitionsThatFit = perRepetitionUsedSpace > 0 ? (containerInnerSize - firstRepetitionAndNonRepeatingUsedSpace) / perRepetitionUsedSpace : 0;
				var strategy = containerSizeIsDefinite ? AutoRepeatStrategy.MaxRepetitionsThatDoNotOverflow : AutoRepeatStrategy.MinRepetitionsThatDoOverflow;
				numRepetitions = strategy == AutoRepeatStrategy.MaxRepetitionsThatDoNotOverflow
					? (int)MathF.Floor( numRepetitionsThatFit ) + 1
					: (int)MathF.Ceiling( numRepetitionsThatFit ) + 1;
			}
		}

		var remainingTracks = MaxGridTracks - autoRepeatInsertionPoint;
		if ( remainingTracks <= 0 )
		{
			numRepetitions = 0;
		}
		else
		{
			var maxRepetitions = (remainingTracks + repetitionTrackCount - 1) / repetitionTrackCount;
			numRepetitions = Math.Clamp( numRepetitions, 1, maxRepetitions );
		}

		var trackCount = Math.Min( nonAutoRepeatingTrackCount + repetitionTrackCount * numRepetitions, MaxGridTracks );
		return (numRepetitions, trackCount);
	}

	private static bool HasFixedComponent( TrackSizingFunction function )
	{
		return function.Min.IsFixed
			|| function.Max.IsFixed
			|| (function.IsFitContent && !function.FitContentLimitLength().IsUndefined);
	}

	private static StyleLength FitContentLimitLength( this TrackSizingFunction function ) => function.FitContentLimit;

	/// <summary>
	/// Fills the interleaved track vector [gutter, track, gutter, ..., track, gutter] for one axis.
	/// </summary>
	private static void InitializeGridTracks(
		List<GridTrack> tracks,
		TrackCounts counts,
		TrackList template,
		TrackSizingFunction[] autoTracks,
		StyleLength gap,
		int autoRepetitionCount,
		CellOccupancyMatrix matrix,
		Dimension axis )
	{
		GridTrack.ReturnAll( tracks );
		tracks.Add( GridTrack.Gutter( gap ) );

		var autoTrackCount = autoTracks.Length;

		if ( counts.NegativeImplicit > 0 )
		{
			if ( autoTrackCount == 0 )
			{
				for ( int i = 0; i < counts.NegativeImplicit; i++ )
				{
					tracks.Add( GridTrack.Create( TrackSizingFunction.Auto ) );
					tracks.Add( GridTrack.Gutter( gap ) );
				}
			}
			else
			{
				// Implicit tracks before the explicit grid cycle backwards through the auto track list.
				var offset = autoTrackCount - (counts.NegativeImplicit % autoTrackCount);
				for ( int i = 0; i < counts.NegativeImplicit; i++ )
				{
					tracks.Add( GridTrack.Create( autoTracks[(offset + i) % autoTrackCount] ) );
					tracks.Add( GridTrack.Gutter( gap ) );
				}
			}
		}

		var currentTrackIndex = counts.NegativeImplicit;
		var explicitTrackLimit = counts.NegativeImplicit + counts.Explicit;

		if ( counts.Explicit > 0 && !template.IsNone )
		{
			foreach ( var component in template.ComponentItems )
			{
				if ( !component.IsAutoRepeat )
				{
					var repeatedCount = (int)Math.Min( (long)component.TrackItems.Length * component.RepeatCount, explicitTrackLimit - currentTrackIndex );
					for ( int i = 0; i < repeatedCount; i++ )
					{
						tracks.Add( GridTrack.Create( component.TrackItems[i % component.TrackItems.Length] ) );
						tracks.Add( GridTrack.Gutter( gap ) );
						currentTrackIndex++;
					}
				}
				else
				{
					var autoRepeatedTrackCount = (int)Math.Min( (long)component.TrackItems.Length * autoRepetitionCount, explicitTrackLimit - currentTrackIndex );
					for ( int i = 0; i < autoRepeatedTrackCount; i++ )
					{
						var track = GridTrack.Create( component.TrackItems[i % component.TrackItems.Length] );
						var gutter = GridTrack.Gutter( gap );
						if ( component.Repetition == RepetitionKind.AutoFit && !matrix.TrackIsOccupied( axis, currentTrackIndex ) )
						{
							track.Collapse();
							gutter.Collapse();
						}
						tracks.Add( track );
						tracks.Add( gutter );
						currentTrackIndex++;
					}

					// If the auto-fit repeat ends the grid, collapse the trailing gutter before the last collapsed run.
					var isLast = currentTrackIndex == counts.Length;
					if ( component.Repetition == RepetitionKind.AutoFit && isLast )
					{
						for ( int i = tracks.Count - 1; i >= 0; i-- )
						{
							var previous = tracks[i];
							if ( previous.Kind == GridTrackKind.Track && !previous.IsCollapsed )
							{
								break;
							}

							previous.Collapse();
						}
					}
				}
			}
		}

		var gridAreaTracks = (counts.NegativeImplicit + counts.Explicit) - currentTrackIndex;
		var implicitCount = counts.PositiveImplicit + gridAreaTracks;
		for ( int i = 0; i < implicitCount; i++ )
		{
			tracks.Add( GridTrack.Create( autoTrackCount == 0 ? TrackSizingFunction.Auto : autoTracks[i % autoTrackCount] ) );
			tracks.Add( GridTrack.Gutter( gap ) );
		}

		tracks[0].Collapse();
		tracks[^1].Collapse();
	}

	// -----------------------------------------------------------------------------------------------
	// Named lines
	// -----------------------------------------------------------------------------------------------

	/// <summary>
	/// Line names of one axis: name to the (1-based, explicit-grid) lines that carry it, in order. A template
	/// without line names builds no table; named placements then resolve against the implicit grid only.
	/// </summary>
	private readonly struct NamedLines
	{
		private readonly Dictionary<string, List<int>> _lines;
		public int ExplicitTrackCount { get; }

		public NamedLines( TrackList template, int autoRepetitions, int explicitTrackCount )
		{
			ExplicitTrackCount = explicitTrackCount;
			_lines = null;
			if ( template.IsNone || !template.HasLineNames )
			{
				return;
			}

			var lines = new Dictionary<string, List<int>>( StringComparer.Ordinal );
			_lines = lines;

			int line = 1;
			foreach ( var component in template.ComponentItems )
			{
				foreach ( var name in component.LeadingNameItems )
				{
					Add( lines, name, line );
				}

				var repeats = component.IsAutoRepeat ? autoRepetitions : component.RepeatCount;
				for ( int repetition = 0; repetition < repeats; repetition++ )
				{
					foreach ( var name in component.RepeatLeadingNameItems )
					{
						Add( lines, name, line );
					}

					for ( int trackIndex = 0; trackIndex < component.TrackItems.Length; trackIndex++ )
					{
						line++;
						foreach ( var name in component.TrailingNameItems[trackIndex] )
						{
							Add( lines, name, line );
						}

						if ( line > MaxGridTracks )
						{
							return;
						}
					}
				}
			}
		}

		private static void Add( Dictionary<string, List<int>> lines, string name, int line )
		{
			if ( !lines.TryGetValue( name, out var list ) )
			{
				lines[name] = list = new List<int>();
			}

			if ( list.Count == 0 || list[^1] != line )
			{
				list.Add( line );
			}
		}

		private List<int> Lookup( string name )
		{
			List<int> lines = null;
			_lines?.TryGetValue( name, out lines );
			return lines;
		}

		/// <summary>
		/// The Nth line with a name (negative N counts from the end). Lines beyond those that exist are
		/// taken from the implicit grid, which is assumed to carry every name (css-grid-1 §8.3).
		/// </summary>
		public int FindLine( string name, int n )
		{
			var lines = Lookup( name );
			var count = lines?.Count ?? 0;

			if ( n > 0 )
			{
				if ( n <= count )
				{
					return lines[n - 1];
				}

				return ExplicitTrackCount + 1 + (n - count);
			}

			var index = -n;
			if ( index <= count )
			{
				return lines[count - index];
			}

			return -(ExplicitTrackCount + 1 + (index - count));
		}

		/// <summary>
		/// Resolve a span's named parts into numeric CSS lines; <c>span name</c> becomes a numeric span once
		/// the opposite edge is known, otherwise it stays a plain span of 1.
		/// </summary>
		public (GridPlacement Start, GridPlacement End) Resolve( GridPlacement start, GridPlacement end )
		{
			if ( start.Name is null && end.Name is null )
			{
				return (start, end);
			}

			if ( start.Kind == GridPlacementKind.NamedLine )
			{
				start = GridPlacement.Line( FindLine( start.Name, start.Value ) );
			}

			if ( end.Kind == GridPlacementKind.NamedLine )
			{
				end = GridPlacement.Line( FindLine( end.Name, end.Value ) );
			}

			if ( start.Kind == GridPlacementKind.NamedSpan )
			{
				if ( end.Kind == GridPlacementKind.Line )
				{
					var endLine = end.Value;
					var startLine = FindLineBefore( start.Name, endLine, start.Value );
					start = GridPlacement.Line( startLine );
				}
				else
				{
					start = GridPlacement.Span( 1 );
				}
			}

			if ( end.Kind == GridPlacementKind.NamedSpan )
			{
				if ( start.Kind == GridPlacementKind.Line )
				{
					var startLine = start.Value;
					var endLine = FindLineAfter( end.Name, startLine, end.Value );
					end = GridPlacement.Line( endLine );
				}
				else
				{
					end = GridPlacement.Span( 1 );
				}
			}

			return (start, end);
		}

		private int FindLineAfter( string name, int line, int count )
		{
			var lines = Lookup( name );
			int remaining = count;
			if ( lines is not null )
			{
				foreach ( var namedLine in lines )
				{
					if ( namedLine > line && --remaining == 0 )
					{
						return namedLine;
					}
				}
			}
			return Math.Max( line, ExplicitTrackCount + 1 ) + remaining;
		}

		private int FindLineBefore( string name, int line, int count )
		{
			var lines = Lookup( name );
			int remaining = count;
			if ( lines is not null )
			{
				for ( int i = lines.Count - 1; i >= 0; i-- )
				{
					if ( lines[i] < line && --remaining == 0 )
					{
						return lines[i];
					}
				}
			}
			return Math.Min( line, 1 ) - remaining;
		}
	}

	// -----------------------------------------------------------------------------------------------
	// Origin-zero placements
	// -----------------------------------------------------------------------------------------------

	private enum OriginZeroKind : byte
	{
		Auto,
		Line,
		Span,
	}

	private readonly record struct OriginZeroPlacement( OriginZeroKind Kind, int Value )
	{
		public static OriginZeroPlacement Auto => new( OriginZeroKind.Auto, 0 );
		public bool IsLine => Kind == OriginZeroKind.Line;
	}

	private readonly record struct OriginZeroLine( OriginZeroPlacement Start, OriginZeroPlacement End )
	{
		public bool IsDefinite => Start.IsLine || End.IsLine;

		public int IndefiniteSpan
		{
			get
			{
				if ( Start.Kind == OriginZeroKind.Span )
				{
					return Start.Value;
				}

				if ( End.Kind == OriginZeroKind.Span )
				{
					return End.Value;
				}

				return 1;
			}
		}

		public GridSpan ResolveDefiniteGridLines()
		{
			if ( Start.IsLine && End.IsLine )
			{
				if ( Start.Value == End.Value )
				{
					return new GridSpan( Start.Value, Start.Value + 1 );
				}

				return new GridSpan( Math.Min( Start.Value, End.Value ), Math.Max( Start.Value, End.Value ) );
			}

			if ( Start.IsLine && End.Kind == OriginZeroKind.Span )
			{
				return new GridSpan( Start.Value, Start.Value + End.Value );
			}

			if ( Start.IsLine )
			{
				return new GridSpan( Start.Value, Start.Value + 1 );
			}

			if ( Start.Kind == OriginZeroKind.Span && End.IsLine )
			{
				return new GridSpan( End.Value - Start.Value, End.Value );
			}

			return new GridSpan( End.Value - 1, End.Value );
		}

		/// <summary>For absolutely positioned items: which lines are pinned, null for auto.</summary>
		public (int? Start, int? End) ResolveAbsolutelyPositionedGridTracks()
		{
			if ( Start.IsLine && End.IsLine )
			{
				if ( Start.Value == End.Value )
				{
					return (Start.Value, Start.Value + 1);
				}

				return (Math.Min( Start.Value, End.Value ), Math.Max( Start.Value, End.Value ));
			}

			if ( Start.IsLine && End.Kind == OriginZeroKind.Span )
			{
				return (Start.Value, Start.Value + End.Value);
			}

			if ( Start.IsLine )
			{
				return (Start.Value, null);
			}

			if ( Start.Kind == OriginZeroKind.Span && End.IsLine )
			{
				return (End.Value - Start.Value, End.Value);
			}

			if ( End.IsLine )
			{
				return (null, End.Value);
			}

			return (null, null);
		}
	}

	private static OriginZeroPlacement ToOriginZero( GridPlacement placement, int explicitTrackCount )
	{
		switch ( placement.Kind )
		{
			case GridPlacementKind.Span:
				return new OriginZeroPlacement( OriginZeroKind.Span, Math.Clamp( placement.Value, 1, MaxGridTracks ) );
			case GridPlacementKind.Line:
				{
					var line = placement.Value;
					if ( line == 0 )
					{
						return OriginZeroPlacement.Auto;
					}

					var originZeroLine = line > 0 ? line - 1 : line + explicitTrackCount + 1;
					return new OriginZeroPlacement(
						OriginZeroKind.Line,
						Math.Clamp( originZeroLine, -MaxGridTracks, MaxGridTracks ) );
				}
			default:
				return OriginZeroPlacement.Auto;
		}
	}

	private static OriginZeroLine ToOriginZero(
		GridPlacement start,
		GridPlacement end,
		NamedLines names,
		int explicitTrackCount )
	{
		(start, end) = names.Resolve( start, end );
		return new OriginZeroLine( ToOriginZero( start, explicitTrackCount ), ToOriginZero( end, explicitTrackCount ) );
	}

	// -----------------------------------------------------------------------------------------------
	// Implicit grid size estimate (§8.5 preamble)
	// -----------------------------------------------------------------------------------------------

	private static (TrackCounts Columns, TrackCounts Rows) ComputeGridSizeEstimate(
		int explicitColumnCount,
		int explicitRowCount,
		List<LayoutNode> children,
		NamedLines columnNames,
		NamedLines rowNames )
	{
		int columnMin = 0;
		int columnMax = 0;
		int columnMaxSpan = 0;
		int rowMin = 0;
		int rowMax = 0;
		int rowMaxSpan = 0;

		foreach ( var child in children )
		{
			if ( child.Style.Display == Display.None || child.Style.IsOutOfFlow )
			{
				continue;
			}

			var column = ToOriginZero(
				child.Style.GridColumnStart,
				child.Style.GridColumnEnd,
				columnNames,
				explicitColumnCount );
			var row = ToOriginZero( child.Style.GridRowStart, child.Style.GridRowEnd, rowNames, explicitRowCount );

			var (itemColumnMin, itemColumnMax, itemColumnSpan) = MinLineMaxLineSpan( column );
			var (itemRowMin, itemRowMax, itemRowSpan) = MinLineMaxLineSpan( row );

			columnMin = Math.Min( columnMin, itemColumnMin );
			columnMax = Math.Max( columnMax, itemColumnMax );
			columnMaxSpan = Math.Max( columnMaxSpan, itemColumnSpan );
			rowMin = Math.Min( rowMin, itemRowMin );
			rowMax = Math.Max( rowMax, itemRowMax );
			rowMaxSpan = Math.Max( rowMaxSpan, itemRowSpan );
		}

		var negativeColumns = columnMin < 0 ? -columnMin : 0;
		var positiveColumns = columnMax > explicitColumnCount ? columnMax - explicitColumnCount : 0;
		var negativeRows = rowMin < 0 ? -rowMin : 0;
		var positiveRows = rowMax > explicitRowCount ? rowMax - explicitRowCount : 0;

		if ( negativeColumns + explicitColumnCount + positiveColumns < columnMaxSpan )
		{
			positiveColumns = columnMaxSpan - explicitColumnCount - negativeColumns;
		}

		if ( negativeRows + explicitRowCount + positiveRows < rowMaxSpan )
		{
			positiveRows = rowMaxSpan - explicitRowCount - negativeRows;
		}

		return (
			new TrackCounts
			{
				NegativeImplicit = negativeColumns,
				Explicit = explicitColumnCount,
				PositiveImplicit = positiveColumns,
			},
			new TrackCounts
			{
				NegativeImplicit = negativeRows,
				Explicit = explicitRowCount,
				PositiveImplicit = positiveRows,
			});
	}

	private static (int Min, int Max, int Span) MinLineMaxLineSpan( OriginZeroLine line )
	{
		var start = line.Start;
		var end = line.End;
		int min, max, span;

		if ( start.IsLine && end.IsLine )
		{
			min = start.Value == end.Value ? start.Value : Math.Min( start.Value, end.Value );
			max = start.Value == end.Value ? start.Value + 1 : Math.Max( start.Value, end.Value );
			span = 1;
		}
		else if ( start.IsLine )
		{
			min = start.Value;
			max = end.Kind == OriginZeroKind.Span ? start.Value + end.Value : start.Value + 1;
			span = 1;
		}
		else if ( end.IsLine )
		{
			min = start.Kind == OriginZeroKind.Span ? end.Value - start.Value : end.Value - 1;
			max = end.Value;
			span = 1;
		}
		else
		{
			min = 0;
			max = 0;
			span = line.IndefiniteSpan;
		}

		return (Math.Max( min, -MaxGridTracks ), Math.Min( max, MaxGridTracks ), span);
	}

	// -----------------------------------------------------------------------------------------------
	// Placement (§8.5)
	// -----------------------------------------------------------------------------------------------

	private static GridSpan ResolveIndefiniteGridSpan( int position, int span ) => new( position, position + span );

	private readonly record struct ItemPlacement( int Index, LayoutNode Node, OriginZeroLine Column, OriginZeroLine Row );

	private static void PlaceGridItems(
		CellOccupancyMatrix matrix,
		List<GridItem> items,
		List<LayoutNode> children,
		GridAutoFlow autoFlow,
		Align alignItems,
		Align justifyItems,
		NamedLines columnNames,
		NamedLines rowNames )
	{
		var primaryAxis = autoFlow is GridAutoFlow.Column or GridAutoFlow.ColumnDense ? Dimension.Height : Dimension.Width;
		var secondaryAxis = primaryAxis == Dimension.Width ? Dimension.Height : Dimension.Width;
		var isDense = autoFlow is GridAutoFlow.RowDense or GridAutoFlow.ColumnDense;
		var explicitColumnCount = matrix.TrackCounts( Dimension.Width ).Explicit;
		var explicitRowCount = matrix.TrackCounts( Dimension.Height ).Explicit;

		var placements = GridPool<List<ItemPlacement>>.Rent();
		placements.Clear();
		try
		{
			for ( int i = 0; i < children.Count; i++ )
			{
				var child = children[i];
				if ( child.Style.Display == Display.None || child.Style.IsOutOfFlow )
				{
					continue;
				}

				placements.Add( new ItemPlacement( i, child,
					ToOriginZero( child.Style.GridColumnStart, child.Style.GridColumnEnd, columnNames, explicitColumnCount ),
					ToOriginZero( child.Style.GridRowStart, child.Style.GridRowEnd, rowNames, explicitRowCount ) ) );
			}

			static OriginZeroLine GetPlacement( in ItemPlacement placement, Dimension axis )
			{
				return axis == Dimension.Width ? placement.Column : placement.Row;
			}

			// 1. Items with definite positions in both axes.
			foreach ( var placement in placements )
			{
				if ( !placement.Column.IsDefinite || !placement.Row.IsDefinite )
				{
					continue;
				}

				var primarySpan = GetPlacement( placement, primaryAxis ).ResolveDefiniteGridLines();
				var secondarySpan = GetPlacement( placement, secondaryAxis ).ResolveDefiniteGridLines();
				RecordGridPlacement(
					matrix,
					items,
					placement.Node,
					placement.Index,
					alignItems,
					justifyItems,
					primaryAxis,
					primarySpan,
					secondarySpan,
					CellOccupancyState.DefinitelyPlaced );
			}

			// 2. Items locked to a track in the secondary axis only.
			foreach ( var placement in placements )
			{
				if ( !GetPlacement( placement, secondaryAxis ).IsDefinite
					|| GetPlacement( placement, primaryAxis ).IsDefinite )
				{
					continue;
				}

				var primaryStartLine = matrix.TrackCounts( primaryAxis ).ImplicitStartLine;
				var secondarySpan = GetPlacement( placement, secondaryAxis ).ResolveDefiniteGridLines();
				var startingPosition = isDense
					? primaryStartLine
					: matrix.LastOfType( primaryAxis, secondarySpan.Start, CellOccupancyState.AutoPlaced ) ?? primaryStartLine;

				var primarySpanLength = GetPlacement( placement, primaryAxis ).IndefiniteSpan;
				var position = startingPosition;
				while ( true )
				{
					var primarySpan = ResolveIndefiniteGridSpan( position, primarySpanLength );
					var collision = matrix.LineAreaCollisionJump( primaryAxis, primarySpan, secondarySpan );
					if ( collision is null )
					{
						RecordGridPlacement(
							matrix,
							items,
							placement.Node,
							placement.Index,
							alignItems,
							justifyItems,
							primaryAxis,
							primarySpan,
							secondarySpan,
							CellOccupancyState.AutoPlaced );
						break;
					}
					position = collision.Value;
				}
			}

			// 3. Everything else, walking a cursor through the grid.
			var primaryGridStartLine = matrix.TrackCounts( primaryAxis ).ImplicitStartLine;
			var secondaryGridStartLine = matrix.TrackCounts( secondaryAxis ).ImplicitStartLine;
			var cursor = (Primary: primaryGridStartLine, Secondary: secondaryGridStartLine);

			foreach ( var placement in placements )
			{
				if ( GetPlacement( placement, secondaryAxis ).IsDefinite )
				{
					continue;
				}

				var (primarySpan, secondarySpan) = PlaceIndefinitelyPositionedItem(
					matrix,
					GetPlacement( placement, primaryAxis ),
					GetPlacement( placement, secondaryAxis ),
					primaryAxis,
					isDense,
					cursor );
				RecordGridPlacement(
					matrix,
					items,
					placement.Node,
					placement.Index,
					alignItems,
					justifyItems,
					primaryAxis,
					primarySpan,
					secondarySpan,
					CellOccupancyState.AutoPlaced );

				cursor = isDense ? (primaryGridStartLine, secondaryGridStartLine) : (primarySpan.End, secondarySpan.Start);
			}
		}
		finally
		{
			placements.Clear();
			GridPool<List<ItemPlacement>>.Return( placements, placements.Capacity );
		}
	}

	private static (GridSpan Primary, GridSpan Secondary) PlaceIndefinitelyPositionedItem(
		CellOccupancyMatrix matrix,
		OriginZeroLine primaryPlacement,
		OriginZeroLine secondaryPlacement,
		Dimension primaryAxis,
		bool isDense,
		(int Primary, int Secondary) cursor )
	{
		var secondaryAxis = primaryAxis == Dimension.Width ? Dimension.Height : Dimension.Width;
		var secondarySpanLength = secondaryPlacement.IndefiniteSpan;
		var primaryGridStartLine = matrix.TrackCounts( primaryAxis ).ImplicitStartLine;
		var primaryGridEndLine = matrix.TrackCounts( primaryAxis ).ImplicitEndLine;
		var secondaryGridStartLine = matrix.TrackCounts( secondaryAxis ).ImplicitStartLine;

		var (primaryIndex, secondaryIndex) = cursor;

		if ( primaryPlacement.IsDefinite )
		{
			var primarySpan = primaryPlacement.ResolveDefiniteGridLines();
			secondaryIndex = isDense
				? secondaryGridStartLine
				: (primarySpan.Start < primaryIndex ? secondaryIndex + 1 : secondaryIndex);

			while ( true )
			{
				var secondarySpan = ResolveIndefiniteGridSpan( secondaryIndex, secondarySpanLength );
				var collision = matrix.LineAreaCollisionJump( secondaryAxis, secondarySpan, primarySpan );
				if ( collision is not null )
				{
					secondaryIndex = collision.Value;
					continue;
				}
				return (primarySpan, secondarySpan);
			}
		}
		else
		{
			var primarySpanLength = primaryPlacement.IndefiniteSpan;
			var spansAllPrimaryTracks = primarySpanLength >= matrix.TrackCounts( primaryAxis ).Length;

			while ( true )
			{
				var primarySpan = ResolveIndefiniteGridSpan( primaryIndex, primarySpanLength );
				var secondarySpan = ResolveIndefiniteGridSpan( secondaryIndex, secondarySpanLength );

				var primaryOutOfBounds = primarySpan.End > primaryGridEndLine;
				if ( primaryOutOfBounds )
				{
					if ( primaryIndex == primaryGridStartLine )
					{
						return (primarySpan, secondarySpan);
					}

					secondaryIndex++;
					primaryIndex = primaryGridStartLine;
					continue;
				}

				if ( spansAllPrimaryTracks )
				{
					var jump = matrix.OccupiedTrackJump( secondaryAxis, secondarySpan );
					if ( jump is not null )
					{
						secondaryIndex = jump.Value;
						primaryIndex = primaryGridStartLine;
						continue;
					}
					return (primarySpan, secondarySpan);
				}

				var collision = matrix.LineAreaCollisionJump( primaryAxis, primarySpan, secondarySpan );
				if ( collision is not null )
				{
					primaryIndex = collision.Value;
					continue;
				}
				return (primarySpan, secondarySpan);
			}
		}
	}

	private static GridSpan ClampSpanToLimitedGrid( GridSpan span )
	{
		var start = Math.Clamp( span.Start, -MaxGridTracks, MaxGridTracks - 1 );
		var end = Math.Clamp( span.End, start + 1, MaxGridTracks );
		return new GridSpan( start, end );
	}

	private static void RecordGridPlacement(
		CellOccupancyMatrix matrix,
		List<GridItem> items,
		LayoutNode node,
		int index,
		Align parentAlignItems,
		Align parentJustifyItems,
		Dimension primaryAxis,
		GridSpan primarySpan,
		GridSpan secondarySpan,
		CellOccupancyState state )
	{
		primarySpan = ClampSpanToLimitedGrid( primarySpan );
		secondarySpan = ClampSpanToLimitedGrid( secondarySpan );
		matrix.MarkAreaAs( primaryAxis, primarySpan, secondarySpan, state );

		var (columnSpan, rowSpan) = primaryAxis == Dimension.Width
			? (primarySpan, secondarySpan)
			: (secondarySpan, primarySpan);

		items.Add( GridItem.Rent(
			node,
			index,
			columnSpan,
			rowSpan,
			node.Style.AlignSelf == Align.Auto ? parentAlignItems : node.Style.AlignSelf,
			node.Style.JustifySelf == Align.Auto ? parentJustifyItems : node.Style.JustifySelf ) );
	}
}
