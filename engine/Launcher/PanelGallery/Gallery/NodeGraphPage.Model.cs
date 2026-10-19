using static Sandbox.Internal.GlobalGameNamespace;
using Sandbox.UI;
using Sandbox.UI.NodeGraph;

namespace Sandbox.PanelGallery;

/// <summary>
/// The value an inline editor edits, as a property the editor can bind to.
/// </summary>
public class NodeGraphValue
{
	public float Number { get; set; }
	public Color Colour { get; set; } = Color.White;

	/// <summary>
	/// A property of this object, for binding an editor to.
	/// </summary>
	public SerializedProperty Property( string name )
	{
		return Game.TypeLibrary.GetSerializedObject( this ).GetProperty( name );
	}
}

public partial class NodeGraphPage
{
	/// <summary>
	/// A list of nodes. Removing a node cuts the wires that came from it.
	/// </summary>
	sealed class DemoGraph : IGraph
	{
		readonly List<DemoNode> _nodes = new();

		public IEnumerable<INode> Nodes => _nodes;

		public void AddNode( INode node ) => _nodes.Add( (DemoNode)node );

		public void RemoveNode( INode node )
		{
			if ( !_nodes.Remove( (DemoNode)node ) ) return;

			foreach ( var other in _nodes )
			{
				other.Disconnect( node );
			}
		}
	}

	/// <summary>
	/// A node with plugs added by <see cref="AddInput"/> and <see cref="AddOutput"/>.
	/// </summary>
	class DemoNode : INode
	{
		readonly List<DemoPlugIn> _inputs = new();
		readonly List<DemoPlugOut> _outputs = new();

		public event Action Changed;

		public string Identifier { get; }
		public DisplayInfo DisplayInfo { get; }
		public string Subtitle { get; init; }
		public bool CanRemove { get; init; } = true;
		public Vector2 Position { get; set; }
		public Vector2 ExpandSize { get; init; }
		public bool AutoSize => true;
		public string ErrorMessage { get; init; }
		public bool IsReachable { get; init; } = true;
		public Texture Thumbnail { get; init; }
		public bool HasTitleBar { get; init; } = true;
		public bool HasSubtitle => Subtitle is not null;
		/// <summary>
		/// Neutral body like the shader graph's; the colour goes on the header.
		/// </summary>
		public Color PrimaryColor { get; init; } = Color.Parse( "#303030" ).Value.Lighten( 2.0f );
		public Color TitleColor { get; init; }

		/// <summary>
		/// Operators leave their plugs unlabelled, which keeps them compact.
		/// </summary>
		public bool ShowLabels { get; init; } = true;

		public IEnumerable<IPlugIn> Inputs => _inputs;
		public IEnumerable<IPlugOut> Outputs => _outputs;

		public DemoNode( string identifier, string name, string icon, string description, Color color, Vector2 position )
		{
			Identifier = identifier;
			DisplayInfo = new DisplayInfo { Name = name, Icon = icon, Description = description };
			TitleColor = color;
			Position = position;
		}

		public DemoPlugIn AddInput( string identifier, string name, Type type, string group = null, Func<PlugPanel, NodeValueEditor> editor = null, string error = null )
		{
			var plug = new DemoPlugIn( this, identifier, name, type, group, editor, error );
			_inputs.Add( plug );
			return plug;
		}

		public DemoPlugOut AddOutput( string identifier, string name, Type type, string group = null )
		{
			var plug = new DemoPlugOut( this, identifier, name, type, group );
			_outputs.Add( plug );
			return plug;
		}

		/// <summary>
		/// Cut every wire from <paramref name="source"/>'s outputs into this node.
		/// </summary>
		public void Disconnect( INode source )
		{
			var changed = false;

			foreach ( var input in _inputs.Where( x => x.ConnectedOutput?.Node == source ) )
			{
				input.ConnectedOutput = null;
				changed = true;
			}

			if ( changed ) Changed?.Invoke();
		}

		/// <summary>
		/// Shown as "Go to Definition" in the node's menu when set.
		/// </summary>
		public Action GoToDefinition { get; init; }

		/// <summary>
		/// A submenu of the node's own in its right-click menu. Comments and reroutes have none.
		/// </summary>
		public Sandbox.UI.Menu CreateContextMenu( NodePanel panel )
		{
			if ( this is ICommentNode or IRerouteNode ) return null;

			var menu = new Sandbox.UI.Menu( "Demo node", "science" );
			menu.AddOption( "Say hello", "waving_hand", () => Log.Info( $"Hello from {DisplayInfo.Name}" ) );
			return menu;
		}

		public virtual void OnDoubleClick( NodePanel panel, MousePanelEvent e ) { }

		public virtual void OnPaint<T>( T paint, Rect rect ) where T : INodePaint, allows ref struct { }
	}

