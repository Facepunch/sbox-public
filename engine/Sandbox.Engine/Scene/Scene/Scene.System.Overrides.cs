using System.Text.Json.Nodes;

namespace Sandbox;

public partial class Scene
{
	readonly List<SystemOverrideScope> _transientSystemOverrides = new();
	readonly Dictionary<(GameObjectSystem System, PropertyDescription Property), object> _preTransientSystemValues = new();

	/// <summary>
	/// Applies GameObjectSystem property overrides from the given node.
	/// </summary>
	internal void ApplyGameObjectSystemOverrides( JsonNode overridesNode )
	{
		ApplyGameObjectSystemOverrides( overridesNode, null );
	}

	/// <summary>
	/// Applies GameObjectSystem property overrides and returns a scope that reverts them.
	/// </summary>
	internal IDisposable ApplyTransientGameObjectSystemOverrides( JsonNode overridesNode )
	{
		SystemOverrideScope scope = new( this );

		try
		{
			ApplyGameObjectSystemOverrides( overridesNode, scope );

			if ( !scope.HasOverrides )
				return null;

			_transientSystemOverrides.Add( scope );
			var result = scope;
			scope = null;
			return result;
		}
		finally
		{
			scope?.Dispose();
		}
	}

	void ApplyGameObjectSystemOverrides( JsonNode overridesNode, SystemOverrideScope scope )
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

		if ( overrides is null || overrides.Count == 0 )
			return;

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
					if ( scope is not null && !property.CanRead )
						continue;

					try
					{
						var value = Json.FromNode( valueNode, property.PropertyType );
						var previousValue = scope is not null
							? property.GetValue( system )
							: null;

						property.SetValue( system, value );
						scope?.AddOverride( system, property, previousValue, value );
					}
					catch ( Exception ex )
					{
						Log.Warning( $"Failed to apply scene override to {systemType.FullName}.{property.Name}: {ex.Message}" );
					}
				}
			}
		}
	}

	bool TryGetPreTransientValue( GameObjectSystem system, PropertyDescription property, out object value )
	{
		return _preTransientSystemValues.TryGetValue( (system, property), out value );
	}

	void RestoreTransientValue( GameObjectSystem system, PropertyDescription property )
	{
		for ( var i = _transientSystemOverrides.Count - 1; i >= 0; i-- )
		{
			if ( _transientSystemOverrides[i].TryGetOverride( system, property, out var overrideValue ) )
			{
				SetSystemPropertyValue( system, property, overrideValue );
				return;
			}
		}

		var key = (system, property);
		if ( !_preTransientSystemValues.TryGetValue( key, out var originalValue ) )
			return;

		if ( SetSystemPropertyValue( system, property, originalValue ) )
		{
			_preTransientSystemValues.Remove( key );
		}
	}

	static bool SetSystemPropertyValue( GameObjectSystem system, PropertyDescription property, object value )
	{
		try
		{
			property.SetValue( system, value );
			return true;
		}
		catch ( Exception ex )
		{
			Log.Warning( $"Failed to revert system override on {system.GetType().FullName}.{property.Name}: {ex.Message}" );
			return false;
		}
	}

	sealed class SystemOverrideScope : IDisposable
	{
		readonly Scene _scene;
		readonly List<(GameObjectSystem System, PropertyDescription Property, object Value)> _overrides = new();
		bool _disposed;

		public bool HasOverrides => _overrides.Count > 0;

		public SystemOverrideScope( Scene scene )
		{
			_scene = scene;
		}

		public void AddOverride( GameObjectSystem system, PropertyDescription property, object previousValue, object value )
		{
			var key = (system, property);
			_scene._preTransientSystemValues.TryAdd( key, previousValue );
			_overrides.Add( (system, property, value) );
		}

		public bool TryGetOverride( GameObjectSystem system, PropertyDescription property, out object value )
		{
			foreach ( var item in _overrides )
			{
				if ( item.System == system && item.Property == property )
				{
					value = item.Value;
					return true;
				}
			}

			value = null;
			return false;
		}

		public void Dispose()
		{
			if ( _disposed )
				return;

			_disposed = true;
			_scene._transientSystemOverrides.Remove( this );

			for ( var i = _overrides.Count - 1; i >= 0; i-- )
			{
				var (system, property, _) = _overrides[i];
				_scene.RestoreTransientValue( system, property );
			}

			_overrides.Clear();
		}
	}
}
