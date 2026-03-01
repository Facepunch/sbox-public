using Sandbox;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

/// <summary>
/// An <a href="https://en.wikipedia.org/wiki/Minimum_bounding_box">Axis Aligned Bounding Box</a>.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct BBox : System.IEquatable<BBox>
{
	/// <summary>
	/// The minimum corner extents of the AABB. Values on each axis should be mathematically smaller than values on the same axis of <see cref="Maxs"/>. See <see cref="Vector3.Sort"/>
	/// </summary>
	[JsonInclude]
	public Vector3 Mins;

	/// <summary>
	/// The maximum corner extents of the AABB. Values on each axis should be mathematically larger than values on the same axis of <see cref="Mins"/>. See <see cref="Vector3.Sort"/>
	/// </summary>
	[JsonInclude]
	public Vector3 Maxs;

	/// <summary>
	/// Initialize an AABB with given mins and maxs corners. See <see cref="Vector3.Sort"/>.
	/// </summary>
	public BBox( Vector3 mins, Vector3 maxs )
	{
		Mins = Vector3.Min( mins, maxs );
		Maxs = Vector3.Max( mins, maxs );
	}

	/// <summary>
	/// Initializes a zero sized BBox with given center. This is useful if you intend to use AddPoint to expand the box later.
	/// </summary>
	[System.Obsolete( "Use BBox.FromPositionAndSize" )]
	public BBox( Vector3 center, float size = 0 )
	{
		size = MathF.Abs( size );

		Mins = center - size * 0.5f;
		Maxs = center + size * 0.5f;
	}

	/// <summary>
	/// An enumerable that contains all corners of this AABB.
	/// </summary>
	[JsonIgnore]
	public readonly IEnumerable<Vector3> Corners
	{
		get
		{
			yield return new Vector3( Mins.x, Mins.y, Mins.z );
			yield return new Vector3( Maxs.x, Mins.y, Mins.z );

			yield return new Vector3( Maxs.x, Maxs.y, Mins.z );
			yield return new Vector3( Mins.x, Maxs.y, Mins.z );
			yield return new Vector3( Mins.x, Mins.y, Maxs.z );

			yield return new Vector3( Maxs.x, Mins.y, Maxs.z );
			yield return new Vector3( Maxs.x, Maxs.y, Maxs.z );
			yield return new Vector3( Mins.x, Maxs.y, Maxs.z );
		}
	}

	/// <summary>
	/// Calculated center of the AABB.
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 Center => System.Numerics.Vector3.FusedMultiplyAdd( Size, new Vector3( 0.5f ), Mins );

	/// <summary>
	/// Calculated size of the AABB on each axis.
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 Size => (Maxs - Mins);


	/// <summary>
	/// The extents of the bbox. This is half the size.
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 Extents => Size * 0.5f;


	/// <summary>
	/// Move this box by this amount and return
	/// </summary>
	public readonly BBox Translate( in Vector3 point )
	{
		var b = this;

		b.Mins += point;
		b.Maxs += point;

		return b;
	}

	/// <summary>
	/// Rotate this box by this amount and return
	/// </summary>
	public readonly BBox Rotate( in Rotation rotation )
	{
		var b = this;

		var rotationInv = rotation.Conjugate.Normal;
		var xAxis = Vector3.Forward * rotationInv;
		var yAxis = Vector3.Right * rotationInv;
		var zAxis = Vector3.Up * rotationInv;
		var localCenter = 0.5f * (b.Mins + b.Maxs);
		var localExtents = b.Maxs - localCenter;
		var center = rotation * localCenter;
		var extents = new Vector3(
			MathF.Abs( localExtents.x * xAxis.x ) + MathF.Abs( localExtents.y * xAxis.y ) + MathF.Abs( localExtents.z * xAxis.z ),
			MathF.Abs( localExtents.x * yAxis.x ) + MathF.Abs( localExtents.y * yAxis.y ) + MathF.Abs( localExtents.z * yAxis.z ),
			MathF.Abs( localExtents.x * zAxis.x ) + MathF.Abs( localExtents.y * zAxis.y ) + MathF.Abs( localExtents.z * zAxis.z ) );

		b.Mins = center - extents;
		b.Maxs = center + extents;

		return b;
	}

	/// <summary>
	/// Transform this box by this amount and return
	/// </summary>
	public readonly BBox Transform( in Transform transform )
	{
		// Inspired by https://gist.github.com/cmf028/81e8d3907035640ee0e3fdd69ada543f (Solution3)
		Vector3 center = Center;
		Vector3 extents = Extents;

		// Transform center with the full transform
		Vector3 transformedCenter = transform.PointToWorld( center );

		// Get rotation matrix components and take absolute values
		// We need the absolute value of each rotation component multiplied by scale
		Rotation rotation = transform.Rotation;
		Vector3 scale = transform.Scale;

		// Axis transformation
		Vector3 absX = (rotation.Forward * scale.x).Abs();
		Vector3 absY = (rotation.Right * scale.y).Abs();
		Vector3 absZ = (rotation.Up * scale.z).Abs();

		// Apply absolute rotation+scale to extents (using dot product)
		Vector3 transformedExtents = new Vector3(
			absX.x * extents.x + absY.x * extents.y + absZ.x * extents.z,
			absX.y * extents.x + absY.y * extents.y + absZ.y * extents.z,
			absX.z * extents.x + absY.z * extents.y + absZ.z * extents.z
		);

		return new BBox(
			transformedCenter - transformedExtents,
			transformedCenter + transformedExtents
		);
	}

	/// <summary>
	/// Scale this box by this amount and return
	/// </summary>
	internal readonly BBox Scale( in Vector3 scale ) => new( Mins * scale, Maxs * scale );

	/// <summary>
	/// Returns a random point within this AABB.
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 RandomPointInside
	{
		get
		{
			return Random.Shared.VectorInCube( this );
		}
	}

	/// <summary>
	/// Returns a random point within this AABB.
	/// </summary>
	[JsonIgnore]
	public readonly Vector3 RandomPointOnEdge
	{
		get
		{
			var originalSize = Size;

			var size = originalSize;
			size.x *= SandboxSystem.Random.Float( 0.0f, 1.0f );
			size.y *= SandboxSystem.Random.Float( 0.0f, 1.0f );
			size.z *= SandboxSystem.Random.Float( 0.0f, 1.0f );

			var face = Random.Shared.Int( 0, 5 );
			if ( face == 0 ) size.x = 0;
			else if ( face == 1 ) size.y = 0;
			else if ( face == 2 ) size.z = 0;
			else if ( face == 3 ) size.x = originalSize.x;
			else if ( face == 4 ) size.y = originalSize.y;
			else if ( face == 5 ) size.z = originalSize.z;

			return Mins + size;
		}
	}

	/// <summary>
	/// Returns the physical volume of this AABB.
	/// </summary>
	[JsonIgnore]
	public readonly float Volume
	{
		get
		{
			var size = Size.Abs();
			return size.x * size.y * size.z;
		}
	}

	/// <summary>
	/// Returns true if this AABB completely contains given AABB
	/// </summary>
	public readonly bool Contains( in BBox b )
	{
		return b.Mins.x >= Mins.x && b.Maxs.x <= Maxs.x &&
			   b.Mins.y >= Mins.y && b.Maxs.y <= Maxs.y &&
			   b.Mins.z >= Mins.z && b.Maxs.z <= Maxs.z;
	}

	/// <summary>
	/// Returns true if this AABB contains given point
	/// </summary>
	public readonly bool Contains( in Vector3 b, float epsilon = 0.0001f )
	{
		return b.x >= Mins.x - epsilon && b.x <= Maxs.x + epsilon &&
			   b.y >= Mins.y - epsilon && b.y <= Maxs.y + epsilon &&
			   b.z >= Mins.z - epsilon && b.z <= Maxs.z + epsilon;
	}

	/// <summary>
	/// Returns true if this AABB somewhat overlaps given AABB
	/// </summary>
	public readonly bool Overlaps( in BBox b )
	{
		return Mins.x < b.Maxs.x && b.Mins.x < Maxs.x &&
				Mins.y < b.Maxs.y && b.Mins.y < Maxs.y &&
				Mins.z < b.Maxs.z && b.Mins.z < Maxs.z;
	}

	/// <summary>
	/// Returns this bbox but stretched to include given point
	/// </summary>
	public readonly BBox AddPoint( in Vector3 point )
	{
		var b = this;

		b.Mins = Vector3.Min( Mins, point );
		b.Maxs = Vector3.Max( Maxs, point );

		return b;
	}

	/// <summary>
	/// Returns this bbox but stretched to include given bbox
	/// </summary>
	public readonly BBox AddBBox( in BBox point )
	{
		var b = this;

		b.Mins = Vector3.Min( Mins, point.Mins );
		b.Maxs = Vector3.Max( Maxs, point.Maxs );

		return b;
	}

	/// <summary>
	/// Return a slightly bigger box
	/// </summary>
	public readonly BBox Grow( in float skin )
	{
		var b = this;

		b.Mins -= skin;
		b.Maxs += skin;

		return b;
	}

	/// <summary>
	/// Returns the closest point on this AABB to another point
	/// </summary>
	public readonly Vector3 ClosestPoint( in Vector3 point )
	{
		return Vector3.Clamp( point, Mins, Maxs );
	}

	/// <summary>
	/// Creates an AABB of <paramref name="radius"/> length and depth, and given <paramref name="height"/>
	/// </summary>
	public static BBox FromHeightAndRadius( float height, float radius )
	{
		return new BBox( (Vector3.One * -radius).WithZ( 0 ), (Vector3.One * radius).WithZ( height ) );
	}

	/// <summary>
	/// Creates an AABB at given position <paramref name="center"/> and given <paramref name="size"/> which acts as a <b>diameter</b> of a sphere contained within the AABB.
	/// </summary>
	public static BBox FromPositionAndSize( in Vector3 center, float size = 0.0f )
	{
		var o = new BBox();
		o.Mins = center - size * 0.5f;
		o.Maxs = center + size * 0.5f;
		return o;
	}

	/// <summary>
	/// Creates an AABB at given position <paramref name="center"/> and given <paramref name="size"/> a.k.a. "extents".
	/// </summary>
	public static BBox FromPositionAndSize( Vector3 center, Vector3 size )
	{
		var o = new BBox();

		o.Mins = System.Numerics.Vector3.FusedMultiplyAdd( -size, new Vector3( 0.5f ), center );
		o.Maxs = System.Numerics.Vector3.FusedMultiplyAdd( size, new Vector3( 0.5f ), center );

		return o;
	}

	public static BBox operator *( BBox c1, float c2 )
	{
		c1.Mins *= c2;
		c1.Maxs *= c2;
		return c1;
	}

	public static BBox operator +( BBox c1, Vector3 c2 )
	{
		c1.Mins += c2;
		c1.Maxs += c2;
		return c1;
	}

	/// <summary>
	/// Create a bounding box from an arbituary number of other boxes
	/// </summary>
	public static BBox FromBoxes( IEnumerable<BBox> boxes )
	{
		using var e = boxes.GetEnumerator();

		if ( !e.MoveNext() )
			return default;

		BBox bbox = e.Current;

		while ( e.MoveNext() )
		{
			bbox = bbox.AddBBox( e.Current );
		}

		return bbox;
	}

	/// <summary>
	/// Create a bounding box from an arbituary number of points
	/// </summary>
	public static BBox FromPoints( IEnumerable<Vector3> points, float size = 0.0f )
	{
		using var e = points.GetEnumerator();

		if ( !e.MoveNext() )
			return default;

		BBox bbox = BBox.FromPositionAndSize( e.Current, size );

		while ( e.MoveNext() )
		{
			bbox = bbox.AddBBox( BBox.FromPositionAndSize( e.Current, size ) );
		}

		return bbox;
	}

	/// <summary>
	/// Trace a ray against this box. If hit then return the distance.
	/// </summary>
	public readonly bool Trace( in Ray ray, float distance, out float hitDistance )
	{
		hitDistance = 0;

		int i;
		float d1, d2;
		float f;

		int nHitSide = -1;
		float t1 = -1.0f;
		float t2 = 1.0f;

		var _delta = ray.Forward.Normal * distance;

		bool startsolid = false;

		for ( i = 0; i < 6; ++i )
		{
			if ( i >= 3 )
			{
				d1 = ray.Position[i - 3] - Maxs[i - 3];
				d2 = d1 + _delta[i - 3];
			}
			else
			{
				d1 = -ray.Position[i] + Mins[i];
				d2 = d1 - _delta[i];
			}

			// if completely in front of face, no intersection
			if ( d1 > 0 && d2 > 0 )
				return false;

			// completely inside, check next face
			if ( d1 <= 0 && d2 <= 0 )
				continue;

			if ( d1 > 0 )
			{
				startsolid = false;
			}

			// crosses face
			if ( d1 > d2 )
			{
				f = d1;
				if ( f < 0 )
				{
					f = 0;
				}
				f = f / (d1 - d2);
				if ( f > t1 )
				{
					t1 = f;
					nHitSide = i;
				}
			}
			else
			{
				// leave
				f = (d1) / (d1 - d2);
				if ( f < t2 )
				{
					t2 = f;
					if ( nHitSide < 0 )
					{
						nHitSide = i;
					}
				}
			}
		}

		hitDistance = distance * t1;

		return startsolid || (t1 < t2 && t1 >= 0.0f);
	}

	/// <summary>
	/// Formats this AABB into a string "mins x,y,z, maxs x,y,z"
	/// </summary>
	public override readonly string ToString()
	{
		return $"mins {Mins:0.###}, maxs {Maxs:0.###}";
	}

	/// <summary>
	/// Get the volume of this AABB
	/// </summary>
	[Obsolete( "Use BBox.Volume instead." )]
	public float GetVolume()
	{
		return Volume;
	}

	/// <summary>
	/// Snap this AABB to a grid
	/// </summary>
	public readonly BBox Snap( float distance )
	{
		return new BBox( Mins.SnapToGrid( distance ), Maxs.SnapToGrid( distance ) );
	}

	/// <summary>
	/// Calculates the shortest distance from the specified local position to the nearest edge of the shape.
	/// </summary>
	public readonly float GetEdgeDistance( Vector3 localPos )
	{
		return MathF.Min(
			MathF.Min(
				MathF.Min( MathF.Abs( localPos.x - Mins.x ), MathF.Abs( localPos.x - Maxs.x ) ),
				MathF.Min( MathF.Abs( localPos.y - Mins.y ), MathF.Abs( localPos.y - Maxs.y ) )
			),
			MathF.Min( MathF.Abs( localPos.z - Mins.z ), MathF.Abs( localPos.z - Maxs.z ) )
		);
	}

	#region equality
	public static bool operator ==( BBox left, BBox right ) => left.Equals( right );
	public static bool operator !=( BBox left, BBox right ) => !(left == right);
	public readonly override bool Equals( object obj ) => obj is BBox o && Equals( o );
	public readonly bool Equals( BBox o ) => (Mins, Maxs) == (o.Mins, o.Maxs);
	public override readonly int GetHashCode() => HashCode.Combine( Mins, Maxs );
	#endregion
}
