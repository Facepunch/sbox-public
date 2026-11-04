using Editor.NodeEditor;
using Sandbox.Animation;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph;

internal interface IReorderableInputNode
{
	bool CanReorderInput( IPlugIn input );
	void ReorderInput( IPlugIn input, IPlugIn target );
}

/// <summary>
/// Base class for all AnimGraph nodes. Uses [Input]/[Output] attributes
/// on properties for automatic plug discovery via reflection.
/// </summary>
public abstract class Node : INode, INodeHighlight
{
	private Vector2 _expandSize;

	[Hide, Browsable( false )]
	public string Identifier { get; set; }

	[Hide, Browsable( false )]
	public Vector2 Position { get; set; }

	public event Action Changed;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual DisplayInfo DisplayInfo => _displayInfo;

	private DisplayInfo _displayInfo;

	[JsonIgnore, Hide, Browsable( false )]
	public IGraph Graph { get; set; }

	/// <summary>
	/// The root graph that owns the model and parameters.
	/// </summary>
	[JsonIgnore, Hide, Browsable( false )]
	public Data Root => (Graph as Data)?.Root;

	[JsonIgnore, Hide, Browsable( false )]
	public IEnumerable<IPlugIn> Inputs => _propertyInputs.Concat( DynamicInputs );

	[JsonIgnore, Hide, Browsable( false )]
	public IEnumerable<IPlugOut> Outputs => _propertyOutputs.Concat( DynamicOutputs );

	/// <summary>
	/// Extra inputs beyond the reflected [Input] properties, for nodes with a
	/// variable number of connections (e.g. blend space points).
	/// </summary>
	[JsonIgnore, Hide, Browsable( false )]
	protected virtual IEnumerable<IPlugIn> DynamicInputs => [];

	/// <summary>
	/// Extra outputs beyond the reflected [Output] properties, for nodes with a
	/// variable number of outputs (e.g. groups).
	/// </summary>
	[JsonIgnore, Hide, Browsable( false )]
	protected virtual IEnumerable<IPlugOut> DynamicOutputs => [];

	/// <summary>
	/// Nested graphs owned by this node (e.g. state machine states and conditions).
	/// </summary>
	[JsonIgnore, Hide, Browsable( false )]
	public virtual IEnumerable<Data> SubGraphs => [];

	[JsonIgnore, Hide, Browsable( false )]
	public virtual Data SecondaryGraph => null;

	/// <summary>
	/// The source node connected to an input, following reroutes, or null.
	/// </summary>
	public Node GetConnectedNode( NodeInput input )
	{
		var graph = Graph as Data;
		var node = input.IsValid ? graph?.FindNode( input.Identifier ) : null;
		return node is RerouteNode reroute ? reroute.SourceOutput?.Node as Node : node;
	}

	public IPlugOut GetConnectedOutput( NodeInput input )
	{
		if ( !input.IsValid || Graph is not Data graph )
			return null;

		return graph.FindNode( input.Identifier )?.Outputs.FirstOrDefault( output => output.Identifier == input.Output );
	}

	private List<IPlugIn> _propertyInputs;
	private List<IPlugOut> _propertyOutputs;
	private GraphValueKind _outputKind;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual bool CanClone => true;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual bool CanRemove => true;

	/// <summary>
	/// True when the graph holds another node of the same type. Lets graph-managed
	/// singletons that normally can't be removed delete stray duplicates.
	/// </summary>
	protected bool HasDuplicate => Graph is { } graph && graph.Nodes.Count( node => node.GetType() == GetType() ) > 1;

	[JsonIgnore, Hide, Browsable( false )]
	public Vector2 ExpandSize
	{
		get => _expandSize;
		set => _expandSize = value;
	}

	[JsonIgnore, Hide, Browsable( false )]
	public virtual bool AutoSize => true;

	[JsonIgnore, Hide, Browsable( false )]
	public string ErrorMessage => null;

	[JsonIgnore, Hide, Browsable( false )]
	public bool IsReachable => true;

	[JsonIgnore, Hide, Browsable( false )]
	public bool HasTitleBar => true;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual Pixmap Thumbnail => null;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual Color PrimaryColor { get; } = Color.Lerp( new Color( 0.7f, 0.7f, 0.7f ), Theme.Blue, 0.1f );

