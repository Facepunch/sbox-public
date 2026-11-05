namespace Editor.AnimGraph;

/// <summary>
/// Where everything on the timeline sits. Frames map to pixels in one place, so what gets painted
/// and what gets clicked can't drift apart, and the ruler lines up with the lanes below it.
/// </summary>
internal readonly struct ClipEventLayout
{
	/// <summary>One track's row.</summary>
	public const float LaneHeight = 24f;

	/// <summary>The strip of frame numbers above the lanes.</summary>
	public const float RulerHeight = 22f;

	/// <summary>The track names, left of the clip itself.</summary>
	public float Gutter => Math.Clamp( _width * 0.3f, 84f, 132f );

	private const float RightPadding = 10f;
	private const float EdgeGrab = 4f;
	private const float MarkerWidth = 11f;

	private readonly float _width;
	private readonly int _tracks;

	/// <summary>
	/// Events are placed against frame indices, and the last frame is the end of the clip - the
	/// same span the compiler normalizes them over.
	/// </summary>
	public float LastFrame { get; }

	public ClipEventLayout( float width, int frameCount, int tracks )
	{
		_width = width;
		_tracks = tracks;

		LastFrame = MathF.Max( frameCount - 1, 1f );
	}

	/// <summary>
	/// The clip itself, past the track names.
	/// </summary>
	public Rect LaneArea => new( Gutter, 0f, MathF.Max( _width - Gutter - RightPadding, 1f ), _tracks * LaneHeight );

	public float FrameToX( float frame ) => LaneArea.Left + frame / LastFrame * LaneArea.Width;

	public float XToFrame( float x ) => ((x - LaneArea.Left) / LaneArea.Width * LastFrame).Clamp( 0f, LastFrame );

	public Rect LaneRect( int index ) => new( Gutter, index * LaneHeight, LaneArea.Width, LaneHeight );

	public Rect EventRect( int index, ClipDocument.Event item )
	{
		var lane = LaneRect( index );
		var top = lane.Top + 4f;
		var height = lane.Height - 8f;
		var area = LaneArea;

		// The left edge marks the frame it starts on whether it lasts for frames or fires on one,
		// same as the playhead - so growing a marker into a bar only ever moves its right edge,
		// and collapsing one back doesn't shift it sideways. A marker is left at the width the
		// narrowest bar gets, since nothing thinner could be seen or grabbed
		var left = FrameToX( item.StartFrame );
		var width = MathF.Max( FrameToX( item.EndFrame ) - left, MarkerWidth );

		// A frame typed into the fields can be anywhere, and an event drawn off the end of the
		// clip couldn't be clicked to put it back. Held at the edge instead, where it stays
		// reachable - and painted as a problem, since that's where the compiler drops it
		if ( left >= area.Right )
			return new Rect( area.Right - MarkerWidth, top, MarkerWidth, height );

		if ( left + width <= area.Left )
			return new Rect( area.Left, top, MarkerWidth, height );

		return new Rect( left, top, width, height );
	}

	/// <summary>
	/// The grab zone scales with the bar, so the right end of anything can be pulled out into a
	/// duration - including a marker that fires on a single frame.
	/// </summary>
	public static bool IsResizeEdge( Rect rect, float x ) => x >= rect.Right - MathF.Max( rect.Width * 0.3f, EdgeGrab );

	public int HitLane( Vector2 position )
	{
		var index = (int)(position.y / LaneHeight);

		return index >= 0 && index < _tracks ? index : -1;
	}

	/// <summary>
	/// Round numbers only - a ruler stepping in 7s is far harder to place an event against than
	/// one stepping in 5s, however tightly it fits.
	/// </summary>
	public IEnumerable<float> RulerFrames()
	{
		var perFrame = LaneArea.Width / LastFrame;
		var step = 1f;

		foreach ( var candidate in RulerSteps )
		{
			step = candidate;

			if ( candidate * perFrame >= 38f )
				break;
		}

		for ( var frame = 0f; frame <= LastFrame; frame += step )
		{
			yield return frame;
		}
	}

	private static readonly float[] RulerSteps = [1f, 2f, 5f, 10f, 25f, 50f, 100f, 250f, 500f, 1000f];

	/// <summary>
	/// The room this many tracks need. A minimum rather than a fixed height - the scroll area
	/// stretches the lanes to fill it, so the rows don't stop short of the bottom.
	/// </summary>
	public static float HeightFor( int tracks ) => tracks * LaneHeight;
}
