namespace Sandbox.UI;

public enum BorderShapeKind
{
	None,
	Polygon,
	Circle
}

/// <summary>
/// A vertex of a polygon <see cref="BorderShape"/>, as a length on each axis.
/// </summary>
public readonly record struct BorderShapePoint( Length X, Length Y );

/// <summary>
/// The shape of a panel's border box, generalising <c>border-radius</c> from rounded corners to an
/// arbitrary polygon or a circle. Like <c>border-radius</c>, it shapes what the panel itself paints -
/// background, borders and hit testing - and does not clip descendants; that stays the job of
/// <see cref="OverflowMode"/>. Polygons currently support up to <see cref="MaxPoints"/> vertices.
/// </summary>
public sealed class BorderShape : IEquatable<BorderShape>
{
	public const int MaxPoints = 8;
	public static BorderShape None { get; } = new();

	readonly BorderShapePoint[] _points = [];

	public BorderShapeKind Kind { get; }
	public IReadOnlyList<BorderShapePoint> Points => _points;
	public Length? CircleRadius { get; }
	public Length CircleCenterX { get; }
	public Length CircleCenterY { get; }
	public bool IsNone => Kind == BorderShapeKind.None;

	BorderShape()
	{
		Kind = BorderShapeKind.None;
	}

	internal BorderShape( BorderShapePoint[] points )
	{
		Kind = BorderShapeKind.Polygon;
		_points = points;
	}

	internal BorderShape( Length? radius, Length centerX, Length centerY )
	{
		Kind = BorderShapeKind.Circle;
		CircleRadius = radius;
		CircleCenterX = centerX;
		CircleCenterY = centerY;
	}

	/// <summary>
	/// The same shape with every pixel length scaled, for the root panel scale.
	/// Percentages resolve against the box later, so they're left alone.
	/// </summary>
	internal BorderShape Scale( float amount )
	{
		if ( Kind == BorderShapeKind.None || amount == 1.0f ) return this;

		if ( Kind == BorderShapeKind.Circle )
		{
			var radius = CircleRadius;
			Length.Scale( ref radius, amount );
			var x = CircleCenterX; var y = CircleCenterY;
			Length.Scale( ref x, amount );
			Length.Scale( ref y, amount );
			return new BorderShape( radius, x, y );
		}

		var points = new BorderShapePoint[_points.Length];
		for ( int i = 0; i < _points.Length; i++ )
		{
			var x = _points[i].X; var y = _points[i].Y;
			Length.Scale( ref x, amount );
			Length.Scale( ref y, amount );
			points[i] = new BorderShapePoint( x, y );
		}

		return new BorderShape( points );
	}

	public (Vector2 Center, float Radius) ResolveCircle( Rect rect )
	{
		var center = new Vector2(
			rect.Left + CircleCenterX.GetPixels( rect.Width ),
			rect.Top + CircleCenterY.GetPixels( rect.Height ) );

		float radius;
		if ( CircleRadius.HasValue )
		{
			var radiusReference = MathF.Sqrt( rect.Width * rect.Width + rect.Height * rect.Height ) * 0.70710678118f;
			radius = CircleRadius.Value.GetPixels( radiusReference );
		}
		else
		{
			radius = MathF.Min( MathF.Min( center.x - rect.Left, rect.Right - center.x ), MathF.Min( center.y - rect.Top, rect.Bottom - center.y ) );
		}

		return (center, MathF.Max( radius, 0.0f ));
	}

	public bool Equals( BorderShape other )
	{
		if ( ReferenceEquals( this, other ) ) return true;
		if ( other is null || Kind != other.Kind ) return false;
		if ( Kind == BorderShapeKind.Circle )
			return CircleRadius == other.CircleRadius && CircleCenterX == other.CircleCenterX && CircleCenterY == other.CircleCenterY;
		if ( _points.Length != other._points.Length ) return false;

		for ( int i = 0; i < _points.Length; i++ )
		{
			if ( _points[i] != other._points[i] ) return false;
		}

		return true;
	}

	public override bool Equals( object obj ) => Equals( obj as BorderShape );

	public override int GetHashCode()
	{
		var hash = new HashCode();
		hash.Add( Kind );
		if ( Kind == BorderShapeKind.Circle )
		{
			hash.Add( CircleRadius );
			hash.Add( CircleCenterX );
			hash.Add( CircleCenterY );
		}
		else
		{
			foreach ( var point in _points ) hash.Add( point );
		}
		return hash.ToHashCode();
	}
}