	[JsonIgnore, Hide, Browsable( false )]
	public bool HasLiveDebugInfo { get; private set; }

	[JsonIgnore, Hide, Browsable( false )]
	public GraphDebug.NodeInfo LiveDebugInfo { get; private set; }

	[JsonIgnore, Hide, Browsable( false )]
	public bool HasInvalidLiveHighlightOverride { get; private set; }

	[JsonIgnore, Hide, Browsable( false )]
	public bool IsLive => HasLiveDebugInfo && LiveDebugInfo.IsActive;

	[JsonIgnore, Hide, Browsable( false )]
	public virtual GraphValueKind OutputKind => _outputKind;

	[JsonIgnore, Hide, Browsable( false )]
	public bool HasPoseOutput => OutputKind == GraphValueKind.Pose;

	[JsonIgnore, Hide, Browsable( false )]
	public bool HasValueOutput => ValueDebug.IsSupported( OutputKind );

	/// <summary>
	/// True when this node has anything to show in the Properties Debug section.
	/// Nodes adding their own debug widgets without a pose/value output should override this too.
	/// </summary>
	internal virtual bool HasDebugContent => HasPoseOutput || HasValueOutput;

	/// <summary>
	/// Adds widgets to the Properties dock's Debug section. The base implementation shows
	/// the live pose or value readout; override to add node-specific widgets (e.g. a blend
	/// preview), calling <c>base.BuildDebugWidgets</c> to keep the default readout.
	/// </summary>
	internal virtual void BuildDebugWidgets( Widget parent )
	{
		if ( HasPoseOutput )
			PoseDebug.BuildDebugWidgets( parent, this );
		else if ( HasValueOutput )
			ValueDebug.BuildDebugWidgets( parent, this );
	}

	/// <summary>
	/// A label to show in place of an output pin's name (e.g. a parameter node showing its
	/// live value), or null to keep the pin's normal name.
	/// </summary>
	internal virtual string LiveOutputLabel( string identifier ) => null;

	internal bool IsPoseLive => HasLiveDebugInfo
		&& LiveDebugInfo.IsActive
		&& LiveDebugInfo.Kind == GraphValueKind.Pose;

	[JsonIgnore, Hide, Browsable( false )]
	public GraphDebug.SyncEvent[] LiveSyncTrackEvents => _liveSyncTrackEvents;

	private GraphDebug.SyncEvent[] _liveSyncTrackEvents = [];

	[JsonIgnore, Hide, Browsable( false )]
	public string LiveSyncEventID { get; private set; } = "";

	[JsonIgnore, Hide, Browsable( false )]
	public bool HasLiveDebugValue { get; private set; }

	[JsonIgnore, Hide, Browsable( false )]
	public ValueDebugInfo LiveDebugValue { get; private set; }

	[JsonIgnore, Hide, Browsable( false )]
	public Color HighlightOutlineColor
	{
		get
		{
			if ( HasInvalidLiveHighlightOverride )
				return Color.Red;

			if ( !HasLiveDebugInfo )
				return default;

			if ( LiveDebugInfo.IsInitialized && !LiveDebugInfo.IsValid )
				return Color.Red;

			return LiveDebugInfo.IsActive ? new Color( 50f / 255f, 205f / 255f, 50f / 255f ) : default;
		}
	}

	[Hide, Browsable( false )]
	public Dictionary<string, float> HandleOffsets { get; set; } = new();

	public Node()
	{
		Identifier = Guid.NewGuid().ToString();
		RefreshMetadata();
	}

	internal void RefreshMetadata()
	{
		_displayInfo = DisplayInfo.For( this );
		DiscoverPlugs();
	}

	public void Update() => Changed?.Invoke();
	public virtual NodeUI CreateUI( GraphView view ) => new GraphNodeUI( view, this );
	public Color GetPrimaryColor( GraphView view ) => PrimaryColor;

