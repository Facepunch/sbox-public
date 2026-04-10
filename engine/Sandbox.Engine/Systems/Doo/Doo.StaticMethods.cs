namespace Sandbox;

public partial class Doo
{
	/// <summary>
	/// Built-in static methods available to Doo scripts.
	/// </summary>
	[Expose]
	public static partial class Methods
	{
		/// <summary>
		/// Logs an informational message.
		/// </summary>
		[Doo.Method( "Log.Info" )]
		public static void LogInfo( string text )
		{
			Log.Info( text );
		}

		/// <summary>
		/// Logs a warning message.
		/// </summary>
		[Doo.Method( "Log.Warning" )]
		public static void LogWarning( string text )
		{
			Log.Warning( text );
		}

		/// <summary>
		/// Logs an error message.
		/// </summary>
		[Doo.Method( "Log.Error" )]
		public static void LogError( string text )
		{
			Log.Error( text );
		}

		/// <summary>
		/// Destroys the given GameObject.
		/// </summary>
		[Doo.Method( "GameObject.Destroy" )]
		public static void GameObjectDestroy( GameObject gameObject )
		{
			if ( !gameObject.IsValid() ) return;
			gameObject.Destroy();
		}

		/// <summary>
		/// Clones a GameObject, optionally spawning it on the network.
		/// </summary>
		[Doo.Method( "GameObject.Clone" )]
		public static GameObject GameObjectClone( [Description( "The gameobject you want to clone" )] GameObject gameObject, bool enabled = true, bool networked = true )
		{
			if ( !gameObject.IsValid() ) return null;

			var go = gameObject.Clone( gameObject.WorldTransform, startEnabled: enabled );

			if ( networked )
			{
				go.NetworkSpawn( enabled, null );
			}

			return go;
		}

		/// <summary>
		/// Clones a GameObject with an explicit position, rotation, and scale.
		/// </summary>
		[Doo.Method( "GameObject.CloneEx" )]
		public static GameObject GameObjectCloneEx( [Description( "The gameobject you want to clone" )] GameObject gameObject, Vector3 position, Rotation angles, Vector3 scale )
		{
			return gameObject?.Clone( position, angles, scale );
		}
	}
}
