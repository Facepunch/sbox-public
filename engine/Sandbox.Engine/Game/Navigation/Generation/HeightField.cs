using System.Buffers;
using System.Runtime.InteropServices;

namespace Sandbox.Navigation.Generation;

/// <summary>
/// Flat span record stored per column (sorted by MinY).
/// </summary>
[SkipHotload]
[StructLayout( LayoutKind.Explicit, Size = 8 )]
internal struct SpanData
{
	[FieldOffset( 0 )]
	public ushort MinY;
	[FieldOffset( 2 )]
	public ushort MaxY;
	[FieldOffset( 4 )]
	public int Area;
}

[SkipHotload]
internal sealed class Heightfield : IDisposable
{
	public int Width { get; private set; }
	public int Height { get; private set; }
	public Vector3 BMin { get; private set; }
	public Vector3 BMax { get; private set; }
	public float CellSize { get; private set; }
	public float CellHeight { get; private set; }

	private SpanData[] _spans;
	private int[] _columnCounts;
	private int[] _compressedColumnStarts;

	private int ColumnCount => Width * Height;

	private const int _initialColumnCapacity = 48;

	private int _columnCapacity = _initialColumnCapacity;
	private int _totalSpanCount = 0;

	public int TotalSpanCount => _totalSpanCount;

	public bool _isCompressed = false;

	public Heightfield( int sizeX, int sizeZ, in Vector3 minBounds, in Vector3 maxBounds,
		float cellSize, float cellHeight )
	{
		Init( sizeX, sizeZ, minBounds, maxBounds, cellSize, cellHeight );
	}

	public void Init( int sizeX, int sizeZ, in Vector3 minBounds, in Vector3 maxBounds,
		float cellSize, float cellHeight )
	{
		Width = sizeX;
		Height = sizeZ;
		BMin = minBounds;
		BMax = maxBounds;
		CellSize = cellSize;
		CellHeight = cellHeight;

		if ( _spans == null || _spans.Length < ColumnCount * _columnCapacity )
		{
			if ( _spans != null ) ArrayPool<SpanData>.Shared.Return( _spans );
			_spans = ArrayPool<SpanData>.Shared.Rent( ColumnCount * _columnCapacity );
		}
		if ( _columnCounts == null || _columnCounts.Length < ColumnCount )
		{
			if ( _columnCounts != null ) ArrayPool<int>.Shared.Return( _columnCounts );
			_columnCounts = ArrayPool<int>.Shared.Rent( ColumnCount );
		}
		if ( _compressedColumnStarts == null || _compressedColumnStarts.Length < ColumnCount )
		{
			if ( _compressedColumnStarts != null ) ArrayPool<int>.Shared.Return( _compressedColumnStarts );
			_compressedColumnStarts = ArrayPool<int>.Shared.Rent( ColumnCount );
		}
		Array.Clear( _columnCounts, 0, ColumnCount );
		_totalSpanCount = 0;
		_isCompressed = false;
	}

	public void Dispose()
	{
		if ( _spans != null )
		{
			ArrayPool<SpanData>.Shared.Return( _spans );
			_spans = null;
		}
		if ( _columnCounts != null )
		{
			ArrayPool<int>.Shared.Return( _columnCounts );
			_columnCounts = null;
		}
		if ( _compressedColumnStarts != null )
		{
			ArrayPool<int>.Shared.Return( _compressedColumnStarts );
			_compressedColumnStarts = null;
		}
	}

	public void GrowColumns()
	{
		var newCapacity = _columnCapacity * 2;
		var newSpans = ArrayPool<SpanData>.Shared.Rent( ColumnCount * newCapacity );
		for ( int c = 0; c < ColumnCount; c++ )
		{
			int count = _columnCounts[c];
			Array.Copy( _spans, c * _columnCapacity, newSpans, c * newCapacity, count );
		}
		ArrayPool<SpanData>.Shared.Return( _spans );
		_spans = newSpans;
		_columnCapacity = newCapacity;
	}

	/// <summary>
	/// Adds (and merges) a span into a column.
	/// Disallowed after compression.
	/// </summary>
	public void AddOrMergeSpan( int x, int z, ushort sMin, ushort sMax, int areaId, int flagMergeThreshold )
	{
		// New span to integrate
		int columnIndex = x + z * Width;
		int numSpans = _columnCounts[columnIndex];
		int baseOffset = columnIndex * _columnCapacity;
		int insertIndexStart = 0;
		int insertIndexEnd = 0; // exclusive
		for ( int i = 0; i < numSpans; i++ )
		{
			SpanData cur = _spans[baseOffset + i];

			// If current span starts above new span top -> position to insert before this span, stop.
			if ( cur.MinY > sMax )
			{
				insertIndexEnd = i;
				break;
			}

			// If current span ends below new span bottom -> new span goes after this one
			if ( cur.MaxY < sMin )
			{
				insertIndexStart = i + 1;
				continue;
			}

			// Overlapping (or directly adjacent) - merge
			if ( cur.MinY < sMin ) sMin = cur.MinY;
			if ( cur.MaxY > sMax ) sMax = cur.MaxY;

			// Area merge rule (mirrors Recast: compare newMax against current's top)
			if ( Math.Abs( sMax - cur.MaxY ) <= flagMergeThreshold )
				areaId = Math.Max( areaId, cur.Area );

			insertIndexEnd = i + 1; // extend overlapping range
		}

		int overlapCount = insertIndexEnd - insertIndexStart;
		if ( overlapCount > 0 )
		{
			int removeCount = overlapCount - 1;
			if ( removeCount > 0 ) Array.Copy( _spans, baseOffset + insertIndexEnd, _spans, baseOffset + insertIndexStart + 1, numSpans - insertIndexEnd );

			_columnCounts[columnIndex] -= removeCount;
			_totalSpanCount -= removeCount;
		}
		else
		{
			if ( numSpans == _columnCapacity )
			{
				GrowColumns();
				// Offset changed after reallocation
				baseOffset = columnIndex * _columnCapacity;
			}

			Array.Copy( _spans, baseOffset + insertIndexStart, _spans, baseOffset + insertIndexStart + 1, numSpans - insertIndexStart );

			_columnCounts[columnIndex]++;
			_totalSpanCount++;
		}

		_spans[baseOffset + insertIndexStart] = new SpanData
		{
			MinY = sMin,
			MaxY = sMax,
			Area = areaId
		};
	}

