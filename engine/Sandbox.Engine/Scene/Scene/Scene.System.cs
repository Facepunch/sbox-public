using System.Text.Json.Nodes;

namespace Sandbox;

[Expose]
public partial class Scene
{
	Dictionary<Type, GameObjectSystem> systems = new();

	/// <summary>
	/// Call dispose on all installed hooks
	/// </summary>
	void ShutdownSystems()
	{
		foreach ( var sys in systems.Values )
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

				systems[e.GetType()] = e;
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
	/// Tracks temporary GameObjectSystem overrides (e.g. from MapInstance) so original values can be restored.
	/// </summary>
	readonly List<SystemOverrideScope> _transientSystemOverrides = new();

	/// <summary>
	/// Applies GameObjectSystem overrides from the given node. Returns a disposable to revert changes.
	/// </summary>
	/// <param name="overridesNode">Serialized GameObjectSystems overrides.</param>
	/// <param name="transient">If true, overrides are temporary and reverted on dispose; otherwise, they are saved with the scene.</param>
	internal IDisposable ApplyGameObjectSystemOverrides( JsonNode overridesNode, bool transient = false )
	{
		if ( overridesNode is null )
			return null;

		Dictionary<string, JsonObject> overrides;

		try
		{
			overrides = Json.FromNode<Dictionary<string, JsonObject>>( overridesNode );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error when deserializing GameObjectSystem overrides ({e.Message})" );
			return null;
		}

		if ( overrides is null || overrides.Count == 0 )
			return null;

		var revert = new SystemOverrideScope( this, transient );

		foreach ( var system in systems.Values )
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
						// Remember the current value first, so the override can be unwound later.
						if ( property.CanRead )
							revert.Capture( system, property, property.GetValue( system ) );

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

		if ( !revert.HasCaptures )
			return null;

		if ( transient )
			_transientSystemOverrides.Add( revert );

		return revert;
	}

	/// <summary>
	/// Gets the pre-override value for a system property if masked by a transient override.
	/// </summary>
	bool TryGetPreTransientValue( GameObjectSystem system, PropertyDescription property, out object value )
	{
		foreach ( var scope in _transientSystemOverrides )
		{
			if ( scope.TryGetCaptured( system, property, out value ) )
				return true;
		}

		value = null;
		return false;
	}

	/// <summary>
	/// Saves and restores previous GameObjectSystem property values.
	/// </summary>
	sealed class SystemOverrideScope : IDisposable
	{
		private readonly Scene _scene;
		private readonly bool _transient;
		private readonly List<(GameObjectSystem System, PropertyDescription Property, object PreviousValue)> _captured = new();

		public SystemOverrideScope( Scene scene, bool transient )
		{
			_scene = scene;
			_transient = transient;
		}

		public bool HasCaptures => _captured.Count > 0;

		public void Capture( GameObjectSystem system, PropertyDescription property, object previousValue )
		{
			_captured.Add( (system, property, previousValue) );
		}

		/// <summary>
		/// Returns the pre-override value captured for the given system property, if any.
		/// </summary>
		public bool TryGetCaptured( GameObjectSystem system, PropertyDescription property, out object value )
		{
			foreach ( var (s, p, previousValue) in _captured )
			{
				if ( s == system && p == property )
				{
					value = previousValue;
					return true;
				}
			}

			value = null;
			return false;
		}

		public void Dispose()
		{
			if ( _transient )
				_scene?._transientSystemOverrides.Remove( this );

			// Restore in reverse, so stacked overrides unwind in the opposite order they applied.
			for ( int i = _captured.Count - 1; i >= 0; i-- )
			{
				var (system, property, previousValue) = _captured[i];

				try
				{
					property.SetValue( system, previousValue );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Failed to revert system override on {system.GetType().FullName}.{property.Name}: {ex.Message}" );
				}
			}

			_captured.Clear();
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
		return systems.TryGetValue( typeof( T ), out var sys ) ? sys as T : null;
	}

	/// <summary>
	/// Get a specific system by type.
	/// </summary>
	public void GetSystem<T>( out T val ) where T : GameObjectSystem
	{
		val = systems.TryGetValue( typeof( T ), out var sys ) ? sys as T : null;
	}

	/// <summary>
	/// Get a specific system by <see cref="TypeDescription"/>.
	/// </summary>
	internal GameObjectSystem GetSystemByType( TypeDescription type )
	{
		return systems.TryGetValue( type.TargetType, out var sys ) ? sys : null;
	}

	/// <summary>
	/// Get all systems belonging to this scene.
	/// </summary>
	internal Dictionary<Type, GameObjectSystem>.ValueCollection GetSystems()
	{
		return systems.Values;
	}
}
