namespace Sandbox;

public partial class HitboxSet
{
	/// <summary>
	/// A convex model hitbox defined by points in bone-local space.
	/// </summary>
	public sealed class Hull
	{
		/// <summary>The points used to build the physics shape.</summary>
		public IReadOnlyList<Vector3> Points { get; }

		readonly BBox bounds;
		readonly Vector3 center;
		readonly Plane[] planes;

		internal unsafe Hull( CHitBox hitbox )
		{
			bounds = new BBox( hitbox.m_vMinBounds, hitbox.m_vMaxBounds );
			var points = new ReadOnlySpan<Vector3>( (void*)hitbox.GetHullPoints(), hitbox.GetHullPointCount() );
			Points = Array.AsReadOnly( points.ToArray() );
			foreach ( var vertex in points )
				center += vertex;
			center /= points.Length;

			var facePlanes = hitbox.GetHullPlanes( out var count );
			planes = new ReadOnlySpan<Plane>( (void*)facePlanes, count ).ToArray();
		}

		/// <summary>A random point on the surface of the hull.</summary>
		public Vector3 RandomPointOnEdge
		{
			get
			{
				var direction = Game.Random.VectorOnSphere();
				float distance = float.PositiveInfinity;
				foreach ( var plane in planes )
				{
					float dot = Vector3.Dot( direction, plane.Normal );
					if ( dot > 0 )
						distance = MathF.Min( distance, (plane.Distance - Vector3.Dot( center, plane.Normal )) / dot );
				}

				return float.IsFinite( distance ) ? center + direction * distance : center;
			}
		}

		/// <summary>A random point inside the hull.</summary>
		public Vector3 RandomPointInside
		{
			get
			{
				if ( planes.Length == 0 ) return center;

				// A thin, rotated hull may fill very little of its bounding box.
				for ( int i = 0; i < 32; i++ )
				{
					var point = bounds.RandomPointInside;
					if ( Contains( point ) ) return point;
				}

				return center;
			}
		}

		private bool Contains( Vector3 point )
		{
			foreach ( var plane in planes )
			{
				if ( plane.IsInFront( point ) ) return false;
			}

			return true;
		}
	}
}
