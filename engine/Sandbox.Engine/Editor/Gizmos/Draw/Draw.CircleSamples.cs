namespace Sandbox;

public static partial class Gizmo
{
	public sealed partial class GizmoDraw
	{
		const int MaxCachedCircleSections = 128;
		static readonly Vector2[][] _halfCircles = new Vector2[MaxCachedCircleSections + 1][];
		static readonly Vector2[][] _fullCircles = new Vector2[MaxCachedCircleSections + 1][];
		static readonly Vector2[][] _lineCircles = new Vector2[MaxCachedCircleSections + 1][];

		// Common resolutions are retained. Larger inputs use the caller's rented buffer.
		// Keep the original angle evaluation order so cached shapes retain their exact vertices.
		static ReadOnlySpan<Vector2> CircleSamples( int sections, Span<Vector2> fallback, bool half = false, bool lines = false )
		{
			Span<Vector2> samples;
			if ( sections <= MaxCachedCircleSections )
			{
				var cache = half ? _halfCircles : lines ? _lineCircles : _fullCircles;
				if ( cache[sections] is { } existing ) return existing;
				samples = cache[sections] = new Vector2[sections + 1];
			}
			else samples = fallback;

			for ( var i = 0; i <= sections; ++i )
			{
				var fraction = (float)i / sections;
				var angle = half ? MathF.PI * fraction : lines ? 2.0f * MathF.PI * fraction : fraction * MathF.PI * 2;
				samples[i] = new Vector2( MathF.Sin( angle ), MathF.Cos( angle ) );
			}
			return samples;
		}
	}
}
