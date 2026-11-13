namespace Nav.Build;

/// <summary>
/// The solid spans the triangles rasterize to. Every column owns a fixed number of slots, so a column
/// is found by multiplication and neighbours are read without an index.
/// </summary>
internal sealed class SolidField
{
	public int Width, Height;
	public int SpanCount;

	/// <summary>
	/// Slots per column. Doubles when a column overflows and never shrinks.
	/// </summary>
	public int Capacity = 4;

	/// <summary>
	/// min | max &lt;&lt; 16 | area &lt;&lt; 32, sorted by min inside a column, never touching each other.
	/// </summary>
	public ulong[] Spans = [];

	public ushort[] Counts = [];

	public void Reset( int width, int height )
	{
		Width = width;
		Height = height;
		SpanCount = 0;
		int columns = width * height;
		if ( Counts.Length < columns ) Counts = new ushort[columns];
		if ( Spans.Length < columns * Capacity ) Spans = new ulong[columns * Capacity];
		Array.Clear( Counts, 0, columns );
	}

	public static int Min( ulong span ) => (int)(span & 0xFFFF);
	public static int Max( ulong span ) => (int)((span >> 16) & 0xFFFF);
	public static int Area( ulong span ) => (int)(span >> 32);
	public static ulong Pack( int min, int max, int area ) => (uint)min | ((uint)max << 16) | ((ulong)(uint)area << 32);

	/// <summary>
	/// Adds a span to a column, merging it with every span it overlaps or touches. A merged span takes the
	/// higher area when the tops are within mergeThreshold of each other, else the new span's area.
	/// </summary>
	public void Add( int column, int min, int max, int area, int mergeThreshold )
	{
		var spans = Spans;
		int count = Counts[column];
		int start = column * Capacity;
		if ( count == 0 )
		{
			spans[start] = Pack( min, max, area );
			Counts[column] = 1;
			SpanCount++;
			return;
		}

		// first..end are the existing spans the new one swallows
		int first = 0, end = 0;
		for ( int i = 0; i < count; i++ )
		{
			ulong current = spans[start + i];
			int currentMin = Min( current ), currentMax = Max( current );
			if ( currentMin > max )
			{
				end = i;
				break;
			}

			if ( currentMax < min )
			{
				first = i + 1;
				continue;
			}

			if ( currentMin < min ) min = currentMin;
			if ( currentMax > max ) max = currentMax;
			if ( Math.Abs( max - currentMax ) <= mergeThreshold ) area = Math.Max( area, Area( current ) );
			end = i + 1;
		}

		int swallowed = end - first;
		if ( swallowed > 1 )
		{
			int removed = swallowed - 1;
			for ( int i = end; i < count; i++ ) spans[start + i - removed] = spans[start + i];
			Counts[column] = (ushort)(count - removed);
			SpanCount -= removed;
		}
		else if ( swallowed <= 0 )
		{
			if ( count == Capacity )
			{
				Grow();
				spans = Spans;
				start = column * Capacity;
			}

			for ( int i = count; i > first; i-- ) spans[start + i] = spans[start + i - 1];
			Counts[column] = (ushort)(count + 1);
			SpanCount++;
		}

		spans[start + first] = Pack( min, max, area );
	}

	void Grow()
	{
		int columns = Width * Height;
		int capacity = Capacity * 2;
		var spans = new ulong[columns * capacity];
		for ( int c = 0; c < columns; c++ ) Array.Copy( Spans, c * Capacity, spans, c * capacity, Counts[c] );
		Spans = spans;
		Capacity = capacity;
	}
}
