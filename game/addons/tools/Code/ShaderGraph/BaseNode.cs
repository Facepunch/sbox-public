using Editor.NodeEditor;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

public abstract class BaseNode : INode
{
	public event Action Changed;

	[Hide, Browsable( false )]
	public string Identifier { get; set; }

	[JsonIgnore, Hide, Browsable( false )]
	public virtual DisplayInfo DisplayInfo { get; }

	[JsonIgnore, Hide, Browsable( false )]
	public bool CanClone => true;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual bool CanRemove => true;

	[Hide, Browsable( false )]
	public Vector2 Position { get; set; }

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
			FilterInputsAndOutputs();
		}
	}

	[JsonIgnore, Hide, Browsable( false )]
	public Vector2 ExpandSize { get; set; }

	[JsonIgnore, Hide, Browsable( false )]
	public bool AutoSize => false;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual IEnumerable<IPlugIn> Inputs { get; protected set; }

	[JsonIgnore, Hide, Browsable( false )]
	public virtual IEnumerable<IPlugOut> Outputs { get; protected set; }

	[JsonIgnore, Hide, Browsable( false )]
	public string ErrorMessage { get; set; } = null;

	[JsonIgnore, Hide, Browsable( false )]
	public bool IsReachable => true;

	[Hide, Browsable( false )]
	public Dictionary<string, float> HandleOffsets { get; set; } = new();

	public BaseNode()
	{
		DisplayInfo = DisplayInfo.For( this );
		NewIdentifier();

		(Inputs, Outputs) = GetPlugs( this );
	}

	public void Update()
	{
		Changed?.Invoke();
	}

	public virtual void OnFrame()
	{

	}

	public void ClearError()
	{
		ErrorMessage = null;
	}

	public string NewIdentifier()
	{
		Identifier = Guid.NewGuid().ToString();
		return Identifier;
	}

	public virtual NodeUI CreateUI( GraphView view )
	{
		return new NodeUI( view, this );
	}

	public Color GetPrimaryColor( GraphView view )
	{
		return PrimaryColor;
	}

	public virtual Menu CreateContextMenu( NodeUI node )
	{
		return null;
	}

	[JsonIgnore, Hide, Browsable( false )]
	public virtual Pixmap Thumbnail { get; }

	[JsonIgnore, Hide, Browsable( false )]
	public virtual Color PrimaryColor { get; } = Color.Lerp( new Color( 0.7f, 0.7f, 0.7f ), Theme.Blue, 0.1f );

	public virtual void OnPaint( Rect rect )
	{

	}

	public virtual void OnDoubleClick( MouseEvent e )
	{

	}

	[JsonIgnore, Hide, Browsable( false )]
	public bool HasTitleBar => true;

	[System.AttributeUsage( AttributeTargets.Property )]
	public class InputAttribute : Attribute
	{
		public System.Type Type;

		public InputAttribute( Type type = null )
		{
			Type = type;
		}
	}

	[System.AttributeUsage( AttributeTargets.Property )]
	public class InputDefaultAttribute : Attribute
	{
		public string Input;

		public InputDefaultAttribute( string input )
		{
			Input = input;
		}
	}

	[System.AttributeUsage( AttributeTargets.Property )]
	public class OutputAttribute : Attribute
	{
		public System.Type Type;

		public OutputAttribute( Type type = null )
		{
			Type = type;
		}
	}

	[System.AttributeUsage( AttributeTargets.Property )]
	public class ValueEditorAttribute : Attribute
	{
		public string ValueName;

		public ValueEditorAttribute( string valueName )
		{
			ValueName = valueName;
		}
	}

	[System.AttributeUsage( AttributeTargets.Property )]
	public class RangeAttribute : Attribute
	{
		public string Min;
		public string Max;
		public string Step;

		public RangeAttribute( string min, string max, string step )
		{
			Min = min;
			Max = max;
			Step = step;
		}
	}

	public interface INodeInitialize
	{
		public void OnNodeCreated( JsonSerializerOptions options );
	}

	/// <summary>
	/// Connects a <see cref="NodeResult.Func"/> property from another node to the <see cref="NodeInput"/> property on this <see cref="BaseNode"/> instance.
	/// </summary>
	/// <param name="targetInputName">The internal input name on this <see cref="BaseNode"/> instance that will be connected to.</param>
	/// <param name="sourceNodeOutputName">The internal output name of the source <see cref="BaseNode"/> that the new connection is coming from.</param>
	/// <param name="sourceNodeIdentifier">The Identifier of the souce <see cref="BaseNode"/> that we are connecting from.</param>
	internal void ConnectNode( string targetInputName, string sourceNodeOutputName, string sourceNodeIdentifier )
	{
		if ( Graph == null )
		{
			throw new Exception();
		}

		var graph = Graph as ShaderGraph;
		var targetOutputNode = graph.Nodes.Where( x => x.Identifier == sourceNodeIdentifier ).FirstOrDefault();

		if ( targetOutputNode != null )
		{
			var plugIn = Inputs.Where( x => x.Identifier == targetInputName ).FirstOrDefault();
			var targetOutputPlug = targetOutputNode.Outputs.FirstOrDefault( x => x.Identifier == sourceNodeOutputName );

			if ( plugIn == null )
			{
				throw new Exception( $"Cannot find input with name '{targetInputName}' on node '{this}'" );
			}

			if ( targetOutputPlug == null )
			{
				throw new Exception( $"Cannot find output with name '{sourceNodeOutputName}' on node '{targetOutputNode}'" );
			}


			plugIn.ConnectedOutput = targetOutputPlug;
		}
		else
		{
			throw new Exception( $"Cannot find node with Identifier '{sourceNodeIdentifier}'" );
		}
	}

	private static (IEnumerable<IPlugIn> Inputs, IEnumerable<IPlugOut> Outputs) GetPlugs( BaseNode node )
	{
		var type = node.GetType();

		var inputs = new List<BasePlugIn>();
		var outputs = new List<BasePlugOut>();

		foreach ( var propertyInfo in type.GetProperties() )
		{
			if ( propertyInfo.GetCustomAttribute<InputAttribute>() is { } inputAttrib )
			{
				inputs.Add( new BasePlugIn( node, new( propertyInfo ), inputAttrib.Type ?? typeof( object ) ) );
			}

			if ( propertyInfo.GetCustomAttribute<OutputAttribute>() is { } outputAttrib )
			{
				outputs.Add( new BasePlugOut( node, new( propertyInfo ), outputAttrib.Type ?? typeof( object ) ) );
			}
		}

		return (inputs, outputs);
	}

	private void FilterInputsAndOutputs()
	{
		if ( _graph is not null )
		{
			if ( Graph is ShaderGraph sg && !sg.IsSubgraph && this is IParameterNode )
			{
				Inputs = new List<IPlugIn>();
			}
		}
	}
}

