public sealed class BuilderAlignment
{
	const float SearchRadius = 1024f;
	const int MaxBlocks = 64;
	const float RefreshInterval = 0.25f;
	const float RefreshDistance = 128f;
	const float PointAcquire = 10f;
	const float PointRelease = 16f;
	const float HeightAcquire = 24f;
	const float HeightRelease = 40f;
	const float OffPlaneGuide = 1f;
	const int PointsPerBlock = 9;
	const float MinProjectedAxis = 0.2f;
	const float ParallelSine = 0.26f;
	const float MinRayDistance = 0.01f;

	public readonly record struct Guide( Vector3 From, Vector3 To, Vector3 Direction, bool Extend );

	readonly record struct SourceId( BuilderProp Block, int Index );

	readonly record struct Source( SourceId Id, Vector3 Position, Rotation Rotation );

	readonly record struct LineId( SourceId Source, int Axis );

	readonly record struct Line( LineId Id, Vector3 Source, Vector2 Origin, Vector2 Direction, float Score );

	readonly List<BuilderProp> _blocks = new();
	readonly List<Source> _sources = new();
	readonly List<Line> _lines = new();
	readonly List<LineId> _lastLines = new();
	readonly List<Guide> _guides = new();
	Vector3 _gatherCenter;
	RealTimeSince _sinceGather;
	bool _gathered;
	SourceId? _lastHeight;

	public IReadOnlyList<Guide> Guides => _guides;

	public void BeginFrame( bool active )
	{
		_guides.Clear();
		if ( active ) return;

		_lastLines.Clear();
		_lastHeight = null;
	}

	public Vector2 AlignPoint( Scene scene, CameraComponent camera, in BuilderPlane plane, Vector2 raw, Vector2 snapped )
	{
		var aim = plane.ToWorld( raw );
		Collect( scene, aim );
		FindLines( camera, plane, raw, aim );

		_lastLines.Clear();
		if ( _lines.Count == 0 ) return snapped;

		var first = _lines[0];
		var result = first.Origin + first.Direction * Vector2.Dot( snapped - first.Origin, first.Direction );
		Line? second = null;
		foreach ( var line in _lines )
		{
			if ( MathF.Abs( Cross( first.Direction, line.Direction ) ) <= ParallelSine ) continue;

			var crossing = Intersect( first, line );
			if ( ScreenDistance( camera, aim, plane.ToWorld( crossing ) ) >= PointRelease ) continue;

			second = line;
			result = crossing;
			break;
		}

		AddGuides( plane, first, result );
		if ( second is { } secondLine ) AddGuides( plane, secondLine, result );
		return result;
	}

	public bool TryAlignHeight( Scene scene, CameraComponent camera, Vector3 center, Vector3 normal, out float height )
	{
		height = 0;
		Collect( scene, center );

		var crosshair = camera.ScreenRect.Size * 0.5f;
		Source? best = null;
		Source? kept = null;
		var bestPixels = HeightAcquire;

		foreach ( var source in _sources )
		{
			var candidate = Vector3.Dot( source.Position - center, normal );
			if ( MathF.Abs( candidate ) < BuilderShapeKey.MinSize || MathF.Abs( candidate ) > BuilderShapeKey.MaxSize ) continue;
			if ( !TryScreen( camera, source.Position, out var screen ) ) continue;

			var pixels = screen.Distance( crosshair );
			if ( source.Id == _lastHeight && pixels < HeightRelease ) kept = source;
			if ( pixels >= bestPixels ) continue;

			bestPixels = pixels;
			best = source;
		}

		var chosen = kept ?? best;
		_lastHeight = chosen?.Id;
		if ( chosen is not { } picked ) return false;

		height = Vector3.Dot( picked.Position - center, normal );
		_guides.Add( HeightGuide( picked, center + normal * height, normal ) );
		return true;
	}

	static Guide HeightGuide( Source source, Vector3 top, Vector3 normal )
	{
		var bestDirection = normal;
		var bestAlong = 0f;
		var bestDistance = float.MaxValue;

		for ( int axis = 0; axis < 3; axis++ )
		{
			var along = Axis( source.Rotation, axis );
			var direction = along - normal * Vector3.Dot( along, normal );
			if ( direction.Length < MinProjectedAxis ) continue;

			direction = direction.Normal;
			var t = Vector3.Dot( top - source.Position, direction );
			var distance = (source.Position + direction * t).Distance( top );
			if ( distance >= bestDistance ) continue;

			bestDistance = distance;
			bestDirection = direction;
			bestAlong = t;
		}

		var end = source.Position + bestDirection * bestAlong;
		return new Guide( source.Position, end, bestAlong < 0 ? -bestDirection : bestDirection, true );
	}

	public Vector2 AlignOnRay( Scene scene, CameraComponent camera, in BuilderPlane plane, Vector2 origin, Vector2 direction, Vector2 raw, Vector2 snapped )
	{
		var aim = plane.ToWorld( raw );
		Collect( scene, aim );
		FindLines( camera, plane, raw, aim );

		_lastLines.Clear();
		foreach ( var line in _lines )
		{
			var turn = Cross( direction, line.Direction );
			if ( MathF.Abs( turn ) <= ParallelSine ) continue;

			var distance = Cross( line.Origin - origin, line.Direction ) / turn;
			if ( distance <= MinRayDistance ) continue;

			var crossing = origin + direction * distance;
			if ( ScreenDistance( camera, aim, plane.ToWorld( crossing ) ) >= PointRelease ) continue;

			AddGuides( plane, line, crossing );
			return crossing;
		}

		return snapped;
	}

