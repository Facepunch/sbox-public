using static Sandbox.Internal.GlobalGameNamespace;
using Sandbox.UI;
using Sandbox.UI.NodeGraph;

namespace Sandbox.PanelGallery;

/// <summary>
/// The editor's node graph: titled and operator nodes, thumbnails, errors, inline editors, per-type handles and wires.
/// </summary>
public partial class NodeGraphPage : GalleryPage
{
	public NodeGraphPage() : base( "Node Graph", "Drag nodes by their body; Ctrl or Shift adds to the selection and empty space box-selects. Middle-drag pans and the wheel zooms. Drag from a handle to edit wires, Delete removes the selection. Right-click empty space or press Space for the node menu - type to filter it. Drop a wire on empty space for the nodes that fit it, or hold R to end it in a reroute; Shift-click a wire to reroute it. Right-click a node for its own entries and to comment it; drop nodes inside a comment to put them in it. Ctrl+C, X, V and D copy, cut, paste and duplicate; Ctrl+Z undoes and Ctrl+Y or Ctrl+Shift+Z redoes. Ctrl+F finds a placed node and jumps to it. Hover a node or a handle for its tooltip." )
	{
		var row = Case( "Material graph", column: true );

		var graph = new DemoNodeGraph { Graph = BuildGraph() };
		var tools = row.AddChild<Toolbar>();
		tools.AddButton( "Delete selection", "delete", graph.DeleteSelection );
		tools.AddButton( "Select all", "select_all", graph.SelectAll );
		tools.AddButton( "Center on selection", "center_focus_strong", graph.CenterOnSelection );
		tools.AddButton( "Reset view", "fit_screen", graph.FitView );
		tools.AddButton( "Reset graph", "restart_alt", graph.Reset );
		row.AddChild( graph );

		var output = Output();
		graph.SelectionChanged += () => output.Text = Describe( graph );
		graph.UndoChanged += () => output.Text = Describe( graph );
		output.Text = Describe( graph );
	}

	static string Describe( DemoNodeGraph graph )
	{
		var selected = graph.SelectedNodes.Select( x => x.DisplayInfo.Name ).ToArray();
		var log = graph.UndoLog.ToArray();
		var undo = log.Length == 0 ? "nothing to undo" : $"undo: {string.Join( ", ", log.TakeLast( 5 ) )}";
		return $"{graph.Nodes.Count} nodes · {(selected.Length == 0 ? "nothing selected" : $"selected: {string.Join( ", ", selected )}")} · {undo}";
	}

	/// <summary>
	/// A graph panel with a handle look per value type, a node menu, comments, reroutes and undo
	/// through whole-graph snapshots.
	/// </summary>
	sealed class DemoNodeGraph : NodeGraphPanel
	{
		bool _fitted;
		string _pending;
		List<DemoNode> _before;

		readonly List<(string Name, List<DemoNode> State)> _undo = new();
		readonly List<(string Name, List<DemoNode> State)> _redo = new();

		/// <summary>
		/// The edits that can be undone, by name, oldest first.
		/// </summary>
		public IEnumerable<string> UndoLog => _undo.Select( x => x.Name );

		public event Action UndoChanged;

		DemoGraph Demo => (DemoGraph)Graph;

		public DemoNodeGraph()
		{
			Style.Width = Length.Percent( 100 );
			Style.Height = 560;
		}

		protected override INodeType CommentNodeType => DemoCommentType;
		protected override INodeType RerouteNodeType => DemoRerouteType;

		protected override IEnumerable<INodeType> GetRelevantNodes( NodeQuery query ) => DemoNodeTypes.Filter( query );

		/// <summary>
		/// Back to the graph the page starts with, forgetting the undo history.
		/// </summary>
		public void Reset()
		{
			Graph = BuildGraph();
			_undo.Clear();
			_redo.Clear();
			UndoChanged?.Invoke();
		}

		public override void PushUndo( string name )
		{
			_pending = name;
			_before = Demo.Snapshot();
		}

		public override void PushRedo()
		{
			if ( _pending is null ) return;

			_undo.Add( (_pending, _before) );
			_redo.Clear();
			_pending = null;
			_before = null;
			UndoChanged?.Invoke();
		}

		/// <summary>
		/// Put the graph back as it was before the last edit.
		/// </summary>
		public void Undo() => Step( _undo, _redo );

		/// <summary>
		/// Make the last undone edit again.
		/// </summary>
		public void Redo() => Step( _redo, _undo );

		void Step( List<(string Name, List<DemoNode> State)> from, List<(string Name, List<DemoNode> State)> to )
		{
			if ( from.Count == 0 ) return;

			var (name, state) = from[^1];
			from.RemoveAt( from.Count - 1 );
			to.Add( (name, Demo.Snapshot()) );

			Demo.Restore( state );
			RebuildFromGraph();
			UndoChanged?.Invoke();
		}

