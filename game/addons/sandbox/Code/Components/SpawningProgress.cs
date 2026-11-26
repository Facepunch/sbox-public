/// <summary>
/// Shows a cloud prop at its requested placement while the host spawns it and the local model loads.
/// </summary>
public sealed class SpawningProgress : Component
{
	Guid _request;
	PropSpawner _prop;
	Connection _host;
	bool _spawned;

	internal static Guid Begin( Player player, Connection owner, PropSpawner prop, Transform surface )
	{
		if ( owner == Connection.Local && prop.Loading.IsCompleted ) return Guid.Empty;

		var request = Guid.NewGuid();
		if ( owner == Connection.Local )
		{
			Create( player, request, prop, surface );
		}
		else
		{
			using ( Rpc.FilterInclude( owner ) )
				BeginOnClient( player, request, prop.Path, surface );
		}

		return request;
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	static void BeginOnClient( Player player, Guid request, string path, Transform surface )
	{
		if ( player.IsValid() )
			Create( player, request, new PropSpawner( path ), surface );
	}

	static void Create( Player player, Guid request, PropSpawner prop, Transform surface )
	{
		var go = player.Scene.CreateObject( false );
		go.Name = "Spawning prop";
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Absolute;
		go.Parent = player.GameObject;
		go.WorldTransform = surface;

		var preview = go.AddComponent<SpawningProgress>();
		preview._request = request;
		preview._prop = prop;
		preview._host = Connection.Host;
		go.Enabled = true;
	}

	internal static void End( Connection owner, Guid request, bool spawned )
	{
		if ( request == Guid.Empty ) return;

		using ( Rpc.FilterInclude( owner ) )
			EndOnClient( request, spawned );
	}

	[Rpc.Broadcast( NetFlags.HostOnly | NetFlags.Reliable )]
	static void EndOnClient( Guid request, bool spawned )
	{
		var preview = Game.ActiveScene?.GetAllComponents<SpawningProgress>().FirstOrDefault( x => x._request == request );
		if ( !preview.IsValid() ) return;

		if ( spawned )
		{
			preview._spawned = true;
		}
		else
		{
			preview.GameObject.Destroy();
		}
	}

	protected override void OnUpdate()
	{
		if ( _prop is null ) return;
		if ( _host != Connection.Host || (_prop.Loading.IsCompleted && !_prop.IsReady) )
		{
			GameObject.Destroy();
			return;
		}

		if ( !_prop.DrawLoadingBox( Scene, WorldTransform, finished: _spawned && _prop.IsReady ) )
			GameObject.Destroy();
	}
}