	static Vector3 Axis( Rotation rotation, int axis ) => axis == 0 ? rotation.Forward : axis == 1 ? rotation.Right : rotation.Up;

	void FindLines( CameraComponent camera, in BuilderPlane plane, Vector2 raw, Vector3 aim )
	{
		_lines.Clear();
		if ( !TryScreen( camera, aim, out var aimScreen ) ) return;

		foreach ( var source in _sources )
		{
			if ( !TryScreen( camera, source.Position, out _ ) ) continue;

			var origin = plane.ToPlane( source.Position );
			for ( int axis = 0; axis < 3; axis++ )
			{
				var along = Axis( source.Rotation, axis );
				var direction = new Vector2( Vector3.Dot( along, plane.Right ), Vector3.Dot( along, plane.Forward ) );
				if ( direction.Length < MinProjectedAxis ) continue;

				direction = direction.Normal;
				var closest = origin + direction * Vector2.Dot( raw - origin, direction );
				var id = new LineId( source.Id, axis );
				var kept = _lastLines.Contains( id );
				var limit = kept ? PointRelease : PointAcquire;

				if ( !TryScreen( camera, plane.ToWorld( closest ), out var closestScreen ) ) continue;

				var pixels = closestScreen.Distance( aimScreen );
				if ( pixels >= limit ) continue;

				_lines.Add( new Line( id, source.Position, origin, direction, kept ? pixels - PointRelease : pixels ) );
			}
		}

		_lines.Sort( ( a, b ) => a.Score.CompareTo( b.Score ) );
	}

	void AddGuides( in BuilderPlane plane, Line line, Vector2 result )
	{
		_lastLines.Add( line.Id );

		var onPlane = plane.ToWorld( line.Origin );
		var target = plane.ToWorld( result );
		var direction = (plane.Right * line.Direction.x + plane.Forward * line.Direction.y).Normal;
		if ( Vector3.Dot( target - onPlane, direction ) < 0 ) direction = -direction;

		_guides.Add( new Guide( onPlane, target, direction, true ) );
		if ( line.Source.Distance( onPlane ) > OffPlaneGuide ) _guides.Add( new Guide( line.Source, onPlane, (onPlane - line.Source).Normal, false ) );
	}

	static Vector2 Intersect( Line a, Line b ) => a.Origin + a.Direction * (Cross( b.Origin - a.Origin, b.Direction ) / Cross( a.Direction, b.Direction ));

	static float Cross( Vector2 a, Vector2 b ) => a.x * b.y - a.y * b.x;

	static float ScreenDistance( CameraComponent camera, Vector3 a, Vector3 b )
	{
		if ( !TryScreen( camera, a, out var sa ) || !TryScreen( camera, b, out var sb ) ) return float.MaxValue;
		return sa.Distance( sb );
	}

	static bool TryScreen( CameraComponent camera, Vector3 point, out Vector2 screen )
	{
		screen = default;
		if ( Vector3.Dot( point - camera.WorldPosition, camera.WorldRotation.Forward ) <= 0 ) return false;

		screen = camera.PointToScreenPixels( point );
		var size = camera.ScreenRect.Size;
		return screen.x > 0 && screen.y > 0 && screen.x < size.x && screen.y < size.y;
	}

	void Collect( Scene scene, Vector3 center )
	{
		Gather( scene, center );

		_sources.Clear();
		foreach ( var block in _blocks )
		{
			if ( !block.IsValid() || !block.Renderer.IsValid() || block.Renderer.Model is not { IsError: false } model ) continue;

			var bounds = model.Bounds;
			var transform = block.Renderer.WorldTransform;
			for ( int i = 0; i < PointsPerBlock; i++ )
			{
				var local = i == PointsPerBlock - 1
					? bounds.Center
					: new Vector3( (i & 1) == 0 ? bounds.Mins.x : bounds.Maxs.x, (i & 2) == 0 ? bounds.Mins.y : bounds.Maxs.y, (i & 4) == 0 ? bounds.Mins.z : bounds.Maxs.z );

				_sources.Add( new Source( new SourceId( block, i ), transform.PointToWorld( local ), transform.Rotation ) );
			}
		}
	}

	void Gather( Scene scene, Vector3 center )
	{
		if ( _gathered && _sinceGather < RefreshInterval && center.Distance( _gatherCenter ) < RefreshDistance ) return;

		_gathered = true;
		_sinceGather = 0;
		_gatherCenter = center;
		_blocks.Clear();

		foreach ( var go in scene.FindInPhysics( new Sphere( center, SearchRadius ) ) )
		{
			var block = go.GetComponentInParent<BuilderProp>();
			if ( !block.IsValid() || _blocks.Contains( block ) ) continue;
			if ( block.Renderer?.Model is not { IsError: false } ) continue;

			_blocks.Add( block );
		}

		_blocks.Sort( ( a, b ) => a.WorldPosition.DistanceSquared( center ).CompareTo( b.WorldPosition.DistanceSquared( center ) ) );
		if ( _blocks.Count > MaxBlocks ) _blocks.RemoveRange( MaxBlocks, _blocks.Count - MaxBlocks );
	}
}