		public override void OnButtonTyped( ButtonEvent e )
		{
			var redo = e.HasCtrl && (e.Button == "y" || (e.Button == "z" && e.HasShift));
			var undo = e.HasCtrl && e.Button == "z" && !e.HasShift;

			// A text field keeps its own undo
			if ( (undo || redo) && !Descendants.OfType<TextEntry>().Any( x => x.HasFocus ) )
			{
				if ( undo ) Undo();
				else Redo();

				e.StopPropagation = true;
				return;
			}

			base.OnButtonTyped( e );
		}

		protected override NodeHandleConfig OnGetHandleConfig( Type type )
		{
			if ( type == typeof( float ) ) return new NodeHandleConfig( "Float", (Color)"#8fd6a8", HandleShape.Square );
			if ( type == typeof( Vector3 ) ) return new NodeHandleConfig( "Vector3", (Color)"#e8c56a", HandleShape.Circle );
			if ( type == typeof( Color ) ) return new NodeHandleConfig( "Color", (Color)"#f08fb0", HandleShape.Arrow );

			return base.OnGetHandleConfig( type );
		}

		/// <summary>
		/// Frame every node, zooming out when they don't fit.
		/// </summary>
		public void FitView()
		{
			if ( Nodes.Count == 0 || Viewport.Width <= 0 || Viewport.Height <= 0 ) return;

			var min = new Vector2( float.MaxValue );
			var max = new Vector2( float.MinValue );

			foreach ( var node in Nodes )
			{
				min = Vector2.Min( min, node.Position );
				max = Vector2.Max( max, node.Position + node.Size );
			}

			var available = Viewport.Size / ScaleToScreen;
			var size = max - min + new Vector2( 48 );
			var zoom = MathF.Min( 1, MathF.Min( available.x / size.x, available.y / size.y ) );

			SetView( (min + max) * 0.5f, zoom );
		}

		public override void Tick()
		{
			base.Tick();

			if ( !_fitted && Viewport.Width > 0 && Viewport.Height > 0 )
			{
				_fitted = true;
				FitView();
			}
		}
	}

	static Texture _thumbnail;

	/// <summary>
	/// A small tiled gradient, so the thumbnails show something.
	/// </summary>
	static Texture Thumbnail()
	{
		if ( _thumbnail is not null ) return _thumbnail;

		const int size = 32;
		var data = new byte[size * size * 4];

		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
			{
				var i = (y * size + x) * 4;
				var tile = ((x / 8) + (y / 8)) % 2 == 0 ? 1f : 0.7f;

				data[i + 0] = (byte)(255 * x / (size - 1) * tile);
				data[i + 1] = (byte)(255 * y / (size - 1) * tile);
				data[i + 2] = (byte)(200 * tile);
				data[i + 3] = 255;
			}
		}

