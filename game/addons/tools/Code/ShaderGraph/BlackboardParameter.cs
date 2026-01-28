using Editor.NodeEditor;
using Editor.ShaderGraph.Nodes;
using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

// TODO - Mabye it would be a good idea to add the blackboard shit to the base NodeEditor stuff.
// If people dont want to use it make it so they dont have to. - QuackCola

public record struct BlackboardConfig( string Name, Color Color );

public interface IBlackboardParameter
{
	Guid Identifier { get; }

	string Name { get; }
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

	public string Name { get; set; }

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

				// TODO : Subgraph input parameters
			}

			return true;
		} );
	}

	public static BaseNode InitilzeNode( BlackboardParameter parameter )
	{
		switch ( parameter )
		{
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
					Image = v.DefaultTexture,
				};
		}

		throw new NotImplementedException();
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

public abstract class BlackboardMaterialParameter<T,Y> : BlackboardParameter where Y : IParameterUI
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

public abstract class BlackboardTextureMaterialParameter : BlackboardParameter
{
	[ImageAssetPath]
	[InlineEditor( Label = false ), Group( "Value" )]
	public string DefaultTexture { get; set; }

	[Hide]
	private TextureInput _ui;
	[InlineEditor( Label = false ), Group( "UI" )]
	public TextureInput UI 
	{ 
		get => _ui with { Name = Name };
		set
		{
			_ui = value;
		}
	}

	public BlackboardTextureMaterialParameter() : base()
	{
	}

	public BlackboardTextureMaterialParameter( string name, TextureInput value ) : base( name )
	{
		UI = value;
	}

	public override object GetValue()
	{
		return UI;
	}

	public override void SetValue( object value )
	{
		if ( value.GetType() != typeof( TextureInput ) )
		{
			throw new InvalidCastException( $"Cannot cast {value.GetType()} to {typeof( TextureInput )}" );
		}

		UI = (TextureInput)value;
	}
}
