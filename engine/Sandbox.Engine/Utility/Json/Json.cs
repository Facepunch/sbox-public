using Facepunch.ActionGraphs;
using Sandbox.ActionGraphs;
using Sandbox.Engine;
using Sandbox.MovieMaker;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A convenience JSON helper that handles <see cref="Resource"/> types for you.
/// </summary>
public static partial class Json
{
	internal static JsonSerializerOptions options => GlobalContext.Current.JsonSerializerOptions;

	static Json()
	{
		Initialize();
	}

	/// <summary>
	/// Should be called on startup and when hotloading. 
	/// The reason for doing on hotloading is to clear all the types in JsonSerializableFactory
	/// </summary>
	internal static void Initialize()
	{
		var typeLibrary = Game.TypeLibrary;

		GlobalContext.Current.JsonSerializerOptions = new JsonSerializerOptions( JsonSerializerOptions.Default );
		options.WriteIndented = true;
		options.PropertyNameCaseInsensitive = true;
		options.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals;
		options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
		options.ReadCommentHandling = JsonCommentHandling.Skip;
		options.MaxDepth = 512;
		options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

		options.Converters.Add( new JsonStringEnumConverter( null, true ) );
		options.Converters.Add( new BinaryConvert() );
		options.Converters.Add( new JsonConvertFactory() );
		options.Converters.Add( new MovieResourceConverter() );
		options.Converters.Add( new AnyOfTypeConverterFactory() );
		options.Converters.Add( new InterfaceConverterFactory() );

		if ( typeLibrary is not null )
		{
			options.Converters.Add( new TypeConverter( new TypeLoader( () => typeLibrary ) ) );
		}

		options.AddActionGraphConverters( () =>
			Game.NodeLibrary ?? throw new InvalidOperationException(
				$"{nameof( Game.NodeLibrary )} not set when deserializing." ) );

		BaseFileSystem.JsonSerializerOptions = options;

		if ( typeLibrary is not null )
		{
			PopulateReflectionCache( typeLibrary );
		}
	}

	/// <summary>
	/// Try to deserialize given source to given type.
	/// </summary>
	public static object Deserialize( string source, System.Type t )
	{
		return JsonSerializer.Deserialize( source, t, options );
	}

	/// <summary>
	/// Try to deserialize given source to given type.
	/// </summary>
	public static T Deserialize<T>( string source )
	{
		return JsonSerializer.Deserialize<T>( source, options );
	}

