using Editor.NodeEditor;
using Editor.ShaderGraph.Nodes;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

partial class ShaderGraph
{
	internal static JsonSerializerOptions SerializerOptions( bool indented = false )
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

	public string Serialize()
	{
		var doc = new JsonObject();
		var options = SerializerOptions( true );

		SerializeObject( this, doc, options );
		SerializeNodes( Nodes, doc, options );
		SerializeParameters( Parameters, doc, options );

		doc.Add( "__version", JsonSerializer.SerializeToNode( Version, options ) );

		return doc.ToJsonString( options );
	}

	public void Deserialize( string json, string subgraphPath = null )
	{
		using var doc = JsonDocument.Parse( json );
		var root = doc.RootElement;
		var options = SerializerOptions();
		var fileVersion = GetVersion( root );

		DeserializeObject( this, root, options );
		DeserializeParameters( root, options );
		DeserializeNodes( root, options, subgraphPath, fileVersion );
	}

	public IEnumerable<BaseNode> DeserializeNodes( string json, bool useCurrentVersion = false )
	{
		using var doc = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip } );
		var root = doc.RootElement;
		var fileVersion = GetVersion( root );

		if ( useCurrentVersion )
		{
			fileVersion = Version;
		}

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

			if ( fileVersion < 2 && ShouldUseNewParameterTypeName_v2Upgrade( typeName ) )
			{
				typeName = GetNewParameterTypeName_v2Upgrade( typeName );
			}

			var typeDesc = EditorTypeLibrary.GetType( typeName );
			var type = new ClassNodeType( typeDesc );

			BaseNode node;
			if ( typeDesc is null )
			{
				var missingNode = new MissingNode( typeName, element );
				node = missingNode;
				DeserializeObject( node, element, options );
			}
			else
			{
				// Check if this is a legacy parameter node that should be upgraded to SubgraphInput
				// Only upgrade for old subgraph files (files without Version property aka. 0 -> 1)
				if ( IsSubgraph && fileVersion < 1 && ShouldUpgradeToSubgraphInput_v1Upgrade( typeName, element ) )
				{
					node = CreateUpgradedSubgraphInput_v1Upgrade( typeName, element, options );
				}
				else if ( fileVersion < 2 )
				{
					if ( !IsSubgraph && IsParameterNodeType_v2Upgrade( typeName ) )
					{
						if ( IsNamedParameterNode_v2Upgrade( element ) )
						{
							node = EditorTypeLibrary.Create<BaseNode>( typeName );
							DeserializeObject( node, element, options );

							var parameter = CreateBlackboardParameter_v2Upgrade( typeName, element, options );

							if ( !HasParameterWithName( parameter.Name ) )
							{
								AddParameter( parameter );
							}

							if ( node is IParameterNode parameterNode )
							{
								parameterNode.ParameterIdentifier = parameter.Identifier;
								node = (BaseNode)parameterNode;
							}
						}
						else
						{
							node = ConvertToConstantNode_v2Upgrade( typeName, element, options );
						}
					}
					else if ( ShouldUpgradeSamplerNodeType_v2Upgrade( typeName ) )
					{
						if ( IsNamedTextureSamplerNode_v2Upgrade( element ) )
						{
							node = EditorTypeLibrary.Create<BaseNode>( typeName );
							DeserializeObject( node, element, options );
							node.Graph = this;

							var parameter = CreateBlackboardParameter_v2Upgrade( typeName, element, options );

							if ( !HasParameterWithName( parameter.Name ) )
							{
								AddParameter( parameter );
							}

							if ( !IsSubgraph )
							{
								var newTexture2DParameterNode = new Texture2DParameterNode()
								{
									Position = node.Position.WithX( node.Position.x - 192 ),
									ParameterIdentifier = parameter.Identifier,
								};

								AddNode( newTexture2DParameterNode );

								node.ConnectNode(
									"Texture2D",
									"Result",
									newTexture2DParameterNode.Identifier
								);
							}
							else
							{
								var subgraphInput = new SubgraphInput()
								{
									Position = node.Position.WithX( node.Position.x - 192 ),
									ParameterIdentifier = parameter.Identifier,
								};

								AddNode( subgraphInput );

								node.ConnectNode(
									"Texture2D",
									"Result",
									subgraphInput.Identifier
								);
							}
						}
						else
						{
							node = EditorTypeLibrary.Create<BaseNode>( typeName );
							DeserializeObject( node, element, options );
						}
					}
					else if ( typeName == "Branch" )
					{
						node = UpgradeBranchNode_v2Upgrade( element, options );
					}
					else if ( IsSubgraph && typeName == "SubgraphInput" )
					{
						node = UpgradeSubgraphinput_v2Upgrade( element, options );
					}
					else if ( IsSubgraph && IsParameterNodeType_v2Upgrade( typeName ) ) 
					{
						// If we come across a parameter node in a subgraph. Just convert it to a constant.
						node = ParameterNodeToConstantNode_v2Upgrade( typeName, element, options );
					}
					else
					{
						node = EditorTypeLibrary.Create<BaseNode>( typeName );
						DeserializeObject( node, element, options );
					}
				}
				else
				{
					node = EditorTypeLibrary.Create<BaseNode>( typeName );
					DeserializeObject( node, element, options );
				}

				if ( identifiers != null && _nodes.ContainsKey( node.Identifier ) )
				{
					identifiers.Add( node.Identifier, node.NewIdentifier() );
				}

				if ( node is FunctionResult funcResult )
				{
					funcResult.CreateInputs();
				}

				if ( node is BaseNode.INodeInitialize nodeInitialize )
				{
					nodeInitialize.OnNodeDeserialize( element, options );
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

	public IEnumerable<BlackboardParameter> DeserializeParameters( string json )
	{
		using var doc = JsonDocument.Parse( json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip } );
		var root = doc.RootElement;

		return DeserializeParameters( root, SerializerOptions() );
	}

	private IEnumerable<BlackboardParameter> DeserializeParameters( JsonElement doc, JsonSerializerOptions options )
	{
		var parameters = new Dictionary<string, BlackboardParameter>();

		if ( doc.TryGetProperty( "parameters", out var arrayProperty ) )
		{
			foreach ( var element in arrayProperty.EnumerateArray() )
			{
				var typeName = element.GetProperty( "_class" ).GetString();
				var typeDesc = EditorTypeLibrary.GetType<BlackboardParameter>( typeName );
				var type = new ClassBlackboardParameterType( typeDesc );

				BlackboardParameter parameter;

				if ( typeDesc != null )
				{
					parameter = EditorTypeLibrary.Create<BlackboardParameter>( typeName );
					DeserializeObject( parameter, element, options );

					if ( string.IsNullOrWhiteSpace( parameter.Name ) )
					{
						var name = "Parameter";
						var id = name;
						int count = 0;

						while ( parameters.ContainsKey( id ) )
						{
							id = $"{name}_{count++}";
						}

						parameter.Name = id;
					}

					parameters.Add( parameter.Name, parameter );

					AddParameter( parameter );
				}
			}
		}

		return parameters.Values;
	}

	public string SerializeNodes()
	{
		return SerializeNodes( Nodes );
	}

	public string UndoStackSerialize()
	{
		var doc = new JsonObject();
		var options = SerializerOptions();

		doc = SerializeNodes( Nodes, doc );

		return SerializeParameters( Parameters, doc ).ToJsonString( options );
	}

	public string SerializeNodes( IEnumerable<BaseNode> nodes )
	{
		var doc = new JsonObject();
		var options = SerializerOptions();

		SerializeNodes( nodes, doc, options );

		return doc.ToJsonString( options );
	}

	public JsonObject SerializeNodes( IEnumerable<BaseNode> nodes, JsonObject doc )
	{
		var options = SerializerOptions();

		SerializeNodes( nodes, doc, options );

		return doc;
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

	public string SerializeParameters()
	{
		return SerializeParameters( Parameters );
	}

	private string SerializeParameters( IEnumerable<BlackboardParameter> parameters )
	{
		var doc = new JsonObject();
		var options = SerializerOptions();

		SerializeParameters( parameters, doc, options );

		return doc.ToJsonString( options );
	}

	private JsonObject SerializeParameters( IEnumerable<BlackboardParameter> parameters, JsonObject doc )
	{
		var options = SerializerOptions();

		SerializeParameters( parameters, doc, options );

		return doc;
	}

	private static void SerializeParameters( IEnumerable<BlackboardParameter> parameters, JsonObject doc, JsonSerializerOptions options )
	{
		var parameterArray = new JsonArray();

		foreach ( var parameter in parameters )
		{
			var type = parameter.GetType();
			var parameterObject = new JsonObject { { "_class", type.Name } };

			SerializeObject( parameter, parameterObject, options );

			parameterArray.Add( parameterObject );
		}

		doc.Add( "parameters", parameterArray );
	}
}
