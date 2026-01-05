using Editor.MapEditor;
using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.ActionGraphs;
using Connection = Editor.NodeEditor.Connection;

namespace Editor.ActionGraphs;

/// <summary>
/// Graph view widget for an <see cref="ActionGraph"/>.
/// </summary>
public partial class ActionGraphView : GraphView, AssetSystem.IEventListener
{
	private static Action<ActionGraphResource> ResourceSaved;

	private record struct ActionGraphKey( Guid Guid, ISourceLocation Source )
	{
		public static implicit operator ActionGraphKey( ActionGraph ag ) => new( ag.Guid, ag.SourceLocation );
	}

	private static Dictionary<ActionGraphKey, ActionGraphView> AllViews { get; } = new Dictionary<ActionGraphKey, ActionGraphView>();

	private static bool? _cachedConnectionStyle;

	public static bool EnableGridAlignedWires
	{
		get => _cachedConnectionStyle ??= EditorCookie.Get( "actiongraph.gridwires", false );
		set => EditorCookie.Set( "actiongraph.gridwires", _cachedConnectionStyle = value );
	}

	private static void OpenContainingResource( ActionGraph actionGraph )
	{
		switch ( actionGraph.SourceLocation )
		{
			case GameResourceSourceLocation { Resource: SceneFile sceneFile }:
				if ( SceneEditorSession.Resolve( sceneFile ) is not null ) break;
				EditorScene.OpenScene( sceneFile );
				break;

			case GameResourceSourceLocation { Resource: PrefabFile prefabFile }:
				if ( SceneEditorSession.Resolve( prefabFile ) is not null ) break;
				EditorScene.OpenPrefab( prefabFile );
				break;

			case HammerSourceLocation { EditorSession: var session }:
				session.Focus();
				break;

			case MapSourceLocation { MapPathName: var pathName }:
				var asset = AssetSystem.FindByPath( Path.ChangeExtension( pathName, ".vmap" ) )
					?? throw new Exception( $"Unable to find asset for map \"{pathName}\"." );

				asset.OpenInEditor();
				break;
		}
	}

	private delegate Task DefaultDelegate( [Target] GameObject target );

	[EditorForAssetType( "action" )]
	public static ActionGraphView Open( Asset asset )
	{
		var resource = asset.LoadResource<ActionGraphResource>();

		if ( resource.Graph is null )
		{
			resource.Graph = ActionGraph.CreateDelegate<DefaultDelegate>( EditorNodeLibrary ).Graph;
			resource.Graph.Title = asset.Name?.ToTitleCase() ?? "Untitled Graph";
			resource.Graph.SourceLocation = new GameResourceSourceLocation( resource );
		}

		return Open( resource );
	}

	public static ActionGraphView Open( ActionGraphResource resource )
	{
		return Open( resource.Graph );
	}

	public static ActionGraphView Open( ActionGraph actionGraph )
	{
		OpenContainingResource( actionGraph );

		var newWindow = false;

		ActionGraphKey key = actionGraph;

		if ( !AllViews.TryGetValue( key, out var inst ) )
		{
			var parent = actionGraph.SourceLocation is MapSourceLocation or HammerSourceLocation
				? Hammer.Window ?? throw new Exception( "Can't find hammer window!" )
				: EditorWindow;

			var window = MainWindow.AllWindows.LastOrDefault();

			if ( window is null )
			{
				newWindow = true;
				window = new MainWindow( parent );
			}

			AllViews[key] = inst = window.Open( actionGraph );
		}

		if ( newWindow )
		{
			inst.Window?.Show();
		}

		inst.Window?.Focus();

		inst.Show();
		inst.Focus();

		inst.Window?.DockManager.RaiseDock( inst.Name );

		return inst;
	}

	public static void Rebuild( ActionGraph graph )
	{
		if ( AllViews.TryGetValue( graph, out var view ) )
		{
			view.EditorGraph.UpdateNodes();
			view.RebuildFromGraph();
		}
	}

	private class Pulse
	{
		public Type Type { get; set; }
		public object Value { get; set; }
		public float Time { get; set; }
	}

	public PulseValueInspector PulseValueInspector { get; private set; }
	private Dictionary<GraphicsItem, Pulse> Pulses { get; } = new();
	private List<GraphicsItem> FinishedPulsing { get; } = new();
	private HashSet<Pulse> UpdatedPulses { get; } = new();

