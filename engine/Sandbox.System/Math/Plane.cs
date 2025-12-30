using System.Runtime.InteropServices;
using System.Runtime.Serialization;
namespace Sandbox;

/// <summary>
/// Represents a plane.
/// </summary>
[DataContract]
[StructLayout( LayoutKind.Sequential )]
public struct Plane : System.IEquatable<Plane>
{
	//
	// Internal data has to be floats,
	// to stay compatible with engine
	//

	/// <summary>
	/// The direction of the plane.
	/// </summary>
	public Vector3 Normal;

	/// <summary>
	/// Distance of the plane from world origin in the direction given by <see cref="Normal"/>.
	/// </summary>
	public float Distance;

	public Plane( in Vector3 normal, in float dist )
	{
		Normal = normal.Normal;
		Distance = dist;
	}

	public Plane( in Vector3 origin, in Vector3 normal )
	{
		Normal = normal.Normal;
		Distance = origin.Dot( Normal );
	}

	/// <summary>
	/// Creates a new plane from 3 given positions.
	/// </summary>
	/// <param name="origin">Origin of the plane.</param>
	/// <param name="posA">A position to calculate a normal with.</param>
	/// <param name="posB">Another position to calculate a normal with.</param>
	public Plane( in Vector3 origin, in Vector3 posA, in Vector3 posB )
	{
		Normal = Vector3.Cross( posA - origin, posB - origin ).Normal;
		Distance = origin.Dot( Normal );
	}

	/// <summary>
	/// Origin position of the plane, basically a vector <see cref="Distance"/> away from world origin in the direction given by <see cref="Normal"/>.
	/// </summary>
	[Obsolete( "Use Plane.Position" )]
	public readonly Vector3 Origin => Position;

	/// <summary>
	/// Origin position of the plane, basically a vector <see cref="Distance"/> away from world origin in the direction given by <see cref="Normal"/>.
	/// </summary>
	public readonly Vector3 Position => Normal * Distance;

	/// <summary>
	/// Returns the distance from this plane to given point.
	/// </summary>
	public readonly float GetDistance( in Vector3 point )
	{
		return point.Dot( Normal ) - Distance;
	}

	/// <summary>
	/// Returns true if given point is on the side of the plane where its normal is pointing.
	/// </summary>
	public readonly bool IsInFront( Vector3 point )
	{
		return GetDistance( point ) > 0.0f;
	}

	/// <summary>
	/// Returns true if given bounding box is on the side of the plane where its normal is pointing.
	/// </summary>
	public readonly bool IsInFront( BBox box, bool partially = false )
	{
		Vector3 s = default;

		if ( partially )
		{
			s.x = Normal.x < 0 ? box.Mins.x : box.Maxs.x;
			s.y = Normal.y < 0 ? box.Mins.y : box.Maxs.y;
			s.z = Normal.z < 0 ? box.Mins.z : box.Maxs.z;
		}
		else
		{
			s.x = Normal.x > 0 ? box.Mins.x : box.Maxs.x;
			s.y = Normal.y > 0 ? box.Mins.y : box.Maxs.y;
			s.z = Normal.z > 0 ? box.Mins.z : box.Maxs.z;
		}

		return IsInFront( s );
	}

	/// <summary>
	/// Returns closest point on the plane to given point.
	/// </summary>
	public readonly Vector3 SnapToPlane( in Vector3 point )
	{
		return point - Normal * GetDistance( point );
	}

	/// <summary>
	/// Trace a Ray against this plane
	/// </summary>
	public readonly bool TryTrace( in Ray ray, out Vector3 hitPoint, bool twosided = false, double maxDistance = double.MaxValue )
	{
		hitPoint = ray.Position;
		var r = Trace( ray, twosided, maxDistance );
		if ( r == null ) return false;

		hitPoint = r.Value;
		return true;
	}

	/// <summary>
	/// Trace a Ray against this plane
	/// </summary>
	/// <param name="ray">The origin and direction to trace from</param>
	/// <param name="twosided">If true we'll trace against the underside of the plane too.</param>
	/// <param name="maxDistance">The maximum distance from the ray origin to trace</param>
	/// <returns>The hit position on the ray. Or null if we didn't hit.</returns>
	public readonly Vector3? Trace( in Ray ray, bool twosided = false, double maxDistance = double.MaxValue )
	{
		var n = Normal;
		var denominator = ray.Forward.Dot( n );

		if ( twosided && denominator >= -0.00001f )
		{
			n *= -1.0f;
			denominator = ray.Forward.Dot( n );
		}

		if ( denominator >= -0.00001f )
			return null;

		var t = (Position - ray.Position).Dot( n ) / denominator;

		if ( t < 0.0f || t > maxDistance )
			return null;

		return ray.Position + ray.Forward * t;
	}

	/// <summary>
	/// Gets the intersecting point of the three planes if it exists.
	/// If the planes don't all intersect will return null.
	/// </summary>
	public static Vector3? GetIntersection( in Plane vp1, in Plane vp2, in Plane vp3 )
	{
		Vector3 v2Cross3 = Vector3.Cross( vp2.Normal, vp3.Normal );
		float flDenom = Vector3.Dot( vp1.Normal, v2Cross3 );
		if ( MathF.Abs( flDenom ) < 1.192092896e-07F )
			return null;

		Vector3 vRet = vp1.Distance * v2Cross3 + vp2.Distance * Vector3.Cross( vp3.Normal, vp1.Normal ) + vp3.Distance * Vector3.Cross( vp1.Normal, vp2.Normal );
		return vRet * (1.0f / flDenom);
	}

	/// <summary>
	/// Gets the intersecting point of a line segment.
	/// </summary>
	public readonly Vector3? IntersectLine( Line line )
	{
		return IntersectLine( line.Start, line.End );
	}

	/// <summary>
	/// Gets the intersecting point of a line segment.
	/// </summary>
	public readonly Vector3? IntersectLine( Vector3 start, Vector3 end )
	{
		float d1 = GetDistance( start );
		float d2 = GetDistance( end );

		const float eps = 0.001f;

		if ( MathF.Abs( d1 - d2 ) < eps )
		{
			if ( MathF.Abs( d1 ) < eps )
				return start;

			return default;
		}

		float t = -d1 / (d2 - d1);
		if ( t >= 0.0f && t <= 1.0f )
			return start + (end - start) * t;

		return default;
	}

	/// <summary>
	/// Reflects a point across the plane.
	/// </summary>
	public readonly Vector3 ReflectPoint( Vector3 point )
	{
		float flProjectionOntoPlane = Vector3.Dot( point, Normal ) - Distance;
		return point - (2.0f * flProjectionOntoPlane) * Normal;
	}

	/// <summary>
	/// Reflects a direction across the plane.
	/// </summary>
	public readonly Vector3 ReflectDirection( Vector3 direction )
	{
		return direction - (2.0f * Vector3.Dot( direction, Normal )) * Normal;
	}

	#region equality
	public static bool operator ==( Plane left, Plane right ) => left.Equals( right );
	public static bool operator !=( Plane left, Plane right ) => !(left == right);
	public override bool Equals( object obj ) => obj is Plane o && Equals( o );
	public readonly bool Equals( Plane o ) => (Normal, Distance) == (o.Normal, o.Distance);
	public override readonly int GetHashCode() => HashCode.Combine( Normal, Distance );
	#endregion
}
