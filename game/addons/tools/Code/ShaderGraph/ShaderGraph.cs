using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public enum BlendMode
{
	[Icon( "circle" )]
	Opaque,
	[Icon( "radio_button_unchecked" )]
	Masked,
	[Icon( "blur_on" )]
	Translucent,
}

public enum ShadingModel
{
	[Icon( "tungsten" )]
	Lit,
	[Icon( "brightness_3" )]
	Unlit,
}

public enum ShaderDomain
{
	[Icon( "view_in_ar" )]
	Surface,
	[Icon( "desktop_windows" )]
	PostProcess,
}

public class PreviewSettings
{
	public bool RenderBackfaces { get; set; } = false;
	public bool EnableShadows { get; set; } = true;
	public bool ShowGround { get; set; } = false;
	public bool ShowSkybox { get; set; } = true;
	public Color BackgroundColor { get; set; } = Color.Black;
	public Color Tint { get; set; } = Color.White;
}

public sealed partial class ShaderGraph : IGraph
{
	[Hide, JsonIgnore]
	public IEnumerable<BaseNode> Nodes => _nodes.Values;

	[Hide, JsonIgnore]
	private readonly Dictionary<string, BaseNode> _nodes = new();

	[Hide, JsonIgnore]
	IEnumerable<INode> IGraph.Nodes => Nodes;

	[Hide, JsonIgnore]
	public IEnumerable<BlackboardParameter> Parameters => _parameters.Values;

	[Hide, JsonIgnore]
	public readonly Dictionary<Guid, BlackboardParameter> _parameters = new();

	[Hide]
	public bool IsSubgraph { get; set; }

	[Hide]
	public string Path { get; set; }

	[Hide]
	public string Model { get; set; }

	/// <summary>
	/// The name of the Node when used in ShaderGraph
	/// </summary>
	[ShowIf( nameof( IsSubgraph ), true )]
	public string Title { get; set; }

	public string Description { get; set; }

	/// <summary>
	/// The category of the Node when browsing the Node Library (optional)
	/// </summary>
	[ShowIf( nameof( AddToNodeLibrary ), true )]
	public string Category { get; set; }

	[IconName, ShowIf( nameof( IsSubgraph ), true )]
	public string Icon { get; set; }

	/// <summary>
	/// Whether or not this Node should appear when browsing the Node Library.
	/// Otherwise can only be referenced by dragging the Subgraph asset into the graph.
	/// </summary>
	[ShowIf( nameof( IsSubgraph ), true )]
	public bool AddToNodeLibrary { get; set; }

	public BlendMode BlendMode { get; set; }

	[ShowIf( nameof( ShowShadingModel ), true )]
	public ShadingModel ShadingModel { get; set; }
	[Hide] private bool ShowShadingModel => Domain != ShaderDomain.PostProcess;

	public ShaderDomain Domain { get; set; }

	[Hide]
	public PreviewSettings PreviewSettings { get; set; } = new();

	[Hide]
	public int Version => 2;

	public ShaderGraph()
	{
	}

	public void AddNode( BaseNode node )
	{
		node.Graph = this;
		_nodes.Add( node.Identifier, node );
	}

	public void RemoveNode( BaseNode node )
	{
		if ( node.Graph != this )
			return;

		_nodes.Remove( node.Identifier );
	}

	public BaseNode FindNode( string name )
	{
		_nodes.TryGetValue( name, out var node );
		return node;
	}

	public BlackboardParameter FindParameter( Guid identifier )
	{
		if ( _parameters.TryGetValue( identifier, out var parameter ) )
		{
			return parameter;
		}

		throw new Exception( $"There is no parameter with the identifier : {identifier}" );
	}

	public T FindParameter<T>( Guid identifier ) where T : BlackboardParameter
	{
		if ( _parameters.TryGetValue( identifier, out var parameter ) )
		{
			return (T)parameter;
		}

		throw new Exception( $"There is no parameter with the identifier : {identifier}" );
	}

	public BlackboardParameter FindParameter( string name )
	{
		var parameter = _parameters.Values.Where( x => x.Name == name ).FirstOrDefault();

		if ( parameter != null )
			return parameter;

		return null;
	}

	public T FindParameter<T>( string name ) where T : BlackboardParameter
	{
		var parameter = _parameters.Values.OfType<T>().Where( x => x.Name == name ).FirstOrDefault();

		if ( parameter != null )
			return parameter;
		
		return null;
	}

	public bool ContainsParameterWithName( string name )
	{
		foreach ( var parameter in _parameters )
		{
			if ( name == parameter.Value.Name )
				return true;
		}

		return false;
	}

	public void AddParameter( BlackboardParameter parameter )
	{
		parameter.Graph = this;
		_parameters.Add( parameter.Identifier, parameter );
	}

	public void UpdateParameter( BlackboardParameter parameter )
	{
		if ( parameter.Graph != this )
			return;

		_parameters[parameter.Identifier] = parameter;
	}

	public void UpdateParameterValue( Guid identifier, object value )
	{
		if ( !_parameters.ContainsKey( identifier ) )
			throw new Exception( $"There is no parameter with the identifier : {identifier}" );

		_parameters[identifier].SetValue( value );
	}

	public void RemoveParameter( BlackboardParameter parameter )
	{
		if ( parameter.Graph != this )
			return;

		RemoveParameter( parameter.Identifier );
	}

	public void RemoveParameter( Guid identifier )
	{
		_parameters.Remove( identifier );
	}

	public void ClearNodes()
	{
		_nodes.Clear();
	}

	public void ClearParameters()
	{
		_parameters.Clear();
	}

	string IGraph.SerializeNodes( IEnumerable<INode> nodes )
	{
		return SerializeNodes( nodes.Cast<BaseNode>() );
	}

	IEnumerable<INode> IGraph.DeserializeNodes( string serialized )
	{
		return DeserializeNodes( serialized );
	}

	void IGraph.AddNode( INode node )
	{
		AddNode( (BaseNode)node );
	}

	void IGraph.RemoveNode( INode node )
	{
		RemoveNode( (BaseNode)node );
	}
}
