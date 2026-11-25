using System.Text.Json.Nodes;

namespace NetSim;

/// <summary>
/// Prefabs and objects for the serialization tests and benchmarks: a player-like prefab with many components,
/// the engine's fire prefab sandbox puts on burning props.
/// </summary>
internal static class SerializationScene
{
	public const string PlayerPrefabPath = "netsim/player.prefab";
	public const string FirePrefabPath = "prefabs/engine/ignite.prefab";

	static PrefabFile _player;
	static PrefabFile _fire;

	static PrefabFile Register( string path, string rootObjectJson )
	{
		var prefabFile = new PrefabFile();
		prefabFile.LoadFromJson( "{ \"RootObject\": " + rootObjectJson + "}" );
		prefabFile.RegisterWeakResourceId( path );
		prefabFile.Register( path );
		return prefabFile;
	}

	/// <summary>
	/// Built in a scratch scene, the prefab file is shared by every peer.
	/// </summary>
	public static PrefabFile PlayerPrefab
	{
		get
		{
			if ( _player is not null ) return _player;

			var scene = new Scene();
			using ( scene.Push() )
			{
				var root = new GameObject( true, "player" );
				root.Tags.Add( "player" );

				// Only managed components, the sim has no resource or physics system
				root.Components.Create<SimStats>();
				root.Components.Create<SimInventory>();
				root.Components.Create<SimMovement>();
				root.Components.Create<SimCosmetics>();
				root.Components.Create<SimGrabber>();

				var body = new GameObject( root, true, "body" );
				body.Components.Create<SimCosmetics>();
				body.Components.Create<SimMovement>();

				var head = new GameObject( root, true, "head" );
				head.LocalPosition = new Vector3( 0, 0, 64 );
				head.Components.Create<SimStats>();

				var hand = new GameObject( root, true, "hand" );
				hand.Components.Create<SimInventory>();
				hand.Components.Create<ParticleEffect>( false );
				hand.Components.Create<ParticleSphereEmitter>( false );

				_player = Register( PlayerPrefabPath, root.Serialize().ToJsonString() );
			}

			scene.Destroy();
			return _player;
		}
	}

	public static PrefabFile FirePrefab
	{
		get
		{
			if ( _fire is not null ) return _fire;

			var path = System.IO.Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), "core", FirePrefabPath );
			var root = JsonNode.Parse( System.IO.File.ReadAllText( path ) )["RootObject"].AsObject();
			KeepResourceFreeComponents( root );
			_fire = Register( FirePrefabPath, root.ToJsonString() );
			return _fire;
		}
	}

	static readonly HashSet<string> ResourceFreeComponents = ["Sandbox.ParticleEffect", "Sandbox.ParticleSphereEmitter", "Sandbox.FireDamage"];

	/// <summary>
	/// The real fire's hierarchy and particle settings, minus renderers, sounds and model emitters, which load resources.
	/// </summary>
	static void KeepResourceFreeComponents( JsonObject go )
	{
		if ( go[GameObject.JsonKeys.Components] is JsonArray components )
		{
			foreach ( var component in components.ToArray() )
			{
				if ( !ResourceFreeComponents.Contains( (string)component[Component.JsonKeys.Type] ) )
					components.Remove( component );
				else
					component[Component.JsonKeys.Enabled] = false; // Particles trace against physics, which the sim lacks
			}
		}

		if ( go[GameObject.JsonKeys.Children] is JsonArray children )
		{
			foreach ( var child in children.OfType<JsonObject>() )
				KeepResourceFreeComponents( child );
		}
	}

	/// <summary>
	/// Host or owner side: a networked instance of the player prefab.
	/// </summary>
	public static GameObject SpawnPlayer( Vector3 position, Connection owner = null )
	{
		var go = GameObject.Clone( PlayerPrefab, new CloneConfig { Transform = new Transform( position ), StartEnabled = true } );
		if ( owner is null ) go.NetworkSpawn(); else go.NetworkSpawn( owner );
		return go;
	}

	/// <summary>
	/// What sandbox's Prop.Ignite does: clone the fire under the prop and refresh just that child.
	/// </summary>
	public static GameObject Ignite( GameObject prop )
	{
		var fire = GameObject.Clone( FirePrefab, new CloneConfig { Parent = prop, Transform = global::Transform.Zero, StartEnabled = true } );
		prop.Network.Refresh( fire );
		return fire;
	}

	public static readonly GameObject.SerializeOptions CreateOptions = new() { SingleNetworkObject = true, SkipNulls = true };

	/// <summary>
	/// What a peer would put in a create message for this object, without the parts that are only local.
	/// </summary>
	public static JsonObject Snapshot( GameObject go )
	{
		var json = go.Serialize( CreateOptions );
		Strip( json );
		return json;
	}

	static void Strip( JsonObject json )
	{
		// Flags carry runtime state like the network root, transforms of networked objects travel in snapshots
		json.Remove( GameObject.JsonKeys.Flags );

		if ( json[GameObject.JsonKeys.Children] is JsonArray children )
		{
			foreach ( var child in children.OfType<JsonObject>() )
				Strip( child );
		}
	}
}
