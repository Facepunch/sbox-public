using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

/// <summary>
/// Defines an input for a subgraph with detailed configuration options
/// </summary>
[Title( "Subgraph Input" ), Category( "Subgraph" ), Icon( "input" )]
public sealed class SubgraphInput : ShaderNode, IErroringNode
{
	[Hide]
	public override string Title => string.IsNullOrWhiteSpace( InputName ) ?
		$"Subgraph Input" :
		$"{InputName} ({InputType})";

	/// <summary>
	/// The name of the input parameter
	/// </summary>
	[KeyProperty]
	public string InputName { get; set; } = "";

	/// <summary>
	/// Description of what this input does
	/// </summary>
	[TextArea]
	public string InputDescription { get; set; } = "";

	/// <summary>
	/// The type of the input parameter
	/// </summary>
	public InputType InputType { get; set; } = InputType.Float;

	/// <summary>
	/// Default value for float inputs
	/// </summary>
	[ShowIf( nameof( InputType ), InputType.Float )]
	public float DefaultFloat { get; set; } = 0.0f;

	/// <summary>
	/// Default value for float2 inputs
	/// </summary>
	[ShowIf( nameof( InputType ), InputType.Float2 )]
	public Vector2 DefaultFloat2 { get; set; } = Vector2.Zero;

	/// <summary>
	/// Default value for float3 inputs
	/// </summary>
	[ShowIf( nameof( InputType ), InputType.Float3 )]
	public Vector3 DefaultFloat3 { get; set; } = Vector3.Zero;

	/// <summary>
	/// Default value for color inputs
	/// </summary>
	[ShowIf( nameof( InputType ), InputType.Color )]
	public Color DefaultColor { get; set; } = Color.White;

	/// <summary>
	/// Whether this input is required (must have a connection in order to compile)
	/// </summary>
	public bool IsRequired { get; set; } = false;

	/// <summary>
	/// The order of this input port on the subgraph node
	/// </summary>
	[Title( "Order" )]
	public int PortOrder { get; set; } = 0;

	/// <summary>
	/// Preview input for testing values in subgraphs
	/// </summary>
	[Input( typeof( object ) ), Title( "Preview" ), Hide]
	public NodeInput PreviewInput { get; set; }

	/// <summary>
	/// Output for the input value
	/// </summary>
	[Output( typeof( float ) ), Title( "Value" ), Hide]
	public NodeResult.Func Result => ( GraphCompiler compiler ) =>
	{
		// In subgraphs, check if preview input is connected
		if ( compiler.Graph.IsSubgraph && PreviewInput.IsValid )
		{
			return compiler.Result( PreviewInput );
		}

		// Use the appropriate default value based on input type
		var outputValue = GetOutputValue();

		// If we're in a subgraph context, just return the value directly
		if ( compiler.Graph.IsSubgraph )
		{
			return compiler.ResultValue( outputValue );
		}

		// For normal graphs, use ResultParameter to create a material parameter
		return compiler.ResultParameter( InputName, outputValue, default, default, false, IsRequired, new ParameterUI() );
	};

	[JsonIgnore, Hide]
	public override Color PrimaryColor => Color.Lerp( Theme.Green, Theme.Blue, 0.5f );

	public SubgraphInput()
	{
	}

	private object GetOutputValue()
	{
		return InputType switch
		{
			InputType.Float => DefaultFloat,
			InputType.Float2 => DefaultFloat2,
			InputType.Float3 => DefaultFloat3,
			InputType.Color => DefaultColor,
			_ => DefaultFloat
		};
	}

	public object GetValue()
	{
		return GetOutputValue();
	}

	public List<string> GetErrors()
	{
		var errors = new List<string>();

		if ( string.IsNullOrWhiteSpace( InputName ) )
		{
			errors.Add( "Input name cannot be empty" );
		}

		// Check for duplicate names in the same subgraph
		if ( Graph is ShaderGraph shaderGraph && shaderGraph.IsSubgraph )
		{
			foreach ( var node in Graph.Nodes )
			{
				if ( node == this ) continue;

				if ( node is SubgraphInput otherInput && otherInput.InputName == InputName )
				{
					errors.Add( $"Duplicate input name \"{InputName}\"" );
					break;
				}
			}
		}

		return errors;
	}
}

/// <summary>
/// Available input types for subgraph inputs
/// </summary>
public enum InputType
{
	[Title( "Float" ), Icon( "looks_one" )]
	Float,

	[Title( "Float2" ), Icon( "looks_two" )]
	Float2,

	[Title( "Float3" ), Icon( "looks_3" )]
	Float3,

	[Title( "Color" ), Icon( "palette" )]
	Color
}
