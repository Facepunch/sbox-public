namespace Sandbox.Layout;

internal enum CellOccupancyState : byte
{
	Unoccupied,
	DefinitelyPlaced,
	AutoPlaced,
}

/// <summary>
/// Which cells of the (implicit) grid are taken, tracked per row and per column as sorted occupied
/// intervals in origin-zero line coordinates. Grows as items are placed outside the current bounds.
/// </summary>
internal sealed class CellOccupancyMatrix
{
	private struct Interval
	{
		public int Start;
		public int End;
		public CellOccupancyState State;

		public bool Overlaps( int start, int end ) => Start < end && End > start;
	}

	private sealed class TrackIntervals
	{
		public readonly List<Interval> Intervals = new();

		// Scratch for the splice path of Paint. Paint never re-enters, so one per thread suffices.
		[ThreadStatic] private static List<Interval> s_scratch;

		public bool IsEmpty => Intervals.Count == 0;

		public void Paint( int start, int end, CellOccupancyState state )
		{
			if ( start >= end )
			{
				return;
			}

			if ( Intervals.Count == 0 )
			{
				Intervals.Add( new Interval { Start = start, End = end, State = state } );
				return;
			}

			var last = Intervals[^1];
			if ( last.End <= start )
			{
				if ( last.State == state && last.End == start )
				{
					last.End = end;
					Intervals[^1] = last;
				}
				else
				{
					Intervals.Add( new Interval { Start = start, End = end, State = state } );
				}
				return;
			}

			var result = s_scratch ??= new List<Interval>();
			if ( result.Capacity > 1024 )
			{
				s_scratch = null;
			}
			else
			{
				result.Clear();
			}
			foreach ( var interval in Intervals )
			{
				if ( interval.End <= start )
				{
					result.Add( interval );
				}
				else if ( interval.Start < start )
				{
					result.Add( new Interval { Start = interval.Start, End = start, State = interval.State } );
				}
			}

			if ( result.Count > 0 && result[^1].State == state && result[^1].End == start )
			{
				var r = result[^1];
				r.End = end;
				result[^1] = r;
			}
			else
			{
				result.Add( new Interval { Start = start, End = end, State = state } );
			}

			foreach ( var interval in Intervals )
			{
				Interval trimmed;
				if ( interval.Start >= end )
				{
					trimmed = interval;
				}
				else if ( interval.End > end )
				{
					trimmed = new Interval { Start = end, End = interval.End, State = interval.State };
				}
				else
				{
					continue;
				}

				if ( result[^1].State == trimmed.State && result[^1].End == trimmed.Start )
				{
					var r = result[^1];
					r.End = trimmed.End;
					result[^1] = r;
				}
				else
				{
					result.Add( trimmed );
				}
			}

			Intervals.Clear();
			Intervals.AddRange( result );
			result.Clear();
		}

		/// <summary>Last occupied coordinate overlapping the range, or null.</summary>
		public int? CollisionExtent( int start, int end )
		{
			for ( int i = Intervals.Count - 1; i >= 0; i-- )
			{
				if ( Intervals[i].Overlaps( start, end ) )
				{
					return Intervals[i].End - 1;
				}
			}
			return null;
		}

		public int? LastOfState( CellOccupancyState state )
		{
			for ( int i = Intervals.Count - 1; i >= 0; i-- )
			{
				if ( Intervals[i].State == state )
				{
					return Intervals[i].End - 1;
				}
			}
			return null;
		}
	}

	private TrackCounts _columns;
	private TrackCounts _rows;
	private readonly List<TrackIntervals> _rowIntervals = new();
	private readonly List<TrackIntervals> _columnIntervals = new();

	/// <summary>Matrices and their per-track interval lists are pooled per thread; pair with <see cref="Return"/>.</summary>
	public static CellOccupancyMatrix Rent( TrackCounts columns, TrackCounts rows )
	{
		var matrix = GridPool<CellOccupancyMatrix>.Rent();
		matrix._columns = columns;
		matrix._rows = rows;
		try
		{
			for ( int i = 0; i < rows.Length; i++ )
			{
				matrix._rowIntervals.Add( RentIntervals() );
			}

			for ( int i = 0; i < columns.Length; i++ )
			{
				matrix._columnIntervals.Add( RentIntervals() );
			}
			return matrix;
		}
		catch
		{
			Return( matrix );
			throw;
		}
	}

	public static void Return( CellOccupancyMatrix matrix )
	{
		foreach ( var intervals in matrix._rowIntervals )
		{
			ReturnIntervals( intervals );
		}

		foreach ( var intervals in matrix._columnIntervals )
		{
			ReturnIntervals( intervals );
		}
		matrix._rowIntervals.Clear();
		matrix._columnIntervals.Clear();
		GridPool<CellOccupancyMatrix>.Return( matrix, matrix._rowIntervals.Capacity + matrix._columnIntervals.Capacity );
	}

	private static TrackIntervals RentIntervals() => GridPool<TrackIntervals>.Rent();

	private static void ReturnIntervals( TrackIntervals intervals )
	{
		intervals.Intervals.Clear();
		GridPool<TrackIntervals>.Return( intervals, intervals.Intervals.Capacity );
	}

	public TrackCounts TrackCounts( Dimension axis ) => axis == Dimension.Width ? _columns : _rows;

	private List<TrackIntervals> TrackLists( Dimension axis ) => axis == Dimension.Width ? _columnIntervals : _rowIntervals;