	protected override string ClipboardIdent => "actiongraph";

	public ActionGraph ActionGraph { get; }
	public EditorActionGraph EditorGraph { get; }

	internal UndoStack UndoStack { get; }
	public MainWindow Window { get; set; }

	protected override string ViewCookie => $"actiongraph.{EditorGraph.Graph.Guid}";

	public override ConnectionStyle ConnectionStyle => EnableGridAlignedWires
		? GridConnectionStyle.Instance
		: ConnectionStyle.Default;

	private ConnectionStyle _oldConnectionStyle;
	private WarningFrame _warningFrame;

	private object _lastHostObject;

	public SerializedProperty Property
	{
		get
		{
			if ( _lastHostObject == EditorGraph.HostObject && PropertyContainsGraph( field, ActionGraph ) )
			{
				return field;
			}

			_lastHostObject = EditorGraph.HostObject;

			return field = ResolveProperty( _lastHostObject, ActionGraph, null );
		}
	}

	public ActionGraphView( ActionGraph actionGraph ) : base( null )
	{
		if ( actionGraph.SourceLocation is null )
		{
			Log.Warning( $"Unknown source location for ActionGraph \"{actionGraph.Title}\"!" );
		}

		Name = $"View:{actionGraph.Guid}";

		ActionGraph = actionGraph;

		base.Graph = EditorGraph = new EditorActionGraph( actionGraph );

		ActionGraph.LinkTriggered += OnLinkTriggered;
		EditorGraph.GraphPropertiesChanged += UpdateTitle;

		UpdateTitle();

		UndoStack = new UndoStack( () => EditorGraph!.Serialize() );

		Layout = Layout.Column();

		_warningFrame = Layout.Add( new WarningFrame() );

		OnSelectionChanged += SelectionChanged;
		ResourceSaved += OnResourceSaved;
	}

	private void UpdateTitle()
	{
		WindowTitle = MainWindow.GetFullPath( ActionGraph );

		SetWindowIcon( ActionGraph.Icon ?? "account_tree" );
	}

	public Connection GetConnection( Link link )
	{
		// Being careful about Input / Output being null here, which will happen while you drag a new
		// connection from a node

		return Items.OfType<Connection>()
			.FirstOrDefault( x => x.Input?.Inner is ActionInputPlug plugIn && plugIn.Parameter == link.Target
				&& x.Output?.Inner is ActionOutputPlug plugOut && plugOut.Parameter == link.Source );
	}

	public (PlugOut Source, PlugIn Target) GetPlugs( Link link )
	{
		var sourceNode = link.Source.Node.Parent != link.Target.Node
			? Items.OfType<NodeUI>().FirstOrDefault( x =>
				x.Node is EditorNode actionNode && actionNode.Node == link.Source.Node )
			: null;

		var targetNode = Items.OfType<NodeUI>().FirstOrDefault( x =>
			x.Node is EditorNode actionNode && actionNode.Node == link.Target.Node );

		return (
			sourceNode?.Outputs.FirstOrDefault( x => x.Inner is ActionOutputPlug plug && plug.Parameter == link.Source ),
			targetNode?.Inputs.FirstOrDefault( x => x.Inner is ActionInputPlug plug && plug.Parameter == link.Target )
		);
	}

	private Scene.ISceneEditorSession GetSceneEditorSession()
	{
		var hostResource = EditorGraph.HostResource;

		if ( HammerSceneEditorSession.Resolve( ActionGraph.SourceLocation ) is { } hammerSession )
		{
			return hammerSession;
		}

		if ( hostResource is SceneFile sceneFile )
		{
			if ( SceneEditorSession.Resolve( sceneFile ) is { } sceneSession )
			{
				return sceneSession;
			}
		}

		if ( hostResource is PrefabFile prefabFile )
		{
			if ( SceneEditorSession.Resolve( prefabFile ) is { } prefabSession )
			{
				return prefabSession;
			}
		}

		return null;
	}

