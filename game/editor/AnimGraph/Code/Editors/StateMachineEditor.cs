using Editor.AnimGraph.Nodes;
using Editor.NodeEditor;
using Sandbox.Animation;

namespace Editor.AnimGraph;

[NodePage( typeof( StateMachineNode ) )]
internal sealed class StateMachinePage : Widget, INodePage
{
	private readonly StateMachineCanvas _canvas;

	internal StateMachineCanvas Canvas => _canvas;
	public CanvasWidget ViewCanvas => _canvas;
	public bool SupportsSecondaryView => true;
	internal object CaptureSelection() => _canvas.CaptureSelection();
	internal void RestoreSelection( object state ) => _canvas.RestoreSelection( state );
	internal void FocusSelection( object state ) => _canvas.FocusSelection( state );
	internal void NotifySelection() => _canvas.NotifySelection();
	bool INodePage.CanCutSelection => _canvas.CanCutSelection;
	bool INodePage.CanCopySelection => _canvas.CanCopySelection;
	bool INodePage.CanPasteSelection => _canvas.CanPasteSelection;
	bool INodePage.CutSelection() => _canvas.CutSelection();
	bool INodePage.CopySelection() => _canvas.CopySelection();
	bool INodePage.PasteSelection() => _canvas.PasteSelection();

	public StateMachinePage( Widget parent, StateMachineNode node, Window window ) : base( parent )
	{
		Layout = Layout.Column();
		_canvas = new StateMachineCanvas( this, node, window );
		Layout.Add( _canvas, 1 );
	}

	public bool Rebind( Node node )
	{
		return _canvas.Rebind( node );
	}
}

/// <summary>
/// The pannable, zoomable state machine canvas — states as draggable boxes,
/// transitions as arrows between them. Alt+drag from a state to create a transition,
/// double-click a state or transition to step into its graph.
/// </summary>
internal sealed class StateMachineCanvas : CanvasWidget
{
	private const string StateClipboardIdent = "animgraph-state";

	private sealed class StateClipboardData
	{
		public StateMachineNode.State State { get; set; }
		public List<StateMachineNode.Conduit> OutgoingConduits { get; set; } = [];

		public StateClipboardData() { }
	}

	internal readonly record struct StateSelection( string Name );
	internal readonly record struct ConduitSelection( string From, string To );

	/// <summary>
	/// One of the state machine's shared graphs - entry overrides or global
	/// transitions. Singletons carrying their own display info and navigation.
	/// </summary>
	internal sealed class SharedSelection
	{
		public static readonly SharedSelection GlobalTransitions =
			new( "Global Transitions", "public", "global-transitions", typeof( GlobalTransitionNode ), machine => machine.GlobalGraph );

		public static readonly SharedSelection EntryOverrides =
			new( "Entry Overrides", "login", "entry-overrides", typeof( EntryStateOverrideConditionsNode ), machine => machine.EntryGraph );

		public string Title { get; }
		public string Icon { get; }
		public string Key { get; }
		public Type DocumentationType { get; }

		private readonly Func<StateMachineNode, Data> _getGraph;

		private SharedSelection( string title, string icon, string key, Type documentationType, Func<StateMachineNode, Data> getGraph )
		{
			Title = title;
			Icon = icon;
			Key = key;
			DocumentationType = documentationType;
			_getGraph = getGraph;
		}

		public Data GetGraph( StateMachineNode machine ) => machine is null ? null : _getGraph( machine );
	}

	private StateMachineNode _node;

	internal StateMachineNode Node => _node;
	internal static StateMachineCanvas Current { get; private set; }

	public object Selected { get; private set; }
	public event Action<object> SelectionChanged;

	private StateMachineNode.State _dragState;
	private Vector2 _dragOffset;
	private StateMachineNode.State _linkSource;
	private StateMachineNode.State _hoverState;
	private StateMachineNode.Conduit _hoverConduit;
	private Vector2 _mouse;

	private Rect _globalsButton;
	private Rect _entriesButton;
	private int _hoverButton = -1;

	private bool[] _hasLiveStateInfo = [];
	private GraphDebug.NodeInfo[] _liveStateInfo = [];
	private GraphDebug.SyncEvent[][] _liveStateSyncTrackEvents = [];
	private string[] _liveStateSyncEventIDs = [];
	private StateMachineNode.State _liveState;
	private StateMachineNode.Conduit _liveConduit;
	private StateMachineNode.State _liveGlobalFrom;
	private StateMachineNode.State _liveGlobalTo;
	private TransitionNode _liveTransition;
	private float _liveTransitionProgress;

	private const float GridStep = 16;

	public StateMachineCanvas( Widget parent, StateMachineNode node, Window window ) : base( parent, window )
	{
		_node = node;
		Current = this;

		MinimumSize = new Vector2( 400, 300 );

		_node.SyncStates();
		AutoLayout();
	}

	public override void OnDestroyed()
	{
		if ( Current == this )
			Current = null;

		base.OnDestroyed();
	}

	/// <summary>
	/// States created before the canvas existed all sit at the origin — spread them in a ring.
	/// </summary>
	private void AutoLayout()
	{
		var states = _node.States;
		if ( states.Count < 2 || states.Any( s => s.Position != states[0].Position ) )
			return;

		var radius = 100 + states.Count * 30;
		for ( var i = 0; i < states.Count; i++ )
		{
			var angle = MathF.Tau * i / states.Count - MathF.PI * 0.5f;
			states[i].Position = new Vector2( MathF.Cos( angle ), MathF.Sin( angle ) ) * radius;
		}
	}

