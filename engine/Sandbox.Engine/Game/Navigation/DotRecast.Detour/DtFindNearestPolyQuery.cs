namespace DotRecast.Detour
{
	internal class DtFindNearestPolyQuery : IDtPolyQuery
	{
		private DtNavMeshQuery _query;
		private Vector3 _center;
		private float _nearestDistanceSqr;
		private long _nearestRef;
		private Vector3 _nearestPoint;
		private bool _overPoly;

		public DtFindNearestPolyQuery( DtNavMeshQuery query, Vector3 center )
		{
			Init( query, center );
		}

		public void Init( DtNavMeshQuery query, Vector3 center )
		{
			_query = query;
			_center = center;
			_nearestDistanceSqr = float.MaxValue;
			_nearestRef = 0;
			_nearestPoint = center;
			_overPoly = false;
		}

		public void Process( DtMeshTile tile, Span<long> refs, int count )
		{
			for ( int i = 0; i < count; ++i )
			{
				long polyRef = refs[i];
				float d;

				// Find nearest polygon amongst the nearby polygons.
				_query.ClosestPointOnPoly( polyRef, _center, out var closestPtPoly, out var posOverPoly );

				// If a point is directly over a polygon and closer than
				// climb height, favor that instead of straight line nearest point.
				Vector3 diff = _center - closestPtPoly;
				if ( posOverPoly )
				{
					d = MathF.Abs( diff.y ) - tile.data.header.walkableClimb;
					d = d > 0 ? d * d : 0;
				}
				else
				{
					d = diff.LengthSquared;
				}

				if ( d < _nearestDistanceSqr )
				{
					_nearestPoint = closestPtPoly;
					_nearestDistanceSqr = d;
					_nearestRef = polyRef;
					_overPoly = posOverPoly;
				}
			}
		}

		public long NearestRef()
		{
			return _nearestRef;
		}

		public Vector3 NearestPt()
		{
			return _nearestPoint;
		}

		public bool OverPoly()
		{
			return _overPoly;
		}
	}
}