	public void Save()
	{
		ActionGraph.RemoveUnusedChildNodes();
		ActionGraph.UpdateReferences();

		// Saved?.Invoke();

		if ( GetSceneEditorSession() is not { } session )
		{
			OpenContainingResource( ActionGraph );

			// Try again

			session = GetSceneEditorSession();
		}

		if ( session is not null )
		{
			session.Save( false );
		}
		else if ( ActionGraph.SourceLocation is GameResourceSourceLocation { Resource: { } resource } )
		{
			if ( resource is PrefabFile or SceneFile )
			{
				Log.Warning( $"Unknown editor session, can't save graph!" );
			}
			else
			{
				var asset = AssetSystem.FindByPath( resource.ResourcePath );

				if ( resource is ActionGraphResource graphResource )
				{
					graphResource.Graph = ActionGraph;
					asset?.SaveToDisk( resource );
					ResourceSaved?.Invoke( graphResource );
				}
				else
				{
					EditorEvent.Run( "actiongraph.saving", ActionGraph, resource );
					asset?.SaveToDisk( resource );
				}
			}
		}
		else if ( ActionGraph.SourceLocation is HammerSourceLocation { EditorSession: { } hammerSession } )
		{
			throw new NotImplementedException( $"Need to save map: {hammerSession.MapWorld.MapPathName}" );
		}
		else if ( ActionGraph.SourceLocation is MapSourceLocation { MapPathName: { } pathName } )
		{
			throw new NotImplementedException( $"Need to save map: {pathName}" );
		}
		else
		{
			Log.Warning( "Unknown source, can't save graph!" );
		}

		Window?.UpdateTitle( this );

		EditorEvent.Run( "actiongraph.saved", ActionGraph );
	}

	private void SelectionChanged()
	{
		Window?.DispatchSelectionChanged( this );
	}

	public void FocusOnInput( Node.Input input, int? index )
	{
		Window?.DispatchFocusedOnInput( input, index );
	}

	private void OnLinkTriggered( Link link, object value )
	{
		var pulse = new Pulse { Time = 1f, Type = link.Type, Value = value };

		if ( GetConnection( link ) is { } connection )
		{
			Pulses[connection] = pulse;
		}

		var (source, target) = GetPlugs( link );

		if ( source?.Editor is { } sourceEditor )
		{
			Pulses[sourceEditor] = pulse;
		}

		if ( target?.Editor is { } targetEditor )
		{
			Pulses[targetEditor] = pulse;
		}
	}

	private void OnResourceSaved( ActionGraphResource resource )
	{
		var path = resource.ResourcePath;

		var matchingNodes = ActionGraph.Nodes.Values
			.Where( x => x.Definition.Identifier == "graph" )
			.Where( x =>
				x.Properties["graph"].Value is string refPath &&
				refPath.Equals( path, StringComparison.OrdinalIgnoreCase ) )
			.ToArray();

		Log.Info( $"{matchingNodes.Length} matches with path \"{path}\" in {ActionGraph.SourceLocation}!" );

		foreach ( var node in matchingNodes )
		{
			// Force referencing nodes to invalidate

			node.Properties["graph"].Value = null;
			node.Properties["graph"].Value = path;
		}
	}

	private bool _needsRebuilding;

	private void MarkChanged()
	{
		if ( GetSceneEditorSession() is { } session )
		{
			session.HasUnsavedChanges = true;
		}
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		UpdateWarningFrame();

		if ( _needsRebuilding )
		{
			_needsRebuilding = false;

			EditorGraph.Graph.Validate( true );

			foreach ( var node in EditorGraph.Nodes.OfType<EditorNode>() )
			{
				node.MarkDirty();
			}
		}

		UpdatePulses();

		if ( PulseValueInspector.IsValid() && PulseValueInspector.Target.IsValid() && Pulses.TryGetValue( PulseValueInspector.Target, out var pulse ) )
		{
			PulseValueInspector.Value = pulse.Value;
		}

		var updated = EditorGraph.Update();

		if ( updated.Any() )
		{
			UpdateConnections( updated );

			foreach ( var item in Items )
			{
				item.Update();
			}
		}

		if ( _oldConnectionStyle != ConnectionStyle )
		{
			_oldConnectionStyle = ConnectionStyle;

			foreach ( var connection in Items.OfType<Connection>() )
			{
				connection.Layout();
			}
		}

		if ( IsActiveWindow )
		{
			Window?.UpdateMenuOptions( UndoStack );
		}
	}

	private void UpdateWarningFrame()
	{
		string warning = null;

		var hostObject = EditorGraph.HostObject;
		var hostResource = EditorGraph.HostResource;

		if ( hostResource is null )
		{
			warning = "Graph has no valid source location, so can't be saved!";
		}
		else if ( hostResource is { IsValid: false } )
		{
			warning = "Graph is from a deleted resource, so can't be saved!";
		}
		else if ( hostObject is IValid { IsValid: false } )
		{
			warning = "Graph is from a destroyed object, so can't be saved!";
		}
		else if ( Property?.IsValid is not true )
		{
			warning = $"Graph is no longer referenced, so can't be saved!";
		}

		_warningFrame.Text = warning;
	}

