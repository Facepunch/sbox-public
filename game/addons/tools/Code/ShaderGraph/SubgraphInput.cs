using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

/// <summary>
/// Defines an input for a subgraph with detailed configuration options
/// </summary>
[Title( "Subgraph Input" ), Category( "Subgraph" ), Icon( "input" )]
public sealed class SubgraphInput : ShaderNode, IBlackboardNode, IErroringNode, BaseNode.INodeInitialize
{
	[Hide]
	private bool IsPreviewInputEnabled => InputType != InputType.Texture2D;

	[Hide]
	public override string Title => string.IsNullOrWhiteSpace( InputName ) ?
		$"Subgraph Input" :
		$"{InputName} ({InputType})";

	[JsonIgnore, Hide]
	public override Color PrimaryColor => Color.Lerp( Theme.Green, Theme.Blue, 0.5f );

	[Hide]
	public Guid BlackboardParameterIdentifier { get; set; }

	/// <summary>
	/// The name of the input parameter
	/// </summary>
	[Hide, JsonIgnore]
	public string InputName => GetParameter().Name;

	/// <summary>
	/// Description of what this input does
	/// </summary>
	[Hide, JsonIgnore]
	public string InputDescription => GetParameter().InputDescription;

	/// <summary>
	/// The type of the input parameter
	/// </summary>
	[Hide, JsonIgnore]
	public InputType InputType => GetParameter().InputType;

	//[Editor( "subgraphInputDefaultValue" )]
	[Hide, JsonIgnore]
	public object DefaultValue
	{
		get => GetParameter().GetValue();
		set
		{
			if ( Graph is ShaderGraph graph )
			{
				graph.UpdateParameterValue( BlackboardParameterIdentifier, value );

				Update();
				IsDirty = true;
			}
		}
	}

	[Hide, JsonIgnore]
	public TextureInput DefaultTextureInput { get; set; } = new() { Type = TextureType.Tex2D };

	/// <summary>
	/// Default value for float inputs
	/// </summary>
	[Hide, JsonIgnore]
	public float DefaultFloat { get; set; } = 0.0f;

	/// <summary>
	/// Default value for float2 inputs
	/// </summary>
	[Hide, JsonIgnore]
	public Vector2 DefaultFloat2 { get; set; } = Vector2.Zero;

	/// <summary>
	/// Default value for float3 inputs
	/// </summary>
	[Hide, JsonIgnore]
	public Vector3 DefaultFloat3 { get; set; } = Vector3.Zero;

	/// <summary>
	/// Default value for color inputs
	/// </summary>
	[Hide, JsonIgnore]
	public Color DefaultColor { get; set; } = Color.White;

	/// <summary>
	/// Whether this input is required (must have a connection in order to compile)
	/// </summary>
	[Hide, JsonIgnore]
	public bool IsRequired => GetParameter().IsRequired;

	/// <summary>
	/// The order of this input port on the subgraph node
	/// </summary>
	[Hide, JsonIgnore]
	public int PortOrder => GetParameter().PortOrder;

	/// <summary>
	/// Preview input for testing values in subgraphs
	/// </summary>
	[Input( typeof( object ) ), Title( "Preview" ), Hide]
	[ShowIf( nameof( IsPreviewInputEnabled ), true )]
	public NodeInput PreviewInput { get; set; }

	[Hide, JsonIgnore]
	int _lastHashCode = 0;

	public override void OnFrame()
	{
		var hashCode = new HashCode();
		hashCode.Add( InputType );
		var hc = hashCode.ToHashCode();
		if ( hc != _lastHashCode )
		{
			_lastHashCode = hc;

			CreatePreviewInput();
			Update();
		}
	}

	public SubgraphInput()
	{
	}

	private void CreatePreviewInput()
	{
		var property = this.GetSerialized().GetProperty( nameof( PreviewInput ) );

		if ( property.TryGetAttribute<ConditionalVisibilityAttribute>( out var conditionalVisibilityAttr ) )
		{
			if ( conditionalVisibilityAttr.TestCondition( this.GetSerialized() ) )
			{
				Inputs = new List<IPlugIn>();
				return;
			}
		}
		var propertyInfo = typeof( SubgraphInput ).GetProperty( property.Name );
		if ( propertyInfo is null )
		{
			Inputs = new List<IPlugIn>();
			return;
		}
		var info = new PlugInfo( propertyInfo );
		var displayInfo = info.DisplayInfo;
		displayInfo.Name = property.DisplayName;
		info.DisplayInfo = displayInfo;
		var plug = new BasePlugIn( this, info, info.Type );

		Inputs = new List<IPlugIn>() { plug };
	}

	private ISubgraphInputBlackboardParameter GetParameter()
	{
		if ( Graph is ShaderGraph graph )
		{
			var parameter = graph.FindParameter( BlackboardParameterIdentifier );

			if ( parameter is ISubgraphInputBlackboardParameter subgraphInputParameter )
			{
				return subgraphInputParameter;
			}

			return default;
		}

		return default;
	}

	public void OnNodeDeserialize( JsonElement element, JsonSerializerOptions options )
	{
	}

	public List<string> GetErrors()
	{
		var errors = new List<string>();

		return errors;
	}

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
		var outputValue = DefaultValue;

