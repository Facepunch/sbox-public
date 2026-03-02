using Editor.NodeEditor;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

partial class ShaderGraph
{
	/// <summary>
	/// Handles node upgrades for the given <paramref name="fileVersion"/>. 
	/// Returns true on a successful upgrade and false when no upgrade has been performed.
	/// </summary>
	/// <param name="fileVersion">Current file version of the graph we are deserializing.</param>
	/// <param name="typeName">Type name of the node we are attempting to upgrade.</param>
	/// <param name="element">JsonElement of the node we are attempting to upgrade.</param>
	/// <param name="options"></param>
	/// <param name="node">Resulting upgraded node.</param>
	private bool HandleNodeUpgrades( int fileVersion, string typeName, JsonElement element, JsonSerializerOptions options, ref BaseNode node )
	{
		// Check if this is a legacy parameter node that should be upgraded to SubgraphInput
		// Only upgrade for old subgraph files (files without Version property aka. 0 -> 1)
		if ( IsSubgraph && fileVersion < 1 && ShouldUpgradeToSubgraphInput( typeName, element ) )
		{
			node = CreateUpgradedSubgraphInput( typeName, element, options );

			return true;
		}

		return false;
	}

	/// <summary>
	/// Check if a legacy parameter node should be upgraded to SubgraphInput.
	/// </summary>
	private static bool ShouldUpgradeToSubgraphInput( string typeName, JsonElement element )
	{
		// Only upgrade if it's a parameter node type
		if ( !IsParameterNodeType( typeName ) )
			return false;

		// Only upgrade if it has a name (indicating it's meant to be an input)
		if ( element.TryGetProperty( "Name", out var nameProperty ) )
		{
			var name = nameProperty.GetString();
			return !string.IsNullOrWhiteSpace( name );
		}

		return false;
	}

	/// <summary>
	/// Check if the type name represents a parameter node
	/// </summary>
	private static bool IsParameterNodeType( string typeName )
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

	/// <summary>
	/// Create a new SubgraphInput node from a legacy parameter node
	/// </summary>
	private SubgraphInput CreateUpgradedSubgraphInput( string typeName, JsonElement element, JsonSerializerOptions options )
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
}
