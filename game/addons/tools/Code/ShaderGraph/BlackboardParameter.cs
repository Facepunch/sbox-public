using Editor.NodeEditor;
using Editor.ShaderGraph.Nodes;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public record struct BlackboardConfig( string Name, Color Color );

public interface IBlackboardParameter
{
	Guid Identifier { get; }

	string Name { get; set; }

	object GetValue();

	void SetValue( object value );
}

public interface IBlackboardMaterialParameter : IBlackboardParameter
{
	bool IsAttribute { get; set; }
	IParameterUI GetParameterUI();
}

public interface IBlackboardSubgraphInputParameter : IBlackboardParameter
{
	/// <summary>
	/// Description of what this input does
	/// </summary>
	string InputDescription { get; set; }

	/// <summary>
	/// Whether this input is required (must have a connection in order to compile)
	/// </summary>
	bool IsRequired { get; set; }

	/// <summary>
	/// The order of this input port.
	/// </summary>
	int PortOrder { get; set; }

	abstract InputType InputType { get; }
}

public interface IBlackboardParameterType
{
	public TypeDescription Type { get; }

	IBlackboardParameter CreateParameter( IGraph graph, string name = "" );
}

public abstract class BlackboardParameter : IBlackboardParameter
{
	[Hide, Browsable( false )]
	public Guid Identifier { get; set; }

	[JsonIgnore, Hide]
	public IGraph _graph;
	[Browsable( false )]
	[JsonIgnore, Hide]
	public IGraph Graph
	{
		get => _graph;
		set
		{
			_graph = value;
		}
	}

	public virtual string Name { get; set; }

	public BlackboardParameter()
	{
		NewIdentifier();
		Name = "";
	}

	public Guid NewIdentifier()
	{
		Identifier = Guid.NewGuid();
		return Identifier;
	}

	public abstract object GetValue();

	public abstract void SetValue( object value );

	/// <summary>
	/// Check parameter for any issues.
	/// </summary>
	/// <param name="issues">Any issues that are found.</param>
	/// <returns>False when check has failed otherwise returns true when check has passed.</returns>
	public bool CheckParameter( out List<string> issues )
	{
		issues = new List<string>();

		if ( string.IsNullOrWhiteSpace( Name ) )
		{
			issues.Add( $"Parameter with identifier \"{Identifier}\" must have name!" );

			return false;
		}

		return true;
	}

	public static IEnumerable<IBlackboardParameterType> GetRelevantParameters( Dictionary<string, IBlackboardParameterType> availableParameters, bool isSubgraph )
	{
		return availableParameters.Values.Where( x =>
		{
			if ( x is ClassBlackboardParameterType classParameterType )
			{
				var targetType = classParameterType.Type.TargetType;

				// Only show material parameters when not in a subgraph
				if ( isSubgraph && targetType == typeof( BoolParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( IntParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( FloatParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Float2Parameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Float3Parameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Float4Parameter ) ) return false;
				if ( isSubgraph && targetType == typeof( ColorParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Texture2DParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( TextureCubeParameter ) ) return false;

				// Only show subgraph input parameters when in a subgraph
				if ( !isSubgraph && targetType == typeof( BoolSubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( IntSubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( FloatSubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Float2SubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Float3SubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Float4SubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( ColorSubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Texture2DSubgraphInputParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( TextureCubeSubgraphInputParameter ) ) return false;

			}

			return true;
		} );
	}

	public static BaseNode InitializeParameterNode( IBlackboardParameter parameter )
	{
		return parameter switch
		{
			// Not In Subgraph
			BoolParameter => new BoolParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			IntParameter => new IntParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			FloatParameter => new FloatParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			Float2Parameter => new Float2ParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			Float3Parameter => new Float3ParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			Float4Parameter => new Float4ParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			ColorParameter => new ColorParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			Texture2DParameter => new Texture2DParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},
			TextureCubeParameter => new TextureCubeParameterNode()
			{
				ParameterIdentifier = parameter.Identifier,
			},

			// In Subgraph
			BoolSubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = false,
				ParameterIdentifier = parameter.Identifier,
			},
			IntSubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = 0,
				ParameterIdentifier = parameter.Identifier,
			},
			FloatSubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = 0.0f,
				ParameterIdentifier = parameter.Identifier,
			},
			Float2SubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = Vector2.Zero,
				ParameterIdentifier = parameter.Identifier,
			},
			Float3SubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = Vector3.Zero,
				ParameterIdentifier = parameter.Identifier,
			},
			Float4SubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = Vector4.Zero,
				ParameterIdentifier = parameter.Identifier,
			},
			ColorSubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = Color.White,
				ParameterIdentifier = parameter.Identifier,
			},
			Texture2DSubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = new TextureInput() { Type = TextureType.Tex2D },
				ParameterIdentifier = parameter.Identifier,
			},
			TextureCubeSubgraphInputParameter => new SubgraphInput()
			{
				DefaultValue = new TextureInput() { Type = TextureType.TexCube },
				ParameterIdentifier = parameter.Identifier,
			},

