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

		public virtual void OnPaint<T>( T paint, Rect rect ) where T : INodePaint, allows ref struct { }
	}

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

	abstract class DemoPlug : IPlug
	{
		public INode Node { get; }
		public string Identifier { get; }
		public Type Type { get; }
		public DisplayInfo DisplayInfo { get; }
		public bool ShowLabel => true;
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