	private static Rect StateRect( StateMachineNode.State state )
	{
		var width = Math.Clamp( state.Name.Length * 8f + 48, 160, 280 );
		var size = new Vector2( width, 44 + PoseDebug.Height + PoseDebug.Margin );
		return new Rect( state.Position - size * 0.5f, size );
	}

	private StateMachineNode.State FindState( string name ) => _node.States.FirstOrDefault( s => s.Name == name );

	/// <summary>
	/// Arrow endpoints in graph space — border to border, aimed at the midpoint between
	/// the two states, nudged perpendicular so opposite directions render side by side.
	/// </summary>
	private bool TransitionLine( StateMachineNode.Conduit conduit, out Vector2 start, out Vector2 end )
	{
		start = end = default;

		if ( FindState( conduit.From ) is not { } from || FindState( conduit.To ) is not { } to || from == to )
			return false;

		return TransitionLine( from, to, out start, out end );
	}

	private static bool TransitionLine( StateMachineNode.State from, StateMachineNode.State to, out Vector2 start, out Vector2 end )
	{
		var ra = StateRect( from );
		var rb = StateRect( to );
		var mid = (ra.Center + rb.Center) * 0.5f;

		start = ClampToRect( ra, mid );
		end = ClampToRect( rb, mid );

		var dir = end - start;
		if ( dir.Length < 1 )
			return false;

		var offset = dir.Normal.Perpendicular * 6;

		start += offset;
		end += offset;
		return true;
	}

	private static Vector2 ClampToRect( Rect r, Vector2 p ) => new(
		Math.Clamp( p.x, r.Left, r.Right ),
		Math.Clamp( p.y, r.Top, r.Bottom ) );

	private static float DistanceToSegment( Vector2 p, Vector2 a, Vector2 b )
	{
		var ab = b - a;
		var t = Math.Clamp( Vector2.Dot( p - a, ab ) / MathF.Max( ab.LengthSquared, 0.001f ), 0, 1 );
		return p.Distance( a + ab * t );
	}

	private StateMachineNode.State HitState( Vector2 local )
	{
		var p = FromCanvas( local );

		for ( var i = _node.States.Count - 1; i >= 0; i-- )
		{
			if ( StateRect( _node.States[i] ).IsInside( p ) )
				return _node.States[i];
		}

		return null;
	}

	private StateMachineNode.Conduit HitTransition( Vector2 local )
	{
		StateMachineNode.Conduit closest = null;
		var closestDistance = 10f;
		foreach ( var conduit in _node.Conduits )
		{
			if ( !TransitionLine( conduit, out var start, out var end ) )
				continue;

			var a = ToCanvas( start );
			var b = ToCanvas( end );
			var count = conduit.Transitions.Count();
			if ( count > 1 && TransitionBadgeRect( a, b, count ).IsInside( local ) )
				return conduit;

			var distance = DistanceToSegment( local, a, b );
			if ( distance < closestDistance )
			{
				closest = conduit;
				closestDistance = distance;
			}
		}

		return closest;
	}

	internal void Select( object item )
	{
		if ( Equals( Selected, item ) )
		{
			Editor.RestorePrimarySelection();
			return;
		}

		Selected = item;
		NotifySelection();
		SelectionChanged?.Invoke( item );
		Update();
	}

	internal void SelectForTest( object item ) => Select( item );

	internal object CaptureSelection() => Selected switch
	{
		StateMachineNode.State state => new StateSelection( state.Name ),
		StateMachineNode.Conduit conduit => new ConduitSelection( conduit.From, conduit.To ),
		SharedSelection shared => shared,
		_ => null
	};

	internal void RestoreSelection( object state )
	{
		Select( state switch
		{
			StateSelection selection => _node.States.FirstOrDefault( state => state.Name == selection.Name ),
			ConduitSelection selection => _node.Conduits.FirstOrDefault( conduit => conduit.From == selection.From && conduit.To == selection.To ),
			SharedSelection shared => shared,
			_ => null
		} );
	}

	internal void FocusSelection( object state )
	{
		RestoreSelection( state );

		var center = Selected switch
		{
			StateMachineNode.State selectedState => selectedState.Position,
			StateMachineNode.Conduit conduit when FindState( conduit.From ) is { } from && FindState( conduit.To ) is { } to
				=> (from.Position + to.Position) * 0.5f,
			_ => ViewCenter
		};

		RestoreView( center, Zoom );
		Focus();
	}

	internal void NotifySelection()
	{
		Editor.OnCanvasSelection( _node, Selected );
	}

	/// <summary>
	/// Points this canvas at the same node in a rebuilt graph, keeping the
	/// view and selection - no widget churn, no flicker.
	/// </summary>
	public bool Rebind( Node node )
	{
		if ( node is not StateMachineNode machine || machine.Identifier != _node.Identifier )
			return false;

		_node = machine;
		_node.SyncStates();
		_liveState = null;
		_liveConduit = null;
		_liveGlobalFrom = null;
		_liveGlobalTo = null;
		_liveTransition = null;
		_liveTransitionProgress = 0.0f;

		Selected = Selected switch
		{
			StateMachineNode.State state => machine.States.FirstOrDefault( s => s.Name == state.Name ),
			StateMachineNode.Conduit conduit => machine.Conduits.FirstOrDefault( c => c.From == conduit.From && c.To == conduit.To ),
			SharedSelection shared => shared,
			_ => null
		};

		NotifySelection();
		SelectionChanged?.Invoke( Selected );
		Update();
		return true;
	}

	private void Changed()
	{
		_node.Update();
		Editor.MarkDirty();
		Update();
	}

	private bool CanLink( StateMachineNode.State from, StateMachineNode.State to ) =>
		from != to && !_node.Conduits.Any( c => c.From == from.Name && c.To == to.Name );

