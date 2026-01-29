using System.Runtime.InteropServices;
namespace Sandbox;

/// <summary>
/// Represents a <a href="https://en.wikipedia.org/wiki/Frustum">frustum</a>.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct Frustum : System.IEquatable<Frustum>
{
	/// <summary>
	/// Right plane of the frustum, pointing inwards.
	/// </summary>
	public Plane RightPlane;

	/// <summary>
	/// Left plane of the frustum, pointing inwards.
	/// </summary>
	public Plane LeftPlane;

	/// <summary>
	/// Top plane of the frustum, pointing inwards.
	/// </summary>
	public Plane TopPlane;

	/// <summary>
	/// Bottom plane of the frustum, pointing inwards.
	/// </summary>
	public Plane BottomPlane;

	/// <summary>
	/// Near plane of the frustum, pointing inwards.
	/// </summary>
	public Plane NearPlane;

	/// <summary>
	/// Far plane of the frustum, pointing inwards.
	/// </summary>
	public Plane FarPlane;

	/// <summary>
	/// Creates a frustum from 6 planes.
	/// </summary>
	public Frustum( Plane right, Plane left, Plane top, Plane bottom, Plane near, Plane far )
	{
		RightPlane = right;
		LeftPlane = left;
		TopPlane = top;
		BottomPlane = bottom;
		NearPlane = near;
		FarPlane = far;
	}

	/// <summary>
	/// Returns the corner point of one of the 8 corners.
	/// This may return null if i is > 7 or the frustum is invalid.
	/// </summary>
	public readonly Vector3? GetCorner( int i )
	{
		switch ( i )
		{
			case 0: return Plane.GetIntersection( NearPlane, LeftPlane, TopPlane );
			case 1: return Plane.GetIntersection( NearPlane, TopPlane, RightPlane );
			case 2: return Plane.GetIntersection( NearPlane, RightPlane, BottomPlane );
			case 3: return Plane.GetIntersection( NearPlane, BottomPlane, LeftPlane );

			case 4: return Plane.GetIntersection( FarPlane, LeftPlane, TopPlane );
			case 5: return Plane.GetIntersection( FarPlane, TopPlane, RightPlane );
			case 6: return Plane.GetIntersection( FarPlane, RightPlane, BottomPlane );
			case 7: return Plane.GetIntersection( FarPlane, BottomPlane, LeftPlane );
		}

		return null;
	}

	/// <summary>
	/// Returns the AABB of this frustum.
	/// </summary>
	public readonly BBox GetBBox()
	{
		var bbox = BBox.FromPositionAndSize( GetCorner( 0 ).Value );

		for ( int i = 1; i < 8; i++ )
		{
			bbox = bbox.AddPoint( GetCorner( i ).Value );
		}

		return bbox;
	}

	/// <summary>
	/// Returns whether the given point is inside this frustum.
	/// </summary>
	public readonly bool IsInside( in Vector3 point )
	{
		if ( !LeftPlane.IsInFront( point ) ) return false;
		if ( !RightPlane.IsInFront( point ) ) return false;
		if ( !TopPlane.IsInFront( point ) ) return false;
		if ( !BottomPlane.IsInFront( point ) ) return false;
		if ( !NearPlane.IsInFront( point ) ) return false;
		if ( !FarPlane.IsInFront( point ) ) return false;

		return true;
	}

	/// <summary>
	/// Returns whether given AABB is inside this frustum.
	/// </summary>
	/// <param name="box">The AABB to test.</param>
	/// <param name="partially">Whether test for partial intersection, or complete encompassing of the AABB within this frustum.</param>
	public readonly bool IsInside( in BBox box, bool partially = false )
	{
		if ( !LeftPlane.IsInFront( box, partially ) ) return false;
		if ( !RightPlane.IsInFront( box, partially ) ) return false;
		if ( !TopPlane.IsInFront( box, partially ) ) return false;
		if ( !BottomPlane.IsInFront( box, partially ) ) return false;
		if ( !NearPlane.IsInFront( box, partially ) ) return false;
		if ( !FarPlane.IsInFront( box, partially ) ) return false;

		return true;
	}

	/// <summary>
	/// Returns whether the given sphere is inside this frustum.
	/// </summary>
	/// <param name="center">The center of the sphere.</param>
	/// <param name="radius">The radius of the sphere.</param>
	/// <param name="partially">Whether test for partial intersection, or complete encompassing of the sphere within this frustum.</param>
	public readonly bool IsInside( in Vector3 center, float radius, bool partially = false )
	{
		float normalizedRadius = Math.Abs( radius );
		float threshold = partially ? -normalizedRadius : normalizedRadius;

		if ( LeftPlane.GetDistance( center ) < threshold ) return false;
		if ( RightPlane.GetDistance( center ) < threshold ) return false;
		if ( TopPlane.GetDistance( center ) < threshold ) return false;
		if ( BottomPlane.GetDistance( center ) < threshold ) return false;
		if ( NearPlane.GetDistance( center ) < threshold ) return false;
		if ( FarPlane.GetDistance( center ) < threshold ) return false;

		return true;
	}

	/// <summary>
	/// Returns whether the given sphere is inside this frustum.
	/// </summary>
	/// <param name="sphere">The sphere to test against.</param>
	/// <param name="partially">Whether test for partial intersection, or complete encompassing of the sphere within this frustum.</param>
	public readonly bool IsInside( in Sphere sphere, bool partially = false ) => IsInside( sphere.Center, sphere.Radius, partially );

	/// <summary>
	/// Create a frustum from four corner rays. These rays commonly come from SceneCamera.GetRay.
	/// </summary>
	public static Frustum FromCorners( in Ray tl, in Ray tr, in Ray br, in Ray bl, float near, float far )
	{
		Frustum f = default;

		var forward = (tl.Forward + br.Forward + tr.Forward + bl.Forward).Normal;

		f.TopPlane = new Plane( tl.Position, Vector3.Cross( tl.Forward, tr.Forward ) );
		f.LeftPlane = new Plane( tl.Position, Vector3.Cross( bl.Forward, tl.Forward ) );
		f.RightPlane = new Plane( br.Position, Vector3.Cross( tr.Forward, br.Forward ) );
		f.BottomPlane = new Plane( br.Position, Vector3.Cross( br.Forward, bl.Forward ) );
		f.NearPlane = new Plane( tl.Position + forward * near, forward );
		f.FarPlane = new Plane( tl.Position + forward * far, -forward );

		return f;
	}

	internal static Frustum FromOrtho(
		Vector2 screenMin,
		Vector2 screenMax,
		Vector3 screenSize,
		Vector3 cameraPosition,
		Rotation cameraRotation,
		float orthoHeight,
		float zNear,
		float zFar )
	{
		var aspectRatio = screenSize.x / screenSize.y;
		var halfHeight = orthoHeight * 0.5f;
		var halfWidth = halfHeight * aspectRatio;

		var screenSize2 = (Vector2)screenSize;
		var min = (screenMin / screenSize2) * 2.0f - Vector2.One;
		var max = (screenMax / screenSize2) * 2.0f - Vector2.One;

		var left = min.x * halfWidth;
		var right = max.x * halfWidth;
		var bottom = -max.y * halfHeight;
		var top = -min.y * halfHeight;

		var rightDir = cameraRotation.Right;
		var upDir = cameraRotation.Up;
		var forwardDir = cameraRotation.Forward;

		return new Frustum
		{
			LeftPlane = new( cameraPosition + rightDir * left, rightDir ),
			RightPlane = new( cameraPosition + rightDir * right, -rightDir ),
			BottomPlane = new( cameraPosition + upDir * bottom, upDir ),
			TopPlane = new( cameraPosition + upDir * top, -upDir ),
			NearPlane = new( cameraPosition + forwardDir * zNear, forwardDir ),
			FarPlane = new( cameraPosition + forwardDir * zFar, -forwardDir )
		};
	}

	internal readonly unsafe bool TryGetCorners( Vector3* outCorners )
	{
		for ( int i = 0; i < 8; i++ )
		{
			var corner = GetCorner( i );
			if ( !corner.HasValue )
				return false;

			outCorners[i] = corner.Value;
		}

		return true;
	}

	#region equality
	public static bool operator ==( Frustum left, Frustum right ) => left.Equals( right );
	public static bool operator !=( Frustum left, Frustum right ) => !(left == right);
	public override bool Equals( object obj ) => obj is Frustum o && Equals( o );
	public readonly bool Equals( Frustum o ) => (LeftPlane, RightPlane, TopPlane, BottomPlane, NearPlane, FarPlane) == (o.LeftPlane, o.RightPlane, o.TopPlane, o.BottomPlane, o.NearPlane, o.FarPlane);
	public override readonly int GetHashCode() => HashCode.Combine( LeftPlane, RightPlane, TopPlane, BottomPlane, NearPlane, FarPlane );
	#endregion
}
