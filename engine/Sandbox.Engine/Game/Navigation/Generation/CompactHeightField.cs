using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Sandbox.Navigation.Generation;

[SkipHotload]
[StructLayout( LayoutKind.Explicit, Size = 4 )]
internal struct CompactCell
{
	[FieldOffset( 0 )]
	private int indexAndCount;

	// 24bit
	public int Index
	{
		readonly get => indexAndCount & 0xFFFFFF;
		set => indexAndCount = (indexAndCount & ~0xFFFFFF) | (value & 0xFFFFFF);
	}

	// 8bit
	public int Count
	{
		readonly get => (indexAndCount >> 24) & 0xFF;
		set => indexAndCount = (indexAndCount & 0xFFFFFF) | ((value & 0xFF) << 24);
	}
}

[SkipHotload]
[StructLayout( LayoutKind.Explicit, Size = 8 )]
internal struct CompactSpan
{
	[FieldOffset( 0 )]
	public ushort StartY;
	[FieldOffset( 2 )]
	public ushort Region;
	[FieldOffset( 4 )]
	private int connectionsAndHeight;

	public int Con
	{
		readonly get => connectionsAndHeight & 0xFFFFFF;
		set => connectionsAndHeight = (connectionsAndHeight & ~0xFFFFFF) | (value & 0xFFFFFF);
	}

	public byte Height
	{
		readonly get => (byte)((connectionsAndHeight >> 24) & 0xFF);
		set => connectionsAndHeight = (connectionsAndHeight & 0xFFFFFF) | ((value & 0xFF) << 24);
	}

}

[SkipHotload]
internal partial class CompactHeightfield : IDisposable
{
	public int Width;
	public int Height;
	public int SpanCount;
	public int WalkableHeight;
	public int WalkableClimb;

	// Those two probably shouldn't be here they are only used during contour building
	public int BorderSize;
	public ushort MaxRegions;

	public Vector3 BMin;
	public Vector3 BMax;
	public float CellSize;
	public float CellHeight;
	public Span<CompactCell> Cells => cellsArray.AsSpan( 0, Width * Height );
	private CompactCell[] cellsArray;
	public Span<CompactSpan> Spans => spansArray.AsSpan( 0, SpanCount );
	private CompactSpan[] spansArray;

	public Span<int> Areas => areasArray.AsSpan( 0, SpanCount );
	private int[] areasArray;

	internal void Init( int width, int height, int spanCount, int walkableHeight, int walkableClimb,
		Vector3 bmin, Vector3 bmax, float cellSize, float cellHeight )
	{
		Width = width;
		Height = height;
		SpanCount = spanCount;
		WalkableHeight = walkableHeight;
		WalkableClimb = walkableClimb;
		MaxRegions = 0;
		BMin = bmin;
		BMax = bmax;
		BMax.y += walkableHeight * cellHeight;
		CellSize = cellSize;
		CellHeight = cellHeight;
		if ( cellsArray == null || cellsArray.Length < Width * Height )
		{
			if ( cellsArray != null ) ArrayPool<CompactCell>.Shared.Return( cellsArray );
			cellsArray = ArrayPool<CompactCell>.Shared.Rent( Width * Height * 2 );
		}
		if ( spansArray == null || spansArray.Length < SpanCount )
		{
			if ( spansArray != null ) ArrayPool<CompactSpan>.Shared.Return( spansArray );
			spansArray = ArrayPool<CompactSpan>.Shared.Rent( SpanCount * 2 );
		}
		if ( areasArray == null || areasArray.Length < SpanCount )
		{
			if ( areasArray != null ) ArrayPool<int>.Shared.Return( areasArray );
			areasArray = ArrayPool<int>.Shared.Rent( SpanCount * 2 );
		}
		// We intentionally do NOT clear/fill here. All used elements
		// are fully written during BuildCompactHeightfield, and then
		// SpanCount is reduced to the actual number of written spans.
	}

	private CompactHeightfield()
	{
		// Private constructor for Copy
	}

	public void CopyTo( CompactHeightfield dest )
	{
		dest.Init( Width, Height, SpanCount, WalkableHeight, WalkableClimb, BMin, BMax, CellSize, CellHeight );

		Cells.CopyTo( dest.Cells );
		Spans.CopyTo( dest.Spans );
		Areas.CopyTo( dest.Areas );
	}

	public CompactHeightfield Copy()
	{
		var copy = new CompactHeightfield();
		CopyTo( copy );

		return copy;
	}

	private static ConcurrentQueue<CompactHeightfield> _pool = new();

	public static CompactHeightfield GetPooled()
	{
		return _pool.TryDequeue( out var hf ) ? hf : new CompactHeightfield();
	}

	public void Dispose()
	{
		_pool.Enqueue( this );
		// Those will get disposed on shutdown, i guess
		//ArrayPool<CompactCell>.Shared.Return( cellsArray );
		//ArrayPool<CompactSpan>.Shared.Return( spansArray );
		//ArrayPool<byte>.Shared.Return( areasArray );
	}
}