	private void AddState( Vector2 position, StateMachineNode.StateType type = StateMachineNode.StateType.BlendTree )
	{
		var baseName = type == StateMachineNode.StateType.Off ? "Off" : "State";
		var index = _node.States.Count + 1;
		while ( _node.States.Any( s => s.Name == $"{baseName} {index}" ) )
			index++;

		Editor.PushGraphUndo( "Add State" );
		var state = new StateMachineNode.State { Name = $"{baseName} {index}", Type = type, Position = Snap( position, true ) };

		if ( type == StateMachineNode.StateType.StateMachine )
			SeedStateMachine( state );

		_node.States.Add( state );
		_node.SyncStates();
		Changed();
		Editor.PushGraphRedo();
		Select( state );
	}

	private void CopyState( StateMachineNode.State state )
	{
		var clipboard = new StateClipboardData
		{
			State = state,
			OutgoingConduits = _node.Conduits.Where( conduit => conduit.From == state.Name ).ToList(),
		};

		var json = System.Text.Json.JsonSerializer.Serialize( clipboard );
		GraphClipboard.Copy( StateClipboardIdent, json );
	}

	private void CutState( StateMachineNode.State state )
	{
		CopyState( state );
		RemoveState( state );
	}

	private bool CanPasteState() => GraphClipboard.CanPaste( StateClipboardIdent );

	internal bool CanCutSelection => Selected is StateMachineNode.State;
	internal bool CanCopySelection => Selected is StateMachineNode.State;
	internal bool CanPasteSelection => CanPasteState();

	internal bool CutSelection()
	{
		if ( Selected is not StateMachineNode.State state )
			return false;

		CutState( state );
		return true;
	}

	internal bool CopySelection()
	{
		if ( Selected is not StateMachineNode.State state )
			return false;

		CopyState( state );
		return true;
	}

	internal bool PasteSelection()
	{
		if ( !CanPasteState() )
			return false;

		PasteState( FromCanvas( _mouse ) );
		return true;
	}

	private void PasteState( Vector2 position )
	{
		if ( !GraphClipboard.TryPaste( StateClipboardIdent, out var json ) )
			return;

		StateClipboardData clipboard;
		try
		{
			clipboard = System.Text.Json.JsonSerializer.Deserialize<StateClipboardData>( json );

			if ( clipboard?.State is null )
			{
				clipboard = new StateClipboardData
				{
					State = System.Text.Json.JsonSerializer.Deserialize<StateMachineNode.State>( json ),
				};
			}
		}
		catch ( System.Text.Json.JsonException exception )
		{
			Log.Warning( $"Could not paste AnimGraph state: {exception.Message}" );
			return;
		}

		var state = clipboard?.State;
		if ( state is null )
			return;

		var outgoingConduits = (clipboard.OutgoingConduits ?? [])
			.Where( conduit => _node.States.Any( target => target.Name == conduit.To ) )
			.ToList();
		var transitionOrder = state.TransitionOrder?.ToArray() ?? [];
		var copiedTransitions = outgoingConduits
			.SelectMany( conduit => conduit.Transitions )
			.GroupBy( transition => transition.Identifier )
			.ToDictionary( group => group.Key, group => group.First() );

		state.Graph ??= Data.CreateSubGraph<PoseResultNode>();
		state.LayerGraph ??= Data.CreateSubGraph<StateLayerDataNode>( GraphKind.LayerData );
		state.Graph.RegenerateNodeIdentifiers();
		state.LayerGraph.RegenerateNodeIdentifiers();
		state.Name = UniqueStateName( state.Name );
		state.Position = Snap( position, true );

		foreach ( var conduit in outgoingConduits )
		{
			conduit.From = state.Name;
			conduit.Graph ??= Data.CreateSubGraph( GraphKind.TransitionConduit );
			conduit.Graph.RegenerateNodeIdentifiers();
		}

		var transitionIdentifiers = copiedTransitions.ToDictionary(
			entry => entry.Key,
			entry => entry.Value.Identifier );
		var globalTransitionIdentifiers = _node.GlobalTransitions
			.Select( transition => transition.Identifier )
			.ToHashSet();
		var remappedTransitionOrder = new List<string>( transitionOrder.Length );

		foreach ( var identifier in transitionOrder )
		{
			if ( transitionIdentifiers.TryGetValue( identifier, out var remappedIdentifier ) )
				remappedTransitionOrder.Add( remappedIdentifier );
			else if ( globalTransitionIdentifiers.Contains( identifier ) )
				remappedTransitionOrder.Add( identifier );
		}

		state.TransitionOrder = remappedTransitionOrder;

		Editor.PushGraphUndo( "Paste State" );
		_node.States.Add( state );
		_node.Conduits.AddRange( outgoingConduits );
		_node.SyncStates();
		Changed();
		Editor.PushGraphRedo();
		Select( state );
	}

	private string UniqueStateName( string name )
	{
		var baseName = string.IsNullOrWhiteSpace( name ) ? "State" : name;
		if ( !_node.States.Any( state => state.Name.Equals( baseName, StringComparison.OrdinalIgnoreCase ) ) )
			return baseName;

		var index = 2;
		while ( _node.States.Any( state => state.Name.Equals( $"{baseName} {index}", StringComparison.OrdinalIgnoreCase ) ) )
			++index;

		return $"{baseName} {index}";
	}

	// A state machine state's graph is a state machine wired to the output
	private static void SeedStateMachine( StateMachineNode.State state )
	{
		if ( state.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault() is not { } output )
			return;

		var machine = new StateMachineNode { Position = output.Position - new Vector2( 300, 0 ) };
		state.Graph.AddNode( machine );
		output.Pose = new NodeInput { Identifier = machine.Identifier, Output = nameof( StateMachineNode.Result ) };
	}

