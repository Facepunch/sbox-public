namespace Editor.AnimGraph;

/// <summary>
/// Graph view for the AnimGraph editor.
/// </summary>
public class View : GraphView, Nodes.IInternalGraphEditorActions
{
	protected override string ClipboardIdent => "animgraph";

	protected override INodeType CommentNodeType { get; } = new NodeType( EditorTypeLibrary.GetType<Nodes.CommentNode>() );

	protected override INodeType RerouteNodeType { get; } = new NodeType( EditorTypeLibrary.GetType<RerouteNode>() );

	private static bool? _cachedConnectionStyle;

	public static bool EnableGridAlignedWires
	{
		get => _cachedConnectionStyle ??= EditorCookie.Get( "animgraph.gridwires", false );
		set => EditorCookie.Set( "animgraph.gridwires", _cachedConnectionStyle = value );
	}

	private ConnectionStyle _oldConnectionStyle;

	public override ConnectionStyle ConnectionStyle => EnableGridAlignedWires
		? GridConnectionStyle.Instance
		: ConnectionStyle.Default;

	protected override bool CanDisconnectByDroppingConnection => false;
	protected override bool UseSmoothZoom => true;

	protected override bool AreConnectionTypesCompatible( Type a, Type b ) => a == b;

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		var item = GetItemAt( ToScene( e.LocalPosition ) );
		var connection = item as NodeEditor.Connection ?? item?.Parent as NodeEditor.Connection;

		if ( connection is null )
		{
			base.OnContextMenu( e );
			return;
		}