	/// <summary>
	/// A dot that bends a wire; takes and gives anything.
	/// </summary>
	sealed class DemoReroute : DemoNode, IRerouteNode
	{
		public string Comment { get; set; }

		public DemoReroute( string identifier, Vector2 position )
			: base( identifier, "Reroute", null, null, Color.White, position )
		{
			HasTitleBar = false;
			ShowLabels = false;
			AddInput( "in", "In", typeof( object ) );
			AddOutput( "out", "Out", typeof( object ) );
		}
	}

	/// <summary>
	/// A node the menu offers, made by a function. Plugs are matched on a node it makes to look at.
	/// </summary>
	sealed class DemoNodeType : INodeType
	{
		readonly Func<DemoNode> _create;
		DemoNode _sample;

		public NodePathElement[] Path { get; }
		public bool IsCommon { get; init; } = true;

		/// <summary>
		/// <paramref name="path"/> is <c>/</c>-separated; a level starting with <c>#</c> is a heading.
		/// </summary>
		public DemoNodeType( string path, Func<DemoNode> create )
		{
			_create = create;

			var sample = Sample;
			Path = path.Split( '/' )
				.Select( x => x.StartsWith( '#' ) ? new NodePathElement( x[1..], IsHeading: true ) : new NodePathElement( x ) )
				.ToArray();
			Path[^1] = Path[^1] with { Icon = sample.DisplayInfo.Icon, Description = sample.DisplayInfo.Description };
		}

		DemoNode Sample => _sample ??= _create();

		static bool Accepts( Type plug, Type value ) => plug == value || plug == typeof( object ) || value == typeof( object );

		public bool TryGetInput( Type valueType, out string name )
		{
			name = Sample.Inputs.FirstOrDefault( x => Accepts( x.Type, valueType ) )?.Identifier;
			return name is not null;
		}

		public bool TryGetOutput( Type valueType, out string name )
		{
			name = Sample.Outputs.FirstOrDefault( x => Accepts( x.Type, valueType ) )?.Identifier;
			return name is not null;
		}

		public INode CreateNode( IGraph graph ) => _create();
	}

	static string NewId() => Guid.NewGuid().ToString();

	static readonly DemoNodeType[] DemoNodeTypes =
	[
		new( "Math/Add", () =>
		{
			var node = new DemoNode( NewId(), "Add", "+", "Adds A and B.", (Color)"#8fd6a8", default ) { HasTitleBar = false, ShowLabels = false };
			node.AddInput( "a", "A", typeof( float ) );
			node.AddInput( "b", "B", typeof( float ) );
			node.AddOutput( "result", "Result", typeof( float ) );
			return node;
		} ),
		new( "Math/Multiply", () =>
		{
			var node = new DemoNode( NewId(), "Multiply", "close", "Multiplies A by B.", (Color)"#8fd6a8", default ) { HasTitleBar = false, ShowLabels = false };
			node.AddInput( "a", "A", typeof( float ) );
			node.AddInput( "b", "B", typeof( float ) );
			node.AddOutput( "result", "Result", typeof( float ) );
			return node;
		} ),
		new( "Input/Time", () =>
		{
			var node = new DemoNode( NewId(), "Time", "schedule", "Seconds since the graph started.", (Color)"#7aa7e8", default );
			node.AddOutput( "seconds", "Seconds", typeof( float ), "Time" );
			return node;
		} ),
		new( "Texture/Sample", () =>
		{
			var node = new DemoNode( NewId(), "Sample Normal", "image", "Reads a normal from a texture.", (Color)"#d9b55a", default );
			node.AddOutput( "normal", "Normal", typeof( Vector3 ) );
			return node;
		} ),
		new( "#Common/Color", () =>
		{
			var node = new DemoNode( NewId(), "Color", "palette", "A constant colour.", (Color)"#e07fa6", default );
			node.AddOutput( "color", "Color", typeof( Color ) );
			return node;
		} ),
		new( "Math/Lerp", () =>
		{
			var node = new DemoNode( NewId(), "Lerp", "linear_scale", "Blends A to B by T.", (Color)"#8fd6a8", default );
			node.AddInput( "a", "A", typeof( float ) );
			node.AddInput( "b", "B", typeof( float ) );
			node.AddInput( "t", "T", typeof( float ) );
			node.AddOutput( "result", "Result", typeof( float ) );
			return node;
		} ) { IsCommon = false },
	];

	static readonly DemoNodeType DemoCommentType = new( "Comment", () => new DemoComment( NewId(), "Comment", null, NodeGraphPanel.DefaultCommentColor, default ) );

	static readonly DemoNodeType DemoRerouteType = new( "Reroute", () => new DemoReroute( NewId(), default ) );

