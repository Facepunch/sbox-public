using Editor.NodeEditor;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

partial class ShaderGraph
{
	private static JsonSerializerOptions SerializerOptions( bool indented = false )
	{
		var options = new JsonSerializerOptions
		{
			WriteIndented = indented,
			PropertyNameCaseInsensitive = true,
			NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals,
			DefaultIgnoreCondition = JsonIgnoreCondition.Never,
			ReadCommentHandling = JsonCommentHandling.Skip,
		};

		options.Converters.Add( new JsonStringEnumConverter( null, true ) );

		return options;
	}

	/// <summary>
	/// Json Keys used for serialization and deserialization.
	/// </summary>
	internal static class JsonKeys
	{
		internal const string Version = "__version";
		internal const string Class = "_class";
		internal const string NodeArray = "nodes";
	}

	/// <summary>
	/// Gets the version of the provided JsonElement. Returns 0 on failure.
	/// </summary>
	private static int GetVersion( JsonElement element )
	{
		if ( element.TryGetProperty( "__version", out var versionElement ) )
		{
			return versionElement.GetInt32();
		}
		else if ( element.TryGetProperty( nameof( Version ), out var oldVersionElement ) )
		{
			return oldVersionElement.GetInt32();
		}

		return 0;
	}

	public string Serialize()
	{
		var doc = new JsonObject();
		var options = SerializerOptions( true );

		SerializeObject( this, doc, options );
		SerializeNodes( Nodes, doc, options );

		doc.Add( "__version", JsonSerializer.SerializeToNode( Version, options ) );

		return doc.ToJsonString( options );
	}

	public void Deserialize( string json, string subgraphPath = null )
	{
		using var doc = JsonDocument.Parse( json );
		var root = doc.RootElement;
		var options = SerializerOptions();

		// Check for the version so we can handle upgrades
		var fileVersion = GetVersion( root );

		if ( HandleGraphUpgrades( fileVersion, Json.ParseToJsonObject( json ), options, out JsonElement upgradedElement ) )
		{
			root = upgradedElement;
		}

		DeserializeObject( this, root, options );
		DeserializeNodes( root, options, subgraphPath, fileVersion );
	}

	private bool HandleGraphUpgrades( int fileVersion, JsonObject json, JsonSerializerOptions options, out JsonElement upgradedElement )
	{
		upgradedElement = default;

		if ( fileVersion >= Version )
			return false;

		ShaderGraphJsonUpgrader.Upgrade( fileVersion, json, typeof( ShaderGraph ), options );

		upgradedElement = JsonSerializer.Deserialize<JsonElement>( json.ToJsonString(), options );

		return true;
	}

