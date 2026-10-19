using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.UI;
using Sandbox.UI.NodeGraph;

namespace UITests.Controls;

[TestClass]
[DoNotParallelize]
public class NodeGraphPanelTests
{
	UISurface _surface;
	bool _previousTextRendering;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();
		_previousTextRendering = UiTesting.DisableTextRendering();
		_surface = new UISurface { Size = new Vector2( 1000, 800 ), DpiScale = 1, MouseInside = true };
	}

	[TestCleanup]
	public void Cleanup()
	{
		_surface.Dispose();
		TextBlock.ui_rendertext = _previousTextRendering;
	}

	void Frame()
	{
		for ( int i = 0; i < 4; i++ )
			UiTesting.Frame( _surface );
	}

	sealed class TestGraph : IGraph
	{
		readonly List<INode> _nodes = new();

		public IEnumerable<INode> Nodes => _nodes;
		public void AddNode( INode node ) => _nodes.Add( node );
		public void RemoveNode( INode node ) => _nodes.Remove( node );
	}

	class TestNode : INode
	{
		public event Action Changed { add { } remove { } }

		public string Identifier { get; } = Guid.NewGuid().ToString();
		public DisplayInfo DisplayInfo { get; }
		public Vector2 Position { get; set; }
		public bool AutoSize => true;
		public string ErrorMessage => null;
		public bool IsReachable => true;
		public Color PrimaryColor => Color.Gray;

		public readonly List<TestPlugIn> In = new();
		public readonly List<TestPlugOut> Out = new();

		public IEnumerable<IPlugIn> Inputs => In;
		public IEnumerable<IPlugOut> Outputs => Out;

		public TestNode( string name, Vector2 position = default )
		{
			DisplayInfo = new DisplayInfo { Name = name };
			Position = position;
		}

		public TestNode WithInput( Type type )
		{
			In.Add( new TestPlugIn( this, $"in{In.Count}", type ) );
			return this;
		}

		public TestNode WithOutput( Type type )
		{
			Out.Add( new TestPlugOut( this, $"out{Out.Count}", type ) );
			return this;
		}
	}

	sealed class TestReroute : TestNode, IRerouteNode
	{
		public string Comment { get; set; }

		public TestReroute() : base( "Reroute" )
		{
			WithInput( typeof( object ) );
			WithOutput( typeof( object ) );
		}
	}

	abstract class TestPlug : IPlug
	{
		public INode Node { get; }
		public string Identifier { get; }
		public Type Type { get; }
		public DisplayInfo DisplayInfo { get; }
		public bool ShowLabel => true;
		public bool AllowStretch => false;
		public bool ShowConnection => true;
		public bool InTitleBar => false;
		public bool IsReachable => true;
		public string ErrorMessage => null;

		protected TestPlug( INode node, string identifier, Type type )
		{
			Node = node;
			Identifier = identifier;
			Type = type;
			DisplayInfo = new DisplayInfo { Name = identifier };
		}
	}

	sealed class TestPlugIn : TestPlug, IPlugIn
	{
		public IPlugOut ConnectedOutput { get; set; }
		public float? GetHandleOffset( string name ) => null;
		public void SetHandleOffset( string name, float? value ) { }

		public TestPlugIn( INode node, string identifier, Type type ) : base( node, identifier, type ) { }
	}

	sealed class TestPlugOut : TestPlug, IPlugOut
	{
		public TestPlugOut( INode node, string identifier, Type type ) : base( node, identifier, type ) { }
	}

	sealed class TestType : INodeType
	{
		readonly Func<TestNode> _create;
		readonly TestNode _sample;

		public NodePathElement[] Path { get; }
		public bool IsCommon { get; init; } = true;

		public TestType( Func<TestNode> create, params string[] path )
		{
			_create = create;
			_sample = create();
			Path = path.Select( x => new NodePathElement( x ) ).ToArray();
		}

		static bool Accepts( Type plug, Type value ) => plug == value || plug == typeof( object );

		public bool TryGetInput( Type valueType, out string name )
		{
			name = _sample.In.FirstOrDefault( x => Accepts( x.Type, valueType ) )?.Identifier;
			return name is not null;
		}

		public bool TryGetOutput( Type valueType, out string name )
		{
			name = _sample.Out.FirstOrDefault( x => Accepts( x.Type, valueType ) )?.Identifier;
			return name is not null;
		}

		public INode CreateNode( IGraph graph ) => _create();
	}

	sealed class TestGraphPanel : NodeGraphPanel
	{
		public readonly List<string> Undo = new();
		public int Redo;

		public override void PushUndo( string name ) => Undo.Add( name );
		public override void PushRedo() => Redo++;

		protected override INodeType RerouteNodeType { get; } = new TestType( () => new TestReroute(), "Reroute" );
	}

	/// <summary>
	/// A float output node at <paramref name="a"/> wired into a float input node at <paramref name="b"/>.
	/// </summary>
	(TestGraphPanel Panel, TestGraph Graph, TestNode A, TestNode B) CreateWiredGraph( Vector2 a, Vector2 b )
	{
		var graph = new TestGraph();
		var from = new TestNode( "A", a ).WithOutput( typeof( float ) );
		var to = new TestNode( "B", b ).WithInput( typeof( float ) );
		to.In[0].ConnectedOutput = from.Out[0];
		graph.AddNode( from );
		graph.AddNode( to );

		var panel = _surface.Root.AddChild( new TestGraphPanel() );
		panel.Style.Width = 600;
		panel.Style.Height = 400;
		panel.Graph = graph;
		Frame();

		return (panel, graph, from, to);
	}

	[TestMethod]
	public void RerouteConnectionSplitsWireAndPushesOneUndoStep()
	{
		var (panel, graph, a, b) = CreateWiredGraph( new( -250, -50 ), new( 100, -50 ) );
		var connection = panel.Connections.Single();

		panel.RerouteConnection( connection, new Vector2( 0, 0 ) );
		Frame();

		var reroute = graph.Nodes.OfType<TestReroute>().Single();
		Assert.AreEqual( 3, graph.Nodes.Count() );
		Assert.AreSame( a.Out[0], reroute.In[0].ConnectedOutput );
		Assert.AreSame( reroute.Out[0], b.In[0].ConnectedOutput );
		Assert.AreEqual( 2, panel.Connections.Count, "one wire per input, also after the view syncs with the model" );
		Assert.IsInstanceOfType( panel.FindNode( reroute ), typeof( ReroutePanel ) );
		CollectionAssert.AreEqual( new[] { "Reroute Connection" }, panel.Undo );
		Assert.AreEqual( 1, panel.Redo );
	}

	[TestMethod]
	public void CreateNewNodeOnTargetPlugReplacesItsWire()
	{
		var (panel, graph, a, b) = CreateWiredGraph( new( -250, -50 ), new( 100, -50 ) );
		var type = new TestType( () => new TestNode( "Constant" ).WithOutput( typeof( float ) ), "Constant" );

		panel.CreateNewNode( type, new Vector2( -100, 100 ), panel.FindPlug( b.In[0] ) );
		Frame();

		var created = graph.Nodes.OfType<TestNode>().Single( x => x.DisplayInfo.Name == "Constant" );
		Assert.AreSame( created.Out[0], b.In[0].ConnectedOutput );
		Assert.AreEqual( 1, panel.Connections.Count );
		Assert.AreSame( panel.FindNode( created ), panel.SelectedNodes.Single() );
		CollectionAssert.AreEqual( new[] { "Add Node" }, panel.Undo );
		Assert.AreEqual( 1, panel.Redo );
	}

	[TestMethod]
	public void NodeQueryFiltersScoresAndMatchesThePlug()
	{
		static TestNode Math() => new TestNode( "Math" ).WithInput( typeof( float ) ).WithOutput( typeof( float ) );

		var add = new TestType( Math, "Math", "Add" );
		var subtract = new TestType( Math, "Math", "Subtract" );
		var sample = new TestType( () => new TestNode( "Sample" ).WithOutput( typeof( Vector3 ) ), "Texture", "Sample" );
		var lerp = new TestType( Math, "Math", "Lerp" ) { IsCommon = false };
		INodeType[] types = [add, subtract, sample, lerp];
		var graph = new TestGraph();

		CollectionAssert.AreEquivalent( new[] { add, subtract, sample }, types.Filter( new NodeQuery( graph, null ) ).ToArray(), "uncommon types need a filter" );
		CollectionAssert.AreEquivalent( new[] { add }, types.Filter( new NodeQuery( graph, null, "add" ) ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { sample }, types.Filter( new NodeQuery( graph, null, "-math" ) ).ToArray() );
		CollectionAssert.AreEquivalent( new[] { lerp }, types.Filter( new NodeQuery( graph, null, "lerp" ) ).ToArray() );

		var s = new NodeQuery( graph, null, "s" );
		Assert.IsTrue( s.GetScore( sample.Path ) > s.GetScore( subtract.Path ), "shorter names score higher" );

		var floatOut = new TestNode( "Source" ).WithOutput( typeof( float ) ).Out[0];
		CollectionAssert.AreEquivalent( new[] { add, subtract }, types.Filter( new NodeQuery( graph, floatOut ) ).ToArray(), "only types taking a float" );
	}

	[TestMethod]
	public void NodeMenuNestsByPathCollapsesSingleEntriesAndFlattensWhenFiltered()
	{
		static TestNode Node() => new TestNode( "Node" );

		INodeType[] types = [new TestType( Node, "Math", "Add" ), new TestType( Node, "Math", "Subtract" ), new TestType( Node, "Texture", "Sample" )];
		var graph = new TestGraph();

		var tree = new Menu();
		NodeMenu.AddOptions( tree, types, null, _ => { } );
		CollectionAssert.AreEqual( new[] { "Math", "Texture → Sample" }, tree.Options.Select( x => x.Text ).ToArray() );
		CollectionAssert.AreEqual( new[] { "Add", "Subtract" }, tree.Options[0].Options.Select( x => x.Text ).ToArray() );

		INodeType picked = null;
		var flat = new Menu();
		NodeMenu.AddOptions( flat, types.Filter( new NodeQuery( graph, null, "a" ) ), new NodeQuery( graph, null, "a" ), x => picked = x );
		CollectionAssert.AreEqual( new[] { "Math → Add", "Math → Subtract", "Texture → Sample" }, flat.Options.Select( x => x.Text ).ToArray(), "best match first" );

		flat.Options[0].Clicked();
		Assert.AreSame( types[0], picked );

		tree.Delete( true );
		flat.Delete( true );
	}

	[TestMethod]
	public void ShiftClickOnWireInsertsReroute()
	{
		var (panel, graph, _, _) = CreateWiredGraph( new( -250, -50 ), new( 100, -50 ) );
		var connection = panel.Connections.Single();
		var point = panel.Box.Rect.Position + panel.CanvasToScreen( connection.Points[connection.Points.Count / 2] );

		_surface.MouseMoved( point );
		Frame();
		_surface.MouseMoved( point );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, KeyboardModifiers.Shift );
		Frame();
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, KeyboardModifiers.Shift );
		Frame();

		Assert.AreEqual( 1, graph.Nodes.OfType<TestReroute>().Count() );
		Assert.AreEqual( 2, panel.Connections.Count );
		CollectionAssert.AreEqual( new[] { "Reroute Connection" }, panel.Undo );
	}

	/// <summary>
	/// Drag a wire out of <paramref name="plug"/> and let go at <paramref name="to"/>, in canvas
	/// units, with R held from the press when <paramref name="holdR"/>.
	/// </summary>
	void DragWire( NodeGraphPanel panel, PlugPanel plug, Vector2 to, bool holdR )
	{
		var from = panel.Box.Rect.Position + panel.CanvasToScreen( plug.ConnectionPosition );
		var end = panel.Box.Rect.Position + panel.CanvasToScreen( to );

		_surface.MouseMoved( from );
		Frame();
		_surface.MouseMoved( from );
		Frame();

		// In the same frame as the press, so the blurs the press causes arrive after R
		if ( holdR ) _surface.SetKey( "r", true );
		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, true, default );
		Frame();

		for ( var i = 1; i <= 4; i++ )
		{
			_surface.MouseMoved( Vector2.Lerp( from, end, i / 4f ) );
			Frame();
		}

		_surface.Input.AddMouseButton( NativeEngine.ButtonCode.MouseLeft, false, default );
		Frame();
		if ( holdR ) _surface.SetKey( "r", false );
		Frame();
	}

	[TestMethod]
	public void WireDroppedOnNothingOpensTheNodeMenuOrWithRMakesAReroute()
	{
		var (panel, graph, a, _) = CreateWiredGraph( new( -250, -50 ), new( 100, -50 ) );
		var output = panel.FindPlug( a.Out[0] );

		DragWire( panel, output, new Vector2( 0, 120 ), holdR: false );

		var addReroute = _surface.Root.Descendants.OfType<Menu>().SingleOrDefault( x => x.Text == "Add Reroute" && x.ParentMenu?.IsOpen == true );
		Assert.IsNotNull( addReroute, "the node menu opened for the dropped wire" );
		Assert.IsNotNull( panel.Preview, "the dangling wire shows while the menu is open" );

		addReroute.Activate();
		Frame();

		var first = graph.Nodes.OfType<TestReroute>().Single();
		Assert.AreSame( a.Out[0], first.In[0].ConnectedOutput, "the new node is wired to the plug the wire came from" );
		Assert.IsNull( panel.Preview, "closing the menu drops the dangling wire" );

		// Pressing the plug moves the focus around; R must survive that
		DragWire( panel, output, new Vector2( 0, -150 ), holdR: true );

		Assert.AreEqual( 2, graph.Nodes.OfType<TestReroute>().Count() );
		Assert.IsFalse( _surface.Root.Descendants.OfType<Menu>().Any( x => x.Text == "Add Reroute" && x.ParentMenu?.IsOpen == true ), "no menu with R held" );
		Assert.IsNull( panel.Preview );
		CollectionAssert.AreEqual( new[] { "Add Node", "Add Node" }, panel.Undo );
	}
}