	/// <summary>
	/// Draws a curve plot under its plugs, as nodes can draw anything over themselves.
	/// </summary>
	sealed class CurveNode : DemoNode
	{
		const float PlotHeight = 60;

		readonly Curve _curve = new( new Curve.Frame( 0, 0 ), new Curve.Frame( 0.35f, 0.15f ), new Curve.Frame( 0.7f, 0.85f ), new Curve.Frame( 1, 1 ) );

		public CurveNode( string identifier, string name, string icon, string description, Color color, Vector2 position )
			: base( identifier, name, icon, description, color, position )
		{
		}

		/// <summary>
		/// Takes the double-click, so the graph does nothing with it.
		/// </summary>
		public override void OnDoubleClick( NodePanel panel, MousePanelEvent e )
		{
			Log.Info( $"Double-clicked {DisplayInfo.Name}" );
			e.StopPropagation();
		}

		public override void OnPaint<T>( T paint, Rect rect )
		{
			var plot = new Rect( 8, rect.Height - PlotHeight - 8, rect.Width - 16, PlotHeight );

			paint.ClearPen();
			paint.SetBrush( Color.Black.WithAlpha( 0.35f ) );
			paint.DrawRect( plot, 2 );

			paint.SetPen( Color.White.WithAlpha( 0.08f ) );

			for ( var i = 1; i < 4; i++ )
			{
				var x = plot.Left + plot.Width * i / 4f;
				var y = plot.Top + plot.Height * i / 4f;

				paint.DrawLine( new Vector2( x, plot.Top ), new Vector2( x, plot.Bottom ) );
				paint.DrawLine( new Vector2( plot.Left, y ), new Vector2( plot.Right, y ) );
			}

			paint.SetPen( PrimaryColor.Lighten( paint.HasMouseOver ? 0.9f : 0.7f ), 1.5f );

			const int steps = 32;
			var previous = Sample( plot, 0 );

			for ( var i = 1; i <= steps; i++ )
			{
				var point = Sample( plot, i / (float)steps );
				paint.DrawLine( previous, point );
				previous = point;
			}
		}

		Vector2 Sample( Rect plot, float t )
		{
			return new Vector2( plot.Left + plot.Width * t, plot.Bottom - plot.Height * _curve.Evaluate( t ).Clamp( 0, 1 ) );
		}
	}

	/// <summary>
	/// A comment around other nodes, which it holds by identifier.
	/// </summary>
	sealed class DemoComment : DemoNode, ICommentNode
	{
		public string Title { get; set; }
		public string Description { get; set; }
		public Color Color { get; set; }
		public Vector2 Size { get; set; }
		public List<string> Members { get; } = new();

		public DemoComment( string identifier, string title, string description, Color color, Vector2 position, params string[] members )
			: base( identifier, title, "notes", description, color, position )
		{
			Title = title;
			Description = description;
			Color = color;
			Members.AddRange( members );
		}
	}

	abstract class DemoPlug : IPlug
	{
		public INode Node { get; }
		public string Identifier { get; }
		public Type Type { get; }
		public DisplayInfo DisplayInfo { get; }
		public bool ShowLabel => ((DemoNode)Node).ShowLabels;
		public bool AllowStretch => false;
		public bool ShowConnection => true;
		public bool InTitleBar => false;
		public bool IsReachable => Node.IsReachable;
		public string ErrorMessage { get; }

		readonly Func<PlugPanel, NodeValueEditor> _editor;

		protected DemoPlug( DemoNode node, string identifier, string name, Type type, string group, Func<PlugPanel, NodeValueEditor> editor, string error )
		{
			Node = node;
			Identifier = identifier;
			Type = type;
			DisplayInfo = new DisplayInfo { Name = name, Group = group };
			ErrorMessage = error;
			_editor = editor;
		}

		public NodeValueEditor CreateEditor( PlugPanel plug ) => _editor?.Invoke( plug );
	}

	sealed class DemoPlugIn : DemoPlug, IPlugIn
	{
		readonly Dictionary<string, float> _handleOffsets = new();

		public IPlugOut ConnectedOutput { get; set; }

		public DemoPlugIn( DemoNode node, string identifier, string name, Type type, string group, Func<PlugPanel, NodeValueEditor> editor, string error )
			: base( node, identifier, name, type, group, editor, error )
		{
		}

		public float? GetHandleOffset( string name ) => _handleOffsets.TryGetValue( name, out var offset ) ? offset : null;

		public void SetHandleOffset( string name, float? value )
		{
			if ( value is { } offset ) _handleOffsets[name] = offset;
			else _handleOffsets.Remove( name );
		}
	}

	sealed class DemoPlugOut : DemoPlug, IPlugOut
	{
		public DemoPlugOut( DemoNode node, string identifier, string name, Type type, string group )
			: base( node, identifier, name, type, group, null, null )
		{
		}
	}
}
