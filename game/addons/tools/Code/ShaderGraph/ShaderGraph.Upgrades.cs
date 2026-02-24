using Editor.ShaderGraph.Nodes;
using System.Text.Json.Nodes;

namespace Editor.ShaderGraph;

partial class ShaderGraph
{
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

		Log.Warning( $"JsonElement has no property named \"__version\" or \"Version\". Defaulting to 0...." );

		return 0;
	}

	/// <summary>
	/// Check if a legacy parameter node should be upgraded to SubgraphInput.
	/// </summary>
	private static bool ShouldUpgradeToSubgraphInput_v1Upgrade( string typeName, JsonElement element )
	{
		// Only upgrade if it's a parameter node type
		if ( !IsParameterNodeType_v1Upgrade( typeName ) )
			return false;

		// Only upgrade if it has a name (indicating it's meant to be an input)
		if ( element.TryGetProperty( "Name", out var nameProperty ) )
		{
			var name = nameProperty.GetString();
			return !string.IsNullOrWhiteSpace( name );
		}

		return false;
	}

	private static bool IsNamedParameterNode_v2Upgrade( JsonElement element )
	{
		// Only convert if it dosent have a name (indicating it's meant to be a constant value)
		if ( element.TryGetProperty( "Name", out var nameProperty ) )
		{
			return !string.IsNullOrWhiteSpace( nameProperty.GetString() );
		}

		// No "Name" property? assume its ment to be a constant
		return false;
	}
	private static bool IsNamedTextureSamplerNode_v2Upgrade( JsonElement element )
	{
		if ( element.TryGetProperty( "UI", out var uiProperty ) && uiProperty.TryGetProperty( "Name", out var nameProperty ) )
		{
			return !string.IsNullOrWhiteSpace( nameProperty.GetString() );
		}

		return false;
	}

	private static bool ShouldUseNewParameterTypeName_v2Upgrade( string typeName )
	{
		return typeName switch
		{
			"Float" => true,
			"Float2" => true,
			"Float3" => true,
			"Float4" => true,
			_ => false
		};
	}

	/// <summary>
	/// Check if the type name represents a parameter node.
	/// </summary>
	private static bool IsParameterNodeType_v1Upgrade( string typeName )
	{
		return typeName switch
		{
			"Float" => true,
			"Float2" => true,
			"Float3" => true,
			"Float4" => true,
			"TextureSampler" => true,
			_ => false
		};
	}

	private static bool IsParameterNodeType_v2Upgrade( string typeName )
	{
		return typeName switch
		{
			"FloatParameter" => true,
			"Float2Parameter" => true,
			"Float3Parameter" => true,
			"ColorParameter" => true,
			_ => false
		};
	}

	private static bool ShouldUpgradeSamplerNodeType_v2Upgrade( string typeName )
	{
		return typeName switch
		{
			"TextureSampler" => true,
			"TextureTriplanar" => true,
			"NormapMapTriplanar" => true,
			_ => false
		};
	}

	/// <summary>
	/// Create a new SubgraphInput node from a legacy parameter node
	/// </summary>
	private static SubgraphInput CreateUpgradedSubgraphInput_v1Upgrade( string typeName, JsonElement element, JsonSerializerOptions options )
	{
		var subgraphInput = new SubgraphInput();

		// Copy basic node properties
		DeserializeObject( subgraphInput, element, options );

		// Set input name from the parameter's Name property
		if ( element.TryGetProperty( "Name", out var nameProperty ) )
		{
			//subgraphInput.InputName = nameProperty.GetString();
		}

		// Map the parameter type to InputType and set default values
		switch ( typeName )
		{
			case "Float":
				//subgraphInput.InputType = InputType.Float;
				if ( element.TryGetProperty( "Value", out var floatValue ) )
				{
					subgraphInput.DefaultFloat = floatValue.GetSingle();
				}
				break;

			case "Float2":
				//subgraphInput.InputType = InputType.Float2;
				if ( element.TryGetProperty( "Value", out var float2Value ) )
				{
					var vector2 = JsonSerializer.Deserialize<Vector2>( float2Value.GetRawText(), options );
					subgraphInput.DefaultFloat2 = vector2;
				}
				break;

			case "Float3":
				//subgraphInput.InputType = InputType.Float3;
				if ( element.TryGetProperty( "Value", out var float3Value ) )
				{
					var vector3 = JsonSerializer.Deserialize<Vector3>( float3Value.GetRawText(), options );
					subgraphInput.DefaultFloat3 = vector3;
				}
				break;

			case "Float4":
				//subgraphInput.InputType = InputType.Color;
				if ( element.TryGetProperty( "Value", out var float4Value ) )
				{
					var color = JsonSerializer.Deserialize<Color>( float4Value.GetRawText(), options );
					subgraphInput.DefaultColor = color;
				}
				break;
		}

		return subgraphInput;
	}

	private BaseNode UpgradeBranchNode_v2Upgrade( JsonElement element, JsonSerializerOptions options )
	{
		element.TryGetProperty( "Name", out var nameElement );
		element.TryGetProperty( "Enabled", out var enabledElement );

		if ( string.IsNullOrWhiteSpace( nameElement.GetString() ) )
		{
			var compareNode = new Compare();

			// Copy basic node properties
			DeserializeObject( compareNode, element, options );

			element.TryGetProperty( "Operator", out var operatorElement );
			compareNode.Operator = operatorElement.Deserialize<Compare.OperatorType>( options );

			return compareNode;
		}
		else
		{
			var branchNode = new Branch();

			// Copy basic node properties
			DeserializeObject( branchNode, element, options );
			branchNode.Graph = this;

			BaseNode parameterNode;

			if ( !IsSubgraph )
			{
				var boolParameter = new BoolBlackboardParameter()
				{
					Name = nameElement.GetString(),
					Value = enabledElement.GetBoolean()
				};

				AddParameter( boolParameter );

				parameterNode = new BoolParameter()
				{
					Position = branchNode.Position.WithX( branchNode.Position.x - 192 ),
					ParameterIdentifier = boolParameter.Identifier,
				};
			}
			else
			{
				var boolParameter = new BoolSubgraphInputBlackboardParameter()
				{
					Name = nameElement.GetString(),
					Value = enabledElement.GetBoolean()
				};

				AddParameter( boolParameter );

				parameterNode = new SubgraphInput()
				{
					ParameterIdentifier = boolParameter.Identifier,
				};
			}

			AddNode( parameterNode );

			branchNode.ConnectNode(
				nameof( Branch.Predicate ),
				nameof( SubgraphInput.Result ),
				parameterNode.Identifier
			);

			return branchNode;
		}
	}

	private static BaseNode ConvertToConstantNode_v2Upgrade( string typeName, JsonElement element, JsonSerializerOptions options )
	{
		if ( element.TryGetProperty( "Value", out var valueElement ) )
		{
			BaseNode newNode = null;

			switch ( typeName )
			{
				case "FloatParameter":
					newNode = new ConstantFloat()
					{
						Value = valueElement.GetSingle()
					};
					break;
				case "Float2Parameter":
					var vector2 = JsonSerializer.Deserialize<Vector2>( valueElement.GetRawText(), options );
					newNode = new ConstantFloat2()
					{
						Value = vector2
					};
					break;
				case "Float3Parameter":
					var vector3 = JsonSerializer.Deserialize<Vector3>( valueElement.GetRawText(), options );
					newNode = new ConstantFloat3()
					{
						Value = vector3
					};
					break;
				case "ColorParameter":
					var color = JsonSerializer.Deserialize<Color>( valueElement.GetRawText(), options );
					newNode = new ConstantColor()
					{
						Value = color
					};
					break;
			}

			if ( newNode == null )
				throw new Exception( "Couldnt convert nameless Parameter node to Constant node" );

			// Copy basic node properties
			DeserializeObject( newNode, element, options );

			return newNode;
		}

		throw new Exception( "Couldnt convert nameless Parameter node to Constant node" );
	}

	private SubgraphInput UpgradeSubgraphinput_v2Upgrade( JsonElement element, JsonSerializerOptions options )
	{
		element.TryGetProperty( "Position", out var nodePositionElement );
		element.TryGetProperty( "Identifier", out var identifierElement );
		element.TryGetProperty( "InputName", out var inputNameElement );
		element.TryGetProperty( "InputDescription", out var inputDescriptionElement );
		element.TryGetProperty( "InputType", out var inputTypeElement );
		element.TryGetProperty( "IsRequired", out var isRequiredElement );
		element.TryGetProperty( "PortOrder", out var portOrderElement );

		var nodePosition = JsonSerializer.Deserialize<Vector2>( nodePositionElement.GetRawText(), options );
		var inputName = inputNameElement.GetString();
		var inputDescription = inputDescriptionElement.GetString();
		var inputType = JsonSerializer.Deserialize<InputType>( inputTypeElement.GetRawText(), options );
		var isRequired = isRequiredElement.GetBoolean();
		var portOrder = portOrderElement.GetInt32();

		object defaultValue = null;
		BlackboardParameter parameter = null;
		switch ( inputType )
		{
			case InputType.Float:
				if ( element.TryGetProperty( "DefaultFloat", out var defaultFloatElement ) )
				{
					defaultValue = defaultFloatElement.GetSingle();
				}
				break;
			case InputType.Float2:
				if ( element.TryGetProperty( "DefaultFloat2", out var defaultFloat2Element ) )
				{
					defaultValue = JsonSerializer.Deserialize<Vector2>( defaultFloat2Element.GetRawText(), options );
				}
				break;
			case InputType.Float3:
				if ( element.TryGetProperty( "DefaultFloat3", out var defaultFloat3Element ) )
				{
					defaultValue = JsonSerializer.Deserialize<Vector3>( defaultFloat3Element.GetRawText(), options );
				}
				break;
			case InputType.Color:
				if ( element.TryGetProperty( "DefaultColor", out var defaultColorElement ) )
				{
					defaultValue = JsonSerializer.Deserialize<Color>( defaultColorElement.GetRawText(), options );
				}
				break;
			default:
				throw new NotImplementedException( $"Unknown inputType {inputType}" );
		}

		parameter = CreateBlackboardParameter_v2Upgrade( "SubgraphInput", element, options );

		if ( defaultValue == null )
		{
			throw new Exception();
		}

		if ( parameter is ISubgraphInputBlackboardParameter subgraphInputParameter )
		{
			subgraphInputParameter.Name = inputName;
			subgraphInputParameter.InputDescription = inputDescription;
			subgraphInputParameter.IsRequired = isRequired;
			subgraphInputParameter.PortOrder = portOrder;
			subgraphInputParameter.SetValue( defaultValue );

			if ( !ContainsParameterWithName( parameter.Name ) )
			{
				AddParameter( parameter );
			}
		}

		return new SubgraphInput()
		{
			Position = nodePosition,
			Identifier = identifierElement.GetString(),
			ParameterIdentifier = parameter.Identifier,
			DefaultValue = defaultValue
		};
	}

	private static string GetNewParameterTypeName_v2Upgrade( string typeName )
	{
		return typeName switch
		{
			"Float" => "FloatParameter",
			"Float2" => "Float2Parameter",
			"Float3" => "Float3Parameter",
			"Float4" => "ColorParameter",
			_ => throw new NotImplementedException()
		};
	}

	private BlackboardParameter CreateBlackboardParameter_v2Upgrade( string nodeTypeName, JsonElement element, JsonSerializerOptions options )
	{
		BlackboardParameter blackboardParameter = null;

		if ( IsSubgraph )
		{
			if ( nodeTypeName == "TextureSampler" || nodeTypeName == "NormapMapTriplanar" || nodeTypeName == "NormapMapTriplanar" )
			{
				element.TryGetProperty( "UI", out var textureInputElement );
				textureInputElement.TryGetProperty( "Name", out var textureInputNameElement );
				element.TryGetProperty( "Image", out var imageElement );

				if ( ContainsParameterWithName( textureInputNameElement.GetString() ) )
				{
					return FindParameter<Texture2DSubgraphInputBlackboardParameter>( textureInputNameElement.GetString() );
				}

				blackboardParameter = new Texture2DSubgraphInputBlackboardParameter()
				{
					Name = textureInputNameElement.GetString(),
					Value = textureInputElement.Deserialize<TextureInput>( options ) with { DefaultTexture = imageElement.GetString() },
				};

			}
			else if ( nodeTypeName == nameof( SubgraphInput ) )
			{
				element.TryGetProperty( "InputType", out var inputTypeElement );
				var inputType = JsonSerializer.Deserialize<InputType>( inputTypeElement.GetRawText(), options );

				blackboardParameter = inputType switch
				{
					InputType.Float => new FloatSubgraphInputBlackboardParameter( "", 0.0f ),
					InputType.Float2 => new Float2SubgraphInputBlackboardParameter( "", Vector2.Zero ),
					InputType.Float3 => new Float3SubgraphInputBlackboardParameter( "", Vector3.Zero ),
					InputType.Color => new ColorSubgraphInputBlackboardParameter( "", Color.Black ),
					_ => throw new NotImplementedException( $"Unknown inputType : {inputType}" ),
				};
			}
			else
			{
				throw new NotImplementedException( $"Unknown node type : {nodeTypeName}" );
			}

			return blackboardParameter;
		}
		else
		{
			// Parameter Node Data 
			element.TryGetProperty( "Name", out var nameElement );
			element.TryGetProperty( "Value", out var valueElement );
			element.TryGetProperty( "Min", out var minElement );
			element.TryGetProperty( "Max", out var maxElement );
			element.TryGetProperty( "UI", out var uiElement );
			element.TryGetProperty( "IsAttribute", out var isAttributeElement );

			// Texture Sampler Node Data
			element.TryGetProperty( "UI", out var textureInputElement );
			textureInputElement.TryGetProperty( "Name", out var textureInputNameElement );
			element.TryGetProperty( "Image", out var imageElement );

			switch ( nodeTypeName )
			{
				case nameof( FloatParameter ):
					blackboardParameter = new FloatBlackboardParameter()
					{
						Name = nameElement.GetString(),
						Value = valueElement.GetSingle(),
						Min = minElement.GetSingle(),
						Max = maxElement.GetSingle(),
						UI = uiElement.Deserialize<FloatParameterUI>( options ),
						IsAttribute = isAttributeElement.GetBoolean(),
					};
					break;
				case nameof( Float2Parameter ):
					blackboardParameter = new Float2BlackboardParameter()
					{
						Name = nameElement.GetString(),
						Value = valueElement.Deserialize<Vector2>( options ),
						Min = minElement.Deserialize<Vector2>( options ),
						Max = maxElement.Deserialize<Vector2>( options ),
						UI = uiElement.Deserialize<FloatParameterUI>( options ),
						IsAttribute = isAttributeElement.GetBoolean(),
					};
					break;
				case nameof( Float3Parameter ):
					blackboardParameter = new Float3BlackboardParameter()
					{
						Name = nameElement.GetString(),
						Value = valueElement.Deserialize<Vector3>( options ),
						Min = minElement.Deserialize<Vector3>( options ),
						Max = maxElement.Deserialize<Vector3>( options ),
						UI = uiElement.Deserialize<FloatParameterUI>( options ),
						IsAttribute = isAttributeElement.GetBoolean(),
					};
					break;
				case nameof( ColorParameter ):
					blackboardParameter = new ColorBlackboardParameter()
					{
						Name = nameElement.GetString(),
						Value = valueElement.Deserialize<Color>( options ),
						UI = uiElement.Deserialize<ColorParameterUI>( options ),
						IsAttribute = isAttributeElement.GetBoolean(),
					};
					break;
				case string typename when (typename == "TextureSampler" || typename == "TextureTriplanar" || typename == "NormapMapTriplanar"):
					if ( ContainsParameterWithName( textureInputNameElement.GetString() ) )
					{
						return FindParameter<Texture2DBlackboardParameter>( textureInputNameElement.GetString() );
					}

					blackboardParameter = new Texture2DBlackboardParameter()
					{
						Name = textureInputNameElement.GetString(),
						Value = uiElement.Deserialize<TextureInput>( options ) with { DefaultTexture = imageElement.GetString() },
					};
					break;
				default:
					throw new NotImplementedException( $"Unknown node type : {nodeTypeName}" );
			}

			return blackboardParameter;
		}
	}
}
