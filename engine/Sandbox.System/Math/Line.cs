using Sandbox;
using System.Text.Json.Serialization;

/// <summary>
/// Represents a line in 3D space.
/// </summary>
public struct Line : System.IEquatable<Line>
{
	Vector3 a;
	Vector3 b;

	/// <summary>
	/// Start position of the line.
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 Start => a;

	/// <summary>
	/// End position of the line.
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 End => b;

	/// <summary>
	/// Returns the result of b - a
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 Delta => b - a;

	/// <summary>
	/// Returns the midpoint between a and b
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 Center => (a + b) * 0.5f;

	public Line( Vector3 a, Vector3 b )
	{
		this.a = a;
		this.b = b;
	}

	public Line( Vector3 origin, Vector3 direction, float length )
	{
		this.a = origin;
		this.b = origin + direction * length;
	}

	/// <summary>
	/// Perform a "trace" between this line and given ray. If the 2 lines intersect, returns true.
	/// </summary>
	/// <param name="ray">The ray to test against.</param>
	/// <param name="radius">Radius of this line, which essentially makes this a capsule, since direct line-to-line intersections are very improbable. Must be above 0.</param>
	/// <param name="maxDistance">Maximum allowed distance from the origin of the ray to the intersection.</param>
	/// <returns>Whether there was an intersection or not.</returns>
	public readonly bool Trace( in Ray ray, float radius, float maxDistance = float.MaxValue )
	{
		if ( radius <= 0 )
			return false;

		Vector3 u = b - a;
		Vector3 v = ray.Forward;
		Vector3 w = a - ray.Position;

		float UdotU = Vector3.Dot( u, u ); // >= 0
		float UdotV = Vector3.Dot( u, v );
		float VdotW = Vector3.Dot( v, w );

		float det = UdotU - UdotV * UdotV; // >= 0

		float s = 0.0f;
		float t = VdotW;

		if ( det >= 0.0001f )
		{
			float UdotW = Vector3.Dot( u, w );
			float det_inv = 1.0f / det;

			s = det_inv * (UdotV * VdotW - UdotW);
			t = det_inv * (UdotU * VdotW - UdotV * UdotW);

			s = s.Clamp( 0, 1 );
		}

		// Intersection is behind us or too far away
		if ( t < 0.0f || t > maxDistance ) return false;

		Vector3 p1 = a + s * u;
		Vector3 p2 = ray.Position + t * v;

		Vector3 delta = p2 - p1;
		double distance = delta.Length;

		// Closest point is out of range
		if ( distance > radius ) return false;

		// hit.point = p1;
		//hit.normal = delta / distance;
		//hit.distance = Vector3.Distance( ray.origin, hit.point );
		return true;
	}

	/// <summary>
	/// Returns closest point on this line to the given point.
	/// </summary>
	public readonly Vector3 ClosestPoint( in Vector3 pos )
	{
		var delta = b - a;
		var length = delta.Length;
		var direction = delta / length;

		return a + Vector3.Dot( pos - a, direction ).Clamp( 0, length ) * direction;
	}

	/// <summary>
	/// Returns closest point on this line to the given ray.
	/// </summary>
	public readonly bool ClosestPoint( in Ray ray, out Vector3 point_on_line )
	{
		point_on_line = default;

		Vector3 u = a - b;
		Vector3 v = ray.Forward;
		Vector3 w = b - ray.Position;

		float UdotU = Vector3.Dot( u, u ); // >= 0
		float UdotV = Vector3.Dot( u, v );
		float VdotW = Vector3.Dot( v, w );
		float det = UdotU - UdotV * UdotV; // >= 0

		float s = 0.0f;
		float t = VdotW;

		if ( det >= 0.0001 )
		{
			float UdotW = Vector3.Dot( u, w );
			float det_inv = 1.0f / det;

			s = det_inv * (UdotV * VdotW - UdotW);
			t = det_inv * (UdotU * VdotW - UdotV * UdotW);

			s = s.Clamp( 0, 1 );
		}

		// Intersection is behind us
		if ( t < 0.0f ) return false;

		point_on_line = b + s * u;
		//  Vector3 p2 = ray.Origin + t * v;

		return true;
	}

	/// <summary>
	/// Returns closest point on this line to the given ray.
	/// </summary>
	public readonly bool ClosestPoint( in Ray ray, out Vector3 point_on_line, out Vector3 point_on_ray )
	{
		point_on_line = default;
		point_on_ray = default;

		Vector3 u = a - b;
		Vector3 v = ray.Forward;
		Vector3 w = b - ray.Position;

		float UdotU = Vector3.Dot( u, u ); // >= 0
		float UdotV = Vector3.Dot( u, v );
		float VdotW = Vector3.Dot( v, w );
		float det = UdotU - UdotV * UdotV; // >= 0

		float s = 0.0f;
		float t = VdotW;

		if ( det >= 0.00001f )
		{
			float UdotW = Vector3.Dot( u, w );
			float det_inv = 1.0f / det;

			s = det_inv * (UdotV * VdotW - UdotW);
			t = det_inv * (UdotU * VdotW - UdotV * UdotW);

			s = s.Clamp( 0, 1 );
		}

		// Intersection is behind us
		if ( t < 0.0f ) return false;

		point_on_line = b + s * u;
		point_on_ray = ray.Position + t * v;

		return true;
	}

	/// <summary>
	/// Returns closest distance from this line to given point.
	/// </summary>
	public readonly float Distance( Vector3 pos )
	{
		return (pos - ClosestPoint( pos )).Length;
	}

	/// <summary>
	/// Returns closest distance from this line to given point.
	/// </summary>
	public readonly float Distance( Vector3 pos, out Vector3 closestPoint )
	{
		closestPoint = ClosestPoint( pos );
		return (pos - closestPoint).Length;
	}

	/// <summary>
	/// Returns closest squared distance from this line to given point.
	/// </summary>
	public readonly float SqrDistance( Vector3 pos )
	{
		return (pos - ClosestPoint( pos )).LengthSquared;
	}

	public readonly bool Equals( Line other )
	{
		return other.a == a && other.b == b;
	}
}
