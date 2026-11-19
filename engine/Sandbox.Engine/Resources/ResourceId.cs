using System.Text.Json;

namespace Sandbox;

/// <summary>
/// An identifier of a resource, which can be backed by either a GUID, a path, or both.
/// </summary>
public struct ResourceId : IJsonConvert
{
	/// <summary>
	/// Capture a path's current GUID, when it is registered with the resource system.
	/// </summary>
	public static ResourceId FromPath( string path )
	{
		return new ResourceId
		{
			Path = path,
			Guid = !string.IsNullOrEmpty( path ) && NativeEngine.g_pResourceSystem.ResolvePathToGuid( path, out var guid ) ? guid : null
		};
	}

	/// <summary>
	/// Resolve the current path by GUID, falling back to the stored path when unavailable.
	/// </summary>
	public readonly string ResolvePath()
	{
		var path = Guid is Guid guid && guid != System.Guid.Empty ? NativeEngine.g_pResourceSystem.ResolveGuidToPath( guid ) : null;
		return string.IsNullOrEmpty( path ) ? Path : path;
	}

	/// <summary>
	/// GUID of the resource, prefer this over path if available.
	/// </summary>
	public Guid? Guid { get; set; }

	/// <summary>
	/// Path to the resource, used for feedback and as an alternative when the GUID is not provided.
	/// </summary>
	public string Path { get; set; }

	public bool IsEmpty => (Guid is null || Guid == System.Guid.Empty) && string.IsNullOrEmpty( Path );

	public static ResourceId Get( Resource resource )
	{
		if ( resource is null ) return default;

		return new ResourceId
		{
			Guid = resource.Guid != default ? resource.Guid : null,
			Path = resource.ResourcePath,
		};
	}

	/// <summary>
	/// Deserialize from Json. Supports guid + path, or just as a simple path string.
	/// </summary>
	static object IJsonConvert.JsonRead( ref Utf8JsonReader reader, Type typeToConvert )
	{
		if ( reader.TokenType == JsonTokenType.String )
		{
			return new ResourceId { Path = reader.GetString() };
		}

		using var doc = JsonDocument.ParseValue( ref reader );
		var root = doc.RootElement;

		if ( root.ValueKind != JsonValueKind.Object )
			return default( ResourceId );

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

		return new ResourceId { Guid = guid, Path = path };
	}

	/// <summary>
	/// Serialize to Json. Guid + Path if guid is known, otherwise just the path.
	/// </summary>
	static void IJsonConvert.JsonWrite( object value, Utf8JsonWriter writer )
	{
		var id = (ResourceId)value;

		if ( id.Guid is not Guid guid || guid == default )
		{
			// no known guid, just write the path
			writer.WriteStringValue( id.Path );
			return;
		}

		writer.WriteStartObject();
		writer.WriteString( "Id", guid );
		writer.WriteString( "Path", id.Path );
		writer.WriteEndObject();
	}


	public static implicit operator ResourceId( string path ) => new() { Path = path };

	public static implicit operator ResourceId( Guid guid ) => new() { Guid = guid };

	public override string ToString() => $"{Path} ({Guid})";

}
