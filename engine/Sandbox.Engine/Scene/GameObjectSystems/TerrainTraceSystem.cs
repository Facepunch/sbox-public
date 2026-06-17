namespace Sandbox;

[Expose]
sealed class TerrainTraceSystem : GameObjectSystem<TerrainTraceSystem>, GameObjectSystem.ITraceProvider
{
	public TerrainTraceSystem( Scene scene ) : base( scene )
	{
	}

	public void DoTrace( in SceneTrace trace, List<SceneTraceResult> results )
	{
		if ( !trace.IncludeRenderMeshes || trace.IncludePhysicsWorld )
			return;

		foreach ( var terrain in Scene.GetAllComponents<Terrain>() )
		{
			if ( TryTraceTerrain( terrain, trace, out var result ) )
				results.Add( result );
		}
	}

	public SceneTraceResult? DoTrace( in SceneTrace trace )
	{
		if ( !trace.IncludeRenderMeshes || trace.IncludePhysicsWorld )
			return null;

		SceneTraceResult? best = null;

		foreach ( var terrain in Scene.GetAllComponents<Terrain>() )
		{
			if ( !TryTraceTerrain( terrain, trace, out var result ) )
				continue;

			if ( best is null || result.Fraction < best.Value.Fraction )
				best = result;
		}

		return best;
	}

	private bool TryTraceTerrain( Terrain terrain, in SceneTrace trace, out SceneTraceResult result )
	{
		result = default;

		if ( !terrain.IsValid() || !terrain.Active )
			return false;

		if ( !PassesFilter( terrain.GameObject, trace ) )
			return false;

		var request = trace.PhysicsTrace.request;

		if ( !terrain.TraceHeightField( request.StartPos, request.EndPos, out var position, out var normal, out var fraction ) )
			return false;

		result = new SceneTraceResult
		{
			Scene = Scene,
			Hit = true,
			StartedSolid = false,
			StartPosition = request.StartPos,
			EndPosition = position,
			HitPosition = position,
			Normal = normal,
			Fraction = fraction,
			Direction = (request.EndPos - request.StartPos).Normal,
			GameObject = terrain.GameObject,
			Component = terrain,
			Surface = terrain.Surface,
		};

		return true;
	}

	private static bool PassesFilter( GameObject go, in SceneTrace trace )
	{
		if ( go is null )
			return false;

		if ( trace.IgnoreSingleObject.Contains( go ) )
			return false;

		for ( int i = 0; i < trace.IgnoreHierarchy.Length; i++ )
		{
			if ( go.IsAncestor( trace.IgnoreHierarchy[i] ) )
				return false;
		}

		return PassesTagFilter( go, trace.PhysicsTrace.request );
	}

	private static unsafe bool PassesTagFilter( GameObject go, in PhysicsTrace.Request requestRef )
	{
		var request = requestRef;
		var tokens = go.Tags.GetTokens();

		for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
		{
			var exclude = request.TagExclude[i];
			if ( exclude != 0 && tokens.Contains( exclude ) )
				return false;
		}

		for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
		{
			var require = request.TagRequire[i];
			if ( require != 0 && !tokens.Contains( require ) )
				return false;
		}

		bool anyRequired = false;

		for ( int i = 0; i < PhysicsTrace.Request.NumTagFields; i++ )
		{
			var any = request.TagAny[i];
			if ( any == 0 )
				continue;

			anyRequired = true;
			if ( tokens.Contains( any ) )
				return true;
		}

		return !anyRequired;
	}
}
