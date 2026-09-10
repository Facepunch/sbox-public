using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.Mcp;

/// <summary>
/// The result of an MCP tool call - one or more content blocks the agent reads. Tools can
/// return plain values and get shaped into a single block automatically, but returning one
/// of these gives full control, like pairing a screenshot with a caption.
/// </summary>
public sealed class McpResult
{
	readonly JsonArray content = new();

	JsonNode structured;

	internal McpResult()
	{
	}

	/// <summary>
	/// A text block. Anything that isn't a string gets serialized to json.
	/// </summary>
	public static McpResult Text( object value ) => new McpResult().WithText( value );

	/// <summary>
	/// A structured content block - the value as json the client binds against the tool's
	/// output schema, paired with the same json as text for clients that don't.
	/// </summary>
	public static McpResult Structured( object value )
	{
		var node = JsonSerializer.SerializeToNode( value, SerializeOptions );

		return new McpResult { structured = node }.WithText( node?.ToJsonString() );
	}

	/// <summary>
	/// A png image block.
	/// </summary>
	public static McpResult Image( Bitmap bitmap ) => new McpResult().WithImage( bitmap );

	/// <summary>
	/// An image block from already encoded data.
	/// </summary>
	public static McpResult Image( byte[] data, string mimeType ) => new McpResult().WithImage( data, mimeType );

	/// <summary>
	/// Append a text block. Anything that isn't a string gets serialized to json.
	/// </summary>
	public McpResult WithText( object value )
	{
		content.Add( new JsonObject
		{
			["type"] = "text",
			["text"] = value as string ?? Serialize( value )
		} );

		return this;
	}

	/// <summary>
	/// Append a png image block.
	/// </summary>
	public McpResult WithImage( Bitmap bitmap ) => WithImage( bitmap.ToPng(), "image/png" );

	/// <summary>
	/// Append an image block from already encoded data.
	/// </summary>
	public McpResult WithImage( byte[] data, string mimeType )
	{
		content.Add( new JsonObject
		{
			["type"] = "image",
			["data"] = Convert.ToBase64String( data ),
			["mimeType"] = mimeType
		} );

		return this;
	}

	/// <summary>
	/// Append every content block from another result. A result can only carry one
	/// structuredContent, so in a batch the text twin carries the data instead.
	/// </summary>
	internal McpResult Append( McpResult other )
	{
		foreach ( var block in other.content )
		{
			content.Add( block.DeepClone() );
		}

		return this;
	}

	internal JsonNode ToJson( bool isError = false )
	{
		var result = new JsonObject { ["content"] = content };

		if ( structured is not null )
		{
			result["structuredContent"] = structured;
		}

		if ( isError )
		{
			result["isError"] = true;
		}

		return result;
	}

	static JsonSerializerOptions serializeOptions;
	static JsonSerializerOptions clonedFrom;

	/// <summary>
	/// The engine's json options, except live scene objects become compact references
	/// instead of scene file serialization.
	/// </summary>
	static JsonSerializerOptions SerializeOptions
	{
		get
		{
			// Json.Initialize rebuilds the engine options on hotload - follow it
			if ( serializeOptions is null || clonedFrom != Json.options )
			{
				clonedFrom = Json.options;

				serializeOptions = new JsonSerializerOptions( clonedFrom );
				serializeOptions.Converters.Insert( 0, new GameObjectRef() );
				serializeOptions.Converters.Insert( 0, new ComponentRef() );
			}

			return serializeOptions;
		}
	}

	/// <summary>
	/// Serialize a tool's return value for the agent.
	/// </summary>
	static string Serialize( object value )
	{
		return JsonSerializer.Serialize( value, SerializeOptions );
	}

	/// <summary>
	/// A game object in a result becomes { type, id, name } - the id chains into
	/// get_game_object, and nobody gets a serialized scene subtree by accident.
	/// </summary>
	class GameObjectRef : JsonConverter<GameObject>
	{
		public override bool CanConvert( Type type ) => typeof( GameObject ).IsAssignableFrom( type );

		public override GameObject Read( ref Utf8JsonReader reader, Type type, JsonSerializerOptions options )
			=> throw new NotSupportedException();

		public override void Write( Utf8JsonWriter writer, GameObject go, JsonSerializerOptions options )
		{
			if ( !go.IsValid )
			{
				writer.WriteNullValue();
				return;
			}

			writer.WriteStartObject();
			writer.WriteString( "type", go.GetType().Name );
			writer.WriteString( "id", go.Id );
			writer.WriteString( "name", go.Name );
			writer.WriteEndObject();
		}
	}

	/// <summary>
	/// A component in a result becomes { type, id, gameObject } - type is the component class,
	/// gameObject is the owning game object's id.
	/// </summary>
	class ComponentRef : JsonConverter<Component>
	{
		public override bool CanConvert( Type type ) => typeof( Component ).IsAssignableFrom( type );

		public override Component Read( ref Utf8JsonReader reader, Type type, JsonSerializerOptions options )
			=> throw new NotSupportedException();

		public override void Write( Utf8JsonWriter writer, Component component, JsonSerializerOptions options )
		{
			if ( !component.IsValid )
			{
				writer.WriteNullValue();
				return;
			}

			writer.WriteStartObject();
			writer.WriteString( "type", component.GetType().Name );
			writer.WriteString( "id", component.Id );
			writer.WriteString( "gameObject", component.GameObject.Id );
			writer.WriteEndObject();
		}
	}
}