		return _thumbnail = Texture.Create( size, size ).WithData( data ).Finish();
	}

	static Func<PlugPanel, NodeValueEditor> FloatEditor( string title, float value, float min = 0, float max = 1 )
	{
		var target = new NodeGraphValue { Number = value };

		return plug => new FloatValueEditor( plug, target.Property( nameof( NodeGraphValue.Number ) ) ) { Title = title, Min = min, Max = max };
	}

	static Func<PlugPanel, NodeValueEditor> ColorEditor( string title, Color value )
	{
		var target = new NodeGraphValue { Colour = value };

		return plug => new ColorValueEditor( plug, target.Property( nameof( NodeGraphValue.Colour ) ) ) { Title = title };
	}

	static DemoGraph BuildGraph()
	{
		var graph = new DemoGraph();

		// Inputs: titled nodes with an icon and group-prefixed plug labels.
		var time = new DemoNode( "time", "Time", "schedule", "Seconds since the graph started.", (Color)"#7aa7e8", new( -456, -240 ) );
		var seconds = time.AddOutput( "seconds", "Seconds", typeof( float ), "Time" );
		var sine = time.AddOutput( "sine", "Sine", typeof( float ), "Time" );

		var position = new DemoNode( "position", "World Position", "public", "The position of the shaded point.", (Color)"#d9b55a", new( -456, 12 ) );
		var xyz = position.AddOutput( "xyz", "XYZ", typeof( Vector3 ) );

		// A thumbnail with a title bar.
		var texture = new DemoNode( "texture", "Texture", "image", "Samples a texture.", (Color)"#e07fa6", new( -456, -132 ) ) { Thumbnail = Thumbnail() };
		var sampled = texture.AddOutput( "color", "Color", typeof( Color ), "Sample" );

		// A thumbnail without a title bar: icon in the corner, name over the bottom.
		var albedo = new DemoNode( "albedo", "Albedo", "photo", "A texture reference.", (Color)"#e07fa6", new( -456, 96 ) ) { Thumbnail = Thumbnail(), HasTitleBar = false };
		albedo.AddOutput( "color", "Color", typeof( Color ) );

		// An operator: no title bar, the icon fills the body.
		var add = new DemoNode( "add", "Add", "+", "Adds A and B.", (Color)"#8fd6a8", new( -216, -240 ) ) { HasTitleBar = false, ShowLabels = false };
		var addA = add.AddInput( "a", "A", typeof( float ) );
		add.AddInput( "b", "B", typeof( float ) );
		var addResult = add.AddOutput( "result", "Result", typeof( float ) );
		addA.ConnectedOutput = seconds;

		// Draws its own plot through INode.OnPaint, takes double-clicks and has a definition to go to.
		var remap = new CurveNode( "remap", "Remap Curve", "show_chart", "Shapes a value with a curve.", (Color)"#8fd6a8", new( -216, -60 ) )
		{
			ExpandSize = new( 72, 72 ),
			GoToDefinition = () => Log.Info( "Go to the definition of Remap Curve" ),
		};
		var remapValue = remap.AddInput( "value", "Value", typeof( float ) );
		remap.AddOutput( "result", "Result", typeof( float ) );

		// A reroute bends the sine wire, with a note above it.
		var bend = new DemoReroute( "bend", new( -204, -120 ) ) { Comment = "Sine wave" };
		bend.Inputs.First().ConnectedOutput = sine;
		remapValue.ConnectedOutput = bend.Outputs.First();

		var normalize = new DemoNode( "normalize", "Normalize", "straighten", "Scales a vector to length one.", (Color)"#d9b55a", new( -216, 108 ) );
		var normalizeVector = normalize.AddInput( "vector", "Vector", typeof( Vector3 ) );
		var normalized = normalize.AddOutput( "result", "Result", typeof( Vector3 ) );
		normalizeVector.ConnectedOutput = xyz;

		// Nothing reaches the output through this node, so it is drawn dimmed.
		var unused = new DemoNode( "unused", "Unused Mix", "layers_clear", "Not connected to the output.", (Color)"#9a9a9a", new( -216, 204 ) ) { IsReachable = false };
		unused.AddInput( "a", "A", typeof( float ) );
		unused.AddOutput( "result", "Result", typeof( float ) );

		var blend = new DemoNode( "blend", "Color Mix", "palette", "Blends two colours.", (Color)"#e07fa6", new( 24, -228 ) ) { Subtitle = "Blend" };
		var blendBase = blend.AddInput( "base", "Base", typeof( Color ), "Color" );
		blend.AddInput( "tint", "Tint", typeof( Color ), "Color", ColorEditor( "Tint", (Color)"#ffb35c" ) );
		blend.AddInput( "amount", "Amount", typeof( float ), "Mix", FloatEditor( "Amount", 0.25f ) );
		var blended = blend.AddOutput( "result", "Result", typeof( Color ) );
		blendBase.ConnectedOutput = sampled;

		// A node error shows the badge, a plug error turns its handle red.
		var combine = new DemoNode( "combine", "Combine Vectors", "merge", "Adds two vectors.", (Color)"#d9b55a", new( 24, -48 ) ) { ErrorMessage = "Input B has no value." };
		var combineA = combine.AddInput( "a", "A", typeof( Vector3 ) );
		combine.AddInput( "b", "B", typeof( Vector3 ), error: "Connect a vector." );
		var combined = combine.AddOutput( "result", "Result", typeof( Vector3 ) );
		combineA.ConnectedOutput = normalized;

		var result = new DemoNode( "output", "Material Output", "view_in_ar", "The shaded result.", (Color)"#7aa7e8", new( 288, -156 ) ) { CanRemove = false };
		var resultColor = result.AddInput( "color", "Color", typeof( Color ) );
		var resultOpacity = result.AddInput( "opacity", "Opacity", typeof( float ) );
		var resultNormal = result.AddInput( "normal", "Normal", typeof( Vector3 ) );
		resultColor.ConnectedOutput = blended;
		resultOpacity.ConnectedOutput = addResult;
		resultNormal.ConnectedOutput = combined;

		// A comment: moving it moves the inputs, and it grows around them as they move.
		var inputs = new DemoComment( "inputs", "Inputs", "Everything the material reads.", (Color)"#7aa7e8", new( -480, -300 ), "time", "position", "texture", "albedo" );

		DemoNode[] nodes = [inputs, time, position, texture, albedo, add, bend, remap, normalize, unused, blend, combine, result];

		foreach ( var node in nodes )
		{
			graph.AddNode( node );
		}

		return graph;
	}
}
