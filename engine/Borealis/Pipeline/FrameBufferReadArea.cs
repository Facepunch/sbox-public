namespace Borealis;

/// <summary>
/// Where on a view's screen its frame-buffer readers are: the rectangle around their bounds' projections, in normalized
/// device coordinates (y up), or the whole screen for a reader that crosses the camera's plane or reads where its bounds
/// don't say (a custom object, an aggregate). Made as the view's lists are, so the frame copy covers only what they can read.
/// </summary>
internal struct FrameBufferReadArea
{
	/// <summary>The rectangle's corners, in normalized device coordinates, clamped to the screen; empty when Min > Max.</summary>
	public Vector2 Min, Max;

	/// <summary>A reader may read anywhere.</summary>
	public bool Whole;

	public static FrameBufferReadArea Empty => new() { Min = new Vector2( 1, 1 ), Max = new Vector2( -1, -1 ) };
	public static FrameBufferReadArea All => new() { Min = new Vector2( -1, -1 ), Max = new Vector2( 1, 1 ), Whole = true };

	public readonly bool IsEmpty => !Whole && (Min.x > Max.x || Min.y > Max.y);

	/// <summary>Add a reader's world bounds as <paramref name="worldToProjection"/> sees them: nothing where they're clipped whole.</summary>
	public void Add( Vector3 center, Vector3 extents, in Matrix worldToProjection )
	{
		if ( Whole ) return;

		// The corners are the centre's clip position plus or minus each axis's
		var c = worldToProjection.Transform( new Vector4( center, 1 ) );
		var x = worldToProjection.Transform( new Vector4( extents.x, 0, 0, 0 ) );
		var y = worldToProjection.Transform( new Vector4( 0, extents.y, 0, 0 ) );
		var z = worldToProjection.Transform( new Vector4( 0, 0, extents.z, 0 ) );
		Span<Vector4> corners = stackalloc Vector4[8];
		bool nearer = true, farther = true, behind = false;
		for ( int i = 0; i < 8; i++ )
		{
			var p = corners[i] = c + ((i & 1) != 0 ? x : -x) + ((i & 2) != 0 ? y : -y) + ((i & 4) != 0 ? z : -z);

			// Reverse-Z: the near plane (or an oblique view's clip plane) at depth 1, the far at 0
			nearer &= p.z > p.w;
			farther &= p.z < 0;
			behind |= p.w <= 1e-4f;
		}

		// Clipped whole: nothing it draws is read
		if ( nearer || farther ) return;
		if ( behind )
		{
			Union( All );
			return;
		}

		float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
		foreach ( var p in corners )
		{
			var ndcX = p.x / p.w;
			var ndcY = p.y / p.w;
			minX = MathF.Min( minX, ndcX );
			minY = MathF.Min( minY, ndcY );
			maxX = MathF.Max( maxX, ndcX );
			maxY = MathF.Max( maxY, ndcY );
		}

		// Off screen: nothing it draws is read
		if ( maxX < -1 || maxY < -1 || minX > 1 || minY > 1 ) return;
		Union( new FrameBufferReadArea { Min = new Vector2( MathF.Max( minX, -1 ), MathF.Max( minY, -1 ) ), Max = new Vector2( MathF.Min( maxX, 1 ), MathF.Min( maxY, 1 ) ) } );
	}

	public void Union( in FrameBufferReadArea other )
	{
		if ( other.Whole )
		{
			this = All;
			return;
		}

		if ( other.IsEmpty || Whole ) return;
		Min = new Vector2( MathF.Min( Min.x, other.Min.x ), MathF.Min( Min.y, other.Min.y ) );
		Max = new Vector2( MathF.Max( Max.x, other.Max.x ), MathF.Max( Max.y, other.Max.y ) );
	}
}
