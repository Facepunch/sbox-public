using Sandbox.UI;

namespace Sandbox;

public partial class Scene : GameObject
{
	/// <summary>
	/// Run an event on all components and panels. The find argument is unused when calling this on a scene.
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

		// Support for ISceneEvent on Panels
		foreach ( var panelComponent in GetAll<PanelComponent>() )
		{
			if ( !panelComponent.Panel.IsValid() )
			{
				continue;
			}

			if ( panelComponent.Panel is T t )
			{
				try
				{
					action( t );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, e.Message );
				}
			}

			foreach ( var child in panelComponent.Panel.Descendants )
			{
				if ( child is not T ct )
				{
					continue;
				}

				try
				{
					action( ct );
				}
				catch ( System.Exception e )
				{
					Log.Warning( e, e.Message );
				}
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