	public IEnumerable<BaseNode> DeserializeNodes( string json, bool useCurrentVersion = false )
	{
		using var doc = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip } );
		var root = doc.RootElement;

		// Check for version in the JSON
		var fileVersion = useCurrentVersion ? Version : GetVersion( root );

		return DeserializeNodes( root, SerializerOptions(), null, fileVersion );
	}

	private static void DeserializeObject( object obj, JsonElement doc, JsonSerializerOptions options )
	{
		var type = obj.GetType();
		var properties = type.GetProperties( BindingFlags.Instance | BindingFlags.Public )
			.Where( x => x.GetSetMethod() != null );

		foreach ( var nodeProperty in doc.EnumerateObject() )
		{
			var prop = properties.FirstOrDefault( x =>
			{
				var propName = x.Name;
				if ( x.GetCustomAttribute<JsonPropertyNameAttribute>() is JsonPropertyNameAttribute jpna )
					propName = jpna.Name;

				return string.Equals( propName, nodeProperty.Name, StringComparison.OrdinalIgnoreCase );
			} );

			if ( prop == null )
				continue;

			if ( prop.CanWrite == false )
				continue;

			if ( prop.IsDefined( typeof( JsonIgnoreAttribute ) ) )
				continue;

			prop.SetValue( obj, JsonSerializer.Deserialize( nodeProperty.Value.GetRawText(), prop.PropertyType, options ) );
		}
	}

	private IEnumerable<BaseNode> DeserializeNodes( JsonElement doc, JsonSerializerOptions options, string subgraphPath = null, int fileVersion = -1 )
	{
		var nodes = new Dictionary<string, BaseNode>();
		var identifiers = _nodes.Count > 0 ? new Dictionary<string, string>() : null;
		var connections = new List<(IPlugIn Plug, NodeInput Value)>();

		var arrayProperty = doc.GetProperty( "nodes" );
		foreach ( var element in arrayProperty.EnumerateArray() )
		{
			var typeName = element.GetProperty( "_class" ).GetString();
			var typeDesc = EditorTypeLibrary.GetType( typeName );
			var type = new ClassNodeType( typeDesc );

			BaseNode node = null;
			if ( typeDesc is null )
			{
				var missingNode = new MissingNode( typeName, element );
				node = missingNode;
				DeserializeObject( node, element, options );
			}
			else
			{
				node = EditorTypeLibrary.Create<BaseNode>( typeName );
				DeserializeObject( node, element, options );

				if ( identifiers != null && _nodes.ContainsKey( node.Identifier ) )
				{
					identifiers.Add( node.Identifier, node.NewIdentifier() );
				}

				if ( node is FunctionResult funcResult )
				{
					funcResult.CreateInputs();
				}

				if ( node is SubgraphNode subgraphNode )
				{
					if ( !FileSystem.Content.FileExists( subgraphNode.SubgraphPath ) )
					{
						var missingNode = new MissingNode( typeName, element );
						node = missingNode;
						DeserializeObject( node, element, options );
					}
					else
					{
						subgraphNode.OnNodeCreated();
					}
				}

				foreach ( var input in node.Inputs )
				{
					if ( !element.TryGetProperty( input.Identifier, out var connectedElem ) )
						continue;

					var connected = connectedElem
						.Deserialize<NodeInput?>();

					if ( connected is { IsValid: true } )
					{
						var connection = connected.Value;
						if ( !string.IsNullOrEmpty( subgraphPath ) )
						{
							connection = new()
							{
								Identifier = connection.Identifier,
								Output = connection.Output,
								Subgraph = subgraphPath
							};
						}
						connections.Add( (input, connection) );
					}
				}
			}

			nodes.Add( node.Identifier, node );

			AddNode( node );
		}

		foreach ( var (input, value) in connections )
		{
			var outputIdent = identifiers?.TryGetValue( value.Identifier, out var newIdent ) ?? false
				? newIdent : value.Identifier;

			if ( nodes.TryGetValue( outputIdent, out var node ) )
			{
				var output = node.Outputs.FirstOrDefault( x => x.Identifier == value.Output );
				if ( output is null )
				{
					// Check for Aliases
					foreach ( var op in node.Outputs )
					{
						if ( op is not BasePlugOut plugOut ) continue;

						var aliasAttr = plugOut.Info.Property?.GetCustomAttribute<AliasAttribute>();
						if ( aliasAttr is not null && aliasAttr.Value.Contains( value.Output ) )
						{
							output = plugOut;
							break;
						}
					}
				}
				input.ConnectedOutput = output;
			}
		}

		return nodes.Values;
	}

	public string SerializeNodes()
	{
		return SerializeNodes( Nodes );
	}

	public string SerializeNodes( IEnumerable<BaseNode> nodes )
	{
		var doc = new JsonObject();
		var options = SerializerOptions();

		SerializeNodes( nodes, doc, options );

		return doc.ToJsonString( options );
	}

	private static void SerializeObject( object obj, JsonObject doc, JsonSerializerOptions options, Dictionary<string, string> identifiers = null )
	{
		var type = obj.GetType();
		var properties = type.GetProperties( BindingFlags.Instance | BindingFlags.Public )
			.Where( x => x.GetSetMethod() != null );

		foreach ( var property in properties )
		{
			if ( !property.CanRead )
				continue;

			if ( property.PropertyType == typeof( NodeInput ) )
				continue;

			if ( property.IsDefined( typeof( JsonIgnoreAttribute ) ) )
				continue;

			var propertyName = property.Name;
			if ( property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } jpna )
				propertyName = jpna.Name;

			var propertyValue = property.GetValue( obj );
			if ( propertyName == "Identifier" && propertyValue is string identifier )
			{
				if ( identifiers.TryGetValue( identifier, out var newIdentifier ) )
				{
					propertyValue = newIdentifier;
				}
			}

			doc.Add( propertyName, JsonSerializer.SerializeToNode( propertyValue, options ) );
		}

		if ( obj is INode node )
		{
			foreach ( var input in node.Inputs )
			{
				if ( input.ConnectedOutput is not { } output )
					continue;

				doc.Add( input.Identifier, JsonSerializer.SerializeToNode( new NodeInput
				{
					Identifier = identifiers?.TryGetValue( output.Node.Identifier, out var newIdent ) ?? false ? newIdent : output.Node.Identifier,
					Output = output.Identifier
				} ) );
			}
		}
	}

	private static void SerializeNodes( IEnumerable<BaseNode> nodes, JsonObject doc, JsonSerializerOptions options )
	{
		var identifiers = new Dictionary<string, string>();
		foreach ( var node in nodes )
		{
			identifiers.Add( node.Identifier, $"{identifiers.Count}" );
		}

		var nodeArray = new JsonArray();

		foreach ( var node in nodes )
		{
			var type = node.GetType();
			var nodeObject = new JsonObject { { "_class", type.Name } };

			SerializeObject( node, nodeObject, options, identifiers );

			nodeArray.Add( nodeObject );
		}

		doc.Add( "nodes", nodeArray );
	}
}