	/// <summary>
	/// Try to deserialize given source to given type. Return true if it was a success
	/// </summary>
	public static bool TryDeserialize( string source, System.Type t, out object obj )
	{
		obj = default;

		try
		{
			obj = JsonSerializer.Deserialize( source, t, options );
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Try to deserialize given source to given type. Return true if it was a success
	/// </summary>
	public static bool TryDeserialize<T>( string source, out T obj )
	{
		obj = default;

		try
		{
			obj = JsonSerializer.Deserialize<T>( source, options );
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// Serialize an object.
	/// </summary>
	public static string Serialize( object source )
	{
		if ( source == null )
			return null;

		return JsonSerializer.Serialize( source, options );
	}

	/// <summary>
	/// Parse some Json to a JsonObject
	/// </summary>
	public static JsonObject ParseToJsonObject( string json )
	{
		return ParseToJsonNode( json ) as JsonObject;
	}

	/// <summary>
	/// Parse some Json to a JsonNode
	/// </summary>
	internal static JsonNode ParseToJsonNode( string json )
	{
		var docOptions = new JsonDocumentOptions();
		docOptions.MaxDepth = 512;

		var nodeOptions = new JsonNodeOptions();
		nodeOptions.PropertyNameCaseInsensitive = true;

		return JsonNode.Parse( json, nodeOptions, docOptions );
	}

	/// <summary>
	/// Parse some Json to a JsonNode
	/// </summary>
	public static JsonObject ParseToJsonObject( ref Utf8JsonReader reader )
	{
		var nodeOptions = new JsonNodeOptions();
		nodeOptions.PropertyNameCaseInsensitive = true;

		return JsonNode.Parse( ref reader, nodeOptions ) as JsonObject;
	}

	/// <summary>
	/// Deserialize to this existing object
	/// </summary>
	internal static void DeserializeToObject( object target, string json )
	{
		var node = ParseToJsonObject( json );

		if ( node is JsonObject jso )
		{
			DeserializeToObject( target, jso );
		}
	}

	internal static void DeserializeToObject( object target, JsonObject root )
	{
		if ( target is null )
			return;

		var type = target.GetType();

		// TODO: we can probably cache this
		var propertyDict = type.GetProperties( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )
			.Where( x => x.CanWrite )
			.Where( x =>
				x.SetMethod!.IsPublic && !x.HasAttribute( typeof( JsonIgnoreAttribute ) ) ||
				x.HasAttribute( typeof( JsonIncludeAttribute ) ) ||
				x.HasAttribute( typeof( PropertyAttribute ) ) )
			.Select( x => (Name: x.GetCustomAttribute<JsonPropertyNameAttribute>() is { } jpna ? jpna.Name : x.Name, Property: x) )
			.DistinctBy( x => x.Name, StringComparer.OrdinalIgnoreCase )
			.ToDictionary( x => x.Name, x => x.Property, StringComparer.OrdinalIgnoreCase );

		foreach ( var property in root )
		{
			if ( !propertyDict.TryGetValue( property.Key, out var prop ) )
			{
				//Log.Warning( $"Missing/unknown property {property.Name}" );
				continue;
			}

			var parsedValue = property.Value.Deserialize( prop.PropertyType, options );
			prop.SetValue( target, parsedValue );
		}
	}

	/// <summary>
	/// Serialize a single object to a JsonNode
	/// </summary>
	public static JsonNode ToNode( object obj )
	{
		return System.Text.Json.JsonSerializer.SerializeToNode( obj, options );
	}

	/// <summary>
	/// Serialize a single object to a JsonNode with the given expected type
	/// </summary>
	public static JsonNode ToNode( object obj, Type type )
	{
		if ( obj is IJsonPopulator jss )
		{
			return jss.Serialize();
		}

		return System.Text.Json.JsonSerializer.SerializeToNode( obj, type, options );
	}

	/// <summary>
	/// Deserialize a single object to a type
	/// </summary>
	public static object FromNode( JsonNode node, Type type )
	{
		if ( node is null ) return default;

		return node.Deserialize( type, options );
	}

	/// <summary>
	/// Deserialize a single object to a type
	/// </summary>
	public static T FromNode<T>( JsonNode node )
	{
		if ( node is null ) return default;
		return node.Deserialize<T>( options );
	}

	/// <summary>
	/// Serialize this object property by property - even if JsonConvert has other plans
	/// </summary>
	internal static JsonObject SerializeAsObject( object target )
	{
		var type = target.GetType();
		var doc = new JsonObject();
		var properties = type.GetProperties( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );

		foreach ( var property in properties )
		{
			if ( !property.CanRead ) continue;
			if ( property.GetMethod!.GetParameters().Length > 0 ) continue;
			if ( property.HasAttribute( typeof( JsonIgnoreAttribute ) ) ) continue;
			if ( !property.GetMethod.IsPublic && !property.HasAttribute( typeof( JsonIncludeAttribute ) ) && !property.HasAttribute( typeof( PropertyAttribute ) ) ) continue;

			var value = property.GetValue( target );

			// BinaryBlob types are handled automatically by BinaryBlobJsonConverter
			var node = JsonSerializer.SerializeToNode( value, property.PropertyType, options );

			var propName = property.Name;
			if ( property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } jpna ) propName = jpna.Name;
			doc.Add( propName, node );
		}

		return doc;
	}

	/// <summary>
	/// Deep walk though an entire Json tree, optionally changing values of nodes.
	/// </summary>
	public static JsonNode WalkJsonTree( JsonNode node, Func<string, JsonValue, JsonNode> onValue, Func<string, JsonObject, JsonObject> onObject = null )
	{
		WalkJsonTree( node, onValue, onObject, null );
		return node;
	}

	static void WalkJsonTree( in JsonNode node, in Func<string, JsonValue, JsonNode> onValue, Func<string, JsonObject, JsonObject> onObject, in string keyName )
	{
		if ( node is JsonObject jsonObject )
		{
			if ( onObject is not null )
			{
				var obj = onObject( keyName, jsonObject );

				if ( obj != jsonObject )
				{
					jsonObject.ReplaceWith( obj );
					return;
				}
			}

			foreach ( var entry in jsonObject )
			{
				WalkJsonTree( entry.Value, onValue, onObject, entry.Key );
			}

			return;
		}

		if ( node is JsonArray array )
		{
			for ( int i = 0; i < array.Count; i++ )
			{
				WalkJsonTree( array[i], onValue, onObject );
			}

			return;
		}

		if ( node is JsonValue value )
		{
			var v = onValue( keyName, value );
			if ( v != value )
			{
				value.ReplaceWith( v );
			}
		}
	}
}


/// <summary>
/// Objects that need to be deserialized into can implement this interface
/// which allows them to be populated from a JSON object.
/// </summary>
public interface IJsonPopulator
{
	JsonNode Serialize();
	void Deserialize( JsonNode node );
}
