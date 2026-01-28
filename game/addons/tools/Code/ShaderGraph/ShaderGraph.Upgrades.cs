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
			return string.IsNullOrWhiteSpace( nameProperty.GetString() );
		}

		// No "Name" property? assume its ment to be a constant
		return false;
	}
	private static bool IsNamedTextureSamplerNode_v2Upgrade( JsonElement element )
	{
		if ( element.TryGetProperty( "UI", out var uiProperty ) && uiProperty.TryGetProperty( "Name", out var nameProperty ) )
		{
			return string.IsNullOrWhiteSpace( nameProperty.GetString() );
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

	private static bool ShouldUpgradeNodeType_v2Upgrade( string typeName )
	{
		return typeName switch
		{
			"FloatParameter" => true,
			"Float2Parameter" => true,
			"Float3Parameter" => true,
			"ColorParameter" => true,
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
			subgraphInput.InputName = nameProperty.GetString();
		}

		// Map the parameter type to InputType and set default values
		switch ( typeName )
		{
			case "Float":
				subgraphInput.InputType = InputType.Float;
				if ( element.TryGetProperty( "Value", out var floatValue ) )
				{
					subgraphInput.DefaultFloat = floatValue.GetSingle();
				}
				break;

			case "Float2":
				subgraphInput.InputType = InputType.Float2;
				if ( element.TryGetProperty( "Value", out var float2Value ) )
				{
					var vector2 = JsonSerializer.Deserialize<Vector2>( float2Value.GetRawText(), options );
					subgraphInput.DefaultFloat2 = vector2;
				}
				break;

			case "Float3":
				subgraphInput.InputType = InputType.Float3;
				if ( element.TryGetProperty( "Value", out var float3Value ) )
				{
					var vector3 = JsonSerializer.Deserialize<Vector3>( float3Value.GetRawText(), options );
					subgraphInput.DefaultFloat3 = vector3;
				}
				break;

			case "Float4":
				subgraphInput.InputType = InputType.Color;
				if ( element.TryGetProperty( "Value", out var float4Value ) )
				{
					var color = JsonSerializer.Deserialize<Color>( float4Value.GetRawText(), options );
					subgraphInput.DefaultColor = color;
				}
				break;
		}

		return subgraphInput;
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

	private Guid AddBlackboardParameter_v2Upgrade( string typeName, JsonElement element, JsonSerializerOptions options )
	{
		if ( IsSubgraph )
		{
			// TODO
			throw new NotImplementedException();
		}
		else
		{
			BlackboardParameter blackboardParameter = null;

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

			switch ( typeName )
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
				case "TextureSampler":
					blackboardParameter = new Texture2DBlackboardParameter()
					{
						Name = textureInputNameElement.GetString(),
						DefaultTexture = imageElement.GetString(),
						UI = uiElement.Deserialize<TextureInput>( options ),
					};
					break;
				case "TextureTriplanar":
					blackboardParameter = new Texture2DBlackboardParameter()
					{
						Name = textureInputNameElement.GetString(),
						DefaultTexture = imageElement.GetString(),
						UI = uiElement.Deserialize<TextureInput>( options ),
					};
					break;
				default:
					throw new NotImplementedException();
			}

			if ( !ContainsParameterWithName( blackboardParameter.Name ) )
			{
				AddParameter( blackboardParameter );
			}

			return blackboardParameter.Identifier;
		}
	}

	/*
	[SGJsonUpgrader( typeof( ShaderGraph ), 2 )]
	public static void ShaderGraphUpgrader_v2( JsonObject jsonObj )
	{
		try
		{
			Log.Info( "Running ShaderGraph v2 JsonUpgrader" );

			if ( jsonObj.TryGetPropertyValue( nameof( IsSubgraph ), out var isSubgraphJsonNode ) && isSubgraphJsonNode is JsonValue boolJsonValue && boolJsonValue.TryGetValue<bool>( out var isSubgraph ) )
			{
				if ( isSubgraph )
				{
					Log.Info( "v2 Upgrader operating on a subgraph" );
				}
			}
		}
		catch
		{
		}
	}
	*/

	private static JsonElement UpgradeShaderGraph( int versionNumber, Type type, JsonElement jsonElement, JsonSerializerOptions serializerOptions )
	{
		var jsonObject = JsonNode.Parse( jsonElement.GetRawText() ) as JsonObject;

		SGJsonUpgrader.Upgrade( versionNumber, jsonObject, type );

		return JsonSerializer.Deserialize<JsonElement>( jsonObject.ToJsonString(), serializerOptions );
	}
}
