using Facepunch.ActionGraphs;
using System.Text.Json.Nodes;

namespace Sandbox;

public static class SceneUtility
{
	/// <summary>
	/// Find all "__guid" guids, and replace them with new guids. This is used to make GameObject serializations unique,
	/// so when you duplicate stuff, it copies over uniquely and keeps associations.
	/// </summary>
	public static Dictionary<Guid, Guid> MakeIdGuidsUnique( JsonObject json, Guid? rootGuid = null )
	{
		// Every guid value is found while building the lookup, so the tree is only walked once
		var found = new List<(JsonValue Node, Guid Guid)>();
		var translate = CreateUniqueGuidLookup( json, rootGuid, found );

		//
		// Translate every guid, but only if it's in our guid dictionary.
		//
		foreach ( var (node, guid) in found )
		{
			if ( translate.TryGetValue( guid, out var updatedGuid ) )
				node.ReplaceWith( updatedGuid );
		}

		return translate;
	}

	internal static Dictionary<Guid, Guid> CreateUniqueGuidLookup( JsonObject json, Guid? rootGuid = null ) => CreateUniqueGuidLookup( json, rootGuid, null );

	static Dictionary<Guid, Guid> CreateUniqueGuidLookup( JsonObject json, Guid? rootGuid, List<(JsonValue Node, Guid Guid)> found )
	{
		Dictionary<Guid, Guid> translate = new();

		//
		// This is a prefab that is being spawned into the scene. As such, it already has a guid.
		// So we need to replace whatever the prefab's guid is with the real id, and make sure
		// it doesn't generate a new one for it.. and make sure that it updates all of the children
		// so they correctly reference the new guid.
		//
		if ( rootGuid.HasValue )
		{
			if ( json.TryGetPropertyValue( "__guid", out var node ) && node is JsonValue jsv && jsv.TryGetValue<Guid>( out var guid ) )
			{
				translate[guid] = rootGuid.Value;
			}
		}

		//
		// We want to skip ActionGraph Guids if they're cached, so we can re-use the cached versions.
		//
		if ( ActionGraph.SerializationOptions.Cache is { } cache )
		{
			foreach ( var guid in cache.Guids )
			{
				translate[guid] = guid;
			}
		}

		//
		// Find all guids with "__guid" as their name. Add them to translate
		// with a new target value.
		//
		CollectGuids( json, null, translate, found );

		return translate;
	}

	static void CollectGuids( JsonNode node, string key, Dictionary<Guid, Guid> translate, List<(JsonValue Node, Guid Guid)> found )
	{
		if ( node is JsonObject obj )
		{
			// GameObjects & Components that are part of prefab instances have their id stored in the PrefabToInstanceId lookup in JSON.
			// We need to remap those as well in addtion to __guid id's.
			if ( key == GameObject.JsonKeys.PrefabIdToInstanceId )
			{
				foreach ( var (_, instanceId) in obj )
				{
					if ( instanceId.AsValue().TryGetValue<Guid>( out var guid ) )
						translate.TryAdd( guid, Guid.NewGuid() );
				}
			}

			foreach ( var (childKey, child) in obj )
				CollectGuids( child, childKey, translate, found );

			return;
		}

		if ( node is JsonArray array )
		{
			for ( var i = 0; i < array.Count; i++ )
				CollectGuids( array[i], null, translate, found );

			return;
		}

		if ( node is JsonValue value && value.TryGetValue<Guid>( out var id ) )
		{
			found?.Add( (value, id) );

			if ( key == GameObject.JsonKeys.Id )
				translate.TryAdd( id, Guid.NewGuid() );
		}
	}

	/// <summary>
	/// Find all "Id" guids, and replace them with new guids. This is used to make GameObject serializations unique,
	/// so when you duplicate stuff, it copies over uniquely and keeps associations.
	/// </summary>
	[Obsolete( "Use MakeIdGuidsUnique" )]
	public static void MakeGameObjectsUnique( JsonObject json, Guid? rootGuid = null )
	{
		MakeIdGuidsUnique( json, rootGuid );
	}