	private void RemoveState( StateMachineNode.State state )
	{
		Editor.PushGraphUndo( "Remove State" );
		_node.Conduits.RemoveAll( c => c.From == state.Name || c.To == state.Name );
		_node.States.Remove( state );

		if ( _node.DefaultState == state.Name )
			_node.DefaultState = "";

		_node.SyncStates();
		Changed();
		Editor.PushGraphRedo();
		Select( null );
	}

	private void AddConduit( StateMachineNode.State from, StateMachineNode.State to )
	{
		Editor.PushGraphUndo( "Add Transition" );
		var conduit = new StateMachineNode.Conduit { From = from.Name, To = to.Name };
		conduit.Graph.AddNode( new TransitionNode { Position = new Vector2( 400, 0 ) } );
		_node.Conduits.Add( conduit );
		Changed();
		Editor.PushGraphRedo();
		Select( conduit );
	}

	private void RemoveConduit( StateMachineNode.Conduit conduit )
	{
		Editor.PushGraphUndo( "Remove Transition" );
		_node.Conduits.Remove( conduit );
		Changed();
		Editor.PushGraphRedo();
		Select( null );
	}

	private void ReverseConduit( StateMachineNode.Conduit conduit )
	{
		Editor.PushGraphUndo( "Reverse Transition" );
		(conduit.From, conduit.To) = (conduit.To, conduit.From);
		Changed();
		Editor.PushGraphRedo();
	}

	// Only convertible when the graph is exactly one state machine wired to the output
	internal static bool CanConvertToStateMachineState( StateMachineNode.State state ) =>
		state.Graph?.Nodes.Count() == 2
		&& state.Graph.Nodes.OfType<PoseResultNode>().FirstOrDefault() is { } output
		&& output.GetConnectedNode( output.Pose ) is StateMachineNode;

	internal void ConvertStateType( StateMachineNode.State state, StateMachineNode.StateType type )
	{
		Editor.PushGraphUndo( "Convert State" );
		state.Type = type;
		Changed();
		Editor.PushGraphRedo();
	}

	private void SetDefaultState( StateMachineNode.State state )
	{
		Editor.PushGraphUndo( "Set Default State" );
		_node.DefaultState = state.Name;
		Changed();
		Editor.PushGraphRedo();
	}

	protected override void Fit()
	{
		var min = new Vector2( float.MaxValue );
		var max = new Vector2( float.MinValue );

		foreach ( var state in _node.States )
		{
			var rect = StateRect( state );
			min = Vector2.Min( min, rect.TopLeft );
			max = Vector2.Max( max, rect.BottomRight );
		}

		if ( _node.States.Count == 0 )
		{
			min = new Vector2( -200 );
			max = new Vector2( 200 );
		}

		var span = Vector2.Max( max - min, new Vector2( 100 ) ) + 120;
		FrameView( (min + max) * 0.5f, MathF.Min( MathF.Min( Width / span.x, Height / span.y ), 1.25f ) );
	}

	private Vector2 Snap( Vector2 p, bool snap ) => snap
		? new Vector2( MathF.Round( p.x / GridStep ) * GridStep, MathF.Round( p.y / GridStep ) * GridStep )
		: p;

	protected override void OnMousePress( MouseEvent e )
	{
		Focus();

		if ( e.LeftMouseButton && HitButton( e.LocalPosition ) is { } button )
		{
			Select( button == 0 ? SharedSelection.EntryOverrides : SharedSelection.GlobalTransitions );

			e.Accepted = true;
			return;
		}

		if ( e.LeftMouseButton )
		{
			if ( HitState( e.LocalPosition ) is { } state )
			{
				Select( state );

				if ( e.HasAlt )
				{
					_linkSource = state;
					_mouse = e.LocalPosition;
				}
				else
				{
					_dragState = state;
					_dragOffset = state.Position - FromCanvas( e.LocalPosition );
					Editor.PushGraphUndo( "Move State", GraphChangeScope.EditorOnly );
				}
			}
			else
			{
				Select( HitTransition( e.LocalPosition ) );

				if ( Selected is null )
					StartPan( e.LocalPosition );
			}

			e.Accepted = true;
			return;
		}

		if ( e.RightMouseButton )
		{
			OpenContextMenu( e.LocalPosition );
			e.Accepted = true;
			return;
		}

		base.OnMousePress( e );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( !e.LeftMouseButton )
			return;

		if ( HitButton( e.LocalPosition ) is { } button )
		{
			var selection = button == 0 ? SharedSelection.EntryOverrides : SharedSelection.GlobalTransitions;
			Select( selection );
			Editor.NavigateInto( selection );
			e.Accepted = true;
			return;
		}

		if ( HitState( e.LocalPosition ) is { } state )
		{
			EditState( state );
			e.Accepted = true;
			return;
		}

		if ( HitTransition( e.LocalPosition ) is { } conduit )
		{
			EditConduit( conduit );
			e.Accepted = true;
			return;
		}

		AddState( FromCanvas( e.LocalPosition ) );
		e.Accepted = true;
	}

	private void EditState( StateMachineNode.State state )
	{
		if ( state.Type == StateMachineNode.StateType.Off )
			return;

		Select( state );
		Editor.NavigateInto( state );
	}

	private void EditConduit( StateMachineNode.Conduit conduit )
	{
		Select( conduit );
		Editor.NavigateInto( conduit.Graph );
	}

	private void EditGlobalTransitions()
	{
		_node.SyncStates();
		Select( SharedSelection.GlobalTransitions );
	}

