using System.Text.Json.Nodes;

public abstract partial class ToolMode
{
	/// <summary>
	/// This tool's <see cref="PropertyAttribute"/> settings that share a name and type with <typeparamref name="T"/>'s <see cref="ClientEditableAttribute"/> properties,
	/// as JSON for a spawn RPC, since only the owner has their current values. See <see cref="ApplyConfiguration"/>.
	/// </summary>
	protected string WriteConfiguration<T>() where T : Component
	{
		var tool = TypeLibrary.GetType( GetType() );
		var json = new JsonObject();

		foreach ( var property in GetConfigurable( typeof( T ) ) )
		{
			var setting = tool?.GetProperty( property.Name );
			if ( setting is null || !setting.HasAttribute<PropertyAttribute>() || setting.PropertyType != property.PropertyType ) continue;

			json[property.Name] = Json.ToNode( setting.GetValue( this ), property.PropertyType );
		}

		return json.ToJsonString();
	}

	/// <summary>
	/// Host side of <see cref="WriteConfiguration"/>: sets the settings on a newly placed entity, only ever its
	/// <see cref="ClientEditableAttribute"/> properties, the same ones its context menu can change.
	/// </summary>
	protected static void ApplyConfiguration( Component entity, string configuration )
	{
		if ( !entity.IsValid() || string.IsNullOrEmpty( configuration ) ) return;

		try
		{
			if ( JsonNode.Parse( configuration ) is not JsonObject json ) return;

			foreach ( var property in GetConfigurable( entity.GetType() ) )
			{
				if ( json.TryGetPropertyValue( property.Name, out var value ) )
					property.SetValue( entity, Json.FromNode( value, property.PropertyType ) );
			}
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, $"Couldn't apply tool settings to {entity}." );
		}
	}

	static IEnumerable<PropertyDescription> GetConfigurable( Type type )
	{
		return TypeLibrary.GetType( type )?.Properties.Where( x => x.HasAttribute<ClientEditableAttribute>() && x.CanWrite ) ?? [];
	}
}