			_ => throw new NotImplementedException( $"Unknown parameter : {parameter.GetType()}" ),
		};
	}
}

public abstract class BlackboardMaterialParameter<T, Y> : BlackboardParameter, IBlackboardMaterialParameter where Y : IParameterUI
{
	[InlineEditor( Label = false ), Group( "Value" )]
	public T Value { get; set; }

	[InlineEditor( Label = false ), Group( "UI" )]
	public Y UI { get; set; }

	public bool IsAttribute { get; set; }

	public BlackboardMaterialParameter() : base()
	{
		IsAttribute = false;
	}

	public override object GetValue()
	{
		return Value;
	}

	public IParameterUI GetParameterUI()
	{
		return UI;
	}

	public override void SetValue( object value )
	{
		if ( value.GetType() != typeof( T ) )
		{
			throw new InvalidCastException( $"Cannot cast {value.GetType()} to {typeof( T )}" );
		}

		Value = (T)value;
	}
}

public abstract class BlackboardSubgraphInputParameter<T> : BlackboardParameter, IBlackboardSubgraphInputParameter
{
	[Title( "Input Name" )]
	public override string Name { get; set; }

	/// <summary>
	/// Description of what this input does
	/// </summary>
	[TextArea]
	public string InputDescription { get; set; } = "";

	[InlineEditor( Label = false ), Group( "Value" )]
	public virtual T Value { get; set; }

	/// <summary>
	/// Whether this input is required (must have a connection in order to compile)
	/// </summary>
	public bool IsRequired { get; set; } = false;

	/// <summary>
	/// The order of this input port.
	/// </summary>
	[Title( "Order" )]
	public int PortOrder { get; set; } = 0;

	public abstract InputType InputType { get; }

	public BlackboardSubgraphInputParameter() : base()
	{
	}

	public override object GetValue()
	{
		return Value;
	}

	public override void SetValue( object value )
	{
		if ( value.GetType() != typeof( T ) )
		{
			throw new InvalidCastException( $"Cannot cast {value.GetType()} to {typeof( T )}" );
		}

		Value = (T)value;
	}
}

public abstract class BlackboardTextureMaterialParameter : BlackboardParameter
{
	[Hide]
	private TextureInput _value;
	[InlineEditor( Label = false ), Group( "Value" )]
	public TextureInput Value
	{
		get => _value with { Name = Name };
		set
		{
			_value = value;
		}
	}

	public BlackboardTextureMaterialParameter() : base()
	{
	}

	public override object GetValue()
	{
		return Value;
	}

	public override void SetValue( object value )
	{
		if ( value.GetType() != typeof( TextureInput ) )
		{
			throw new InvalidCastException( $"Cannot cast {value.GetType()} to {typeof( TextureInput )}" );
		}

		Value = (TextureInput)value;
	}
}
