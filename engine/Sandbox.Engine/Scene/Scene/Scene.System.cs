using System.Text.Json.Nodes;

namespace Sandbox;

[Expose]
public partial class Scene
{
	List<GameObjectSystem> systems = new List<GameObjectSystem>();

	/// <summary>
	/// Call dispose on all installed hooks
	/// </summary>
	void ShutdownSystems()
	{
		foreach ( var sys in systems )
		{
			// Can become null during hotload development
			if ( sys is null ) continue;

			try
			{
				RemoveObjectFromDirectory( sys );
				sys.Dispose();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Exception when disposing GameObjectSystem '{sys.GetType()}'" );
			}
		}

		systems.Clear();
	}

	/// <summary>
	/// Find all types of SceneHook, create an instance of each one and install it.
	/// </summary>
	void InitSystems()
	{
		using ( Push() )
		{
			ShutdownSystems();

			var found = Game.TypeLibrary.GetTypes<GameObjectSystem>()
				.Where( x => !x.IsAbstract )
				.ToArray();

			foreach ( var f in found )
			{
				var e = f.Create<GameObjectSystem>( [this] );
				if ( e is null ) continue;

				ApplyGameObjectSystemConfig( e );

				systems.Add( e );
				AddObjectToDirectory( e );
			}
		}
	}

	/// <summary>
	/// Apply configuration values to a GameObjectSystem with priority:
	/// 1. Project-wide value (from <see cref="ProjectSettings.Systems"/>)
	/// 2. Default value (already set by property initializer)
	/// Scene-specific overrides are applied during deserialization via <see cref="ApplyGameObjectSystemOverrides"/>
	/// </summary>
	void ApplyGameObjectSystemConfig( GameObjectSystem system )
	{
		var systemType = Game.TypeLibrary.GetType( system.GetType() );
		if ( systemType is null ) return;

		using ( Push() )
		{
			foreach ( var property in systemType.Properties.Where( x => x.HasAttribute<PropertyAttribute>() ) )
			{
				if ( !property.CanWrite ) continue;

				// Apply project-wide value if it exists
				if ( ProjectSettings.Systems.TryGetPropertyValue( systemType, property, out var value ) )
				{
					try
					{
						property.SetValue( system, value );
					}
					catch ( Exception ex )
					{
						Log.Warning( $"Failed to apply config value to {systemType.FullName}.{property.Name}: {ex.Message}" );
					}
				}
			}
		}
	}

	/// <summary>
	/// Apply scene-specific GameObjectSystem property overrides.
	/// Called during scene deserialization.
	/// </summary>
	internal void ApplyGameObjectSystemOverrides( JsonNode overridesNode )
	{
		if ( overridesNode is null )
			return;

		Dictionary<string, JsonObject> overrides;

		try
		{
			overrides = Json.FromNode<Dictionary<string, JsonObject>>( overridesNode );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error when deserializing GameObjectSystem overrides ({e.Message})" );
			return;
		}

		if ( overrides is null || !overrides.Any() )
			return;

		foreach ( var system in systems )
		{
			var systemType = Game.TypeLibrary.GetType( system.GetType() );
			if ( systemType is null ) continue;

			if ( !overrides.TryGetValue( systemType.FullName, out var properties ) )
				continue;

			foreach ( var property in systemType.Properties.Where( x => x.HasAttribute<PropertyAttribute>() ) )
			{
				if ( !property.CanWrite ) continue;

				if ( properties.TryGetPropertyValue( property.Name, out var valueNode ) )
				{
					try
					{
						// Deserialize the JSON node directly to the property's type
						var value = Json.FromNode( valueNode, property.PropertyType );
						property.SetValue( system, value );
					}
					catch ( Exception ex )
					{
						Log.Warning( $"Failed to apply scene override to {systemType.FullName}.{property.Name}: {ex.Message}" );
					}
				}
			}
		}
	}

	/// <summary>
	/// Signal a hook stage
	/// </summary>
	internal void Signal( in GameObjectSystem.Stage stage )
	{
		GetCallbacks( stage ).Run();
	}

	Dictionary<GameObjectSystem.Stage, TimedCallbackList> listeners = new Dictionary<GameObjectSystem.Stage, TimedCallbackList>();

	/// <summary>
	/// Get the hook container for this stage
	/// </summary>
	TimedCallbackList GetCallbacks( in GameObjectSystem.Stage stage )
	{
		if ( listeners.TryGetValue( stage, out var list ) )
			return list;

		list = new TimedCallbackList();
		listeners[stage] = list;
		return list;
	}

	/// <summary>
	/// Reset the listener metrics to 0, like before a benchmark or something
	/// </summary>
	internal void ResetListenerMetrics()
	{
		foreach ( var l in listeners.Values )
		{
			l.ClearMetrics();
		}
	}

	/// <summary>
	/// Get a JSON serializable list of metrics from the scene's listeners.
	/// (this is just internal object[] right now because I can't be fucked to exose it properly)
	/// </summary>
	internal object[] GetListenerMetrics()
	{
		return listeners.Values.SelectMany( x => x.GetMetrics() ).ToArray();
	}

	/// <summary>
	/// Call this method on this stage. This returns a disposable that will remove the hook when disposed.
	/// </summary>
	public IDisposable AddHook( GameObjectSystem.Stage stage, int order, Action action, string className, string description )
	{
		return GetCallbacks( stage ).Add( order, action, className, description );
	}

	/// <summary>
	/// Get a specific system by type.
	/// </summary>
	public T GetSystem<T>() where T : GameObjectSystem
	{
		return systems.OfType<T>().FirstOrDefault();
	}

	/// <summary>
	/// Get a specific system by type.
	/// </summary>
	public void GetSystem<T>( out T val ) where T : GameObjectSystem
	{
		val = systems.OfType<T>().FirstOrDefault();
	}

	/// <summary>
	/// Get a specific system by <see cref="TypeDescription"/>.
	/// </summary>
	internal GameObjectSystem GetSystemByType( TypeDescription type )
	{
		return systems.FirstOrDefault( s => s.GetType() == type.TargetType );
	}

	/// <summary>
	/// Get all systems belonging to this scene.
	/// </summary>
	internal IEnumerable<GameObjectSystem> GetSystems()
	{
		return systems;
	}
}