	public bool SetLiveDebugInfo(
		bool hasInfo,
		GraphDebug.NodeInfo info,
		GraphDebug.SyncEvent[] syncTrackEvents = null,
		string syncEventID = "",
		bool hasDebugValue = false,
		ValueDebugInfo debugValue = default,
		bool forceInvalidHighlight = false )
	{
		syncTrackEvents ??= [];
		var eventsChanged = PoseDebug.UpdateEvents( ref _liveSyncTrackEvents, syncTrackEvents );

		if ( HasLiveDebugInfo == hasInfo
			&& LiveDebugInfo == info
			&& !eventsChanged
			&& LiveSyncEventID == syncEventID
			&& HasLiveDebugValue == hasDebugValue
			&& LiveDebugValue == debugValue
			&& HasInvalidLiveHighlightOverride == forceInvalidHighlight )
			return false;

		HasLiveDebugInfo = hasInfo;
		LiveDebugInfo = info;
		LiveSyncEventID = syncEventID;
		HasLiveDebugValue = hasDebugValue;
		LiveDebugValue = debugValue;
		HasInvalidLiveHighlightOverride = forceInvalidHighlight;
		return true;
	}

	public virtual void OnPaint( Rect rect ) { }

	public virtual void OnDoubleClick( MouseEvent e ) { }
	public virtual void OnFrame() { }
	public virtual Menu CreateContextMenu( NodeUI node ) => null;

	protected void ReorderInput<T>( IPlugIn input, IPlugIn target, List<T> values, List<IPlugIn> plugs )
	{
		var sourceIndex = plugs.IndexOf( input );
		var targetIndex = plugs.IndexOf( target );
		if ( sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex )
			return;

		var value = values[sourceIndex];
		values.RemoveAt( sourceIndex );
		values.Insert( targetIndex, value );

		var plug = plugs[sourceIndex];
		plugs.RemoveAt( sourceIndex );
		plugs.Insert( targetIndex, plug );

		for ( var i = 0; i < plugs.Count; i++ )
			((AnimPlugIn)plugs[i]).Info.Reindex( i );

		Update();
	}

	private sealed class GraphNodeUI : NodeUI
	{
		private readonly Node _node;
		private readonly VariationBadge _variationBadge;
		private Dictionary<PlugIn, ReorderHandle> _reorderHandles;
		private PlugIn _reorderSource;
		private PlugIn _reorderTarget;

		protected override float TitleAccessoryWidth => Node is VariationDataNode ? 20f : 0f;

		public GraphNodeUI( GraphView view, Node node ) : base( view, node )
		{
			_node = node;

			if ( node is VariationDataNode variationNode )
			{
				_variationBadge = new VariationBadge( this, variationNode )
				{
					Position = new Vector2( Size.x - TitleAccessoryWidth - 4f, 0 ),
					Size = new Vector2( TitleAccessoryWidth, TitleHeight )
				};
			}
		}

		protected override void OnDestroy()
		{
			// Native child teardown does not call the managed OnDestroy hook.
			_variationBadge?.Destroy();
			base.OnDestroy();
		}

		protected override void Layout()
		{
			base.Layout();

			if ( _variationBadge is not null )
				_variationBadge.Position = new Vector2( Size.x - TitleAccessoryWidth - 4f, 0 );

			if ( Node is not IReorderableInputNode reorderable )
				return;

			_reorderHandles ??= new();
			var orderedInputs = Inputs
				.Where( input => reorderable.CanReorderInput( input.Inner ) )
				.ToArray();
			var addedHeight = 0f;

			while ( orderedInputs.FirstOrDefault( input => Outputs.Any( output =>
				input.Position.y < output.Position.y + output.Size.y
				&& output.Position.y < input.Position.y + input.Size.y ) ) is { } overlappingInput )
			{
				foreach ( var input in Inputs.Where( input => input.Position.y >= overlappingInput.Position.y ) )
					input.Position += Vector2.Down * 24f;

				addedHeight += 24f;
			}

			if ( addedHeight > 0f )
			{
				PrepareGeometryChange();
				Size = Size.WithY( Size.y + addedHeight );
			}

			foreach ( var input in orderedInputs )
			{
				if ( !_reorderHandles.TryGetValue( input, out var handle ) )
				{
					handle = new ReorderHandle( this, input );
					_reorderHandles.Add( input, handle );
				}

				handle.Position = new Vector2( Size.x - 24f, input.Position.y + 2f );
				handle.Size = new Vector2( 20f, input.Size.y - 4f );
			}

			foreach ( var (input, handle) in _reorderHandles.ToArray() )
			{
				if ( orderedInputs.Contains( input ) )
					continue;

				handle.Destroy();
				_reorderHandles.Remove( input );
			}
		}