		e.Accepted = true;
		PushUndo( "Disconnect" );
		connection.Disconnect();
		connection.Destroy();
		PushRedo();
	}

	/// <summary>
	/// True once live debug state has been wiped while no preview model is
	/// running, letting the per-frame debug walk skip this view entirely.
	/// </summary>
	internal bool LiveDebugCleared { get; set; }

	[EditorEvent.Frame]
	private void Frame()
	{
		if ( _oldConnectionStyle == ConnectionStyle )
			return;

		_oldConnectionStyle = ConnectionStyle;

		foreach ( var connection in Items.OfType<NodeEditor.Connection>() )
			connection.Layout();
	}

	private readonly Dictionary<string, INodeType> _availableNodes = new( StringComparer.OrdinalIgnoreCase );
	private readonly Window _window;

	void Nodes.IInternalGraphEditorActions.ReflectChildParametersToParent( Nodes.InternalReferencedGraphNode node )
	{
		_window.ReflectChildParametersToParent( node );
	}

	public new Data Graph
	{
		get => (Data)base.Graph;
		set => base.Graph = value;
	}

	public View( Window window, Widget parent ) : base( parent )
	{
		_window = window;

		// Discover all Node types and register them
		var types = EditorTypeLibrary.GetTypes<Node>()
			.Where( x => !x.IsAbstract && !x.HasAttribute<HideAttribute>() )
			.OrderBy( x => x.Name );

		foreach ( var type in types )
		{
			var nodeType = new NodeType( type );
			_availableNodes.TryAdd( nodeType.Identifier, nodeType );
		}

		OnSelectionChanged += SelectionChanged;
	}

	private bool _suppressUndo;
	private bool _suppressSelection;

	private IDisposable SuppressUndo()
	{
		_suppressUndo = true;
		return new Sandbox.Utility.DisposeAction( () => _suppressUndo = false );
	}

	internal IDisposable SuppressSelectionForwarding()
	{
		_suppressSelection = true;
		return new Sandbox.Utility.DisposeAction( () => _suppressSelection = false );
	}

	public override void PushUndo( string name )
	{
		if ( !_suppressUndo )
			_window.PushGraphUndo( name );
	}

	public override void PushLayoutUndo( string name )
	{
		if ( !_suppressUndo )
			_window.PushGraphUndo( name, GraphChangeScope.EditorOnly );
	}

	public override void PushRedo()
	{
		if ( !_suppressUndo )
			_window.PushGraphRedo();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.IsDoubleClick && e.LeftMouseButton && GetNodeAt( ToScene( e.LocalPosition ) )?.Node is Node graphNode && _window.NavigateInto( graphNode ) )
		{
			e.Accepted = true;
			return;
		}

		base.OnMousePress( e );
	}

	private void SelectionChanged()
	{
		if ( _suppressSelection )
			return;

		var item = SelectedItems
			.OfType<NodeUI>()
			.OrderByDescending( n => n is CommentUI )
			.FirstOrDefault();

		if ( !item.IsValid() )
		{
			_window.OnNodeSelected( this, null );
			return;
		}

		_window.OnNodeSelected( this, (Node)item.Node );
	}

	internal void NotifySelection() => SelectionChanged();

	protected override INodeType NodeTypeFromDragEvent( DragEvent ev )
	{
		if ( ev.Data.Object is ClipDragData clipData )
		{
			return new AnimClipDropNodeType( clipData.AnimationNames[0] );
		}

		if ( ev.Data.Object is ParameterDragData paramData )
		{
			return new ParameterDropNodeType( paramData.Parameter );
		}

		return base.NodeTypeFromDragEvent( ev );
	}

	private readonly List<NodeUI> _clipPreviews = new();

	public override void OnDragHover( DragEvent ev )
	{
		base.OnDragHover( ev );

		if ( ev.Data.Object is not ClipDragData { AnimationNames.Count: > 1 } clipData )
			return;

		if ( _clipPreviews.Count == 0 )
		{
			foreach ( var name in clipData.AnimationNames.Skip( 1 ) )
			{
				var node = new AnimClipDropNodeType( name ).CreateNode( Graph );
				_clipPreviews.Add( CreateNodeUI( node ) );
			}
		}

		var position = ToScene( ev.LocalPosition ).SnapToGrid( GridSize );
		for ( var i = 0; i < _clipPreviews.Count; i++ )
			_clipPreviews[i].Position = position + new Vector2( 0, ClipStackStep * (i + 1) );
	}

	public override void OnDragLeave()
	{
		base.OnDragLeave();
		ClearClipPreviews();
	}

	// Grid-aligned so per-node position snapping doesn't make the gaps uneven
	private float ClipStackStep => _clipPreviews.FirstOrDefault().IsValid()
		? MathF.Ceiling( (_clipPreviews[0].Size.y + 8f) / GridSize ) * GridSize
		: 128f;

	private void ClearClipPreviews()
	{
		foreach ( var ui in _clipPreviews.Where( ui => ui.IsValid() ) )
		{
			Graph.RemoveNode( ui.Node );
			ui.Destroy();
		}

		_clipPreviews.Clear();
	}

	internal void PrepareForRebuild()
	{
		_suppressSelection = true;
		OnDragLeave();
		DeleteAllItems();
	}

	public override void OnDestroyed()
	{
		DeleteAllItems();
		base.OnDestroyed();
	}

	public override void OnDragDrop( DragEvent ev )
	{
		var extra = ev.Data.Object is ClipDragData { AnimationNames.Count: > 1 } clipData
			? clipData.AnimationNames.Skip( 1 ).ToArray()
			: null;

		var step = ClipStackStep;
		ClearClipPreviews();

		if ( extra is null )
		{
			base.OnDragDrop( ev );
			return;
		}

		// One undo entry for the whole stack
		using ( SuppressUndo() )
		{
			base.OnDragDrop( ev );

			var position = ToScene( ev.LocalPosition ).SnapToGrid( GridSize );
			for ( var i = 0; i < extra.Length; i++ )
				CreateNewNode( new AnimClipDropNodeType( extra[i] ), position + new Vector2( 0, step * (i + 1) ), null );
		}

		_window.CommitChange( "Add Nodes" );
	}

	protected override IEnumerable<INodeType> GetRelevantNodes( NodeQuery query )
	{
		return _availableNodes.Values
			.Where( t => t is not NodeType nodeType || nodeType.IsAvailableIn( Graph ) )
			.Concat( _window.Graph.AllParameters.Select( parameter => new ParameterDropNodeType( parameter ) ) )
			.Filter( query );
	}

	private sealed record ExtractionPlan(
		Node[] NodesToRemove,
		Node[] NodesToCopy,
		IPlugOut Output,
		IPlugIn[] Consumers,
		Vector2 ReplacementPosition );

	protected override void OnOpenContextMenu( Menu menu, Plug targetPlug )
	{
		base.OnOpenContextMenu( menu, targetPlug );

		if ( targetPlug.IsValid() )
			return;

		var selectedNodes = SelectedItems.OfType<NodeUI>().ToArray();
		if ( selectedNodes.Length == 1 && selectedNodes[0].Node is IParameterReference reference
			&& reference.ResolveParameter() is { } parameter )
			menu.AddOption( "Find Usages", "manage_search", () => _window.FindParameterUsages( parameter ) );

		if ( SelectedItems.OfType<NodeUI>().Any() )
		{
			var canExtract = TryCreateExtractionPlan( out var plan, out var reason );
			var extract = menu.AddOption( "Extract to AnimGraph...", "account_tree", () => ExtractSelection( plan ) );
			extract.Enabled = canExtract;
			extract.ToolTip = reason;
		}

		menu.AddOption( "Fit View", "fit_screen", FitView );
	}

	internal void FitView()
	{
		var nodes = Items.OfType<NodeUI>().ToArray();
		if ( nodes.Length == 0 )
			return;

		var bounds = nodes[0].SceneRect;
		foreach ( var node in nodes.Skip( 1 ) )
			bounds.Add( node.SceneRect );

		var viewport = new Vector2( Math.Max( Width - 96, 1 ), Math.Max( Height - 96, 1 ) );
		var span = Vector2.Max( bounds.Size, new Vector2( 1 ) );
		var scale = Math.Min( MathF.Min( viewport.x / span.x, viewport.y / span.y ), 1 );
		SetView( bounds.Center, scale );
	}

	private void ExtractSelection( ExtractionPlan plan )
	{
		if ( plan is null )
			return;

		var dialog = new FileDialog( null )
		{
			Title = "Extract AnimGraph",
			Directory = Project.Current.GetAssetsPath(),
			DefaultSuffix = ".ag"
		};

		dialog.SelectFile( "subgraph.ag" );
		dialog.SetFindFile();
		dialog.SetModeSave();
		dialog.SetNameFilter( "AnimGraph (*.ag)" );

		if ( !dialog.Execute() || string.IsNullOrWhiteSpace( dialog.SelectedFile ) )
			return;

		var childGraph = CreateExtractedGraph( plan );
		var session = Session.Create( childGraph );
		var result = session.Save( dialog.SelectedFile );

		foreach ( var warning in result.Warnings )
		{
			if ( warning.IsError )
				Log.Error( $"AnimGraph: {warning}" );
			else
				Log.Warning( $"AnimGraph: {warning}" );
		}

		var asset = session.Asset;
		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		var referenceNode = new InternalReferencedGraphNode
		{
			GraphPath = asset.Path,
			Position = plan.ReplacementPosition.SnapToGrid( GridSize )
		};

		ReplaceSelection( plan, referenceNode );
		_window.CommitChange( "Extract AnimGraph" );
	}

	private void ReplaceSelection( ExtractionPlan plan, Node replacement )
	{
		foreach ( var node in plan.NodesToRemove )
			Graph.RemoveNode( node );

		Graph.AddNode( replacement );
		var replacementOutput = replacement.Outputs.Single();

		foreach ( var consumer in plan.Consumers )
			consumer.ConnectedOutput = replacementOutput;

		BuildFromNodes( Graph.Nodes, false );
		SelectNode( replacement );
	}

	private Data CreateExtractedGraph( ExtractionPlan plan )
	{
		var childGraph = new Data();
		childGraph.Deserialize( Graph.SerializeNodes( plan.NodesToCopy ) );
		childGraph.TargetModel = Graph.Root.TargetModel;
		childGraph.TargetSkeleton = Graph.Root.TargetSkeleton;
		childGraph.PreviewFocusBone = Graph.Root.PreviewFocusBone;

		var parameterNames = plan.NodesToCopy
			.OfType<ParameterNode>()
			.Select( x => x.ParameterName )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );

		childGraph.Parameters.Clear();
		foreach ( var parameter in Graph.Root.Parameters.Where( x => parameterNames.Contains( x.Name ) ) )
			childGraph.Parameters.Add( new Parameter().CopyFrom( parameter ) );

		var outputNode = childGraph.FindNode( ((Node)plan.Output.Node).Identifier );
		var result = new PoseResultNode
		{
			Position = outputNode.Position + new Vector2( 320, 0 ),
			Pose = new NodeInput
			{
				Identifier = outputNode.Identifier,
				Output = plan.Output.Identifier
			}
		};

		childGraph.AddNode( result );
		return childGraph;
	}

	private bool TryCreateExtractionPlan( out ExtractionPlan plan, out string reason )
	{
		plan = null;
		reason = null;

		if ( Graph is not { Kind: GraphKind.BlendTree } )
		{
			reason = "Only pose graphs can be extracted as reusable AnimGraphs.";
			return false;
		}

		var selectedUis = SelectedItems.OfType<NodeUI>().ToArray();
		var selected = selectedUis.Select( x => (Node)x.Node ).ToHashSet();
		var nodesToRemove = selected.Where( x => x is not PoseResultNode ).ToArray();
		if ( nodesToRemove.Length == 0 || nodesToRemove.Any( x => !x.CanRemove ) )
		{
			reason = "Select one or more removable pose nodes.";
			return false;
		}

		var removeSet = nodesToRemove.ToHashSet();
		var nodesToCopy = removeSet.ToHashSet();

		foreach ( var node in nodesToRemove )
		{
			foreach ( var input in node.Inputs )
			{
				if ( input.ConnectedOutput?.Node is not Node source || removeSet.Contains( source ) )
					continue;

				if ( source is not ParameterNode )
				{
					reason = $"'{node.DisplayInfo.Name}' depends on an unselected node. Select it before extracting.";
					return false;
				}

				nodesToCopy.Add( source );
			}
		}

		foreach ( var parameter in nodesToCopy.OfType<ParameterNode>() )
		{
			if ( Graph.Root.VirtualParameters.Any( x => string.Equals( x.Name, parameter.ParameterName, StringComparison.OrdinalIgnoreCase ) ) )
			{
				reason = "Virtual parameters cannot be extracted into a reusable graph.";
				return false;
			}

			if ( !Graph.Root.Parameters.Any( x => string.Equals( x.Name, parameter.ParameterName, StringComparison.OrdinalIgnoreCase ) ) )
			{
				reason = $"Parameter '{parameter.ParameterName}' is not defined on the graph.";
				return false;
			}
		}

		var outgoing = Graph.Nodes
			.OfType<Node>()
			.Where( x => !removeSet.Contains( x ) )
			.SelectMany( x => x.Inputs )
			.Where( x => x.ConnectedOutput?.Node is Node source && removeSet.Contains( source ) )
			.ToArray();

		var outputs = outgoing
			.Select( x => x.ConnectedOutput )
			.Distinct()
			.ToArray();

		if ( outputs.Length == 0 )
		{
			var internalInputs = removeSet.SelectMany( x => x.Inputs ).Select( x => x.ConnectedOutput ).ToHashSet();
			outputs = removeSet
				.SelectMany( x => x.Outputs )
				.Where( x => x.Type == typeof( PoseLink ) && !internalInputs.Contains( x ) )
				.ToArray();
		}

		if ( outputs.Length != 1 )
		{
			reason = "The selection must have exactly one pose output.";
			return false;
		}

		var output = outputs[0];
		if ( output.Type != typeof( PoseLink ) || outgoing.Any( x => x.ConnectedOutput != output ) )
		{
			reason = "Only one pose output may leave the extracted selection.";
			return false;
		}

		var removableUis = selectedUis.Where( x => x.Node is Node node && removeSet.Contains( node ) ).ToArray();
		var min = removableUis.Select( x => x.SceneRect.TopLeft ).Aggregate( (a, b) => a.ComponentMin( b ) );
		var max = removableUis.Select( x => x.SceneRect.BottomRight ).Aggregate( (a, b) => a.ComponentMax( b ) );

		plan = new ExtractionPlan( nodesToRemove, [.. nodesToCopy], output, outgoing, (min + max) * 0.5f );
		return true;
	}

	/// <summary>
	/// A parameter node fresh from the palette references nothing - bind it to
	/// the first parameter of its type, or define a new one, so the node and
	/// the parameters dock always agree.
	/// </summary>
	protected override void OnNodeCreated( INode node )
	{
		if ( node is not Nodes.ParameterNode parameterNode || !string.IsNullOrWhiteSpace( parameterNode.ParameterName ) )
			return;

		var root = _window.Graph;
		var parameter = root.Parameters.FirstOrDefault( p => p.Type == parameterNode.ParameterType );

		if ( parameter is null )
		{
			parameter = new Parameter
			{
				Name = root.UniqueParameterName( parameterNode.ParameterType.ToString() ),
				Type = parameterNode.ParameterType
			};
			root.Parameters.Add( parameter );
		}

		parameterNode.ParameterName = parameter.Name;
	}

	protected override HandleConfig OnGetHandleConfig( Type type )
	{
		if ( HandleConfigs.TryGetValue( type, out var config ) )
			return config;

		return base.OnGetHandleConfig( type );
	}

	private static readonly Dictionary<Type, HandleConfig> HandleConfigs = new()
	{
		{ typeof( PoseLink ), new HandleConfig( "Pose", Color.Parse( "#4fc3f7" ).Value ) },
		{ typeof( FloatLink ), new HandleConfig( "Float", Color.Parse( "#8ec07c" ).Value ) },
		{ typeof( BoolLink ), new HandleConfig( "Bool", Color.Parse( "#e06c75" ).Value ) },
		{ typeof( IDLink ), new HandleConfig( "ID", Color.Parse( "#c678dd" ).Value ) },
		{ typeof( VectorLink ), new HandleConfig( "Vector", Color.Parse( "#e0d867" ).Value ) },
		{ typeof( BoneMaskLink ), new HandleConfig( "Bone Mask", Color.Parse( "#d19a66" ).Value ) },
		{ typeof( LayerLink ), new HandleConfig( "Layer", Color.Parse( "#ff6347" ).Value ) },
	};
}

