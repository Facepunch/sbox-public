using Facepunch.ActionGraphs;
using System.Text.Json.Nodes;

namespace Sandbox;

public partial class Scene : GameObject
{
	/// <summary>
	/// Load from the provided <see cref="SceneFile"/>. This will not load the scene for other clients in a
	/// multiplayer session, you should instead use <see cref="Game.ChangeScene"/>
	/// if you want to bring other clients.
	/// </summary>
	public virtual bool Load( GameResource resource )
	{
		if ( resource is SceneFile sf )
		{
			var options = new SceneLoadOptions();

			if ( !options.SetScene( sf ) )
				return false;

			return Load( options );
		}

		return false;
	}

	/// <summary>
	/// Load from the provided <see cref="SceneLoadOptions"/>. This will not load the scene for other clients in a
	/// multiplayer session, you should instead use <see cref="Game.ChangeScene"/>
	/// if you want to bring other clients.
	/// </summary>
	public bool Load( SceneLoadOptions options )
	{
		var sceneFile = options.GetSceneFile();

		if ( !sceneFile.IsValid() )
		{
			Log.Error( "No valid Scene was found in SceneLoadOptions." );
			return false;
		}

		if ( sceneFile.ResourceName != null )
		{
			Name = sceneFile.ResourceName.ToTitleCase();
		}

		ProcessDeletes();

		if ( !options.IsAdditive )
		{
			if ( options.DeleteEverything )
			{
				Clear( true );
			}
			else
			{
				// get all the gameobjects that should survive
				var savedObjects = GetAllObjects( false ).Where( x => x.Flags.Contains( GameObjectFlags.DontDestroyOnLoad ) );

				// move them to the scene root
				foreach ( var saved in savedObjects )
				{
					saved.SetParent( this );
				}

				Clear( false );
			}

			ProcessDeletes();
		}

		if ( !IsEditor && options.ShowLoadingScreen )
		{
			StartLoading();
			LoadingScreen.IsVisible = true;
			LoadingScreen.Title = "Loading Scene";
		}

		RunEvent<ISceneLoadingEvents>( x => x.BeforeLoad( this, options ) );

		if ( sceneFile.Id != Guid.Empty && sceneFile.Id != Id )
		{
			ForceChangeId( sceneFile.Id );
			Directory.Add( this );
		}

		if ( !options.IsAdditive )
		{
			Source = sceneFile;
		}

		{
			using var optionsScope = ActionGraph.PushSerializationOptions( sceneFile.SerializationOptions with { ForceUpdateCached = IsEditor } );
			using var sceneScope = Push();
			using var batchGroup = CallbackBatch.Batch();

			// Depending on if we load a scene from file or from memory, we need to account for that here
			using var blobs = sceneFile.BinaryData != null
				? BlobDataSerializer.LoadFromMemory( sceneFile.BinaryData )
				: BlobDataSerializer.LoadFrom( sceneFile.ResourcePath );

			if ( sceneFile.GameObjects is not null )
			{
				foreach ( var json in sceneFile.GameObjects )
				{
					var go = CreateObject( false );
					go.Deserialize( json );
				}
			}

			if ( sceneFile.SceneProperties is not null )
			{
				DeserializeProperties( sceneFile.SceneProperties, options.IsSystemScene );
			}

			//
			// Let ISceneLoadingEvents add their own tasks
			//
			List<LoadingContext> sceneLoadingTasks = new();
			RunEvent<ISceneLoadingEvents>( x =>
			{
				var context = new LoadingContext();
				context.Task = x.OnLoad( this, options, context );

				sceneLoadingTasks.Add( context );
			} );

			foreach ( var task in sceneLoadingTasks )
			{
				AddLoadingTask( task );
			}

			if ( !IsEditor )
			{
				NetworkSpawnRecursive( null );
			}
		}

		// Now that we're done, add the system scene
		if ( !IsEditor && !options.IsAdditive )
		{
			AddSystemScene();
		}

		if ( !options.IsSystemScene )
		{
			// Now we can signal to GameObjectSystems that we have finished loading.
			// We wrap this in an IsSystemScene check so that it's not called twice
			// for every scene load.
			Signal( GameObjectSystem.Stage.SceneLoaded );
		}

		return true;
	}

	/// <summary>
	/// Load from the provided file name. This will not load the scene for other clients in a
	/// multiplayer session, you should instead use <see cref="Game.ChangeScene"/>
	/// if you want to bring other clients.
	/// </summary>
	public bool LoadFromFile( string filename )
	{
		var options = new SceneLoadOptions();

		if ( !options.SetScene( filename ) )
			return false;

		return Load( options );
	}

	public override JsonObject Serialize( SerializeOptions options = null )
	{
		if ( this is PrefabScene )
		{
			return base.Serialize( options );
		}

		var json = new JsonObject
		{
			{ "Type", "Scene" },
			{ "Properties", SerializeProperties() },
		};

		var children = new JsonArray();

		using var sceneScope = Push();

		foreach ( var child in Children )
		{
			var jso = child.Serialize( options );
			if ( jso is null ) continue;

			children.Add( jso );
		}

		json.Add( "GameObjects", children );

		return json;
	}

	public override void Deserialize( JsonObject node, DeserializeOptions option )
	{
		if ( this is PrefabScene )
		{
			base.Deserialize( node, option );
			return;
		}

		ProcessDeletes();
		Clear();

		if ( node.TryGetPropertyValue( "Properties", out var props ) )
		{
			DeserializeProperties( props.AsObject() );
		}

		using var sceneScope = Push();
		using var batchGroup = CallbackBatch.Batch();

		if ( node["GameObjects"] is JsonArray childArray )
		{
			foreach ( var child in childArray )
			{
				if ( child is not JsonObject jso )
					return;

				var go = new GameObject( false );

				go.Parent = this;
				go.Deserialize( jso, option );
			}
		}
	}