	private void EditEntryOverrides()
	{
		_node.SyncStates();
		Select( SharedSelection.EntryOverrides );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		_mouse = e.LocalPosition;

		if ( _dragState is not null )
		{
			_dragState.Position = Snap( FromCanvas( e.LocalPosition ) + _dragOffset, !e.HasCtrl );
			Changed();
			e.Accepted = true;
			return;
		}

		base.OnMouseMove( e );
		if ( IsPanning )
			return;

		var hoverButton = HitButton( e.LocalPosition ) ?? -1;
		var hoverState = hoverButton >= 0 ? null : HitState( e.LocalPosition );
		var hoverConduit = hoverState is null && hoverButton < 0 ? HitTransition( e.LocalPosition ) : null;

		if ( hoverState != _hoverState || hoverConduit != _hoverConduit || hoverButton != _hoverButton || _linkSource is not null )
		{
			_hoverState = hoverState;
			_hoverConduit = hoverConduit;
			_hoverButton = hoverButton;
			ToolTip = (hoverButton, hoverState, hoverConduit) switch
			{
				(0, _, _) => DisplayInfo.ForType( typeof( EntryStateOverrideConditionsNode ) ).Description,
				(1, _, _) => DisplayInfo.ForType( typeof( GlobalTransitionNode ) ).Description,
				(_, not null, _) => DisplayInfo.ForType( typeof( StateMachineNode.State ) ).Description,
				(_, _, not null) => $"{hoverConduit.From} → {hoverConduit.To}\nTransition rules: {hoverConduit.Transitions.Count()}. Select to choose a rule; double-click to open their graph.",
				_ => null
			};
			Cursor = hoverState is not null || hoverConduit is not null || hoverButton >= 0 ? CursorShape.Finger : CursorShape.Arrow;
			Update();
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		if ( _dragState is not null )
		{
			_dragState = null;
			Editor.PushGraphRedo();
		}

		if ( _linkSource is not null )
		{
			if ( HitState( e.LocalPosition ) is { } target && CanLink( _linkSource, target ) )
				AddConduit( _linkSource, target );

			_linkSource = null;
			Update();
		}

		base.OnMouseReleased( e );
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		switch ( e.Key )
		{
			case KeyCode.Delete when Selected is StateMachineNode.State deletedState:
				RemoveState( deletedState );
				break;
			case KeyCode.Delete when Selected is StateMachineNode.Conduit conduit:
				RemoveConduit( conduit );
				break;
			default:
				base.OnKeyPress( e );
				break;
		}
	}

	private void OpenContextMenu( Vector2 local )
	{
		var menu = new Menu( this );

		if ( HitState( local ) is { } state )
		{
			Select( state );

			var editState = menu.AddOption( "Edit State", "animation", () => EditState( state ) );
			editState.Enabled = state.Type != StateMachineNode.StateType.Off;

			var isDefault = _node.States.IndexOf( state ) == _node.DefaultStateIndex;
			var setDefault = menu.AddOption( "Set as Default State", "star", () => SetDefaultState( state ) );
			setDefault.Enabled = !isDefault;

			if ( state.Type == StateMachineNode.StateType.BlendTree && CanConvertToStateMachineState( state ) )
				menu.AddOption( "Convert to State Machine State", "hub", () => ConvertStateType( state, StateMachineNode.StateType.StateMachine ) );

			if ( state.Type == StateMachineNode.StateType.StateMachine )
				menu.AddOption( "Convert to Blend Tree State", "account_tree", () => ConvertStateType( state, StateMachineNode.StateType.BlendTree ) );

			menu.AddOption( "Cut State", "content_cut", () => CutState( state ), "Ctrl+X" );
			menu.AddOption( "Copy State", "content_copy", () => CopyState( state ), "Ctrl+C" );

			var transitions = menu.AddMenu( "Add Transition To", "arrow_forward" );
			foreach ( var to in _node.States.Where( s => s != state ) )
			{
				var option = transitions.AddOption( to.Name, null, () => AddConduit( state, to ) );
				option.Enabled = CanLink( state, to );
			}
			transitions.Enabled = _node.States.Count > 1;

			menu.AddSeparator();
			menu.AddOption( "Delete State", "delete", () => RemoveState( state ) );
		}
		else if ( HitTransition( local ) is { } conduit )
		{
			Select( conduit );

			menu.AddOption( "Edit Transitions", "route", () => EditConduit( conduit ) );

			var reverse = menu.AddOption( "Reverse Direction", "swap_horiz", () => ReverseConduit( conduit ) );
			reverse.Enabled = FindState( conduit.To ) is { } from && FindState( conduit.From ) is { } to && CanLink( from, to );

			menu.AddSeparator();
			menu.AddOption( "Delete Transition", "delete", () => RemoveConduit( conduit ) );
		}
		else
		{
			var position = Snap( FromCanvas( local ), true );
			menu.AddOption( "Add State", "animation", () => AddState( position ) );
			menu.AddOption( "Add State Machine State", "hub", () => AddState( position, StateMachineNode.StateType.StateMachine ) );
			menu.AddOption( "Add Off State", "power_settings_new", () => AddState( position, StateMachineNode.StateType.Off ) );
			var paste = menu.AddOption( "Paste State", "content_paste", () => PasteState( position ), "Ctrl+V" );
			paste.Enabled = CanPasteState();
			menu.AddSeparator();
			menu.AddOption( "Entry Overrides", "login", EditEntryOverrides );
			menu.AddOption( "Global Transitions", "public", EditGlobalTransitions );
			menu.AddSeparator();
			menu.AddOption( "Fit View", "fit_screen", Fit );
		}

		menu.OpenAtCursor();
	}

	internal void UpdateLiveState()
	{
		if ( !Visible )
			return;

		var changed = false;
		if ( _liveStateInfo.Length != _node.States.Count )
		{
			_hasLiveStateInfo = new bool[_node.States.Count];
			_liveStateInfo = new GraphDebug.NodeInfo[_node.States.Count];
			_liveStateSyncTrackEvents = new GraphDebug.SyncEvent[_node.States.Count][];
			_liveStateSyncEventIDs = new string[_node.States.Count];
			changed = true;
		}

		for ( var i = 0; i < _node.States.Count; i++ )
		{
			if ( !LocalRect.IsInside( ToCanvas( StateRect( _node.States[i] ) ) ) )
				continue;

			var hasInfo = Editor.TryGetLiveStatePoseDebugInfo(
				_node,
				_node.States[i],
				out var info,
				out var syncTrackEvents,
				out var syncEventID );

			var eventsChanged = PoseDebug.UpdateEvents( ref _liveStateSyncTrackEvents[i], syncTrackEvents );
			if ( _hasLiveStateInfo[i] == hasInfo
				&& _liveStateInfo[i] == info
				&& !eventsChanged
				&& _liveStateSyncEventIDs[i] == syncEventID )
				continue;

			_hasLiveStateInfo[i] = hasInfo;
			_liveStateInfo[i] = info;
			_liveStateSyncEventIDs[i] = syncEventID;
			changed = true;
		}

		Editor.TryGetLiveStateMachineState( _node, out var liveState, out var liveFrom, out var liveTransition, out var liveTransitionProgress );
		var liveConduit = liveFrom is not null
			? _node.Conduits.FirstOrDefault( conduit => conduit.From == liveFrom.Name && conduit.To == liveState.Name )
			: null;
		var liveGlobalFrom = liveFrom is not null && liveConduit is null ? liveFrom : null;
		var liveGlobalTo = liveGlobalFrom is null ? null : liveState;

		if ( !ReferenceEquals( _liveState, liveState )
			|| !ReferenceEquals( _liveConduit, liveConduit )
			|| !ReferenceEquals( _liveGlobalFrom, liveGlobalFrom )
			|| !ReferenceEquals( _liveGlobalTo, liveGlobalTo )
			|| !ReferenceEquals( _liveTransition, liveTransition )
			|| _liveTransitionProgress != liveTransitionProgress )
		{
			_liveState = liveState;
			_liveConduit = liveConduit;
			_liveGlobalFrom = liveGlobalFrom;
			_liveGlobalTo = liveGlobalTo;
			_liveTransition = liveTransition;
			_liveTransitionProgress = liveTransitionProgress;
			changed = true;
		}

		if ( changed )
			Update();
	}

	protected override void PaintCanvas()
	{
		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground );
		Paint.DrawRect( LocalRect );

		PaintGrid();

		foreach ( var conduit in _node.Conduits )
			PaintTransition( conduit );

		PaintLiveGlobalTransition();
		PaintLinkPreview();

		for ( var i = 0; i < _node.States.Count; i++ )
			PaintState(
				_node.States[i],
				i == _node.DefaultStateIndex,
				ReferenceEquals( _liveState, _node.States[i] ),
				_hasLiveStateInfo.ElementAtOrDefault( i ),
				_liveStateInfo.ElementAtOrDefault( i ),
				_liveStateSyncTrackEvents.ElementAtOrDefault( i ) ?? [],
				_liveStateSyncEventIDs.ElementAtOrDefault( i ) ?? "" );

		PaintLiveTransitionOverlay();
		PaintOverlayButtons();
	}

