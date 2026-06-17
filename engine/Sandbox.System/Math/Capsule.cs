using Sandbox;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

/// <summary>
/// A capsule object, defined by 2 points and a radius. A capsule is a cylinder with round ends (inset half spheres on each end).
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct Capsule : System.IEquatable<Capsule>
{
	/// <summary>
	/// Position of point A.
	/// </summary>
	[JsonInclude]
	public Vector3 CenterA;

	/// <summary>
	/// Position of point B.
	/// </summary>
	[JsonInclude]
	public Vector3 CenterB;

	/// <summary>
	/// Radius of a capsule.
	/// </summary>
	[JsonInclude]
	public float Radius;

	public Capsule( Vector3 a, Vector3 b, float r )
	{
		CenterA = a;
		CenterB = b;
		Radius = r;
	}

	/// <summary>
	/// Creates a capsule where Point A is radius units above the ground and Point B is height minus radius units above the ground.
	/// </summary>
	public static Capsule FromHeightAndRadius( float height, float radius )
	{
		return new Capsule( Vector3.Up * radius, Vector3.Up * (height - radius), radius );
	}

	/// <summary>
	/// Returns a random point within this capsule.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector3 RandomPointInside
	{
		get
		{
			var diff = CenterB - CenterA;
			var sphereRand = Random.Shared.VectorInSphere( Radius );

			if ( diff.IsNearZeroLength )
			{
				// This capsule is just a sphere
				return sphereRand + CenterA;
			}

			var direction = diff.Normal;

			// Randomly decide whether the point will be in the cylindrical surface or on the hemispherical caps
			// Common factor is PI * Radius * Radius, so volumes are relative to that
			var capVolume = 2f / 3f * Radius; // Relative volume of one cap
			var cylinderVolume = diff.Length; // Relative volume of the cylinder
			var totalVolume = 2f * capVolume + cylinderVolume;
			var rand = Random.Shared.Float( 0.0f, totalVolume );

			if ( rand < 2 * capVolume )
			{
				// Point in either hemispherical cap

				var end = Vector3.Dot( direction, sphereRand ) > 0f
					? CenterB
					: CenterA;

				return end + sphereRand;
			}
			else
			{
				// Point in the connecting cylinder
				var t = Random.Shared.Float( 0f, 1f );
				var pointOnLine = Vector3.Lerp( CenterA, CenterB, t );
				var pointInCircle = Random.Shared.VectorInCircle( Radius );

				var norm1 = Vector3.Cross( direction, sphereRand ).Normal;
				var norm2 = Vector3.Cross( direction, norm1 );

				return pointOnLine + norm1 * pointInCircle.x + norm2 * pointInCircle.y;
			}
		}
	}

	/// <summary>
	/// Returns a random point on the edge of this capsule.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector3 RandomPointOnEdge
	{
		get
		{
			var diff = CenterB - CenterA;
			var sphereRand = Random.Shared.VectorOnSphere( Radius );

			if ( diff.IsNearZeroLength )
			{
				// This capsule is just a sphere
				return sphereRand + CenterA;
			}

			var direction = diff.Normal;

			// Randomly decide whether the point will be on the cylindrical surface or on the hemispherical caps
			// Common factor is 2 * PI * Radius, so areas are relative to that
			var capArea = Radius; // Relative area of one cap
			var cylinderArea = diff.Length; // Relative area of the cylinder
			var totalArea = 2 * capArea + cylinderArea;
			var rand = Random.Shared.Float( 0.0f, totalArea );

			if ( rand < 2 * capArea )
			{
				// Point on either hemispherical cap

				var end = Vector3.Dot( direction, sphereRand ) > 0f
					? CenterB
					: CenterA;

				return end + sphereRand;
			}
			else
			{
				// Point on the cylindrical surface
				var t = Random.Shared.Float( 0f, 1f );
				var pointOnLine = Vector3.Lerp( CenterA, CenterB, t );

				// Random point on the circle around pointOnLine
				var randomPerpendicular = Vector3.Cross( direction, sphereRand ).Normal * Radius;

				return pointOnLine + randomPerpendicular;
			}
		}
	}

	/// <summary>
	/// Gets the volume of the capsule in cubic units.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly float Volume
	{
		get
		{
			// Calculate the length of the cylindrical part
			float cylinderLength = (CenterB - CenterA).Length;

			// Volume of a capsule = volume of cylinder + volume of two hemisphere caps
			// cylinder = π × radius² × height
			// two hemispheres = (4/3) × π × radius³

			float cylinderVolume = MathF.PI * Radius * Radius * cylinderLength;
			float sphereVolume = (4.0f / 3.0f) * MathF.PI * Radius * Radius * Radius;

			return cylinderVolume + sphereVolume;
		}
	}

	/// <summary>
	/// Gets the Bounding Box of the capsule.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly BBox Bounds
	{
		get
		{
			// Create a bounding box that encompasses both sphere ends of the capsule
			Vector3 radiusVector = new Vector3( Radius );

			// Initialize bounds with the first center expanded by radius
			Vector3 mins = Vector3.Min( CenterA - radiusVector, CenterB - radiusVector );
			Vector3 maxs = Vector3.Max( CenterA + radiusVector, CenterB + radiusVector );

			return new BBox( mins, maxs );
		}
	}

	/// <summary>
	/// Calculates the distance from a given point to the edge of the capsule.
	/// </summary>
	/// <param name="localPos">Position in the same coordinate space as the capsule</param>
	public readonly float GetEdgeDistance( Vector3 localPos )
	{
		// Find the closest point on the line segment (CenterA to CenterB) to the position
		Vector3 lineVec = CenterB - CenterA;
		float lineLength = lineVec.Length;

		// Handle degenerate case (capsule is actually a sphere)
		if ( lineLength < 0.00001f )
		{
			float distanceToCenter = (localPos - CenterA).Length;
			return MathF.Abs( distanceToCenter - Radius );
		}

		// Calculate normalized direction vector of the capsule axis
		Vector3 lineDir = lineVec / lineLength;

		// Calculate projection of point onto line
		float projection = Vector3.Dot( localPos - CenterA, lineDir );

		// Clamp projection to line segment
		projection = Math.Clamp( projection, 0, lineLength );

		// Find closest point on line segment
		Vector3 closestPoint = CenterA + lineDir * projection;

		// Calculate distance from point to closest point on line segment
		float distanceToLine = (localPos - closestPoint).Length;

		// Return distance to edge (distance to line minus radius)
		return MathF.Abs( distanceToLine - Radius );
	}

	/// <summary>
	/// Determines if the capsule contains the specified point.
	/// </summary>
	public readonly bool Contains( Vector3 point )
	{
		// A point is inside the capsule if it's within Radius of the axis segment.
		// Note GetEdgeDistance can't be used here - it returns the unsigned distance
		// to the surface, which is positive on both sides of it.
		Vector3 lineVec = CenterB - CenterA;
		float lineLength = lineVec.Length;

		if ( lineLength < 0.00001f )
			return (point - CenterA).LengthSquared <= Radius * Radius;

		Vector3 lineDir = lineVec / lineLength;
		float projection = Math.Clamp( Vector3.Dot( point - CenterA, lineDir ), 0, lineLength );
		Vector3 closestPoint = CenterA + lineDir * projection;

		return (point - closestPoint).LengthSquared <= Radius * Radius;
	}

	#region equality
	public static bool operator ==( Capsule left, Capsule right ) => left.Equals( right );
	public static bool operator !=( Capsule left, Capsule right ) => !(left == right);
	public readonly override bool Equals( object obj ) => obj is Capsule o && Equals( o );
	public readonly bool Equals( Capsule o ) => (CenterA, CenterB, Radius) == (o.CenterA, o.CenterB, o.Radius);
	public readonly override int GetHashCode() => HashCode.Combine( CenterA, CenterB, Radius );
	#endregion
}
