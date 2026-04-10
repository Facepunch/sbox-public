using System.Linq;
using System.Runtime.CompilerServices;
using Editor;
using Facepunch.ActionGraphs.Nodes;

namespace Sandbox.ActionGraphs;

#nullable enable

public static class GameObjectThumbnail
{
	public const int Size = 128;

	private static ConditionalWeakTable<GameObject, Pixmap> _cache = new();

	/// <summary>
	/// If this game object is a prefab root, return the prefab asset.
	/// </summary>
	private static Asset? GetPrefabAsset( GameObject go )
	{
		if ( go.Scene is not PrefabScene { Source: { } source } || go.Scene != go )
		{
			return null;
		}

		return AssetSystem.FindByPath( source.ResourcePath );
	}

	public static Pixmap GetGameObjectThumb( this GameObject go )
	{
		if ( GetPrefabAsset( go ) is { } prefabAsset )
		{
			return prefabAsset.GetAssetThumb();
		}

		if ( _cache.TryGetValue( go, out var cached ) )
		{
			return cached;
		}

		using var sceneScope = go.Scene.Push();
		using var camera = new SceneCamera( "Thumbnail Cam" );

		var bounds = go.GetBounds();

		var center = bounds.Center;
		var distance = Math.Max( 16f, (bounds.Maxs - bounds.Mins).Length * 0.5f ) * 4f;
		var baseYaw = go.WorldRotation.Yaw();
		var cameraRotation = Rotation.From( 30f, baseYaw + 60f, 0f );
		var sunRotation = Rotation.From( 80f, baseYaw + 30f, 0f );
		var lightRotation = Rotation.From( 60f, baseYaw + 120f, 0f );

		var thumb = new Pixmap( Size, Size );

		SceneDirectionalLight? sun = null;
		SceneLight? light = null;

		if ( !go.Scene.GetAllComponents<Light>().Any() )
		{
			sun = new SceneDirectionalLight( go.Scene.SceneWorld, sunRotation, Color.White * 2.5f + Color.Cyan * 0.05f )
			{
				ShadowsEnabled = true,
				ShadowTextureResolution = 1024
			};

			light = new ScenePointLight( go.Scene.SceneWorld, center - lightRotation.Forward * distance, distance * 1.5f,
				new Color( 1.0f, 0.9f, 0.9f ) * 10.0f );
		}

		try
		{
			camera.World = go.Scene.SceneWorld;
			camera.Position = center - cameraRotation.Forward * distance;
			camera.Rotation = cameraRotation;
			camera.FieldOfView = 30f;
			camera.BackgroundColor = Color.Transparent;

			camera.RenderToPixmap( thumb );

			_cache.TryAdd( go, thumb );

			return thumb;
		}
		finally
		{
			sun?.Delete();
			light?.Delete();
		}
	}

	[Event( "scene.saved" )]
	static void OnSceneSaved( Scene scene )
	{
		foreach ( var (key, value) in _cache.ToArray() )
		{
			if ( key.Scene == scene )
			{
				_cache.Remove( key );
			}
		}
	}
}
