namespace Sandbox;

public partial class Scene : GameObject
{
	/// <summary>
	/// Run an event on all components. The find argument is unused when calling this on a scene.
	/// </summary>
	public override void RunEvent<T>( Action<T> action, FindMode find = FindMode.EnabledInSelfAndDescendants )
	{
		foreach ( var c in GetAll<T>() )
		{
			try
			{
				action( c );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, e.Message );
			}
		}

	}

	/// <summary>
	/// Run an IRenderThread event using the snapshot captured in PreRender().
	/// Safe to call from the render thread — iterates a plain List that the main thread
	/// does not touch until the next PreRender().
	/// </summary>
	internal void RunRenderThreadEvent( CameraComponent camera, Sandbox.Rendering.Stage stage )
	{
		foreach ( var c in renderThreadEventTargets )
		{
			try
			{
				c.OnRenderStage( camera, stage );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, e.Message );
			}
		}
	}
}

/// <summary>
/// A wrapper for scene event interfaces. Allows syntax sugar of something like
/// `IPlayerEvents.Post( x => x.OnPlayerHurt( this, amount ) )` instead of using
/// Scene.Run to call them manually.
/// </summary>
public interface ISceneEvent<T>
{
	/// <summary>
	/// Post an event to the entire scene, including GameObjectSystem's
	/// </summary>
	public static void Post( Action<T> action )
	{
		if ( !Game.ActiveScene.IsValid() ) return;

		Game.ActiveScene.RunEvent( action );
	}

	/// <summary>
	/// Post event to a specific GameObject (and its descendants by default - you can specify a <see cref="FindMode"/> to control this)
	/// </summary>
	public static void PostToGameObject( GameObject go, Action<T> action, FindMode find = FindMode.EnabledInSelfAndDescendants )
	{
		if ( !go.IsValid() ) return;

		go.RunEvent<T>( action, find );
	}
}
