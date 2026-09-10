using Sandbox.MovieMaker;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

/// <summary>
/// A <see cref="CurveBlockItem{T}"/> is broken up into fixed-width tiles that get repainted separately.
/// </summary>
public sealed class CurveTile : GraphicsItem
{
	public const int TileWidth = 256;

	private readonly Pixmap _pixmap = new( TileWidth, Timeline.BlockHeight );
	private readonly List<Vector2>[] _lines;

	private readonly (float Min, float Max)[] _ranges;

	/// <summary>
	/// Min and max value for each decomposed element within this tile's time range.
	/// Will always be up to date.
	/// </summary>
	public IReadOnlyList<(float Min, float Max)> Ranges
	{
		get
		{
			UpdateRanges();
			return _ranges;
		}
	}

	/// <summary>
	/// Parent <see cref="CurveBlockItem{T}"/>.
	/// </summary>
	public ICurveBlockItem BlockItem { get; }

	/// <summary>
	/// Which tile is this in the parent block? First tile is index 0, next is 1, etc.
	/// </summary>
	public int Index { get; }

	public MovieTimeRange TimeRange { get; set; }

	private MovieTimeRange _pixmapTimeRange;
	private MovieTimeRange _rangesTimeRange;

	public CurveTile( ICurveBlockItem blockItem, int index )
		: base( (GraphicsItem)blockItem )
	{
		BlockItem = blockItem;
		Index = index;

		Height = Timeline.BlockHeight;

		var elementCount = blockItem.Elements.Count;

		_lines = new List<Vector2>[elementCount];
		_ranges = new (float Min, float Max)[elementCount];

		for ( var i = 0; i < elementCount; ++i )
		{
			_lines[i] = [];
		}
	}

	public void MarkDirty( MovieTimeRange timeRange )
	{
		if ( timeRange.Intersect( TimeRange ) is not { IsEmpty: false } clamped )
		{
			return;
		}

		_pixmapTimeRange = default;
		_rangesTimeRange = default;
	}

	protected override void OnPaint()
	{
		UpdatePixmap();

		var scale = LocalRect.Width / _pixmap.Width;

		var clampedRect = LocalRect;

		clampedRect.Right = Math.Min( clampedRect.Right, Parent.Width - Position.x );

		Paint.ClearPen();
		Paint.Scale( scale, 1f );
		Paint.SetBrush( _pixmap );
		Paint.DrawRect( clampedRect with { Left = clampedRect.Left / scale, Right = clampedRect.Right / scale } );
	}

	[field: ThreadStatic]
	public static List<MovieTime>? UpdatePixmap_Times { get; set; }

	private void UpdatePixmap()
	{
		if ( _pixmapTimeRange == TimeRange ) return;

		_pixmapTimeRange = TimeRange;
		_pixmap.Clear( Color.Transparent );

		var elementCount = BlockItem.Elements.Count;

		if ( elementCount <= 0 ) return;

		var times = UpdatePixmap_Times ??= new List<MovieTime>();
		times.Clear();

		GetCurveTimes( times );

		if ( times.Count == 0 ) return;

		var ranges = BlockItem.Ranges;

		for ( var i = 0; i < elementCount; ++i )
		{
			_lines[i].Clear();
		}

		const float margin = 2f;
		var height = LocalRect.Height - margin * 2f;

		Span<float> mids = stackalloc float[elementCount];

		var range = 0f;

		for ( var j = 0; j < elementCount; ++j )
		{
			range = Math.Max( range, ranges[j].Max - ranges[j].Min );

			mids[j] = (ranges[j].Min + ranges[j].Max) * 0.5f;
		}

		var scale = range <= 0f ? 0f : height / range;

		var t0 = TimeRange.Start;
		var t1 = TimeRange.End;

		var width = _pixmap.Width;
		var dxdt = width / (t1 - t0).TotalSeconds;

		Span<float> floats = stackalloc float[elementCount];

		foreach ( var t in times )
		{
			BlockItem.Read( t - BlockItem.Offset, floats );

			var x = (float)((t - t0).TotalSeconds * dxdt);

			for ( var j = 0; j < elementCount; ++j )
			{
				var y = (mids[j] - floats[j]) * scale + 0.5f * height + margin;

				_lines[j].Add( new Vector2( x, y ) );
			}
		}

		using ( Paint.ToPixmap( _pixmap ) )
		{
			Paint.Antialiasing = true;

			for ( var i = 0; i < elementCount; ++i )
			{
				Paint.SetPen( BlockItem.Elements[i].Color.WithAlpha( 0.25f ), 2f );
				Paint.DrawLine( _lines[i] );
			}
		}
	}

	[field: ThreadStatic]
	public static List<MovieTime>? UpdateRanges_Times { get; set; }

	private void UpdateRanges()
	{
		if ( _rangesTimeRange == TimeRange ) return;

		_rangesTimeRange = TimeRange;

		var elementCount = BlockItem.Elements.Count;

		if ( elementCount <= 0 ) return;

		for ( var i = 0; i < elementCount; ++i )
		{
			_ranges[i].Min = BlockItem.Elements[i].Min ?? float.PositiveInfinity;
			_ranges[i].Max = BlockItem.Elements[i].Max ?? float.NegativeInfinity;
		}

		var times = UpdateRanges_Times ??= new List<MovieTime>();
		times.Clear();

		GetCurveTimes( times );

		if ( times.Count == 0 ) return;

		Span<float> floats = stackalloc float[elementCount];

		foreach ( var t in times )
		{
			if ( t < _rangesTimeRange.Start ) continue;
			if ( t > _rangesTimeRange.End ) break;

			BlockItem.Read( t - BlockItem.Offset, floats );

			for ( var i = 0; i < elementCount; ++i )
			{
				_ranges[i].Min = Math.Min( _ranges[i].Min, floats[i] );
				_ranges[i].Max = Math.Max( _ranges[i].Max, floats[i] );
			}
		}
	}

	/// <summary>
	/// Controls curve resolution based on zoom level.
	/// </summary>
	[FromTheme]
	public static float PixelsPerSample { get; set; } = 2f;

	private void GetCurveTimes( List<MovieTime> times )
	{
		var timeRange = TimeRange;

		times.Add( timeRange.Start );

		var sampleRate = Math.Max( (int)Math.Round( TileWidth / (TimeRange.Duration.TotalSeconds * PixelsPerSample) ), 1 );

		foreach ( var hintRange in BlockItem.GetPaintHints( timeRange ) )
		{
			TryAddTime( hintRange.Start );

			// We subtract epsilon from the end so we can more accurately see
			// sudden value changes going into the next paint hint time range

			var clamped = hintRange with { End = hintRange.End - MovieTime.Epsilon };

			foreach ( var time in clamped.GetSampleTimes( sampleRate ) )
			{
				TryAddTime( time );
			}

			TryAddTime( hintRange.End - MovieTime.Epsilon );
		}

		TryAddTime( timeRange.End );
		return;

		void TryAddTime( MovieTime time )
		{
			time += BlockItem.Offset;

			if ( time < timeRange.Start ) return;
			if ( time > timeRange.End ) return;

			if ( times[^1] < time )
			{
				times.Add( time );
			}
		}
	}
}
