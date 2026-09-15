using Sandbox.UI;

namespace Sandbox;

public readonly partial struct Fill
{
	/// <summary>Starts a linear gradient. Add 2 to 8 ordered stops before assigning it to a fill.</summary>
	public static GradientBuilder LinearGradient( float angle = 0 ) => new( GradientInfo.GradientTypes.Linear, angle );

	/// <summary>Starts an elliptical radial gradient fitted to the shape bounds.</summary>
	public static GradientBuilder RadialGradient() => new( GradientInfo.GradientTypes.Radial, 0 );

	/// <summary>Starts a clockwise conic gradient. Angle is in degrees from right.</summary>
	public static GradientBuilder ConicGradient( float angle = 0 ) => new( GradientInfo.GradientTypes.Conic, angle );

	/// <summary>
	/// Builds a gradient by value without allocating stop arrays. Add 2 to 8 stops before converting to Fill.
	/// </summary>
	public readonly struct GradientBuilder
	{
		readonly GradientInfo _gradient;

		internal GradientBuilder( GradientInfo.GradientTypes type, float angle )
		{
			_gradient = CreateGradientInfo( type, angle );
		}

		GradientBuilder( GradientInfo gradient ) => _gradient = gradient;

		/// <summary>Returns a copy with another stop. Offsets are ordered in [0, 1]; equal offsets create hard transitions.</summary>
		public GradientBuilder WithStop( float offset, Color color )
		{
			var previous = _gradient.ColorOffsets.Length == 0 ? 0 : _gradient.ColorOffsets[_gradient.ColorOffsets.Length - 1].offset.Value;
			ValidateStop( offset, color, previous );
			return new( _gradient with { ColorOffsets = _gradient.ColorOffsets.Add( new() { color = color, offset = offset } ) } );
		}

		/// <summary>Returns a linear or conic gradient with an absolute clockwise angle in degrees from right. Radial gradients have no angle.</summary>
		public GradientBuilder WithAngle( float degrees ) => new( WithGradientAngle( _gradient, degrees ) );

		/// <summary>Completes the gradient, requiring at least two stops.</summary>
		public static implicit operator Fill( GradientBuilder builder )
		{
			if ( builder._gradient.ColorOffsets.Length < 2 )
				throw new InvalidOperationException( "Add at least two stops before using the gradient as a fill." );
			return new Fill( builder._gradient );
		}
	}

	/// <summary>
	/// Returns a bounds-based linear or conic gradient with an absolute clockwise angle in degrees from right.
	/// Positioned gradients take their direction from their points; radial gradients have no angle.
	/// </summary>
	public Fill WithAngle( float degrees )
	{
		if ( _gradient.ColorOffsets.IsDefaultOrEmpty || _gradientCoordinates is not null )
			throw new InvalidOperationException( "WithAngle requires a bounds-based linear or conic gradient." );
		return new Fill( this, WithGradientAngle( _gradient, degrees ) );
	}

	Fill( Fill source, GradientInfo gradient )
	{
		this = source;
		_gradient = gradient;
	}

	static GradientInfo WithGradientAngle( GradientInfo gradient, float degrees )
	{
		if ( gradient.GradientType == GradientInfo.GradientTypes.Radial )
			throw new InvalidOperationException( "Radial gradients have no angle." );
		return gradient with { Angle = GradientAngle( gradient.GradientType, degrees ), Corner = GradientInfo.Corners.None };
	}

	static float GradientAngle( GradientInfo.GradientTypes type, float angle )
	{
		if ( !float.IsFinite( angle ) ) throw new ArgumentOutOfRangeException( nameof( angle ) );
		return (type == GradientInfo.GradientTypes.Linear ? 90 - angle % 360 : angle % 360 + 90) * (MathF.PI / 180);
	}

	static GradientInfo CreateGradientInfo( GradientInfo.GradientTypes type, float angle ) => new()
	{
		GradientType = type,
		Angle = GradientAngle( type, angle ),
		OffsetX = Length.Percent( 50 ).Value,
		OffsetY = Length.Percent( 50 ).Value,
		SizeMode = GradientInfo.RadialSizeMode.FarthestSide
	};

	static void ValidateStop( float offset, Color color, float previous )
	{
		if ( !float.IsFinite( offset ) || offset < previous || offset > 1 )
			throw new ArgumentOutOfRangeException( nameof( offset ), "Stop offsets must be finite, ordered and in [0, 1]." );
		if ( !float.IsFinite( color.r ) || !float.IsFinite( color.g ) || !float.IsFinite( color.b ) || !float.IsFinite( color.a ) )
			throw new ArgumentOutOfRangeException( nameof( color ), "Stop colors must be finite." );
	}

	/// <summary>
	/// A color stop at a normalized position in a shape gradient.
	/// </summary>
	public readonly record struct GradientStop
	{
		/// <summary>
		/// Creates a stop with a normalized position and straight-alpha color.
		/// </summary>
		public GradientStop( float offset, Color color )
		{
			Offset = offset;
			Color = color;
		}

		/// <summary>
		/// Normalized position in [0, 1], validated when constructing a gradient.
		/// </summary>
		public float Offset { get; init; }

		/// <summary>
		/// Straight-alpha color, interpolated in premultiplied sRGB.
		/// </summary>
		public Color Color { get; init; }
	}

	/// <summary>
	/// Creates a two-color linear gradient across the shape bounds. Angle is clockwise degrees from right.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill LinearGradient( Color start, Color end, float angle = 0 )
	{
		return LinearGradient( [new( 0, start ), new( 1, end )], angle );
	}

	/// <summary>
	/// Creates a linear gradient across the shape bounds. Angle is clockwise degrees from right.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill LinearGradient( ReadOnlySpan<GradientStop> stops, float angle = 0 )
	{
		return CreateGradient( stops, GradientInfo.GradientTypes.Linear, angle );
	}

	/// <summary>
	/// Creates a linear gradient between drawing-coordinate points. Colors extend beyond the endpoints.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill LinearGradient( Vector2 start, Vector2 end, Color startColor, Color endColor )
	{
		return LinearGradient( start, end, [new( 0, startColor ), new( 1, endColor )] );
	}

	/// <summary>
	/// Creates a linear gradient between drawing-coordinate points. Colors extend beyond the endpoints.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill LinearGradient( Vector2 start, Vector2 end, ReadOnlySpan<GradientStop> stops )
	{
		return LinearGradient( start.x, start.y, end.x, end.y, stops );
	}

	/// <summary>
	/// Creates a linear gradient between length-based points. Colors extend beyond the endpoints.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill LinearGradient( Length? startX, Length? startY, Length? endX, Length? endY, Color startColor, Color endColor )
	{
		return LinearGradient( startX, startY, endX, endY, [new( 0, startColor ), new( 1, endColor )] );
	}

	/// <summary>
	/// Creates a linear gradient between length-based points. Colors extend beyond the endpoints.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill LinearGradient( Length? startX, Length? startY, Length? endX, Length? endY, ReadOnlySpan<GradientStop> stops )
	{
		return CreateGradient( stops, GradientInfo.GradientTypes.Linear, 0, new GradientCoordinates( startX, startY, endX, endY ) );
	}

	/// <summary>
	/// Creates a two-color elliptical gradient from the center to the sides of the shape bounds.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill RadialGradient( Color center, Color edge )
	{
		return RadialGradient( [new( 0, center ), new( 1, edge )] );
	}

	/// <summary>
	/// Creates an elliptical gradient from the center to the sides of the shape bounds.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill RadialGradient( ReadOnlySpan<GradientStop> stops )
	{
		return CreateGradient( stops, GradientInfo.GradientTypes.Radial, 0 );
	}

	/// <summary>
	/// Creates a circular gradient from center to edge in drawing coordinates. Their distance is the radius.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill RadialGradient( Vector2 center, Vector2 edge, Color start, Color end )
	{
		return RadialGradient( center, edge, [new( 0, start ), new( 1, end )] );
	}

	/// <summary>
	/// Creates a circular gradient from center to edge in drawing coordinates. The last color extends beyond the radius.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill RadialGradient( Vector2 center, Vector2 edge, ReadOnlySpan<GradientStop> stops )
	{
		return RadialGradient( center.x, center.y, edge.x, edge.y, stops );
	}

	/// <summary>
	/// Creates a circular gradient from center to edge using length-based points. Their distance is the radius.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill RadialGradient( Length? centerX, Length? centerY, Length? edgeX, Length? edgeY, Color start, Color end )
	{
		return RadialGradient( centerX, centerY, edgeX, edgeY, [new( 0, start ), new( 1, end )] );
	}

	/// <summary>
	/// Creates a circular gradient from center to edge using length-based points. The last color extends beyond the radius.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill RadialGradient( Length? centerX, Length? centerY, Length? edgeX, Length? edgeY, ReadOnlySpan<GradientStop> stops )
	{
		return CreateGradient( stops, GradientInfo.GradientTypes.Radial, 0, new GradientCoordinates( centerX, centerY, edgeX, edgeY ) );
	}

	/// <summary>
	/// Creates a two-color clockwise conic gradient centered in the shape bounds. Angle is degrees from right.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill ConicGradient( Color start, Color end, float angle = 0 )
	{
		return ConicGradient( [new( 0, start ), new( 1, end )], angle );
	}

	/// <summary>
	/// Creates a clockwise conic gradient centered in the shape bounds. Angle is degrees from right.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill ConicGradient( ReadOnlySpan<GradientStop> stops, float angle = 0 )
	{
		return CreateGradient( stops, GradientInfo.GradientTypes.Conic, angle );
	}

	/// <summary>
	/// Creates a clockwise conic gradient around center, starting toward start. Points use drawing coordinates.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill ConicGradient( Vector2 center, Vector2 start, Color startColor, Color endColor )
	{
		return ConicGradient( center, start, [new( 0, startColor ), new( 1, endColor )] );
	}

	/// <summary>
	/// Creates a clockwise conic gradient around center, starting toward start. Points use drawing coordinates.
	/// Stops describe a full turn; the distance between the points has no effect.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill ConicGradient( Vector2 center, Vector2 start, ReadOnlySpan<GradientStop> stops )
	{
		return ConicGradient( center.x, center.y, start.x, start.y, stops );
	}

	/// <summary>
	/// Creates a clockwise conic gradient using a length-based center and starting ray.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill ConicGradient( Length? centerX, Length? centerY, Length? startX, Length? startY, Color startColor, Color endColor )
	{
		return ConicGradient( centerX, centerY, startX, startY, [new( 0, startColor ), new( 1, endColor )] );
	}

	/// <summary>
	/// Creates a clockwise conic gradient using a length-based center and starting ray.
	/// Stops describe a full turn; the ray's length has no effect.
	/// </summary>
	/// <seealso cref="Fill"/>
	public static Fill ConicGradient( Length? centerX, Length? centerY, Length? startX, Length? startY, ReadOnlySpan<GradientStop> stops )
	{
		return CreateGradient( stops, GradientInfo.GradientTypes.Conic, 0, new GradientCoordinates( centerX, centerY, startX, startY ) );
	}

	static Fill CreateGradient( ReadOnlySpan<GradientStop> stops, GradientInfo.GradientTypes type, float angle, GradientCoordinates coordinates = null )
	{
		var gradient = CreateGradientInfo( type, angle );
		if ( stops.Length < 2 || stops.Length > GradientInfo.MaxStops )
			throw new ArgumentOutOfRangeException( nameof( stops ), "Gradients require 2 to 8 stops." );

		float previous = 0;
		foreach ( var stop in stops )
		{
			ValidateStop( stop.Offset, stop.Color, previous );
			previous = stop.Offset;
			gradient.ColorOffsets = gradient.ColorOffsets.Add( new() { color = stop.Color, offset = stop.Offset } );
		}

		gradient.Circle = type == GradientInfo.GradientTypes.Radial && coordinates is not null;
		return new Fill( gradient, coordinates );
	}

	// Shared by value copies of Fill. Pixel and percentage resolution does not allocate;
	// calc expressions use Length's existing expression evaluator.
	sealed class GradientCoordinates
	{
		readonly Length _x, _y, _endX, _endY;

		public GradientCoordinates( Length? x, Length? y, Length? endX, Length? endY )
		{
			_x = Validate( x, nameof( x ) );
			_y = Validate( y, nameof( y ) );
			_endX = Validate( endX, nameof( endX ) );
			_endY = Validate( endY, nameof( endY ) );
			if ( _x.Equals( _endX ) && _y.Equals( _endY ) )
				throw new ArgumentOutOfRangeException( nameof( endX ), "Gradient points must be distinct." );
			if ( _x.Unit == LengthUnit.Pixels && _y.Unit == LengthUnit.Pixels && _endX.Unit == LengthUnit.Pixels && _endY.Unit == LengthUnit.Pixels )
				GetPoints( default, out _, out _, out _ );
		}

		static Length Validate( Length? value, string parameter )
		{
			if ( value is not Length length || !float.IsFinite( length.Value )
				|| !(length.Unit is LengthUnit.Pixels or LengthUnit.Percentage || length.Unit.IsDynamic()) )
				throw new ArgumentOutOfRangeException( parameter, "Expected a finite coordinate length." );
			return length;
		}

		static float Coordinate( Length length, float origin, float size )
			=> length.GetPixels( size ) + (length.Unit is LengthUnit.Percentage or LengthUnit.Expression ? origin : 0);

		void GetPoints( Rect bounds, out Vector2 start, out Vector2 delta, out float distance )
		{
			start = new Vector2( Coordinate( _x, bounds.Left, bounds.Width ), Coordinate( _y, bounds.Top, bounds.Height ) );
			var end = new Vector2( Coordinate( _endX, bounds.Left, bounds.Width ), Coordinate( _endY, bounds.Top, bounds.Height ) );
			delta = end - start;
			distance = delta.Length;
			if ( !start.IsFinite || !end.IsFinite || !float.IsFinite( distance * 2 ) || distance <= 0 )
				throw new ArgumentOutOfRangeException( nameof( bounds ), "Gradient points must resolve to finite, distinct positions." );
		}

		public void Resolve( Rect bounds, ref GradientInfo gradient, out Vector4 backgroundRect )
		{
			GetPoints( bounds, out var start, out var delta, out var distance );
			if ( gradient.GradientType == GradientInfo.GradientTypes.Conic )
			{
				var offset = start - bounds.Position;
				if ( !offset.IsFinite )
					throw new ArgumentOutOfRangeException( nameof( bounds ), "Gradient center must resolve to finite coordinates." );
				gradient.OffsetX = offset.x;
				gradient.OffsetY = offset.y;
				gradient.Angle = MathF.Atan2( delta.x, -delta.y );
				backgroundRect = new Vector4( 0, 0, bounds.Width, bounds.Height );
				return;
			}

			float size = distance * 2;
			var center = start;
			if ( gradient.GradientType == GradientInfo.GradientTypes.Linear )
			{
				var direction = delta / distance;
				// The projection of a square onto the gradient direction is size * (|dx| + |dy|).
				// Choose its size so the shader's gradient line runs exactly between the two points.
				size = distance / (MathF.Abs( direction.x ) + MathF.Abs( direction.y ));
				center += delta * 0.5f;
				gradient.Angle = MathF.Atan2( delta.x, delta.y );
			}
			var position = center - new Vector2( size * 0.5f ) - bounds.Position;
			if ( !position.IsFinite || !float.IsFinite( position.x / size ) || !float.IsFinite( position.y / size ) )
				throw new ArgumentOutOfRangeException( nameof( bounds ), "Gradient bounds must resolve to finite coordinates." );
			backgroundRect = new Vector4( position.x, position.y, size, size );
		}
	}
}
