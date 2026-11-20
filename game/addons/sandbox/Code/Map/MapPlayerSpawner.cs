public sealed class MapPlayerSpawner : Component
{
	protected override void OnEnabled()
	{
		base.OnEnabled();

		if ( Components.TryGet<MapInstance>( out var mapInstance ) )
		{
			mapInstance.OnMapLoaded += RespawnPlayers;

			// already loaded
			if ( mapInstance.IsLoaded )
			{
				RespawnPlayers();
			}
		}
	}

	protected override void OnDisabled()
	{
		if ( Components.TryGet<MapInstance>( out var mapInstance ) )
		{
			mapInstance.OnMapLoaded -= RespawnPlayers;
		}

	}

	void RespawnPlayers()
	{
		var spawnPoints = Scene.GetAllComponents<SpawnPoint>().ToArray();

		foreach ( var player in Scene.GetAllComponents<Player>().ToArray() )
		{
			if ( player.IsProxy )
				continue;

			var randomSpawnPoint = Random.Shared.FromArray( spawnPoints );
			if ( randomSpawnPoint is null ) continue;

			player.WorldPosition = randomSpawnPoint.WorldPosition;
			player.Controller.EyeAngles = randomSpawnPoint.WorldRotation.Angles();
		}
	}
}