		protected override void OnPaint()
		{
			base.OnPaint();

			if ( _reorderTarget is not null )
			{
				Paint.ClearPen();
				Paint.SetBrush( SelectionOutline.WithAlpha( 0.15f ) );
				Paint.DrawRect( new Rect( 4f, _reorderTarget.Position.y, Size.x - 8f, _reorderTarget.Size.y ), 2f );
			}
		}

		private void BeginInputReorder( PlugIn source )
		{
			_reorderSource = source;
			_reorderTarget = source;
			Update();
		}

		private void UpdateInputReorder( Vector2 scenePosition )
		{
			if ( _reorderSource is null || Node is not IReorderableInputNode reorderable )
				return;

			var target = Inputs
				.Where( x => reorderable.CanReorderInput( x.Inner ) )
				.MinBy( x => MathF.Abs( x.ConnectionPosition.y - scenePosition.y ) );

			if ( target == _reorderTarget )
				return;

			_reorderTarget = target;
			Update();
		}

		private void EndInputReorder()
		{
			var source = _reorderSource;
			var target = _reorderTarget;
			_reorderSource = null;
			_reorderTarget = null;

			if ( source is null || target is null || source == target || Node is not IReorderableInputNode reorderable )
			{
				Update();
				return;
			}

			Graph.PushUndo( "Reorder Inputs" );
			reorderable.ReorderInput( source.Inner, target.Inner );
			Graph.PushRedo();
			Update();
		}

		private sealed class VariationBadge : GraphicsItem
		{
			private readonly VariationDataNode _node;
			private readonly Data _root;
			private bool _hasOverride;

			public VariationBadge( GraphNodeUI owner, VariationDataNode node ) : base( owner )
			{
				_node = node;
				_root = node.Root;
				HoverEvents = true;
				ToolTip = "Not overridden in this variation.";
				_node.OverridesChanged += Refresh;
				if ( _root is not null )
					_root.SelectedVariationChanged += Refresh;
				Refresh();
			}

			protected override void OnDestroy()
			{
				_node.OverridesChanged -= Refresh;
				if ( _root is not null )
					_root.SelectedVariationChanged -= Refresh;
				base.OnDestroy();
			}

			private void Refresh()
			{
				var hasOverride = _root is not null && _node.HasVariationOverride( _root.SelectedVariationID );
				if ( _hasOverride == hasOverride )
					return;

				_hasOverride = hasOverride;
				ToolTip = hasOverride ? "Overridden in this variation." : "Not overridden in this variation.";
				Update();
			}

			protected override void OnPaint()
			{
				Paint.SetPen( _hasOverride ? Theme.Green : Theme.TextControl.WithAlpha( Paint.HasMouseOver ? 0.9f : 0.5f ) );
				Paint.DrawIcon( LocalRect, "call_split", 14f, TextFlag.Center );

				if ( _hasOverride )
				{
					Paint.ClearPen();
					Paint.SetBrush( Theme.Green );
					Paint.DrawRect( new Rect( Size.x - 6f, Size.y - 7f, 4f, 4f ), 2f );
				}
			}

			protected override void OnMousePressed( GraphicsMouseEvent e ) => e.Accepted = false;
		}

		private sealed class ReorderHandle : GraphicsItem
		{
			private readonly GraphNodeUI _owner;
			private readonly PlugIn _plug;
			private bool _dragging;

			public ReorderHandle( GraphNodeUI owner, PlugIn plug ) : base( owner )
			{
				_owner = owner;
				_plug = plug;
				ZIndex = 3;
				HoverEvents = true;
				Cursor = CursorShape.SizeV;
				ToolTip = "Drag to reorder";
			}

