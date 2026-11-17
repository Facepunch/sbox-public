namespace Sandbox;

/// <summary>
/// Indexes a scene's active <see cref="WaterHole"/>s for point queries: swimming, buoyancy, splashes and underwater
/// effects. Rebuilt on the first query after a hole moves, resizes, or is enabled or disabled. It collects the holes
/// itself, so a system recreated by a reload starts complete.
/// </summary>
internal sealed class WaterHoleSystem : GameObjectSystem<WaterHoleSystem>
{
	readonly List<WaterHole> _active = [];
	readonly WaterHoleGrid _grid = new();
	WaterHole[] _holes = [];
	WaterHole.Data[] _data = [];
	BBox[] _bounds = [];
	Vector4[] _rects = [];
	bool _dirty = true;

	public WaterHoleSystem( Scene scene ) : base( scene )
	{
	}

	/// <summary>
	/// Indexed holes: those with a volume. <see cref="Data"/> and <see cref="Bounds"/> share their indices.
	/// </summary>
	internal int Count { get; private set; }

	internal ReadOnlySpan<WaterHole.Data> Data => _data.AsSpan( 0, Count );

	/// <summary>
	/// World-space bounds of each indexed hole's box.
	/// </summary>
	internal ReadOnlySpan<BBox> Bounds => _bounds.AsSpan( 0, Count );

	internal void MarkDirty() => _dirty = true;

	internal void Refresh()
	{
		if ( !_dirty )
			return;

		_dirty = false;
		_active.Clear();
		Scene.GetAll( _active );
		if ( _holes.Length < _active.Count )
		{
			int capacity = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)_active.Count );
			_holes = new WaterHole[capacity];
			_data = new WaterHole.Data[capacity];
			_bounds = new BBox[capacity];
			_rects = new Vector4[capacity];
		}

		Array.Clear( _holes );
		Count = 0;
		foreach ( var hole in _active )
		{
			if ( !hole.Active || !hole.HasVolume )
				continue;

			var bounds = hole.Bounds.Transform( hole.WorldTransform );
			_holes[Count] = hole;
			_data[Count] = hole.GetData();
			_bounds[Count] = bounds;
			// Indexed on world XY. A margin keeps points on a cell boundary from missing the hole.
			_rects[Count] = new Vector4( bounds.Mins.x - 1, bounds.Mins.y - 1, bounds.Maxs.x + 1, bounds.Maxs.y + 1 );
			Count++;
		}
		_active.Clear();

		if ( Count > 0 )
			_grid.Build( _rects.AsSpan( 0, Count ) );
	}

	/// <summary>
	/// Whether an active hole removes water at a world position, ignoring holes on <paramref name="owner"/> or its descendants.
	/// </summary>
	internal bool Cuts( Vector3 position, GameObject owner )
	{
		Refresh();
		if ( Count == 0 )
			return false;

		return Test( _grid.Bucket( new Vector2( position.x, position.y ) ) ) || Test( _grid.Large );

		bool Test( (int Start, int End) range )
		{
			for ( int k = range.Start; k < range.End; k++ )
			{
				int i = (int)_grid.Indices[k];
				if ( _data[i].Contains( position ) && (!owner.IsValid() || !_holes[i].GameObject.IsAncestor( owner )) )
					return true;
			}
			return false;
		}
	}

	public override void Dispose()
	{
		Array.Clear( _holes );
		Count = 0;
		base.Dispose();
	}
}

/// <summary>
/// Hole footprints binned into a hashed square grid, so a point tests only the holes near it. The cell is the median
/// footprint, so holes far from each other don't make the cells bigger. A footprint over many cells goes in one list
/// that every point tests. The water shader repeats <see cref="Bucket"/>.
/// </summary>
internal sealed class WaterHoleGrid
{
	const int LargeCells = 16;