	[EditorEvent.Hotload]
	private void OnHotload()
	{
		_needsRebuilding = true;
		_globalNodeTypes = null;
	}

	[Event( "content.changed" )]
	private void OnAssetChanged( string fileName )
	{
		if ( !string.Equals( Path.GetExtension( fileName ), ".action", StringComparison.OrdinalIgnoreCase ) ) return;

		_globalNodeTypes = null;
	}

	void AssetSystem.IEventListener.OnAssetThumbGenerated( Asset asset )
	{
		foreach ( var node in EditorGraph.Nodes.OfType<EditorNode>() )
		{
			if ( node.Definition.Identifier != "resource.ref" ) continue;
			if ( !node.Node.Properties.TryGetValue( "value", out var valueProperty ) ) continue;
			if ( valueProperty.Value is not Resource resource ) continue;
			if ( !asset.RelativePath.Contains( resource.ResourceName ) ) continue;

			node.MarkDirty();
		}
	}

	protected override void OnFocus( FocusChangeReason reason )
	{
		Window?.OnFocusView( this );

		base.OnFocus( reason );
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		var scenePos = ToScene( e.LocalPosition );
		var item = GetItemAt( scenePos );

		if ( item.IsValid() && Pulses.TryGetValue( item, out var pulse ) )
		{
			if ( !PulseValueInspector.IsValid() )
			{
				Add( PulseValueInspector = new PulseValueInspector( this ) );
			}

			PulseValueInspector.TargetPosition = scenePos;
			PulseValueInspector.Target = item;
			PulseValueInspector.Value = pulse.Value;
		}
		else if ( PulseValueInspector.IsValid() )
		{
			PulseValueInspector.Destroy();
			PulseValueInspector = null;
		}
	}

	private readonly Stopwatch _pulseTimer = new();

	private void UpdatePulses()
	{
		var dt = (float)_pulseTimer.Elapsed.TotalSeconds;

		_pulseTimer.Restart();

		FinishedPulsing.Clear();
		UpdatedPulses.Clear();

		foreach ( var (item, pulse) in Pulses )
		{
			if ( !item.IsValid || pulse.Time < 0f )
			{
				FinishedPulsing.Add( item );
				continue;
			}

			if ( UpdatedPulses.Add( pulse ) )
			{
				pulse.Time -= dt;
			}

			var pulseScale = 1f + MathF.Pow( Math.Max( pulse.Time, 0f ), 8f ) * 3f;
			var color = pulse.Value switch
			{
				Color clr => clr,
				true => Theme.Blue,
				false => Theme.Red,
				null when pulse.Type != typeof( Signal ) => Theme.Red,
				_ => Color.Transparent
			};

			color.a *= pulse.Time;

			switch ( item )
			{
				case Connection connection:
					connection.WidthScale = pulseScale;
					connection.ColorTint = color;
					connection.Update();
					break;

				case IPulseTarget pulseTarget:
					pulseTarget.UpdatePulse( pulseScale, color );
					break;
			}
		}

		foreach ( var target in FinishedPulsing )
		{
			Pulses.Remove( target );
		}
	}

	public override void PushUndo( string name )
	{
		UndoStack.Push( name );
		MarkChanged();
	}

	public override void PushRedo()
	{
		// I'm not sure what this is for, I feel like I don't need it?
	}

	public void SelectNode( Node node )
	{
		var actionNode = EditorGraph.FindNode( node );

		SelectNode( actionNode );
	}

	public void SelectLink( Link link )
	{
		SelectLinks( new[] { link } );
	}

	public void SelectLinks( IEnumerable<Link> links )
	{
		var linkSet = links.Select( x => (x.Source, x.Target) ).ToHashSet();

		var connections = Items.OfType<Connection>().Where( x =>
			x.Input.Inner is ActionPlug<Node.Input> { Parameter: { } input } &&
			x.Output.Inner is ActionPlug<Node.Output> { Parameter: { } output } &&
			linkSet.Contains( (output, input) ) );

		foreach ( var item in SelectedItems )
		{
			item.Selected = false;
		}

		foreach ( var connection in connections )
		{
			connection.Selected = true;
		}
	}

