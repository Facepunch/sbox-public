using System.Numerics;
using Sandbox;

/// <summary>
/// A struct describing an origin and direction
/// </summary>
[Description( "A ray described by an origin and a direction." )]
public struct Ray : IEquatable<Ray>
{
	private Vector3 _origin;
	private Vector3 _direction;

	/// <summary>
	/// Origin of the ray.
	/// </summary>
	public Vector3 Position
	{
		readonly get => _origin;
		set => _origin = value;
	}

	/// <summary>
	/// Direction of the ray.
	/// </summary>
	public Vector3 Forward
	{
		readonly get => _direction;
		set => _direction = value;
	}

	public Ray( Vector3 origin, Vector3 direction )
	{
		_origin = origin;
		_direction = direction;
	}

	/// <summary>
	/// Convert a ray to be local to this transform
	/// </summary>
	public readonly Ray ToLocal( in Transform tx )
	{
		var ray = this;
		ray.Forward = tx.NormalToLocal( ray.Forward );
		ray.Position = tx.PointToLocal( ray.Position );

		return ray;
	}

	/// <summary>
	/// Convert a ray from being local to this transform
	/// </summary>
	public readonly Ray ToWorld( in Transform tx )
	{
		var ray = this;
		ray.Forward = tx.NormalToWorld( ray.Forward );
		ray.Position = tx.PointToWorld( ray.Position );

		return ray;
	}

	/// <summary>
	/// Returns a point on the ray at given distance.
	/// </summary>
	/// <param name="distance">How far from the <see cref="Position"/> the point should be.</param>
	/// <returns>The point at given distance.</returns>
	public readonly Vector3 Project( float distance ) => Position + Forward * distance;

	private const float SafeMagnitude = 1e7f;

	/// <summary>
	/// Returns a point on the ray at given safe distance.
	/// </summary>
	/// <param name="distance">How far from the <see cref="Position"/> the point should be.</param>
	/// <returns>The point at given distance.</returns>
	internal readonly Vector3 ProjectSafe( float distance )
	{
		distance = float.IsNaN( distance ) ? SafeMagnitude : distance;
		return Position + Forward * distance.Clamp( -SafeMagnitude, SafeMagnitude );
	}

	#region equality
	public static bool operator ==( Ray left, Ray right ) => left.Equals( right );
	public static bool operator !=( Ray left, Ray right ) => !(left == right);
	public readonly override bool Equals( object obj ) => obj is Ray o && Equals( o );
	public readonly bool Equals( Ray o ) => (_origin, _direction) == (o._origin, o._direction);
	public readonly override int GetHashCode() => HashCode.Combine( _origin, _direction );
	#endregion
}
