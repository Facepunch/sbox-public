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

	/// <summary>
	/// Light objects in this world, protected by the InternalSceneObjects lock.
	/// Allows lighting systems to inspect lights without traversing unrelated scene objects.
	/// </summary>
	internal HashSet<SceneLight> InternalSceneLights { get; } = new();

	/// <summary>
	/// What this world draws as on Borealis, once a camera has drawn it: its objects make and write their own render objects
	/// (<see cref="SceneObject.OwnsRenderObject"/>) and its lighting there. Null before, and in a process without Borealis.
	/// </summary>
	internal Rendering.ISceneWorldRender Render { get; set; }

	/// <summary>
	/// Whether this is a native tool's world (Hammer's, ModelDoc's), polled and written by the views that draw it on the main
	/// thread, while native keeps its objects alive: never at the sync point (<see cref="Rendering.RenderSync.Run"/>), at some
	/// other moment in the frame, when native may have destroyed objects whose changes wait.
	/// </summary>
	internal bool WrittenByItsViews { get; set; }

	internal event Action<SceneWorld> Destroyed;

	/// <summary>
	/// This world's fog volumes, in the order native's world lists them (<c>CSceneWorld::m_VolumetricFogVolumes</c>: added at
	/// the end, removed by moving the last into the gap), for the managed scene renderer. <see cref="FogVolumesVersion"/> goes
	/// up with every change.
	/// </summary>
	internal List<SceneFogVolume> FogVolumes { get; } = new();

	/// <summary>
	/// Goes up whenever <see cref="FogVolumes"/> changes, or one of them does.
	/// </summary>
	internal int FogVolumesVersion { get; private set; }

	internal void AddFogVolume( SceneFogVolume volume )
	{
		FogVolumes.Add( volume );
		FogVolumesVersion++;
	}

	internal void RemoveFogVolume( SceneFogVolume volume )
	{
		// As CUtlVector::FastRemove
		var index = FogVolumes.IndexOf( volume );
		if ( index < 0 ) return;

		FogVolumes[index] = FogVolumes[^1];
		FogVolumes.RemoveAt( FogVolumes.Count - 1 );
		FogVolumesVersion++;
	}

	/// <summary>
	/// This world's culling boxes (<see cref="SceneCullingBox"/>), which hide its objects in every view of it.
	/// <see cref="CullingBoxesVersion"/> goes up with every change.
	/// </summary>
	internal List<SceneCullingBox> CullingBoxes { get; } = new();

	/// <summary>
	/// Goes up whenever <see cref="CullingBoxes"/> changes, or one of them does.
	/// </summary>
	internal int CullingBoxesVersion { get; private set; }

	internal void AddCullingBox( SceneCullingBox box )
	{
		CullingBoxes.Add( box );
		CullingBoxesVersion++;
	}

	internal void RemoveCullingBox( SceneCullingBox box )
	{
		if ( CullingBoxes.Remove( box ) ) CullingBoxesVersion++;
	}

	internal void CullingBoxChanged() => CullingBoxesVersion++;

	internal HashSet<SceneMap> InternalSceneMaps { get; set; } = new();

	/// <summary>Changes whenever a map is added or removed, so a view of the world knows to look at <see cref="InternalSceneMaps"/> again.</summary>
	internal int MapsVersion { get; private set; }
	internal HashSet<SceneSkybox3D> InternalSkyboxWorlds { get; set; } = new();

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

		// Delete rather than detach, a map outliving its scene world would later tear down objects that are already gone
		foreach ( var map in InternalSceneMaps.ToArray() )
		{
			map.Delete();
		}

		InternalSceneMaps.Clear();

		RunQueuedDeletes();

		// Objects managed code made go with the world here; native destroys the ones native code made with its world
		SceneObject[] remaining;
		lock ( InternalSceneObjects ) remaining = InternalSceneObjects.Where( x => !x.HasNativeObject ).ToArray();
		foreach ( var sceneObject in remaining ) sceneObject.Destroyed();

		Rendering.SceneRenderer.Forget( this );
		CSceneSystem.DestroyWorld( this );
		native = IntPtr.Zero;
		All.Remove( this );
		NotifyDestroyed();
	}

	internal void OnNativeInit( ISceneWorld ptr )
	{
		All.Add( this );
		native = ptr;
	}

	internal void OnNativeDestroy()
	{
		Rendering.SceneRenderer.Forget( this );
		native = IntPtr.Zero;
		All.Remove( this );
		NotifyDestroyed();
	}

	void NotifyDestroyed()
	{
		var destroyed = Destroyed;
		Destroyed = null;
		destroyed?.Invoke( this );
	}

	/// <summary>
	/// Deleted objects are actually deleted at the end of each frame. Call this
	/// to actually delete pending deletes right now instead of waiting. 
	/// </summary>
	public void DeletePendingObjects()
	{
		if ( !native.IsValid ) return;
		native.DeleteEndOfFrameObjects();
		RunQueuedDeletes();
	}

	/// <summary>Objects deleted (<see cref="SceneObject.Delete"/>) whose world's deletes haven't run yet.</summary>
	readonly List<SceneObject> queuedDeletes = new();

	internal void QueueDelete( SceneObject sceneObject )
	{
		lock ( queuedDeletes ) queuedDeletes.Add( sceneObject );
	}

	/// <summary>
	/// Take the objects deleted since the last run out of this world and its renderer. A native object goes with native's
	/// next end-of-frame deletes.
	/// </summary>
	void RunQueuedDeletes()
	{
		SceneObject[] deleted;
		lock ( queuedDeletes )
		{
			if ( queuedDeletes.Count == 0 ) return;
			deleted = queuedDeletes.ToArray();
			queuedDeletes.Clear();

			// One deleted while the render thread draws the frame handed over with it stays for that frame's drawing, until the
			// next run after it's drawn (SceneObject.DeletedEpoch)
			if ( Rendering.RenderSync.RenderThreadDrawing )
			{
				var epoch = Rendering.RenderSync.Epoch;
				int kept = 0;
				for ( int i = 0; i < deleted.Length; i++ )
				{
					if ( deleted[i].DeletedEpoch == epoch ) queuedDeletes.Add( deleted[i] );
					else deleted[kept++] = deleted[i];
				}

				if ( kept < deleted.Length ) Array.Resize( ref deleted, kept );
			}
		}

		foreach ( var sceneObject in deleted ) sceneObject.Destroyed();
	}

	[ThreadStatic] static List<SceneWorld> deleteScratch;

	/// <summary>
	/// The frame's end: every world's deleted objects leave it, whether or not anything called <see cref="DeletePendingObjects"/>
	/// (a native view once ran each drawn world's; nothing draws natively now, so a world nobody flushed kept them).
	/// </summary>
	internal static void RunEndOfFrameDeletes()
	{
		// Taken out while in use so a nested flush makes its own; a snapshot, as deleting may change All
		var worlds = deleteScratch ?? new List<SceneWorld>();
		deleteScratch = null;

		foreach ( var world in All )
		{
			bool any;
			lock ( world.queuedDeletes ) any = world.queuedDeletes.Count > 0;
			if ( any ) worlds.Add( world );
		}

		foreach ( var world in worlds ) world.DeletePendingObjects();

		worlds.Clear();
		deleteScratch = worlds;
	}

	/// <summary>
	/// Flush all system-wide queued scene object and world deletes.
	/// Normally processed at the frame's end, but during shutdown no frame runs so we must flush manually.
	/// </summary>
	internal static void FlushQueuedDeletes()
	{
		RunEndOfFrameDeletes();
		CSceneSystem.PerformEndOfFrameDeletes();
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

			// No PVS is set (owner, 2026-11-18): maps don't build one
			MapsVersion++;
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

			MapsVersion++;
		}
	}
}