public record BasePlug( BaseNode Node, PlugInfo Info, Type Type ) : IPlug
{
	INode IPlug.Node => Node;

	public string Identifier => Info.Name;
	public DisplayInfo DisplayInfo => Info.DisplayInfo;

	public ValueEditor CreateEditor( NodeUI node, Plug plug )
	{
		var editor = Info.CreateEditor( node, plug, Type );
		if ( editor is not null ) return editor;

		// Default
		{
			var defaultEditor = new DefaultEditor( plug );
		}

		return null;
	}

	public Menu CreateContextMenu( NodeUI node, Plug plug )
	{
		return null;
	}

	public void OnDoubleClick( NodeUI node, Plug plug, MouseEvent e )
	{

	}

	public bool ShowLabel => true;
	public bool AllowStretch => true;
	public bool ShowConnection => IsReachable;
	public bool InTitleBar => false;
	public bool IsReachable
	{
		get
		{
			var conditional = Info.Property?.GetCustomAttribute<ConditionalVisibilityAttribute>();
			if ( conditional is not null )
			{
				if ( conditional.TestCondition( Node.GetSerialized() ) ) return false;
			}

			return true;
		}
	}

	public string ErrorMessage => null;

	public override string ToString()
	{
		return $"{Node.Identifier}.{Identifier}";
	}
}