			protected override void OnPaint()
			{
				if ( Paint.HasMouseOver || _dragging )
				{
					Paint.ClearPen();
					Paint.SetBrush( _owner.SelectionOutline.WithAlpha( 0.2f ) );
					Paint.DrawRect( LocalRect, 3f );
				}

				Paint.SetPen( Theme.TextControl.WithAlpha( Paint.HasMouseOver || _dragging ? 1f : 0.55f ) );
				Paint.DrawIcon( LocalRect, "drag_handle", 14f, TextFlag.Center );
			}

			protected override void OnMousePressed( GraphicsMouseEvent e )
			{
				if ( !e.LeftMouseButton )
				{
					base.OnMousePressed( e );
					return;
				}

				e.Accepted = true;
				_dragging = true;
				_owner.BeginInputReorder( _plug );
				Update();
			}

			protected override void OnMouseMove( GraphicsMouseEvent e )
			{
				if ( !_dragging )
				{
					base.OnMouseMove( e );
					return;
				}

				e.Accepted = true;
				_owner.UpdateInputReorder( e.ScenePosition );
			}

			protected override void OnMouseReleased( GraphicsMouseEvent e )
			{
				if ( !_dragging )
				{
					base.OnMouseReleased( e );
					return;
				}

				e.Accepted = true;
				_dragging = false;
				_owner.EndInputReorder();
				Update();
			}
		}
	}

	/// <summary>
	/// Discovers [Input] and [Output] properties via reflection and creates plugs.
	/// </summary>
	private void DiscoverPlugs()
	{
		var inputs = new List<IPlugIn>();
		var outputs = new List<IPlugOut>();

		foreach ( var prop in GetType().GetProperties() )
		{
			if ( prop.GetCustomAttribute<InputAttribute>() is { } inputAttr )
			{
				var info = new AnimPlugInfo( prop );
				inputs.Add( new AnimPlugIn( this, info, inputAttr.Type ?? typeof( object ) ) );
			}

			if ( prop.GetCustomAttribute<OutputAttribute>() is { } outputAttr )
			{
				var info = new AnimPlugInfo( prop );
				outputs.Add( new AnimPlugOut( this, info, outputAttr.Type ?? typeof( object ) ) );
			}
		}

		_propertyInputs = inputs;
		_propertyOutputs = outputs;
		_outputKind = GetOutputKind( Outputs.FirstOrDefault()?.Type );
	}

	private static GraphValueKind GetOutputKind( Type type ) => type switch
	{
		_ when type == typeof( BoolLink ) => GraphValueKind.Bool,
		_ when type == typeof( IDLink ) => GraphValueKind.Id,
		_ when type == typeof( FloatLink ) => GraphValueKind.Float,
		_ when type == typeof( VectorLink ) => GraphValueKind.Vector,
		_ when type == typeof( TargetLink ) => GraphValueKind.Target,
		_ when type == typeof( PoseLink ) => GraphValueKind.Pose,
		_ => GraphValueKind.Unknown
	};

	[AttributeUsage( AttributeTargets.Property )]
	public class InputAttribute : Attribute
	{
		public Type Type { get; }
		public InputAttribute( Type type = null ) => Type = type;
	}

	[AttributeUsage( AttributeTargets.Property )]
	public class OutputAttribute : Attribute
	{
		public Type Type { get; }
		public OutputAttribute( Type type = null ) => Type = type;
	}


	internal virtual GraphBuilder.Node Compile( Compiler compiler )
	{
		compiler.Error( $"{GetType().Name} does not implement compilation", this );
		return compiler.ConstDefault( Outputs.OfType<AnimPlugOut>().FirstOrDefault()?.Type ?? typeof( PoseLink ) );
	}

}

/// <summary>
/// Metadata for a plug — either wrapping a NodeInput property, or backed by
/// custom accessors for dynamic plugs.
/// </summary>
public class AnimPlugInfo
{
	public string Name => _namePrefix is null ? _name : $"{_namePrefix}{Index}";
	public Type Type { get; }
	public PropertyInfo Property { get; }
	public int Index { get; private set; }

	private readonly string _name;
	private readonly string _namePrefix;
	private readonly Func<string> _title;
	private readonly Func<Node, NodeInput> _getInput;
	private readonly Action<Node, NodeInput> _setInput;

