using Sandbox.Resources;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sandbox;

public partial class Resource
{
	/// <summary>
	/// Embedded data for this resource
	/// </summary>
	[Hide, JsonIgnore]
	public EmbeddedResource? EmbeddedResource { get; set; }

	/// <summary>
	/// Read the resource from a JSON element. This is usually a string, describing the path to the resource
	/// </summary>
	static object IJsonConvert.JsonRead( ref Utf8JsonReader reader, Type typeToConvert )
	{
		return LoadJsonReference( typeToConvert, ref reader );
	}

	/// <summary>
	/// Write the resource reference to a json element. This is usually a string, describing the path to the resource
	/// </summary>
	static void IJsonConvert.JsonWrite( object value, Utf8JsonWriter writer )
	{
		if ( value is not Resource resource )
		{
			writer.WriteNullValue();
			return;
		}

		resource.WriteJsonReference( writer );
	}

	/// <summary>
	/// Load a resource with improved deferred loading support
	/// </summary>
	internal static Resource Load( Type targetType, ResourceId id )
	{
		if ( targetType.IsAssignableTo( typeof( GameResource ) ) )
		{
			if ( !string.IsNullOrEmpty( id.Path ) )
			{
				string path = id.Path;
				if ( !path.EndsWith( "_c" ) ) path += "_c";

				// at this point the type may be a common base class
				// but we want to make sure we're loading this resource as the type it ACTUALLY is
				var extension = System.IO.Path.GetExtension( path );
				if ( Game.Resources.TryGetType( extension, out var resourceAttribute ) )
				{
					targetType = resourceAttribute.TargetType;
				}
			}

			// GameResource: Fetch it from the cache, or setup a deferred load
			return GameResource.GetPromise( targetType, id );
		}

		// For native resource types, use direct loading
		return LoadNative( targetType, id );
	}

	/// <summary>
	/// Load a resource reference from JSON data.
	/// Handles both string paths and embedded resource objects for all resource types.
	/// </summary>
	internal static Resource LoadJsonReference( Type targetType, ref Utf8JsonReader reader )
	{
		if ( reader.TokenType == JsonTokenType.String )
		{
			// legacy: just a path
			return Load( targetType, reader.GetString() );
		}

		using var doc = JsonDocument.ParseValue( ref reader );
		var root = doc.RootElement;

		if ( root.ValueKind != JsonValueKind.Object )
			return default;

		//
		// Embedded resource
		//
		if ( root.TryGetProperty( "$compiler", out var _ ) )
		{
			EmbeddedResource serializedResource;
			try
			{
				serializedResource = root.Deserialize<EmbeddedResource>();
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Couldn't deserialize embedded resource data for {targetType.Name}" );
				return default;
			}

			//
			// If there's a compiled version then use it, but store the generation data too
			//
			if ( !string.IsNullOrWhiteSpace( serializedResource.CompiledPath ) )
			{
				var resource = Load( targetType, serializedResource.CompiledPath );

				// Store embedded resource data if the resource supports it
				if ( resource is not null )
				{
					resource.EmbeddedResource = serializedResource;
				}

				return resource;
			}

			//
			// This is an embedded type, it's edited inline wherever it is
			// We could make this applicable for Resources too surely, the only thing stopping us is PushDeserializationScope
			//
			if ( targetType.IsAssignableTo( typeof( GameResource ) ) && serializedResource.ResourceCompiler == "embed" )
			{
				// 
				// Inherited resource
				//
				if ( !string.IsNullOrEmpty( serializedResource.TypeName ) )
				{
					var type = Game.TypeLibrary.GetType( serializedResource.TypeName );
					if ( type is not null )
					{
						targetType = type.TargetType;
					}
				}

				var resource = System.Activator.CreateInstance( targetType ) as GameResource;
				if ( resource is not null )
				{
					resource.Deserialize( serializedResource.Data );
					resource.EmbeddedResource = serializedResource;

					return resource;
				}
			}

			var options = ResourceGenerator.Options.Default;
			return ResourceGenerator.CreateResource( serializedResource, options, targetType );
		}

		//
		// Resource reference, either by path or by guid
		//

		Guid? guid = null;
		if ( root.TryGetProperty( "Id", out var idElement ) && idElement.TryGetGuid( out var id ) && id != default )
		{
			guid = id;
		}

		string path = null;
		if ( root.TryGetProperty( "Path", out var pathElement ) )
		{
			path = pathElement.GetString();
		}

		return Load( targetType, new ResourceId { Guid = guid, Path = path } );
	}

	/// <summary>
	/// Allows a resource type to override how it reference entry is written. This is generally
	/// just going to be a path to the on disk resource, but we can use this to store metadata too.
	/// </summary>
	internal virtual void WriteJsonReference( Utf8JsonWriter writer )
	{
		// if we have an embedded resource, write that instead of the id
		if ( EmbeddedResource.HasValue )
		{
			//
			// This is an embedded resource, so we want to store all the data inline
			//
			if ( EmbeddedResource.Value is var resource && resource.ResourceCompiler == "embed" )
			{
				EmbeddedResource = resource with { Data = Json.SerializeAsObject( this ) };
			}

			JsonSerializer.Serialize( writer, EmbeddedResource );
			return;
		}

		if ( Guid == default )
		{
			// no known guid, just write the path
			writer.WriteStringValue( ResourcePath );
			return;
		}

		writer.WriteStartObject();
		writer.WriteString( "Id", Guid );
		writer.WriteString( "Path", ResourcePath );
		// might be a good idea to store type?
		writer.WriteEndObject();
	}
}