	private void RemoveInvalidElements()
	{
		var invalidNodes = Items
			.OfType<NodeUI>()
			.Where( x => x.Node is EditorNode { Node.IsValid: false } )
			.ToArray();

		var invalidConnections = Connections
			.Where( x =>
				x.Input.Node.Node is EditorNode { Node.IsValid: false } ||
				x.Output.Node.Node is EditorNode { Node.IsValid: false } )
			.ToArray();

		foreach ( var invalidNode in invalidNodes )
		{
			EditorGraph.RemoveNode( (EditorNode)invalidNode.Node );
			invalidNode.Destroy();
		}

		foreach ( var connection in invalidConnections )
		{
			Connections.Remove( connection );
			connection.Destroy();
		}
	}

	private void CleanUpNewSubGraph( Facepunch.ActionGraphs.ActionGraph graph )
	{
		const string positionKey = "Position";
		const float inputOutputMargin = 300f;

		var minPos = new Vector2( float.PositiveInfinity, float.PositiveInfinity );
		var maxPos = new Vector2( float.NegativeInfinity, float.NegativeInfinity );
		var posCount = 0;

		foreach ( var node in graph.Nodes.Values )
		{
			if ( node.UserData[positionKey] is not { } posValue )
			{
				continue;
			}

			var pos = posValue.Deserialize<Vector2>();

			minPos = Vector2.Min( minPos, pos );
			maxPos = Vector2.Max( maxPos, pos );
			posCount++;
		}

		if ( posCount == 0 )
		{
			minPos = maxPos = 0f;
		}

		var midPos = (minPos + maxPos) * 0.5f;
		var width = maxPos.x - minPos.x;

		foreach ( var node in graph.Nodes.Values )
		{
			if ( node.UserData[positionKey] is not { } posValue )
			{
				continue;
			}

			var pos = posValue.Deserialize<Vector2>() - midPos;
			node.UserData[positionKey] = JsonSerializer.SerializeToNode( pos );
		}

		if ( graph.InputNode is { } input )
		{
			var pos = new Vector2( width * -0.5f - inputOutputMargin, 0f );
			input.UserData[positionKey] = JsonSerializer.SerializeToNode( pos );
		}

		if ( graph.PrimaryOutputNode is { } output )
		{
			var pos = new Vector2( width * 0.5f + inputOutputMargin, 0f );
			output.UserData[positionKey] = JsonSerializer.SerializeToNode( pos );
		}
	}

	public async Task<ActionGraph> CreateSubGraph( IReadOnlyList<(NodeUI NodeUI, Node ActionNode)> nodes, CreateSubGraphNodeDelegate createNodeDelegate )
	{
		var avgPos = nodes.Aggregate( Vector2.Zero, ( s, x ) => s + x.NodeUI.Position ) / nodes.Count;

		var actionGraph = EditorGraph.Graph;
		var result = await actionGraph.CreateSubGraphAsync(
			nodes.Select( x => x.ActionNode ),
			EditorJsonOptions,
			subGraph =>
			{
				CleanUpNewSubGraph( subGraph );
				return createNodeDelegate( subGraph );
			} );

		if ( !result.HasValue )
		{
			return null;
		}

		result.Value.GraphNode.UserData["Position"] = Json.ToNode( avgPos );

		if ( !IsValid )
		{
			// We might not exist any more if a hotload happened
			return null;
		}

		var newNode = new EditorNode( EditorGraph, result.Value.GraphNode );

		EditorGraph.AddNode( newNode );

		RemoveInvalidElements();
		BuildFromNodes( new[] { newNode }, true, default, true );

		return result.Value.NewGraph;
	}