	/// <summary>
	/// Create a unique copy of the passed in GameObject
	/// </summary>
	[System.Obsolete( "Please use GameObject.Clone( ... )" )]
	public static GameObject Instantiate( GameObject template, Transform transform )
	{
		Assert.NotNull( Game.ActiveScene, "No Active Scene" );

		JsonObject json = null;

		if ( template is PrefabScene prefabScene && prefabScene.Source is PrefabFile prefabFile )
		{
			json = prefabFile.RootObject;
		}
		else
		{
			json = template.Serialize();
		}

		if ( json is not null )
		{
			MakeIdGuidsUnique( json );
		}

		var go = new GameObject();

		if ( json is not null )
		{
			using var blobs = BlobDataSerializer.Load( null, null );
			using var batchGroup = CallbackBatch.Batch();

			go.Deserialize( json );
		}

		go.LocalTransform = transform.WithScale( transform.Scale * go.LocalScale );

		if ( template is PrefabScene prefabScene1 )
		{
			go.InitPrefabInstance( ResourceId.Get( prefabScene1.Source ), false );
		}

		go.Parent = Game.ActiveScene;

		return go;
	}

	/// <summary>
	/// Create a unique copy of the passed in GameObject
	/// </summary>
	[System.Obsolete( "Please use GameObject.Clone( ... )" )]
	public static GameObject Instantiate( GameObject template ) => Instantiate( template, Transform.Zero );

	/// <summary>
	/// Create a unique copy of the passed in GameObject
	/// </summary>
	[System.Obsolete( "Please use GameObject.Clone( ... )" )]
	public static GameObject Instantiate( GameObject template, Vector3 position, Rotation rotation )
		=> Instantiate( template, new Transform( position, rotation, 1.0f ) );

	/// <summary>
	/// Create a unique copy of the passed in GameObject
	/// </summary>
	[System.Obsolete( "Please use GameObject.Clone( ... )" )]
	public static GameObject Instantiate( GameObject template, Vector3 position )
		=> Instantiate( template, new Transform( position, Rotation.Identity, 1.0f ) );

	/// <summary>
	/// Get a (cached) scene from a PrefabFile
	/// </summary>
	public static PrefabScene GetPrefabScene( PrefabFile prefabFile ) => prefabFile.GetScene();