	public void EnsureCompressed()
	{
		if ( _isCompressed ) return;

		int currentOffset = 0;
		for ( int c = 0; c < ColumnCount; c++ )
		{
			int count = _columnCounts[c];
			_compressedColumnStarts[c] = currentOffset;
			if ( count > 0 )
			{
				int sourceOffset = c * _columnCapacity;
				if ( sourceOffset != currentOffset ) Array.Copy( _spans, sourceOffset, _spans, currentOffset, count );
			}
			currentOffset += count;
		}

		_isCompressed = true;
	}

	public Span<SpanData> GetColumn( int columnIndex )
	{
		int count = _columnCounts[columnIndex];
		int start = _compressedColumnStarts[columnIndex];
		return _spans.AsSpan( start, count );
	}


	/// <summary>
	/// Builds a compact heightfield from current spans. Auto-compresses if not already compressed.
	/// </summary>
	public CompactHeightfield BuildCompactHeightfield( int walkableHeight, int walkableClimb )
	{
		var compactHeightfield = CompactHeightfield.GetPooled();
		compactHeightfield.Init( Width, Height, TotalSpanCount, walkableHeight, walkableClimb, BMin, BMax, CellSize, CellHeight );

		const int MAX_HEIGHT = 0xFFFF;

		// Fill in cells and spans
		int currentSpanWrite = 0;
		int numColumns = Width * Height;

		for ( int columnIndex = 0; columnIndex < numColumns; columnIndex++ )
		{
			CompactCell cell = new CompactCell
			{
				Index = currentSpanWrite,
				Count = 0
			};

			var column = GetColumn( columnIndex );

			for ( int i = 0; i < column.Length; i++ )
			{
				SpanData src = column[i];
				if ( src.Area == Constants.NULL_AREA ) continue;

				int bot = src.MaxY;
				int top = (i + 1 < column.Length) ? column[i + 1].MinY : MAX_HEIGHT;

				CompactSpan cspan = new CompactSpan
				{
					StartY = (ushort)Math.Clamp( bot, 0, 0xFFFF ),
					Height = (byte)Math.Clamp( top - bot, 0, 0xFF ),
					Region = 0,
					Con = 0
				};

				compactHeightfield.Spans[currentSpanWrite] = cspan;
				compactHeightfield.Areas[currentSpanWrite] = src.Area;

				currentSpanWrite++;
				cell.Count++;
			}

			compactHeightfield.Cells[columnIndex] = cell;
		}

		// Shrink logical span count to actual written spans (skipping any unused tail).
		compactHeightfield.SpanCount = currentSpanWrite;

		// Neighbor connections (only iterate real spans through cells)
		const int MAX_LAYERS = Constants.NOT_CONNECTED - 1;
		int maxLayerIndex = 0;
		int zStride = Width; // for readability

		for ( int z = 0; z < Height; ++z )
		{
			for ( int x = 0; x < Width; ++x )
			{
				CompactCell cell = compactHeightfield.Cells[x + z * zStride];

				for ( int i = cell.Index; i < cell.Index + cell.Count; ++i )
				{
					CompactSpan span = compactHeightfield.Spans[i];

					for ( int dir = 0; dir < 4; ++dir )
					{
						Utils.SetCon( ref span, dir, Constants.NOT_CONNECTED );
						int neighborX = x + Utils.GetDirOffsetX( dir );
						int neighborZ = z + Utils.GetDirOffsetZ( dir );

						// Check if neighbor is in bounds
						if ( neighborX < 0 || neighborZ < 0 || neighborX >= Width || neighborZ >= Height )
							continue;

						CompactCell neighborCell = compactHeightfield.Cells[neighborX + neighborZ * zStride];

						for ( int k = neighborCell.Index; k < neighborCell.Index + neighborCell.Count; ++k )
						{
							CompactSpan neighborSpan = compactHeightfield.Spans[k];
							int bot = Math.Max( span.StartY, neighborSpan.StartY );
							int top = Math.Min( span.StartY + span.Height, neighborSpan.StartY + neighborSpan.Height );

							// Check walkable connection
							if ( (top - bot) >= walkableHeight && Math.Abs( neighborSpan.StartY - span.StartY ) <= walkableClimb )
							{
								int layerIndex = k - neighborCell.Index;
								if ( layerIndex < 0 || layerIndex > MAX_LAYERS )
								{
									if ( layerIndex > maxLayerIndex ) maxLayerIndex = layerIndex;
									continue;
								}

								Utils.SetCon( ref span, dir, layerIndex );
								break;
							}
						}
					}
					compactHeightfield.Spans[i] = span;
				}
			}
		}

		if ( maxLayerIndex > MAX_LAYERS )
		{
			Log.Warning( $"BuildCompactHeightfield: Heightfield has too many layers: {maxLayerIndex} (max: {MAX_LAYERS})" );
		}

		return compactHeightfield;
	}
}