	internal float InverseCell;
	internal int Mask;
	internal uint[] Starts = [];
	internal uint[] Indices = [];
	uint[] _cursors = [];
	int[] _last = [];
	float[] _sizes = [];

	/// <summary>
	/// Entries in <see cref="Starts"/>: bucket b's holes are Indices[Starts[b] .. Starts[b + 1]), and the large
	/// footprints are the bucket after the last.
	/// </summary>
	internal int StartCount => Mask + 3;
	internal int IndexCount => (int)Starts[Mask + 2];
	internal (int Start, int End) Large => ((int)Starts[Mask + 1], (int)Starts[Mask + 2]);

	internal (int Start, int End) Bucket( Vector2 point )
	{
		uint bucket = Hash( Cell( point.x ), Cell( point.y ) ) & (uint)Mask;
		return ((int)Starts[bucket], (int)Starts[bucket + 1]);
	}

	int Cell( float value ) => (int)Math.Clamp( MathF.Floor( value * InverseCell ), -1e9f, 1e9f );

	static uint Hash( int x, int y ) => unchecked((uint)x * 73856093u ^ (uint)y * 19349663u);

	/// <summary>
	/// Bin footprints given as (min x, min y, max x, max y). Their indices are the rows of the result.
	/// </summary>
	internal void Build( ReadOnlySpan<Vector4> rects )
	{
		int count = rects.Length;
		if ( _sizes.Length < count )
			_sizes = new float[System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)count )];
		for ( int i = 0; i < count; i++ )
			_sizes[i] = MathF.Max( rects[i].z - rects[i].x, rects[i].w - rects[i].y );
		var sizes = _sizes.AsSpan( 0, count );
		sizes.Sort();
		InverseCell = 1 / MathF.Max( sizes[count / 2], 1 );

		long entries = 0;
		foreach ( var rect in rects )
		{
			long cells = Cells( rect );
			if ( cells <= LargeCells )
				entries += cells;
		}

		int buckets = (int)System.Numerics.BitOperations.RoundUpToPowerOf2( (uint)Math.Max( entries, 1 ) ) * 2;
		Mask = buckets - 1;
		if ( Starts.Length < buckets + 2 )
		{
			Starts = new uint[buckets + 2];
			_cursors = new uint[buckets + 1];
			_last = new int[buckets + 1];
		}
		Array.Clear( Starts, 0, buckets + 2 );

		for ( int pass = 0; pass < 2; pass++ )
		{
			// A hole over several cells hashing to one bucket is listed there once.
			Array.Fill( _last, -1, 0, buckets + 1 );
			for ( int hole = 0; hole < count; hole++ )
			{
				var rect = rects[hole];
				if ( Cells( rect ) > LargeCells )
				{
					Add( buckets, hole, pass );
					continue;
				}

				for ( int y = Cell( rect.y ); y <= Cell( rect.w ); y++ )
				{
					for ( int x = Cell( rect.x ); x <= Cell( rect.z ); x++ )
						Add( (int)(Hash( x, y ) & (uint)Mask), hole, pass );
				}
			}

			if ( pass == 1 )
				break;

			for ( int bucket = 1; bucket < buckets + 2; bucket++ )
				Starts[bucket] += Starts[bucket - 1];
			Array.Copy( Starts, _cursors, buckets + 1 );
			if ( Indices.Length < Starts[buckets + 1] )
				Indices = new uint[System.Numerics.BitOperations.RoundUpToPowerOf2( Starts[buckets + 1] )];
		}
	}

	long Cells( Vector4 rect ) => (long)(Cell( rect.z ) - Cell( rect.x ) + 1) * (Cell( rect.w ) - Cell( rect.y ) + 1);

	void Add( int bucket, int hole, int pass )
	{
		if ( _last[bucket] == hole )
			return;

		_last[bucket] = hole;
		if ( pass == 0 )
			Starts[bucket + 1]++;
		else
			Indices[_cursors[bucket]++] = (uint)hole;
	}
}