	/// <summary>
	/// Render a GameObject to a bitmap. This is usually used for easily rendering "previews" of GameObjects, 
	/// for things like saving thumbnails etc.
	/// </summary>
	static void RenderToBitmap( Bitmap bitmap, Func<GameObject> func )
	{
		var scene = new Scene();
		scene.Name = "RenderGameObjectToBitmap";
		scene.PhysicsWorld?.Gravity = 0;

		try
		{
			using var sceneScope = scene.Push();

			CameraComponent camera = default;

			// camera
			{
				var go = new GameObject( true, "camera" );
				camera = go.AddComponent<CameraComponent>();
				camera.BackgroundColor = Color.Transparent;
				camera.WorldRotation = new Angles( 20, 180 + 45, 0 );
				camera.FieldOfView = 50.0f;
				camera.ZFar = 15000.0f;
				camera.ZNear = 0.1f;

				var sharpen = go.AddComponent<Sharpen>( true );
				sharpen.Scale = 0.2f;

				go.AddComponent<AmbientOcclusion>( true );
				go.AddComponent<Bloom>( true );

			}

			// ambient light
			{
				var go = new GameObject( true, "ambient" );
				var c = go.AddComponent<AmbientLight>();
				c.Color = Color.White * 0.2f;
			}

			// sun light
			{
				var go = new GameObject( true, "sun" );
				var sun = go.AddComponent<DirectionalLight>();
				sun.Shadows = true;
				sun.WorldRotation = new Angles( 60, 45, 0 );
				sun.LightColor = Color.White * 0.4f;
			}

			// envmap
			{
				var go = new GameObject( true, "envmap" );
				var c = go.AddComponent<EnvmapProbe>();
				c.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
				c.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
				c.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 100000 );
			}

			GameObject o = default;

			// prefab spawn
			{
				o = func();
				o.WorldPosition = 0;
			}

			// tick tick
			for ( int i = 0; i < 8; i++ )
			{
				scene.GameTick( 0.1f );
			}

			// place the camera
			{
				var bounds = o.GetBounds();

				var distance = MathX.SphereCameraDistance( bounds.Size.Length * 0.5f, camera.FieldOfView );
				var aspect = bitmap.Width / bitmap.Height;
				if ( aspect > 1 ) distance *= aspect;

				camera.WorldPosition = (o.WorldRotation * bounds.Center) + camera.WorldRotation.Forward * -distance;
			}

			// render twice, for any temporal shit to kick in
			camera.RenderToBitmap( bitmap );
			scene.GameTick( 0.1f );
			camera.RenderToBitmap( bitmap );

			scene.Destroy();
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Render a GameObject to a bitmap. This is usually used for easily rendering "previews" of GameObjects, 
	/// for things like saving thumbnails etc.
	/// </summary>
	public static void RenderGameObjectToBitmap( GameObject objSource, Bitmap bitmap )
	{
		if ( objSource == null ) return;
		if ( bitmap == null ) return;

		Sandbox.Rendering.TextureStreaming.ExecuteWithDisabled( () =>
		{
			RenderToBitmap( bitmap, () =>
			{
				var o = objSource.Clone();
				o.WorldPosition = 0;
				return o;
			} );
		} );
	}

	/// <summary>
	/// Studio lighting that follows the camera, so every thumbnail is lit the same way: a warm key
	/// from above-left, a soft fill from beside the camera, and a cool rim from behind to pick out
	/// the edges.
	/// The rim is a point light - a scene only takes one directional light, a second replaces the first.
	/// </summary>
	internal static void AddThumbnailLights( Rotation view, BBox bounds )
	{
		var key = new GameObject( true, "key" ).Components.Create<DirectionalLight>();
		key.WorldRotation = Rotation.LookAt( (view.Forward + view.Right * 0.7f - view.Up * 0.9f).Normal );
		key.LightColor = new Color( 1f, 0.95f, 0.88f ) * 0.8f;
		key.SkyColor = new Color( 0.55f, 0.6f, 0.7f ) * 0.55f;
		key.Shadows = false;

		var size = bounds.Size.Length;

		// A soft fill from beside the camera, so dark metal facing us doesn't go black
		var fill = new GameObject( true, "fill" ).Components.Create<PointLight>();
		fill.WorldPosition = bounds.Center - view.Forward * size * 1.5f + view.Right * size * 0.6f;
		fill.Radius = size * 4f;
		fill.Attenuation = 0f;
		fill.LightColor = new Color( 1f, 0.97f, 0.92f ) * 0.3f;
		fill.Shadows = false;

		var rim = new GameObject( true, "rim" ).Components.Create<PointLight>();
		rim.WorldPosition = bounds.Center + view.Forward * size * 0.6f + view.Left * size * 0.5f + view.Up * size * 0.5f;
		rim.Radius = size * 4f;
		rim.Attenuation = 0f;
		rim.LightColor = new Color( 0.7f, 0.85f, 1f ) * 6f;
		rim.Shadows = false;
	}

	/// <summary>
	/// Render a Model to a bitmap. This is usually used for easily rendering "previews" of Models for thumbnails
	/// </summary>
	public static void RenderModelBitmap( Model model, Bitmap bitmap )
	{
		if ( model == null ) return;
		if ( bitmap == null ) return;

		Sandbox.Rendering.TextureStreaming.ExecuteWithDisabled( () =>
		{
			RenderToBitmap( bitmap, () =>
			{
				var o = new GameObject();
				o.AddComponent<ModelRenderer>().Model = model;
				return o;
			} );
		} );
	}

	/// <summary>
	/// Run an action inside a batch group. A batchgroup is used with GameObject and Components to
	/// make sure that their OnEnable/OnDisable and other callbacks are called in a deterministic order,
	/// and that they can find each other during creation.
	/// </summary>
	[Obsolete( "Use Scene.BatchGroup() instead" )]
	public static void RunInBatchGroup( Action action )
	{
		using ( Game.ActiveScene?.BatchGroup() )
		{
			action?.Invoke();
		}
	}
}
