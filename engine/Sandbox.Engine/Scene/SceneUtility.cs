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
		var translate = CreateUniqueGuidLookup( json, rootGuid );

		//
		// Find every guid and translate them, but only if they're in our
		// guid dictionary.
		//
		Sandbox.Json.WalkJsonTree( json, ( k, v ) =>
		{
			if ( !v.TryGetValue<Guid>( out var guid ) ) return v;
			if ( !translate.TryGetValue( guid, out var updatedGuid ) ) return v;

			return updatedGuid;
		} );

		return translate;
	}

	internal static Dictionary<Guid, Guid> CreateUniqueGuidLookup( JsonObject json, Guid? rootGuid = null )
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
		Sandbox.Json.WalkJsonTree( json, ( k, v ) =>
		{
			if ( k == GameObject.JsonKeys.Id )
			{
				if ( v.TryGetValue<Guid>( out var guid ) && !translate.ContainsKey( guid ) )
				{
					translate[guid] = Guid.NewGuid();
				}
			}

			return v;
		},
		( k, v ) =>
		{
			// GameObjects & Components that are part of prefab instances have their id stored in the PrefabToInstanceId lookup in JSON.
			// We need to remap those as well in addtion to __guid id's.
			if ( k == GameObject.JsonKeys.PrefabIdToInstanceId )
			{
				foreach ( var (prefabId, instanceID) in v )
				{
					if ( instanceID.AsValue().TryGetValue<Guid>( out var guid ) && !translate.ContainsKey( guid ) )
					{
						translate[guid] = Guid.NewGuid();
					}
				}
			}

			return v;
		} );

		return translate;
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
			go.InitPrefabInstance( prefabScene1.Source.ResourcePath, false );
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
		var scene = Scene.CreateEditorScene();
		scene.Name = "RenderGameObjectToBitmap";
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
				go.AddComponent<ScreenSpaceReflections>( true ); // I don't think this'll work?
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
			float t = 0;
			for ( int i = 0; i < 8; i++ )
			{
				scene.EditorTick( t += 0.1f, 0.1f );
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
			scene.EditorTick( t += 0.1f, 0.1f );
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
	public static void RunInBatchGroup( Action action )
	{
		using ( CallbackBatch.Isolated() )
		{
			action?.Invoke();
		}
	}
}