	public DisplayInfo DisplayInfo
	{
		get
		{
			var info = DisplayInfo.ForMember( Type );
			info.Name = _title();
			return info;
		}
	}

	public AnimPlugInfo( PropertyInfo property )
	{
		Property = property;
		_name = property.Name;
		Type = property.PropertyType;

		var title = property.GetCustomAttribute<TitleAttribute>()?.Value ?? property.Name;
		_title = () => title;

		if ( property.PropertyType == typeof( NodeInput ) )
		{
			_getInput = node => (NodeInput)property.GetValue( node );
			_setInput = ( node, value ) => property.SetValue( node, value );
		}
	}

	public AnimPlugInfo( string name, Func<string> title, Func<Node, NodeInput> getInput, Action<Node, NodeInput> setInput )
	{
		_name = name;
		Type = typeof( NodeInput );
		_title = title;
		_getInput = getInput;
		_setInput = setInput;
	}

	public AnimPlugInfo( string namePrefix, int index, Func<int, string> title, Func<Node, int, NodeInput> getInput, Action<Node, int, NodeInput> setInput )
	{
		_namePrefix = namePrefix;
		Index = index;
		Type = typeof( NodeInput );
		_title = () => title( Index );
		_getInput = node => getInput( node, Index );
		_setInput = ( node, value ) => setInput( node, Index, value );
	}

	public void Reindex( int index ) => Index = index;

	public bool CanConnect => _getInput is not null;
	public NodeInput GetInput( Node node ) => _getInput?.Invoke( node ) ?? default;
	public void SetInput( Node node, NodeInput value ) => _setInput?.Invoke( node, value );
}

/// <summary>
/// Base plug record shared by inputs and outputs.
/// </summary>
public record AnimPlug( Node Node, AnimPlugInfo Info, Type ValueType ) : IPlug
{
	public Type Type => Node is RerouteNode reroute ? reroute.SourceOutput?.Type ?? typeof( object ) : ValueType;

	INode IPlug.Node => Node;
	public string Identifier => Info.Name;
	public virtual DisplayInfo DisplayInfo => Info.DisplayInfo;
	public bool ShowLabel => true;
	public bool AllowStretch => true;
	public bool ShowConnection => true;
	public bool InTitleBar => false;
	public bool IsReachable => true;
	public string ErrorMessage => null;

	public ValueEditor CreateEditor( NodeUI node, Plug plug ) => null;
	public Menu CreateContextMenu( NodeUI node, Plug plug ) => null;
	public void OnDoubleClick( NodeUI node, Plug plug, MouseEvent e ) { }
}

/// <summary>
/// Input plug that reads/writes NodeInput connections from node properties.
/// </summary>
public record AnimPlugIn( Node Node, AnimPlugInfo Info, Type ValueType ) : AnimPlug( Node, Info, ValueType ), IPlugIn
{
	IPlugOut IPlugIn.ConnectedOutput
	{
		get => Info.CanConnect ? Node.GetConnectedOutput( Info.GetInput( Node ) ) : null;
		set
		{
			if ( !Info.CanConnect )
				return;

			if ( value is null )
			{
				Info.SetInput( Node, default );
				return;
			}

			if ( value is not AnimPlug source )
				return;

			Info.SetInput( Node, new NodeInput
			{
				Identifier = source.Node.Identifier,
				Output = source.Identifier
			} );
		}
	}

	public float? GetHandleOffset( string name )
	{
		return Node.HandleOffsets.TryGetValue( name, out var v ) ? v : null;
	}

	public void SetHandleOffset( string name, float? value )
	{
		if ( value is null ) Node.HandleOffsets.Remove( name );
		else Node.HandleOffsets[name] = value.Value;
	}
}

/// <summary>
/// Output plug. Lets the owning node override its displayed label - parameter nodes
/// substitute their live value for the pin name.
/// </summary>
public record AnimPlugOut( Node Node, AnimPlugInfo Info, Type ValueType ) : AnimPlug( Node, Info, ValueType ), IPlugOut
{
	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = base.DisplayInfo;
			if ( Node.LiveOutputLabel( Identifier ) is { } label )
				info.Name = label;
			return info;
		}
	}
}