	internal JsonObject SerializeProperties()
	{
		var jso = new JsonObject();

		foreach ( var prop in Game.TypeLibrary.GetType<Scene>()
			.Properties
			.Where( x => x.HasAttribute<PropertyAttribute>() )
			.OrderBy( x => x.Name ) )
		{
			if ( prop.Name == "Enabled" ) continue;
			if ( prop.Name == "Name" ) continue;
			if ( prop.Name == "Lerp" ) continue;

			jso.Add( prop.Name, JsonValue.Create( prop.GetValue( this ) ) );
		}

		jso.Add( "Metadata", SerializeMetadata() );
		jso.Add( "NavMesh", NavMesh.Serialize() );
		jso.Add( "GameObjectSystems", SerializeGameObjectSystems() );

		return jso;
	}

	JsonObject SerializeMetadata()
	{
		var metadata = new JsonObject();
		foreach ( var c in GetAllComponents<ISceneMetadata>() )
		{
			var data = c.GetMetadata();
			if ( data is null ) continue;

			foreach ( var entry in data )
			{
				metadata[entry.Key] = entry.Value;
			}
		}
		return metadata;
	}

	JsonArray SerializeGameObjectSystems()
	{
		var array = new JsonArray();

		foreach ( var system in GetSystems() )
		{
			var systemType = Game.TypeLibrary.GetType( system.GetType() );
			if ( systemType is null ) continue;

			// Get only properties with [Property] attribute
			var properties = systemType.Properties.Where( x => x.HasAttribute<PropertyAttribute>() ).ToList();
			if ( properties.Count == 0 ) continue;

			try
			{
				var systemJson = new JsonObject();

				// Serialize only [Property] properties
				foreach ( var prop in properties )
				{
					try
					{
						var value = prop.GetValue( system );
						systemJson[prop.Name] = Json.ToNode( value );
					}
					catch ( System.Exception e )
					{
						Log.Warning( e, $"Error serializing {system.GetType().Name}.{prop.Name}: {e.Message}" );
					}
				}

				// Add type and guid metadata
				systemJson["__type"] = systemType.ClassName;
				systemJson["__guid"] = system.Id.ToString();

				array.Add( systemJson );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error serializing {system.GetType().Name}: {e.Message}" );
			}
		}

		return array;
	}

	void DeserializeProperties( JsonObject data, bool isSystemScene = false )
	{
		var sceneType = Game.TypeLibrary.GetType<Scene>();
		Assert.NotNull( sceneType, "Scene type is inaccessible!" );

		foreach ( var prop in sceneType.Properties.Where( x => x.HasAttribute<PropertyAttribute>() ) )
		{
			if ( prop.Name == "Enabled" ) continue;
			if ( prop.Name == "Name" ) continue;
			if ( prop.Name == "Lerp" ) continue;

			if ( !data.TryGetPropertyValue( prop.Name, out JsonNode node ) )
				continue;

			try
			{
				prop.SetValue( this, Json.FromNode( node, prop.PropertyType ) );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when deserializing {this}.{prop.Name} ({e.Message})" );
			}
		}

		//
		// We don't want navmesh to be overwritten by system scene loads
		//
		if ( !isSystemScene )
		{
			NavMesh.Deserialize( data["NavMesh"] as JsonObject );
		}

		// Deserialize GameObjectSystems
		if ( data.TryGetPropertyValue( "GameObjectSystems", out var systemsNode ) && systemsNode is JsonArray systemsArray )
		{
			DeserializeGameObjectSystems( systemsArray );
		}
	}

	void DeserializeGameObjectSystems( JsonArray systemsArray )
	{
		foreach ( var systemNode in systemsArray )
		{
			if ( systemNode is not JsonObject systemJson ) continue;

			try
			{
				// Get the system type
				if ( !systemJson.TryGetPropertyValue( "__type", out var typeNode ) )
					continue;

				var typeName = typeNode.ToString();
				var systemType = Game.TypeLibrary.GetType( typeName );
				if ( systemType is null )
				{
					Log.Warning( $"Could not find GameObjectSystem type: {typeName}" );
					continue;
				}

				// Find the system instance by type
				var system = GetSystemByType( systemType );
				if ( system is null )
				{
					Log.Warning( $"Could not find GameObjectSystem instance: {typeName}" );
					continue;
				}

				// Update the GUID if provided
				if ( systemJson.TryGetPropertyValue( "__guid", out var guidNode ) && Guid.TryParse( guidNode.ToString(), out var guid ) )
				{
					system.Id = guid;
				}

				// Deserialize all properties at once
				Json.DeserializeToObject( system, systemJson );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error deserializing GameObjectSystem: {e.Message}" );
			}
		}
	}


	/// <summary>
	/// Create a new SceneFile from this scene
	/// </summary>
	internal SceneFile CreateSceneFile()
	{
		var a = new SceneFile();
		ToSceneFile( a );
		return a;
	}

	/// <summary>
	/// Save the contents of this scene to the SceneFile
	/// </summary>
	internal void ToSceneFile( SceneFile target )
	{
		Assert.IsValid( this );

		target.ActionGraphCache.Clear();

		using var sceneScope = Push();
		using var optionsScope = target.PushSerializationScope();
		using var blobs = BlobDataSerializer.Capture();

		target.Id = Id;
		target.GameObjects = Children.Select( x => x.Serialize() ).Where( x => x is not null ).ToArray();
		target.SceneProperties = SerializeProperties();
		target.BinaryData = blobs.ToByteArray();
	}
}