/// <summary>
/// Temporary INodeType used when dropping an animation clip from the clip list.
/// Creates an AnimationClipNode with the animation name pre-filled.
/// </summary>
file class AnimClipDropNodeType : INodeType
{
	private readonly string _animationName;

	public AnimClipDropNodeType( string animationName )
	{
		_animationName = animationName;
	}

	public bool IsCommon => false;

	public Menu.PathElement[] Path => [new() { Name = "Animation", Icon = "movie" }, new() { Name = _animationName }];

	public bool TryGetInput( Type valueType, [System.Diagnostics.CodeAnalysis.NotNullWhen( true )] out string name )
	{
		name = null;
		return false;
	}

	public bool TryGetOutput( Type valueType, [System.Diagnostics.CodeAnalysis.NotNullWhen( true )] out string name )
	{
		if ( valueType.IsAssignableFrom( typeof( PoseLink ) ) )
		{
			name = "Result";
			return true;
		}
		name = null;
		return false;
	}

	public INode CreateNode( IGraph graph )
	{
		var node = new Nodes.AnimationClipNode { AnimationName = _animationName };
		return node;
	}
}

/// <summary>
/// Temporary INodeType used when dropping a parameter from the parameter list.
/// Creates the matching parameter node with the name pre-filled.
/// </summary>
file class ParameterDropNodeType : INodeType
{
	private readonly IParameter _parameter;

	public ParameterDropNodeType( IParameter parameter )
	{
		_parameter = parameter;
	}

	public bool IsCommon => true;

	public Menu.PathElement[] Path =>
	[
		new() { Name = "Parameters", Icon = "tune" },
		new() { Name = "Existing Parameters", Icon = "list", Order = -100 },
		new() { Name = $"{_parameter.Name} ({_parameter.Type})" }
	];

	public bool TryGetInput( Type valueType, [System.Diagnostics.CodeAnalysis.NotNullWhen( true )] out string name )
	{
		name = null;
		return false;
	}

	public bool TryGetOutput( Type valueType, [System.Diagnostics.CodeAnalysis.NotNullWhen( true )] out string name )
	{
		var linkType = _parameter.Type switch
		{
			AnimParameterType.Float => typeof( FloatLink ),
			AnimParameterType.Bool => typeof( BoolLink ),
			AnimParameterType.ID => typeof( IDLink ),
			AnimParameterType.Target => typeof( TargetLink ),
			AnimParameterType.BoneMask => typeof( BoneMaskLink ),
			_ => typeof( VectorLink ),
		};

		if ( valueType.IsAssignableFrom( linkType ) )
		{
			name = "Value";
			return true;
		}

		name = null;
		return false;
	}

	public INode CreateNode( IGraph graph )
	{
		Nodes.ParameterNode node = _parameter.Type switch
		{
			AnimParameterType.Float => new Nodes.FloatParameterReferenceNode(),
			AnimParameterType.Bool => new Nodes.BoolParameterReferenceNode(),
			AnimParameterType.ID => new Nodes.IDParameterReferenceNode(),
			AnimParameterType.Target => new Nodes.TargetParameterReferenceNode(),
			AnimParameterType.BoneMask => new Nodes.BoneMaskParameterReferenceNode(),
			_ => new Nodes.VectorParameterReferenceNode(),
		};

		node.ParameterIdentifier = _parameter.Identifier;
		node.ParameterName = _parameter.Name;
		return node;
	}
}
