using Editor.NodeEditor;
using Editor.ShaderGraph.Nodes;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public record struct BlackboardConfig( string Name, Color Color );

public interface IBlackboardParameter
{
	Guid Identifier { get; }

	string Name { get; }

	object GetValue();

	void SetValue( object value );
}

public interface ISubgraphInputBlackboardParameter : IBlackboardParameter
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

	IBlackboardParameter CreateParameter( ShaderGraph graph, string name = "" );
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

	public BlackboardParameter( string name )
	{
		NewIdentifier();
		Name = name;
	}

	public Guid NewIdentifier()
	{
		Identifier = Guid.NewGuid();
		return Identifier;
	}

	public abstract object GetValue();

	public abstract void SetValue( object value );

	public static IEnumerable<IBlackboardParameterType> GetRelevantParameters( Dictionary<string, IBlackboardParameterType> availableParameters, bool isSubgraph )
	{
		return availableParameters.Values.Where( x =>
		{
			if ( x is ClassBlackboardParameterType classParameterType )
			{
				var targetType = classParameterType.Type.TargetType;

				// Only show material parameters when not in a subgraph
				if ( isSubgraph && targetType == typeof( BoolBlackboardParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( IntBlackboardParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( FloatBlackboardParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Float2BlackboardParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Float3BlackboardParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Float4BlackboardParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( ColorBlackboardParameter ) ) return false;
				if ( isSubgraph && targetType == typeof( Texture2DBlackboardParameter ) ) return false;

				// Only show subgraph input parameters when in a subgraph
				if ( !isSubgraph && targetType == typeof( BoolSubgraphInputBlackboardParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( IntSubgraphInputBlackboardParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( FloatSubgraphInputBlackboardParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Float2SubgraphInputBlackboardParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Float3SubgraphInputBlackboardParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Float4SubgraphInputBlackboardParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( ColorSubgraphInputBlackboardParameter ) ) return false;
				if ( !isSubgraph && targetType == typeof( Texture2DSubgraphInputBlackboardParameter ) ) return false;
				
			}

			return true;
		} );
	}

	public static BaseNode InitilzeNode( BlackboardParameter parameter )
	{
		switch ( parameter )
		{
			// Not In Subgraph
			case BoolBlackboardParameter:
				return new BoolParameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case IntBlackboardParameter:
				return new IntParameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case FloatBlackboardParameter:
				return new FloatParameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Float2BlackboardParameter:
				return new Float2Parameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Float3BlackboardParameter:
				return new Float3Parameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Float4BlackboardParameter:
				return new Float4Parameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case ColorBlackboardParameter:
				return new ColorParameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Texture2DBlackboardParameter v:
				return new Texture2DParameter()
				{
					BlackboardParameterIdentifier = parameter.Identifier,
				};

			// In Subgraph
			case BoolSubgraphInputBlackboardParameter p:
				return new SubgraphInput()
				{
					DefaultValue = false,
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case IntSubgraphInputBlackboardParameter p :
				return new SubgraphInput()
				{
					DefaultValue = 0,
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case FloatSubgraphInputBlackboardParameter p:
				return new SubgraphInput()
				{
					DefaultValue = 0.0f,
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Float2SubgraphInputBlackboardParameter p:
				return new SubgraphInput()
				{
					DefaultValue =  Vector2.Zero,
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Float3SubgraphInputBlackboardParameter p:
				return new SubgraphInput()
				{
					DefaultValue = Vector3.Zero,
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Float4SubgraphInputBlackboardParameter p:
				return new SubgraphInput()
				{
					DefaultValue = Vector4.Zero,
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case ColorSubgraphInputBlackboardParameter p:
				return new SubgraphInput()
				{
					DefaultValue = Color.White,
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			case Texture2DSubgraphInputBlackboardParameter p:
				return new SubgraphInput()
				{
					DefaultValue = "",
					BlackboardParameterIdentifier = parameter.Identifier,
				};
			default:
				throw new NotImplementedException();
		}
	}

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
}

public abstract class BlackboardMaterialParameter<T, Y> : BlackboardParameter where Y : IParameterUI
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

	public BlackboardMaterialParameter( string name, T value, bool isAttribute ) : base( name )
	{
		Value = value;
		IsAttribute = isAttribute;
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

public abstract class BlackboardSubgraphinputParameter<T> : BlackboardParameter, ISubgraphInputBlackboardParameter
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

	public BlackboardSubgraphinputParameter() : base()
	{

	}

	public BlackboardSubgraphinputParameter( string name, T value ) : base( name )
	{
		Value = value;
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

	public BlackboardTextureMaterialParameter( string name, TextureInput value ) : base( name )
	{
		Value = value;
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
