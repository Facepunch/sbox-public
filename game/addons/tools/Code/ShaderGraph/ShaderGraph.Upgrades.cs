using Editor.NodeEditor;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

partial class ShaderGraph
{
#region Version 1 Upgrader
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
	private static SubgraphInput CreateUpgradedSubgraphInput( string typeName, JsonElement element, JsonSerializerOptions options )
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

	[JsonUpgrader( typeof( ShaderGraph ), 1 )]
	internal static void Upgrader_v1( JsonObject obj, JsonSerializerOptions options )
	{
		if ( obj[JsonKeys.NodeArray] is not JsonArray oldNodeArray )
			return;

		if ( CheckIsSubgraph( obj ) )
		{
			var identifiers = new Dictionary<string, string>();
			foreach ( var node in oldNodeArray )
			{
				if ( node[nameof( BaseNode.Identifier )] is not JsonValue identifierValue )
					continue;

				identifiers.Add( identifierValue.GetValue<string>(), $"{identifiers.Count}" );
			}

			var newNodeArray = new JsonArray();

			foreach ( var jsonNode in oldNodeArray )
			{
				if ( jsonNode[JsonKeys.Class] is not JsonValue classValue )
					continue;

				var nodeElement = JsonSerializer.Deserialize<JsonElement>( jsonNode.AsObject().ToJsonString() );
				var typeName = classValue.GetValue<string>();

				if ( ShouldUpgradeToSubgraphInput( typeName, nodeElement ) )
				{
					var newNode = CreateUpgradedSubgraphInput( typeName, nodeElement, options );
					var newNodeObject = new JsonObject { { JsonKeys.Class, newNode.GetType().Name } };

					SerializeObject( newNode, newNodeObject, options, identifiers );

					newNodeArray.Add( newNodeObject );
				}
				else
				{
					newNodeArray.Add( jsonNode.DeepClone() );
				}
			}

			obj.Remove( JsonKeys.NodeArray );
			obj.Add( JsonKeys.NodeArray, newNodeArray );
		}
	}
#endregion Version 1 Upgrader

	private static bool CheckIsSubgraph( JsonObject obj )
	{
		return obj.TryGetPropertyValue( nameof( ShaderGraph.IsSubgraph ), out var subgraphValue ) ? subgraphValue.GetValue<bool>() : false;
	}
}

internal static class ShaderGraphJsonUpgrader
{
	private static (MethodDescription Method, JsonUpgraderAttribute Attribute)[] _methods;

	[EditorEvent.Hotload]
	[Event( "shadergraph.created" )]
	private static void UpdateUpgraders()
	{
		_methods = EditorTypeLibrary.GetMethodsWithAttribute<JsonUpgraderAttribute>().ToArray();
	}

	/// <summary>
	/// Runs through all upgraders that match its class where our version is lower than the specified version.
	/// </summary>
	/// <param name="version">The current version that's serialized in the json object</param>
	/// <param name="json"></param>
	/// <param name="targetType"></param>
	/// <param name="options"></param>
	public static void Upgrade( int version, JsonObject json, Type targetType, JsonSerializerOptions options )
	{
		// This is normal, upgraders have not been initialized using UpdateUpgraders
		// it's fine to ignore this.
		if ( _methods is null )
			return;

		foreach ( var e in _methods
			.Where( x => x.Attribute.Type == targetType )
			.OrderBy( x => x.Attribute.Version )
			.Where( x => x.Attribute.Version > version ) )
		{
			try
			{
				e.Method.Invoke( null, new object[] { json, options } );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"A shader graph version upgrader ({e.Attribute.Type}, version {e.Attribute.Version}) threw an exception while trying to upgrade, so we halted the upgrade." );
				// Let's stop trying to upgrade because something is broken.
				return;
			}
			finally
			{
				// Update our serialized version step by step.
				json["__version"] = e.Attribute.Version;
			}
		}
	}
}