		// If we're in a subgraph context, just return the value directly
		if ( compiler.Graph.IsSubgraph )
		{
			if ( InputType == InputType.Texture2D )
			{
				return new NodeResult( NodeResultType.Texture2D, ProcessTexture2D( compiler, (TextureInput)outputValue ), true );
			}

			return compiler.ResultValue( outputValue );
		}
		else
		{
			if ( InputType == InputType.Texture2D )
			{
				return new NodeResult( NodeResultType.Texture2D, ProcessTexture2D( compiler, (TextureInput)outputValue ), true );
			}

			// For normal graphs, use ResultParameter to create a material parameter
			return compiler.ResultParameter( InputName, outputValue, default, default, false, IsRequired, default );
		}	
	};

	private string ProcessTexture2D( GraphCompiler compiler, TextureInput input )
	{
		input.Type = TextureType.Tex2D;

		return compiler.ResultTexture( input );
	}
}

/// <summary>
/// Available input types for subgraph inputs
/// </summary>
public enum InputType
{
	[Title( "Bool" ), Icon( "check_box" )]
	Bool,

	[Title( "Int" ), Icon( "looks_one" )]
	Int,

	[Title( "Float" ), Icon( "looks_one" )]
	Float,

	[Title( "Float2" ), Icon( "looks_two" )]
	Float2,

	[Title( "Float3" ), Icon( "looks_3" )]
	Float3,

	[Title( "Float4" ), Icon( "looks_4" )]
	Float4,

	[Title( "Color" ), Icon( "palette" )]
	Color,

	[Title( "Texture2D" ), Icon( "image" )]
	Texture2D
}

[CustomEditor( typeof( object ), NamedEditor = "subgraphInputDefaultValue" )]
internal class SubgraphInputNodeControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => false;

	SubgraphInput Node;
	ControlSheet Sheet;

	public SubgraphInputNodeControlWidget( SerializedProperty property ) : base( property )
	{
		Node = property.Parent.Targets.First() as SubgraphInput;

		Layout = Layout.Column();
		Layout.Spacing = 2;
		Sheet = new ControlSheet();
		Layout.Add( Sheet );

		Rebuild();
	}

	protected override void OnPaint()
	{

	}

	private void Rebuild()
	{
		Sheet.Clear( true );

		var type = Node.DefaultValue.GetType();
		var getter = () =>
		{
			if ( Node.DefaultValue is JsonElement el )
				return el.GetDouble();

			return Node.DefaultValue;
		};

		var displayName = $"Default {type.Name}";
		if ( type == typeof( bool ) )
		{
			Sheet.AddRow( TypeLibrary.CreateProperty<bool>(
				displayName, () =>
				{
					var val = getter();
					if ( val is JsonElement el ) return bool.Parse( el.GetRawText() );
					return (bool)val;
				}, x => SetDefaultValue( x )
			) );
		}
		else if ( type == typeof( int ) )
		{
			Sheet.AddRow( TypeLibrary.CreateProperty<int>(
				displayName, () =>
				{
					var val = getter();
					if ( val is JsonElement el ) return int.Parse( el.GetRawText() );
					return (int)val;
				}, x => SetDefaultValue( x )
			) );
		}
		else if ( type == typeof( float ) )
		{
			Sheet.AddRow( TypeLibrary.CreateProperty<float>(
				displayName, () =>
				{
					var val = getter();
					if ( val is JsonElement el ) return float.Parse( el.GetRawText() );
					return (float)val;
				}, x => SetDefaultValue( x )
			) );
		}
		else if ( type == typeof( Vector2 ) )
		{
			Sheet.AddRow( TypeLibrary.CreateProperty<Vector2>(
				displayName, () =>
				{
					var val = getter();
					if ( val is JsonElement el ) return Vector2.Parse( el.GetString() );
					return (Vector2)val;
				}, x => SetDefaultValue(  x )
			) );
		}
		else if ( type == typeof( Vector3 ) )
		{
			Sheet.AddRow( TypeLibrary.CreateProperty<Vector3>(
				displayName, () =>
				{
					var val = getter();
					if ( val is JsonElement el ) return Vector3.Parse( el.GetString() );
					return (Vector3)val;
				}, x => SetDefaultValue( x )
			) );
		}
		else if ( type == typeof( Vector4 ) )
		{
			Sheet.AddRow( TypeLibrary.CreateProperty<Vector4>(
				displayName, () =>
				{
					var val = getter();
					if ( val is JsonElement el ) return Vector4.Parse( el.GetString() );
					return (Vector4)val;
				}, x => SetDefaultValue( x )
			) );
		}
		else if ( type == typeof( Color ) )
		{
			Sheet.AddRow( TypeLibrary.CreateProperty<Color>(
				displayName, () =>
				{
					var val = getter();
					if ( val is JsonElement el )
					{
						return Color.Parse( el.GetString() ) ?? Color.White;
					}
					return (Color)val;
				}, x => SetDefaultValue( x )
			) );
		}
	}

	private void SetDefaultValue( object value )
	{
		Node.DefaultValue = value;
		Node.Update();
		Node.IsDirty = true;
	}
}
