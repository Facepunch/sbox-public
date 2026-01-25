using Editor.NodeEditor;
using Editor.ShaderGraph.Nodes;
using System.ComponentModel;
using System.Text.Json.Serialization;
using static Sandbox.Material;

namespace Editor.ShaderGraph;

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

	public virtual object GetValue()
	{
		throw new NotImplementedException();
	}

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
			
				// TODO : Subgraph input parameters
			}

			return true;
		} );
	}

	public static BaseNode InitilzeNode( BlackboardParameter parameter )
	{
		if ( parameter is BoolBlackboardParameter bbp )
		{
			return new BoolParameter()
			{
				BlackboardParameterIdentifier = bbp.Identifier,
				Name = bbp.Name,
				Value = bbp.Value,
				IsAttribute = bbp.IsAttribute,
				UI = bbp.UI
			};
		}
		else if ( parameter is IntBlackboardParameter ibp )
		{
			return new IntParameter()
			{
				BlackboardParameterIdentifier = ibp.Identifier,
				Name = ibp.Name,
				Value = ibp.Value,
				IsAttribute = ibp.IsAttribute,
				UI = ibp.UI
			};
		}
		else if ( parameter is FloatBlackboardParameter fbp )
		{
			return new FloatParameter()
			{
				BlackboardParameterIdentifier = fbp.Identifier,
				Name = fbp.Name,
				Value = fbp.Value,
				IsAttribute = fbp.IsAttribute,
				UI = fbp.UI
			};
		}
		else if ( parameter is Float2BlackboardParameter f2bp )
		{
			return new Float2Parameter()
			{
				BlackboardParameterIdentifier = f2bp.Identifier,
				Name = f2bp.Name,
				Value = f2bp.Value,
				IsAttribute = f2bp.IsAttribute,
				UI = f2bp.UI
			};
		}
		else if ( parameter is Float3BlackboardParameter f3bp )
		{
			return new Float3Parameter()
			{
				BlackboardParameterIdentifier = f3bp.Identifier,
				Name = f3bp.Name,
				Value = f3bp.Value,
				IsAttribute = f3bp.IsAttribute,
				UI = f3bp.UI
			};
		}
		else if ( parameter is Float4BlackboardParameter f4bp )
		{
			return new Float4Parameter()
			{
				BlackboardParameterIdentifier = f4bp.Identifier,
				Name = f4bp.Name,
				Value = f4bp.Value,
				IsAttribute = f4bp.IsAttribute,
				UI = f4bp.UI
			};
		}
		else if ( parameter is ColorBlackboardParameter cbp )
		{
			return new ColorParameter()
			{
				BlackboardParameterIdentifier = cbp.Identifier,
				Name = cbp.Name,
				Value = cbp.Value,
				IsAttribute = cbp.IsAttribute,
				UI = cbp.UI
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
}
