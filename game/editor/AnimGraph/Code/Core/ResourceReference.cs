using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

internal sealed class ResourceReference : JsonConverter<ResourceId>
{
	public static ResourceId Read( JsonNode value )
	{
		var id = Sandbox.Json.FromNode<ResourceId>( value );
		return id.Guid is null ? ResourceId.FromPath( id.Path ) : id;
	}

	public static JsonNode Write( ResourceId id )
	{
		if ( id.Guid is null )
			id = ResourceId.FromPath( id.Path );
		id.Path = id.ResolvePath() ?? "";
		return Sandbox.Json.ToNode( id );
	}

	public override ResourceId Read( ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options )
	{
		using var document = JsonDocument.ParseValue( ref reader );
		return Read( JsonNode.Parse( document.RootElement.GetRawText() ) );
	}

	public override void Write( Utf8JsonWriter writer, ResourceId value, JsonSerializerOptions options )
		=> Write( value ).WriteTo( writer );
}