	private int? HitButton( Vector2 local ) =>
		_entriesButton.IsInside( local ) ? 0 : _globalsButton.IsInside( local ) ? 1 : null;

	/// <summary>
	/// Always-visible shortcuts into the machine's shared pages, with a count
	/// of the transitions / overrides wired up in each.
	/// </summary>
	private void PaintOverlayButtons()
	{
		var entries = _node.States.Count( s => _node.EntryConditions?.ConditionFor( s.Name ).IsValid ?? false );
		var globals = _node.GlobalTransitions.Count( g => g.Condition.IsValid );

		_entriesButton = PaintOverlayButton( new Vector2( 12, 12 ), SharedSelection.EntryOverrides, entries, _hoverButton == 0 );
		_globalsButton = PaintOverlayButton( new Vector2( _entriesButton.Right + 8, 12 ), SharedSelection.GlobalTransitions, globals, _hoverButton == 1 );
	}

	private Rect PaintOverlayButton( Vector2 position, SharedSelection selection, int count, bool hovered )
	{
		var icon = selection.Icon;
		var label = selection.Title;
		var selected = Selected == selection;
		if ( count > 0 )
			label = $"{label} · {count}";

		Paint.SetDefaultFont( 8, 500 );
		var rect = new Rect( position, new Vector2( Paint.MeasureText( label ).x + 42, 26 ) );
		var active = count > 0;

		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground.Lighten( hovered ? 1.1f : 0.6f ).WithAlpha( 0.95f ) );
		Paint.DrawRect( rect, 13 );

		Paint.ClearBrush();
		Paint.SetPen( selected ? Theme.Red : active ? WireColor.WithAlpha( hovered ? 0.9f : 0.5f ) : Theme.Text.WithAlpha( hovered ? 0.4f : 0.15f ), selected ? 2 : 1.1f );
		Paint.DrawRect( rect, 13 );

		Paint.SetPen( active ? WireColor : Theme.Text.WithAlpha( hovered ? 0.9f : 0.6f ) );
		Paint.DrawIcon( rect.Shrink( 12, 0 ), icon, 14, TextFlag.LeftCenter );

		Paint.SetPen( Theme.Text.WithAlpha( hovered || active ? 0.95f : 0.65f ) );
		Paint.DrawText( rect.Shrink( 12, 0 ), label, TextFlag.RightCenter );