	private void ExpandToFitRange( GridSpan rowSpan, GridSpan colSpan )
	{
		var requiredNegativeRows = Math.Max( -_rows.NegativeImplicit - rowSpan.Start, 0 );
		var requiredPositiveRows = Math.Max( rowSpan.End - _rows.ImplicitEndLine, 0 );
		var requiredNegativeColumns = Math.Max( -_columns.NegativeImplicit - colSpan.Start, 0 );
		var requiredPositiveColumns = Math.Max( colSpan.End - _columns.ImplicitEndLine, 0 );

		for ( int i = 0; i < requiredNegativeRows; i++ )
		{
			_rowIntervals.Insert( 0, RentIntervals() );
		}

		for ( int i = 0; i < requiredPositiveRows; i++ )
		{
			_rowIntervals.Add( RentIntervals() );
		}

		for ( int i = 0; i < requiredNegativeColumns; i++ )
		{
			_columnIntervals.Insert( 0, RentIntervals() );
		}

		for ( int i = 0; i < requiredPositiveColumns; i++ )
		{
			_columnIntervals.Add( RentIntervals() );
		}

		_rows.NegativeImplicit += requiredNegativeRows;
		_rows.PositiveImplicit += requiredPositiveRows;
		_columns.NegativeImplicit += requiredNegativeColumns;
		_columns.PositiveImplicit += requiredPositiveColumns;
	}

	public void MarkAreaAs( Dimension primaryAxis, GridSpan primarySpan, GridSpan secondarySpan, CellOccupancyState state )
	{
		var (rowSpan, colSpan) = primaryAxis == Dimension.Width ? (secondarySpan, primarySpan) : (primarySpan, secondarySpan);
		ExpandToFitRange( rowSpan, colSpan );

		var rowStart = _rows.LineToNextTrack( rowSpan.Start );
		var rowEnd = _rows.LineToNextTrack( rowSpan.End );
		var colStart = _columns.LineToNextTrack( colSpan.Start );
		var colEnd = _columns.LineToNextTrack( colSpan.End );

		for ( int row = rowStart; row < rowEnd; row++ )
		{
			_rowIntervals[row].Paint( colSpan.Start, colSpan.End, state );
		}

		for ( int column = colStart; column < colEnd; column++ )
		{
			_columnIntervals[column].Paint( rowSpan.Start, rowSpan.End, state );
		}
	}

	/// <summary>
	/// If the area collides with occupied cells, returns the primary-axis line just past the furthest
	/// collision (where to retry from), otherwise null.
	/// </summary>
	public int? LineAreaCollisionJump( Dimension primaryAxis, GridSpan primarySpan, GridSpan secondarySpan )
	{
		var secondaryAxis = primaryAxis == Dimension.Width ? Dimension.Height : Dimension.Width;
		var trackLists = TrackLists( secondaryAxis );
		var secondaryCounts = TrackCounts( secondaryAxis );
		var secondaryStart = Math.Max( secondaryCounts.LineToNextTrack( secondarySpan.Start ), 0 );
		var secondaryEnd = Math.Min( secondaryCounts.LineToNextTrack( secondarySpan.End ), trackLists.Count );

		int? extent = null;
		for ( int i = secondaryStart; i < secondaryEnd; i++ )
		{
			var cell = trackLists[i].CollisionExtent( primarySpan.Start, primarySpan.End );
			if ( cell is null )
			{
				continue;
			}
			extent = extent is null ? cell : Math.Max( extent.Value, cell.Value );
		}

		return extent is null ? null : extent.Value + 1;
	}

	/// <summary>Line after the last occupied track within the span in the given axis, or null if all free.</summary>
	public int? OccupiedTrackJump( Dimension axis, GridSpan span )
	{
		var counts = TrackCounts( axis );
		var trackLists = TrackLists( axis );
		var start = Math.Max( counts.LineToNextTrack( span.Start ), 0 );
		var end = Math.Min( counts.LineToNextTrack( span.End ), trackLists.Count );

		for ( int i = end - 1; i >= start; i-- )
		{
			if ( !trackLists[i].IsEmpty )
			{
				return counts.TrackToPrevLine( i ) + 1;
			}
		}

		return null;
	}

	public bool RowIsOccupied( int rowIndex ) => rowIndex < _rowIntervals.Count && !_rowIntervals[rowIndex].IsEmpty;
	public bool ColumnIsOccupied( int columnIndex ) => columnIndex < _columnIntervals.Count && !_columnIntervals[columnIndex].IsEmpty;
	public bool TrackIsOccupied( Dimension axis, int index ) => axis == Dimension.Width ? ColumnIsOccupied( index ) : RowIsOccupied( index );

	/// <summary>
	/// In the track (of the other axis) starting at <paramref name="startAt"/>, the last cell with the given
	/// state along <paramref name="trackAxis"/>, as an origin-zero coordinate; null if none.
	/// </summary>
	public int? LastOfType( Dimension trackAxis, int startAt, CellOccupancyState kind )
	{
		var otherAxis = trackAxis == Dimension.Width ? Dimension.Height : Dimension.Width;
		var counts = TrackCounts( otherAxis );
		var index = counts.LineToNextTrack( startAt );
		var trackLists = TrackLists( otherAxis );
		if ( index < 0 || index >= trackLists.Count )
		{
			return null;
		}
		return trackLists[index].LastOfState( kind );
	}
}
