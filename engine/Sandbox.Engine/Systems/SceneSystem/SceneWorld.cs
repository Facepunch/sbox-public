using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// A scene world that contains <see cref="SceneObject"/>s. See <a href="https://sbox.game/api/Tools.Utility.CreateSceneWorld()">Utility.CreateSceneWorld</a>.
///
/// <para>You may also want a <see cref="SceneCamera"/> to manually render the scene world.</para>
/// </summary>
public sealed partial class SceneWorld : IHandle
{
	[SkipHotload]
	internal static HashSet<SceneWorld> All = new HashSet<SceneWorld>();

	internal HashSet<SceneObject> InternalSceneObjects { get; set; } = new();
	internal HashSet<SceneMap> InternalSceneMaps { get; set; } = new();
	internal HashSet<SceneSkybox3D> InternalSkyboxWorlds { get; set; } = new();
	internal IPVS ActivePVS { get; private set; }

	/// <summary>
	/// List of scene objects belonging to this scene world.
	/// </summary>
	public IReadOnlyCollection<SceneObject> SceneObjects
	{
		get
		{
			lock ( InternalSceneObjects )
			{
				return InternalSceneObjects
								.Where( x => x.IsValid() )
								.ToArray(); // Ha Ha I bet no-one ever notices this
			}
		}
	}

	/// <summary>
	/// Controls gradient fog settings.
	/// </summary>
	public GradientFogSetup GradientFog;

	/// <summary>
	/// Sets the ambient lighting color
	/// </summary>
	public Color AmbientLightColor = Color.Transparent;

	/// <summary>
	/// Sets the clear color, if nothing else is drawn, this is the color you will see
	/// </summary>
	[System.Obsolete( "Use SceneCamera.BackgroundColor" )]
	public Color ClearColor = Color.Black;

	#region IHandle
	//
	// A pointer to the actual native object
	//
	internal ISceneWorld native;

	//
	// IHandle implementation
	//
	void IHandle.HandleInit( IntPtr ptr ) => OnNativeInit( ptr );
	void IHandle.HandleDestroy() => OnNativeDestroy();
	bool IHandle.HandleValid() => !native.IsNull;
	#endregion

	/// <summary>
	/// If a world is transient, it means it was created by game code, and should
	/// be deleted at the end of the game session. If they're non transient then
	/// they were created in the menu, or by the engine code and will be released
	/// properly by that code.
	/// </summary>
	internal bool IsTransient { get; set; }

	internal SceneWorld( HandleCreationData _ ) { }

	public SceneWorld()
	{
		IsTransient = true;

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateWorld( "World Debug Name" );
		}
	}

	/// <summary>
	/// Delete this scene world. You shouldn't access it anymore.
	/// </summary>
	public void Delete()
	{
		if ( !IsTransient ) return;
		if ( !native.IsValid ) return;

		foreach ( var map in InternalSceneMaps.ToArray() )
		{
			RemoveSceneMap( map );
		}

		InternalSceneMaps.Clear();

		CSceneSystem.DestroyWorld( this );
		native = IntPtr.Zero;
		ActivePVS = default;
	}

	internal void OnNativeInit( ISceneWorld ptr )
	{
		All.Add( this );
		native = ptr;
	}

	internal void OnNativeDestroy()
	{
		native = IntPtr.Zero;
		ActivePVS = default;
		All.Remove( this );
	}

	/// <summary>
	/// Deleted objects are actually deleted at the end of each frame. Call this
	/// to actually delete pending deletes right now instead of waiting. 
	/// </summary>
	public void DeletePendingObjects()
	{
		if ( !native.IsValid ) return;
		native.DeleteEndOfFrameObjects();
	}

	/// <summary>
	/// This finishes any loads and actually spawns the world sceneobjects
	/// </summary>
	internal void UpdateObjectsForRendering( Vector3 eyePos = default, float farPlane = 5000.0f )
	{
		if ( InternalSceneMaps is null )
			return;

		if ( !InternalSceneMaps.Any() )
			return;

		foreach ( var worldGroup in InternalSceneMaps.Select( x => x.WorldGroup ).Distinct() )
		{
			g_pWorldRendererMgr.UpdateObjectsForRendering( worldGroup, eyePos, 1.0f, farPlane );
		}

		// If we have a 3D skybox, update its objects too
		foreach ( var world in InternalSkyboxWorlds )
		{
			world?.SkyboxWorld.UpdateObjectsForRendering( world.Origin, farPlane );
		}
	}

	/// <summary>
	/// Add a scenemap to this world
	/// </summary>
	internal void AddSceneMap( SceneMap sceneMap )
	{
		lock ( InternalSceneMaps )
		{
			if ( !InternalSceneMaps.Add( sceneMap ) )
			{
				Log.Warning( "Couldn't add sceneMap" );
			}

			// If the PVS data is valid - then apply it to this world
			// We need to be careful that the pvs doesn't get deleted
			// because this will be a hanging pointer. 
			// I think it would be better to wrap it in a c# object and
			// only delete when it gets garbage collected
			if ( sceneMap.PVS.IsValid )
			{
				//Log.Info( $"{this} - SET PVS from {sceneMap}" );
				native.SetPVS( sceneMap.PVS );
				ActivePVS = sceneMap.PVS;
			}
		}
	}


	internal void RemoveSceneMap( SceneMap sceneMap )
	{
		lock ( InternalSceneMaps )
		{
			if ( !InternalSceneMaps.Remove( sceneMap ) )
			{
				Log.Warning( "Couldn't remove sceneMap" );
			}

			if ( ActivePVS == sceneMap.PVS )
			{
				if ( !native.IsNull )
				{
					native.SetPVS( default );
				}
				ActivePVS = default;
			}
		}
	}
}