	protected override void OnOpenContextMenu( Menu menu, Plug targetPlug )
	{
		if ( targetPlug.IsValid() )
		{
			return;
		}

		var selectedNodes = SelectedItems
			.OfType<NodeUI>()
			.Select( x => (NodeUI: x, ActionNode: x.Node is EditorNode { Node: { } node } ? node : null) )
			.Where( x => x.ActionNode != null )
			.ToArray();

		var actionGraph = EditorGraph.Graph;

		if ( !actionGraph.CanCreateSubGraph( selectedNodes.Select( x => x.ActionNode ) ) )
		{
			return;
		}

		{
			EditorEvent.Run( PopulateCreateSubGraphMenuEvent.EventName, new PopulateCreateSubGraphMenuEvent( this, menu, selectedNodes ) );

			menu.AddOption( "Create Custom Node...", "add_box", () =>
			{
				_ = CreateSubGraph( selectedNodes, subGraph =>
				{
					const string extension = "action";

					var fd = new FileDialog( null );
					fd.Title = "Create ActionGraph Node";
					fd.Directory = Project.Current.RootDirectory.FullName;
					fd.DefaultSuffix = $".{extension}";
					fd.SelectFile( $"untitled.{extension}" );
					fd.SetFindFile();
					fd.SetModeSave();
					fd.SetNameFilter( $"ActionGraph Node (*.{extension})" );

					if ( !fd.Execute() )
						return null;

					var fileName = Path.GetFileNameWithoutExtension( fd.SelectedFile );
					var title = fileName.ToTitleCase();

					var asset = AssetSystem.CreateResource( "action", fd.SelectedFile );
					var resource = asset.LoadResource<ActionGraphResource>();

					resource.Graph = subGraph;
					resource.Title = title;
					resource.Description = "No description provided.";
					resource.Icon = "account_tree";
					resource.Category = "Custom";

					asset.SaveToDisk( resource );

					MainAssetBrowser.Instance?.Local.UpdateAssetList();

					var graphNode = EditorGraph.Graph.AddNode( EditorNodeLibrary.Graph );

					graphNode.Properties["graph"].Value = asset.Path;
					graphNode.UpdateParameters();

					var targetInput = graphNode.Inputs.Values.FirstOrDefault( x => x.IsTarget );

					if ( targetInput is not null && EditorGraph.Graph.TargetOutput is { } targetOutput )
					{
						targetInput.SetLink( targetOutput );
					}

					return Task.FromResult( graphNode );
				} );
			} );
		}
	}

	private static bool TryGetHandleConfig( Type type, out Type matchingType, out HandleConfig config )
	{
		if ( ActionGraphTheme.HandleConfigs.TryGetValue( type, out config ) )
		{
			matchingType = type;
			return true;
		}

		if ( type.BaseType != null && TryGetHandleConfig( type.BaseType, out matchingType, out config ) )
		{
			return true;
		}

		if ( type.IsConstructedGenericType && TryGetHandleConfig( type.GetGenericTypeDefinition(), out matchingType, out config ) )
		{
			return true;
		}

		if ( !type.IsInterface )
		{
			foreach ( var @interface in type.GetInterfaces() )
			{
				if ( TryGetHandleConfig( @interface, out matchingType, out config ) )
				{
					return true;
				}
			}
		}

		matchingType = null;
		return false;
	}

	protected override HandleConfig OnGetHandleConfig( Type type )
	{
		if ( Nullable.GetUnderlyingType( type ) is { } underlyingType )
		{
			type = underlyingType;
		}

		if ( TryGetHandleConfig( type, out var matchingType, out var config ) )
		{
			return config with { Name = type == matchingType ? config.Name : null };
		}

		if ( type.IsEnum && ActionGraphTheme.HandleConfigs.TryGetValue( typeof( Enum ), out config ) )
		{
			return config with { Name = type.Name };
		}

		return base.OnGetHandleConfig( type );
	}

	public void Undo()
	{
		if ( !UndoStack.CanUndo ) return;

		EditorGraph.Deserialize( UndoStack.Undo() );
		BuildFromNodes( EditorGraph.Nodes, false );
	}

	public void Redo()
	{
		if ( !UndoStack.CanRedo ) return;

		EditorGraph.Deserialize( UndoStack.Redo() );
		BuildFromNodes( EditorGraph.Nodes, false );
	}

	public override void OnDestroyed()
	{
		ActionGraph.LinkTriggered -= OnLinkTriggered;
		ResourceSaved -= OnResourceSaved;

		if ( AllViews.TryGetValue( ActionGraph, out var inst ) && inst == this )
		{
			AllViews.Remove( ActionGraph );
		}

		Window?.OnRemoveView( this );

		base.OnDestroyed();
	}

	public void LogExpression()
	{
		var expression = ActionGraph.BuildExpression();
		var debugView = typeof( Expression ).GetProperty( "DebugView", BindingFlags.Instance | BindingFlags.NonPublic )!
			.GetValue( expression );

		Log.Info( debugView );
	}
}
