using Sandbox;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

/// <summary>
/// A tapered shape between two points with a radius at each end.
/// Supports cones and cylinders, with flat ends.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct Cone( Vector3 a, Vector3 b, float ra, float rb ) : IEquatable<Cone>
{
	/// <summary>
	/// Start point.
	/// </summary>
	[JsonInclude] public Vector3 CenterA = a;

	/// <summary>
	/// End point.
	/// </summary>
	[JsonInclude] public Vector3 CenterB = b;

	/// <summary>
	/// Radius at the start.
	/// </summary>
	[JsonInclude] public float RadiusA = ra;

	/// <summary>
	/// Radius at the end.
	/// </summary>
	[JsonInclude] public float RadiusB = rb;

	static void BuildBasis( in Vector3 n, out Vector3 b1, out Vector3 b2 )
	{
		b1 = MathF.Abs( n.x ) > MathF.Abs( n.z ) ? new Vector3( -n.y, n.x, 0 ).Normal : new Vector3( 0, -n.z, n.y ).Normal;
		b2 = Vector3.Cross( n, b1 );
	}

	/// <summary>
	/// Get a random point inside.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector3 RandomPointInside
	{
		get
		{
			var axis = CenterB - CenterA;
			var length = axis.Length;

			if ( length == 0 )
				return CenterA + Random.Shared.VectorInSphere( RadiusA );

			var dir = axis / length;
			BuildBasis( dir, out var right, out var forward );

			var t = Random.Shared.Float( 0f, 1f );
			var r = RadiusA.LerpTo( RadiusB, t );

			var p = Vector3.Lerp( CenterA, CenterB, t );
			var c = Random.Shared.VectorInCircle( r );

			return p + right * c.x + forward * c.y;
		}
	}

	/// <summary>
	/// Get a random point on the surface.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly Vector3 RandomPointOnEdge
	{
		get
		{
			var axis = CenterB - CenterA;
			var length = axis.Length;

			if ( length == 0 )
				return CenterA + Random.Shared.VectorOnSphere( RadiusA );

			var dir = axis / length;
			BuildBasis( dir, out var right, out var forward );

			var side = length;
			var caps = RadiusA + RadiusB;
			var total = side + caps;

			if ( Random.Shared.Float( 0, total ) < side )
			{
				var t = Random.Shared.Float( 0f, 1f );
				var r = RadiusA.LerpTo( RadiusB, t );

				var p = Vector3.Lerp( CenterA, CenterB, t );

				var angle = Random.Shared.Float( 0, MathF.Tau );
				var x = MathF.Cos( angle ) * r;
				var y = MathF.Sin( angle ) * r;

				return p + right * x + forward * y;
			}
			else
			{
				var a = Random.Shared.Float( 0, 1 ) < 0.5f;
				var center = a ? CenterA : CenterB;
				var r = a ? RadiusA : RadiusB;

				var c = Random.Shared.VectorInCircle( r );
				return center + right * c.x + forward * c.y;
			}
		}
	}

	/// <summary>
	/// Bounding box that contains the shape.
	/// </summary>
	[JsonIgnore, Hide]
	public readonly BBox Bounds
	{
		get
		{
			var ra = new Vector3( RadiusA );
			var rb = new Vector3( RadiusB );

			var mins = Vector3.Min( CenterA - ra, CenterB - rb );
			var maxs = Vector3.Max( CenterA + ra, CenterB + rb );

			return new BBox( mins, maxs );
		}
	}

	/// <summary>
	/// Distance from a point to the surface.
	/// </summary>
	public readonly float GetEdgeDistance( Vector3 p )
	{
		var axis = CenterB - CenterA;
		var length = axis.Length;

		if ( length == 0 )
			return MathF.Abs( (p - CenterA).Length - RadiusA );

		var dir = axis / length;

		var t = (p - CenterA).Dot( dir );
		var ct = t.Clamp( 0, length );

		var nt = ct / length;
		var r = RadiusA.LerpTo( RadiusB, nt );

		var closest = CenterA + dir * ct;
		var d = (p - closest).Length;

		return MathF.Abs( d - r );
	}

	/// <summary>
	/// Check if a point is inside.
	/// </summary>
	public readonly bool Contains( Vector3 p )
	{
		var axis = CenterB - CenterA;
		var length = axis.Length;

		if ( length == 0 )
			return (p - CenterA).Length <= RadiusA;

		var dir = axis / length;

		var t = (p - CenterA).Dot( dir );
		if ( t < 0 || t > length ) return false;

		var nt = t / length;
		var r = RadiusA.LerpTo( RadiusB, nt );

		var closest = CenterA + dir * t;
		return (p - closest).Length <= r;
	}

	#region equality
	public static bool operator ==( Cone l, Cone r ) => l.Equals( r );
	public static bool operator !=( Cone l, Cone r ) => !(l == r);
	public readonly override bool Equals( object obj ) => obj is Cone o && Equals( o );
	public readonly bool Equals( Cone o ) => (CenterA, CenterB, RadiusA, RadiusB) == (o.CenterA, o.CenterB, o.RadiusA, o.RadiusB);
	public readonly override int GetHashCode() => HashCode.Combine( CenterA, CenterB, RadiusA, RadiusB );
	#endregion
}