		return rect;
	}

	private void PaintGrid()
	{
		var step = GridStep * 4 * Zoom;
		while ( step < 24 ) step *= 2;

		var origin = ToCanvas( Vector2.Zero );

		Paint.ClearBrush();
		Paint.SetPen( Theme.WindowBackground.Lighten( 0.3f ) );

		for ( var x = origin.x % step; x < Width; x += step )
			Paint.DrawLine( new Vector2( x, 0 ), new Vector2( x, Height ) );

		for ( var y = origin.y % step; y < Height; y += step )
			Paint.DrawLine( new Vector2( 0, y ), new Vector2( Width, y ) );
	}

	private void PaintArrowLine( Vector2 a, Vector2 b, Color color, float width, bool blocked = false )
	{
		Paint.ClearBrush();
		Paint.SetPen( color, width );
		Paint.DrawLine( a, b );

		var dir = (b - a).Normal;
		var mid = (a + b) * 0.5f;
		var size = MathF.Max( 13 * Zoom, 9 );

		if ( blocked )
		{
			// X at the midpoint — this conduit has no transitions, so it blocks
			var d1 = (dir + dir.Perpendicular).Normal * size * 0.5f;
			var d2 = (dir - dir.Perpendicular).Normal * size * 0.5f;

			Paint.SetPen( color, MathF.Max( width, 2.5f ) );
			Paint.DrawLine( mid - d1, mid + d1 );
			Paint.DrawLine( mid - d2, mid + d2 );
			return;
		}

		// Direction chevron at the midpoint, Mecanim style
		Paint.ClearPen();
		Paint.SetBrush( color );
		Paint.DrawArrow( mid - dir * size * 0.5f, mid + dir * size * 0.5f, size );
	}

	private static readonly Color WireColor = Color.Parse( "#4fc3f7" ) ?? default;
	private static readonly Color TransitionColor = Color.Parse( "#c0c0c0" ) ?? default;

	private void PaintTransition( StateMachineNode.Conduit conduit )
	{
		if ( !TransitionLine( conduit, out var start, out var end ) )
			return;

		var a = ToCanvas( start );
		var b = ToCanvas( end );

		var selected = ReferenceEquals( Selected, conduit );
		var hovered = conduit == _hoverConduit;
		var live = conduit == _liveConduit;
		var hasTransitions = conduit.HasTransitions;

		var color = hovered ? SelectionColor
			: selected ? SelectionColor.Darken( 0.2f )
			: live ? Color.Cyan
			: hasTransitions ? TransitionColor
			: Theme.Red;
		var width = (live ? 4 : hovered ? 4.5f : 3) * Zoom;
		PaintArrowLine( a, b, color, width, blocked: !hasTransitions );
		PaintTransitionEndpoints( a, b, color, width );

		var count = conduit.Transitions.Count();
		if ( count > 1 )
		{
			var scale = MathF.Max( Zoom, 1f );
			var badge = TransitionBadgeRect( a, b, count );
			Paint.SetBrush( Theme.WindowBackground );
			Paint.SetPen( color, scale );
			Paint.DrawRect( badge, 6 * scale );
			Paint.SetDefaultFont( 8 * scale, 600 );
			Paint.DrawText( badge, count.ToString(), TextFlag.Center );
		}
	}

	private Rect TransitionBadgeRect( Vector2 a, Vector2 b, int count )
	{
		var scale = MathF.Max( Zoom, 1f );
		var size = new Vector2( Math.Max( 24, count.ToString().Length * 7 + 12 ), 20 ) * scale;
		var center = (a + b) * 0.5f + (b - a).Normal * 24 * scale;
		return new Rect( center - size * 0.5f, size );
	}

	private void PaintLiveGlobalTransition()
	{
		if ( _liveGlobalFrom is null
			|| _liveGlobalTo is null
			|| !TransitionLine( _liveGlobalFrom, _liveGlobalTo, out var start, out var end ) )
			return;

		var a = ToCanvas( start );
		var b = ToCanvas( end );
		PaintArrowLine( a, b, Color.Cyan, 4 * Zoom );
		PaintTransitionEndpoints( a, b, Color.Cyan, 4 * Zoom );
	}

	private static void PaintTransitionEndpoints( Vector2 a, Vector2 b, Color color, float width )
	{
		var radius = MathF.Max( width * 2.2f, 6.5f );

		Paint.ClearPen();
		Paint.SetBrush( color );
		Paint.DrawCircle( a, radius );
		Paint.DrawCircle( b, radius );
	}

	private void PaintLiveTransitionOverlay()
	{
		Vector2 start;
		Vector2 end;

		if ( _liveConduit is not null )
		{
			if ( !TransitionLine( _liveConduit, out start, out end ) )
				return;
		}
		else if ( _liveGlobalFrom is not null && _liveGlobalTo is not null )
		{
			if ( !TransitionLine( _liveGlobalFrom, _liveGlobalTo, out start, out end ) )
				return;
		}
		else
		{
			return;
		}

		PaintLiveTransitionFeedback( ToCanvas( start ), ToCanvas( end ) );
	}

	private void PaintLiveTransitionFeedback( Vector2 a, Vector2 b )
	{
		var progress = _liveTransitionProgress.Clamp( 0.0f, 1.0f );
		var current = a + (b - a) * progress;
		var direction = (b - a).Normal;
		var tracerSize = MathF.Max( 15 * Zoom, 10 );

		Paint.ClearPen();
		Paint.SetBrush( Color.Cyan.WithAlpha( 0.18f ) );
		Paint.DrawCircle( current, tracerSize );
		Paint.SetBrush( Color.Cyan.WithAlpha( 0.35f ) );
		Paint.DrawCircle( current, tracerSize * 0.7f );
		Paint.SetBrush( Color.Cyan );
		Paint.DrawArrow( current - direction * tracerSize * 0.65f, current + direction * tracerSize * 0.35f, tracerSize );
		Paint.SetBrush( Color.White );
		Paint.DrawArrow( current - direction * tracerSize * 0.5f, current + direction * tracerSize * 0.25f, tracerSize * 0.65f );

		if ( Zoom < 0.45f )
			return;

		var name = string.IsNullOrWhiteSpace( _liveTransition?.TransitionName )
			? "Transition"
			: _liveTransition.TransitionName;
		var global = _liveTransition is GlobalTransitionNode ? "Global · " : "";
		var label = $"{global}{name} · {progress * 100:0}%";

		Paint.SetDefaultFont( 8 * Zoom, 500 );
		var labelSize = new Vector2( Paint.MeasureText( label ).x + 16 * Zoom, 22 * Zoom );
		var center = (a + b) * 0.5f + direction.Perpendicular * 18 * Zoom;
		var rect = new Rect( center - labelSize * 0.5f, labelSize );

		Paint.ClearPen();
		Paint.SetBrush( Theme.WindowBackground.WithAlpha( 0.95f ) );
		Paint.DrawRect( rect, 11 * Zoom );
		Paint.ClearBrush();
		Paint.SetPen( Color.Cyan.WithAlpha( 0.85f ), Zoom );
		Paint.DrawRect( rect, 11 * Zoom );
		Paint.SetPen( Theme.Text );
		Paint.DrawText( rect, label, TextFlag.Center );
	}

	private void PaintLinkPreview()
	{
		if ( _linkSource is null )
			return;

		var target = HitState( _mouse );
		var valid = target is not null && CanLink( _linkSource, target );

		var color = target is null ? Color.White : valid ? Theme.Green : Theme.Red;
		var a = ToCanvas( ClampToRect( StateRect( _linkSource ), FromCanvas( _mouse ) ) );
		var b = target is not null && valid ? ToCanvas( ClampToRect( StateRect( target ), _linkSource.Position ) ) : _mouse;

		if ( a.Distance( b ) < 16 )
			return;

		PaintArrowLine( a, b, color, 2 );
	}

	private void PaintState(
		StateMachineNode.State state,
		bool isDefault,
		bool isLiveState,
		bool hasLiveInfo,
		GraphDebug.NodeInfo liveInfo,
		IReadOnlyList<GraphDebug.SyncEvent> syncTrackEvents,
		string syncEventID )
	{
		var rect = ToCanvas( StateRect( state ) );
		var titleRect = new Rect( rect.Left, rect.Top, rect.Width, 44 * Zoom );
		var selected = ReferenceEquals( Selected, state );
		var hovered = state == _hoverState || state == _linkSource;
		var isOff = state.Type == StateMachineNode.StateType.Off;
		var primary = isOff ? Color.Gray : isDefault ? Theme.Green : Theme.Blue;
		var radius = 5 * Zoom;

		if ( hasLiveInfo && liveInfo.IsInitialized && !liveInfo.IsValid )
		{
			Paint.ClearBrush();
			Paint.SetPen( Color.Red, 2.0f * Zoom );
			Paint.DrawRect( rect.Grow( 4 * Zoom ), radius + 4 * Zoom );
		}

		Paint.ClearPen();
		Paint.SetBrush( Color.Black.WithAlpha( 0.3f ) );
		Paint.DrawRect( rect + new Vector2( 0, 3 * Zoom ), radius );

		Paint.SetBrush( primary.Darken( hovered || selected ? 0.5f : 0.6f ) );
		Paint.DrawRect( rect, radius );

		Paint.ClearBrush();
		var edgeColor = selected ? SelectionColor
			: isLiveState ? Color.Cyan
			: primary.WithAlpha( hovered ? 0.9f : 0.5f );
		var edgeWidth = isLiveState ? 5 : selected ? 2 : 1.2f;
		Paint.SetPen( edgeColor, edgeWidth * Zoom );
		Paint.DrawRect( rect, radius );

		var inner = titleRect.Shrink( 9 * Zoom, 0 );

		var icon = isOff ? "power_settings_new"
			: state.Type == StateMachineNode.StateType.StateMachine ? "hub"
			: isDefault ? "play_arrow" : "animation";

		Paint.ClearBrush();
		Paint.SetPen( primary.Lighten( 0.7f ).WithAlpha( 0.8f ) );
		Paint.DrawIcon( inner, icon, 15 * Zoom, TextFlag.LeftCenter );

		Paint.SetDefaultFont( 8 * Zoom, 500 );
		Paint.SetPen( Theme.Text.WithAlpha( hovered || selected ? 1f : 0.9f ) );
		Paint.DrawText( titleRect, state.Name );

		if ( _node.EntryConditions?.ConditionFor( state.Name ).IsValid ?? false )
		{
			Paint.SetPen( Theme.Yellow.WithAlpha( 0.9f ) );
			Paint.DrawIcon( inner, "login", 13 * Zoom, TextFlag.RightCenter );
		}

		var debugRect = new Rect(
			rect.Left + PoseDebug.Margin * Zoom,
			titleRect.Bottom,
			rect.Width - PoseDebug.Margin * 2.0f * Zoom,
			PoseDebug.Height * Zoom );

		PoseDebug.PaintDebugInfo(
			debugRect,
			hasLiveInfo && liveInfo.IsActive && liveInfo.Kind == GraphValueKind.Pose,
			liveInfo,
			syncTrackEvents,
			syncEventID,
			Zoom );
	}
}