public record BasePlugIn( BaseNode Node, PlugInfo Info, Type Type ) : BasePlug( Node, Info, Type ), IPlugIn
{
	IPlugOut IPlugIn.ConnectedOutput
	{
		get
		{
			if ( Info.Property is null )
			{
				return Info.ConnectedPlug;
			}

			if ( Info.Type != typeof( NodeInput ) )
			{
				return null;
			}

			var value = Info.GetInput( Node );

			if ( !value.IsValid )
			{
				return null;
			}

			var node = ((ShaderGraph)Node.Graph).FindNode( value.Identifier );
			var output = node?.Outputs
				.FirstOrDefault( x => x.Identifier == value.Output );

			return output;
		}
		set
		{
			var property = Info.Property;
			if ( property is null )
			{
				Info.ConnectedPlug = value;
				return;
			}

			if ( property.PropertyType != typeof( NodeInput ) )
			{
				return;
			}

			if ( value is null )
			{
				property.SetValue( Node, default( NodeInput ) );
				return;
			}

			if ( value is not BasePlug fromPlug )
			{
				return;
			}

			property.SetValue( Node, new NodeInput
			{
				Identifier = fromPlug.Node.Identifier,
				Output = fromPlug.Identifier
			} );
		}
	}

	public float? GetHandleOffset( string name )
	{
		if ( Node.HandleOffsets.TryGetValue( name, out var value ) )
		{
			return value;
		}
		return null;
	}

	public void SetHandleOffset( string name, float? value )
	{
		if ( value is null ) Node.HandleOffsets.Remove( name );
		else Node.HandleOffsets[name] = value.Value;
	}
}

public record BasePlugOut( BaseNode Node, PlugInfo Info, Type Type ) : BasePlug( Node, Info, Type ), IPlugOut;

public class PlugInfo
{
	public Guid Id { get; set; }
	public string Name { get; set; }
	public Type Type { get; set; }
	public DisplayInfo DisplayInfo { get; set; }
	public PropertyInfo Property { get; set; } = null;
	public IPlugOut ConnectedPlug { get; set; } = null;

	public PlugInfo()
	{
		DisplayInfo = new();
	}
	public PlugInfo( PropertyInfo property )
	{
		Name = property.Name;
		Type = property.PropertyType;
		var info = DisplayInfo.ForMember( Type );
		info.Name = property.Name;
		var titleAttr = property.GetCustomAttribute<TitleAttribute>();
		if ( titleAttr is not null )
		{
			info.Name = titleAttr.Value;
		}
		DisplayInfo = info;
		Property = property;
	}

	public NodeInput GetInput( BaseNode node )
	{
		if ( Property is not null )
		{
			return (NodeInput)Property.GetValue( node )!;
		}

		return default;
	}

	public ValueEditor CreateEditor( NodeUI node, Plug plug, Type type )
	{
		var editor = Property?.GetCustomAttribute<BaseNode.ValueEditorAttribute>();

		if ( editor is not null )
		{
			if ( type == typeof( float ) )
			{
				var slider = new FloatEditor( plug ) { Title = DisplayInfo.Name, Node = node };
				slider.Bind( "Value" ).From( node.Node, editor.ValueName );

				var range = Property.GetCustomAttribute<BaseNode.RangeAttribute>();
				if ( range != null )
				{
					slider.Bind( "Min" ).From( node.Node, range.Min );
					slider.Bind( "Max" ).From( node.Node, range.Max );
					slider.Bind( "Step" ).From( node.Node, range.Step );
				}
				else if ( Property.GetCustomAttribute<MinMaxAttribute>() is MinMaxAttribute minMax )
				{
					slider.Min = minMax.MinValue;
					slider.Max = minMax.MaxValue;
				}

				return slider;
			}

			if ( type == typeof( Color ) )
			{
				var slider = new ColorEditor( plug ) { Title = DisplayInfo.Name, Node = node };
				slider.Bind( "Value" ).From( node.Node, editor.ValueName );

				return slider;
			}
		}
		return null;
	}
}
