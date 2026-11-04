using Sandbox.Animation;

namespace Editor.AnimGraph;

public partial class Window
{
	private const string AutoCompilePreviewCookie = "animgraph.liveupdate";

	private sealed record StateMachineLiveDebug(
		int NodeIndex,
		Dictionary<StateMachineNode.State, int> StateNodes,
		Dictionary<Nodes.TransitionNode, int> TransitionNodes );

	private sealed record LiveDebugData(
		IReadOnlyList<Compiler.Warning> Warnings,
		Dictionary<string, StateMachineLiveDebug> StateMachines,
		Dictionary<string, int> Nodes,
		Dictionary<string, Compiler.LayerDebug> Layers,
		Dictionary<string, (StateMachineNode Machine, StateMachineNode.State Target)> TransitionTargets,
		(int NodeIndex, string AnimationName)[] AnimationClips );

	private Dictionary<string, StateMachineLiveDebug> _liveDebug;
	private Dictionary<string, int> _liveNodes;
	private Dictionary<string, Compiler.LayerDebug> _liveLayers;
	private Dictionary<string, (StateMachineNode Machine, StateMachineNode.State Target)> _transitionTargets;
	private (int NodeIndex, string AnimationName)[] _liveAnimationClips = [];
	private readonly LiveDebugSampler _debugSampler = new();
	private readonly Dictionary<Node, bool> _liveNodeChanges = new( ReferenceEqualityComparer.Instance );
	private readonly object _previewCompileLock = new();
	private bool _autoCompilePreviewEnabled = EditorCookie.Get( AutoCompilePreviewCookie, true );
	private bool _previewCompiling;
	private bool _previewCompileWorkerRunning;
	private int _previewCompileRevision;
	private int _pendingPreviewCompileRevision;
	private string _pendingPreviewCompileSnapshot;
	private readonly Dictionary<string, Compiler.Result> _previewVariationResults = new();
	private Sandbox.Animation.Graph _previewGraph;
	private byte[] _previewDefinition;
	private bool _previewCompileStale;

	private void SetAutoCompilePreviewEnabled( bool enabled )
	{
		if ( _autoCompilePreviewEnabled == enabled )
			return;

		_autoCompilePreviewEnabled = enabled;
		EditorCookie.Set( AutoCompilePreviewCookie, enabled );

		if ( enabled && _previewCompileStale )
			CompilePreview();
		else if ( !enabled )
			InvalidatePreviewCompile();
	}

	private void CompilePreview()
	{
		if ( _saving )
			return;

		int revision;
		string snapshot;

		lock ( _previewCompileLock )
		{
			revision = ++_previewCompileRevision;
			_pendingPreviewCompileSnapshot = null;
		}

		// The cached per variation results belong to the graph as it was, they're replaced when
		// this compile lands
		_previewVariationResults.Clear();

		try
		{
			snapshot = _session.CreateCompileSnapshot();
		}
		catch ( Exception e )
		{
			SetPreviewCompiling( false );
			ApplyCompileFailure( e );
			return;
		}

		lock ( _previewCompileLock )
		{
			if ( revision != _previewCompileRevision )
				return;

			_pendingPreviewCompileSnapshot = snapshot;
			_pendingPreviewCompileRevision = revision;
		}

		SetPreviewCompiling( true );
		StartPreviewCompile();
	}

	private void StartPreviewCompile()
	{
		string snapshot;
		int revision;

		lock ( _previewCompileLock )
		{
			if ( _previewCompileWorkerRunning || _pendingPreviewCompileSnapshot is null )
				return;

			_previewCompileWorkerRunning = true;
			snapshot = _pendingPreviewCompileSnapshot;
			revision = _pendingPreviewCompileRevision;
			_pendingPreviewCompileSnapshot = null;
		}

		_ = Task.Run( () => CompilePreviewSnapshot( snapshot, revision ) );
	}

	private void CompilePreviewSnapshot( string snapshot, int revision )
	{
		Session.CompiledGraph compiled = null;
		Exception error = null;

		try
		{
			// Every variation compiles on its own, into its own definition
			compiled = Session.CompileSnapshot( snapshot );
		}
		catch ( Exception e )
		{
			error = e;
		}

		var publish = false;

		lock ( _previewCompileLock )
		{
			publish = revision == _previewCompileRevision;
			_previewCompileWorkerRunning = false;
		}

		if ( publish )
			MainThread.Queue( () => ApplyPreviewCompile( revision, compiled, error ) );

		StartPreviewCompile();
	}

	private void ApplyPreviewCompile( int revision, Session.CompiledGraph compiled, Exception error )
	{
		lock ( _previewCompileLock )
		{
			if ( revision != _previewCompileRevision )
				return;
		}

		SetPreviewCompiling( false );

		if ( !_preview.IsValid() )
			return;

		if ( error is not null )
		{
			ApplyCompileFailure( error );
			return;
		}

		try
		{
			ApplyPreviewCompile( compiled );
		}
		catch ( Exception e )
		{
			ApplyPreviewFailure( e, "AnimGraph: failed to update preview" );
		}
	}

	/// <summary>
	/// Play a compiled graph in the preview, keeping its per variation results for live debug.
	/// </summary>
	private void ApplyPreviewCompile( Session.CompiledGraph compiled )
	{
		_previewVariationResults.Clear();

		foreach ( var (variationID, result) in compiled.Variations )
			_previewVariationResults[variationID] = result;

		ApplyPreviewCompileResult( SelectedVariationID );
	}

	private bool TryGetPreviewResult( string variationID, out Compiler.Result result )
	{
		return _previewVariationResults.TryGetValue( variationID ?? Variation.DefaultVariationID, out result )
			|| _previewVariationResults.TryGetValue( Variation.DefaultVariationID, out result );
	}

	/// <summary>
	/// A variation is its own graph, so switching to one plays the definition it compiled into.
	/// </summary>
	private bool TryApplyPreviewVariation( string variationID )
	{
		if ( !_preview.IsValid() || _previewCompileStale || _previewCompiling )
			return false;

		// The graph predates this variation, so we never compiled it to play
		if ( !_previewVariationResults.ContainsKey( variationID ?? Variation.DefaultVariationID ) )
			return false;

		try
		{
			ApplyPreviewCompileResult( variationID );
			return true;
		}
		catch ( Exception e )
		{
			ApplyPreviewFailure( e, "AnimGraph: failed to update preview" );
			return false;
		}
	}

	private int InvalidatePreviewCompile()
	{
		int revision;

		lock ( _previewCompileLock )
		{
			revision = ++_previewCompileRevision;
			_pendingPreviewCompileSnapshot = null;
		}

		SetPreviewCompiling( false );
		return revision;
	}

	private void SetPreviewCompiling( bool compiling )
	{
		if ( _previewCompiling == compiling )
			return;

		_previewCompiling = compiling;
		_preview?.IsCompiling = compiling;

		if ( _compilePreviewOption is not null )
		{
			_compilePreviewOption.Icon = compiling ? "schedule" : "refresh";
			_compilePreviewOption.ToolTip = compiling ? "Compiling preview..." : "Compile Preview";
			_compilePreviewOption.Enabled = !compiling;
		}

		_compileLog?.Refresh();
	}

	internal bool IsPreviewCompiling => _previewCompiling;

	private void UpdateSavedPreviewGraph()
	{
		_previewVariationResults.Clear();

		if ( _asset is null )
		{
			SetPreviewGraph( null );
			ClearLiveDebug();
			return;
		}

		SetPreviewGraph( Sandbox.Animation.Graph.Load( Variation.GetResourcePath( _asset.Path, SelectedVariationID ) ) );
		ApplyPreviewParameters();
		ClearLiveDebug();
	}

	private void ApplyPreviewCompileResult( string variationID )
	{
		if ( !TryGetPreviewResult( variationID, out var result ) )
			return;

		var liveDebug = BuildLiveDebugData( result, variationID );

		// A definition that failed to compile isn't safe to run, and a graph the runtime
		// refuses to instantiate leaves the model on its legacy animgraph rather than showing
		// nothing, so it's never played
		if ( result.Failed )
		{
			SetPreviewGraph( null );

			ApplyCompileResult( result, liveDebug );
			_previewCompileStale = false;
			return;
		}

		var definitionChanged = !result.Definition.AsSpan().SequenceEqual( _previewDefinition );

		// Procedural graphs are anonymous, the name is only there for logs and profiles
		if ( definitionChanged )
		{
			SetPreviewGraph( Sandbox.Animation.Graph.Create( "animgraph_preview", result.Definition )
				?? throw new InvalidOperationException( "GraphBuilder failed to create the preview graph." ) );

			_previewDefinition = result.Definition;
		}

		ApplyCompileResult( result, liveDebug );

		if ( definitionChanged )
			ApplyPreviewParameters();

		_previewCompileStale = false;
	}

	/// <summary>
	/// Play a graph in the preview and take ownership of it. Procedural graphs are ours alone, so
	/// the one we were playing is disposed here - graphs loaded from disk are shared and unaffected.
	/// </summary>
	private void SetPreviewGraph( Sandbox.Animation.Graph graph )
	{
		_debugSampler.Clear();

		// The definition describes the graph we were playing, not this one
		_previewDefinition = null;

		// Destroying the window drops the graph after the preview is already gone
		if ( _preview.IsValid() )
			_preview.SetAnimationGraph( graph );

		_previewGraph?.Dispose();
		_previewGraph = graph;
	}

	private LiveDebugData BuildLiveDebugData( Compiler.Result result, string variationID )
	{
		ArgumentNullException.ThrowIfNull( result );

		var currentStateMachines = _graph.AllNodes
			.OfType<StateMachineNode>()
			.ToDictionary( machine => machine.Identifier );
		var currentNodes = _graph.AllNodes
			.OfType<Node>()
			.ToDictionary( node => node.Identifier );

		// The compile ran on a snapshot of the graph, so its warnings point at nodes that are gone
		var warnings = result.Warnings
			.Select( warning => new Compiler.Warning(
				warning.Message,
				warning.Node is not null && currentNodes.TryGetValue( warning.Node.Identifier, out var node ) ? node : null,
				warning.Severity ) )
			.Concat( Validator.Validate( _graph, variationID ) )
			.ToArray();
		var stateMachines = new Dictionary<string, StateMachineLiveDebug>();

		foreach ( var (compiledMachine, debug) in result.StateMachines )
		{
			if ( !currentStateMachines.TryGetValue( compiledMachine.Identifier, out var machine ) )
				continue;

			var states = debug.StateNodes
				.Select( entry => (Index: compiledMachine.States.IndexOf( entry.Key ), entry.Value) )
				.Where( entry => entry.Index >= 0 && entry.Index < machine.States.Count )
				.ToDictionary( entry => machine.States[entry.Index], entry => entry.Value );
			var transitionsById = machine.Conduits
				.SelectMany( conduit => conduit.Transitions )
				.Concat( machine.GlobalTransitions )
				.ToDictionary( transition => transition.Identifier );
			var transitions = debug.TransitionNodes
				.Where( entry => transitionsById.ContainsKey( entry.Key.Identifier ) )
				.ToDictionary( entry => transitionsById[entry.Key.Identifier], entry => entry.Value );

			stateMachines[compiledMachine.Identifier] = new StateMachineLiveDebug( debug.NodeIndex, states, transitions );
		}

		var nodes = result.NodeIndices.ToDictionary( kv => kv.Key.Identifier, kv => kv.Value );
		var animationClips = result.NodeIndices
			.Select( kv => (kv.Value, AnimationName: kv.Key switch
			{
				Nodes.AnimationClipNode clip => clip.AnimationName,
				Nodes.TimeControlledAnimationClipNode clip => clip.AnimationName,
				_ => null,
			}) )
			.Where( clip => !string.IsNullOrWhiteSpace( clip.AnimationName ) )
			.ToArray();
		var layers = result.StateMachineLayers.ToDictionary( kv => kv.Key.Identifier, kv => kv.Value );
		var transitionTargets = _graph.AllNodes
			.OfType<StateMachineNode>()
			.SelectMany( machine => machine.Conduits.SelectMany( conduit =>
				conduit.Transitions.Select( transition => (transition.Identifier, Machine: machine, Target: machine.States.FirstOrDefault( state => state.Name == conduit.To )) ) ) )
			.Where( entry => entry.Target is not null )
			.ToDictionary( entry => entry.Identifier, entry => (entry.Machine, entry.Target) );

		return new LiveDebugData( warnings, stateMachines, nodes, layers, transitionTargets, animationClips );
	}

	private void ApplyCompileResult( Compiler.Result result, LiveDebugData liveDebug )
	{
		_debugSampler.Clear();
		CompileWarnings = liveDebug.Warnings;
		CompileTime = result.ElapsedMilliseconds;
		_liveDebug = liveDebug.StateMachines;
		_liveNodes = liveDebug.Nodes;
		_liveLayers = liveDebug.Layers;
		_transitionTargets = liveDebug.TransitionTargets;
		_liveAnimationClips = liveDebug.AnimationClips;
		_compileLog?.Refresh();
	}

	private void ApplyCompileFailure( Exception error )
	{
		ApplyPreviewFailure( error, "AnimGraph: failed to compile preview" );
	}

	private void ApplyPreviewFailure( Exception error, string logMessage )
	{
		Log.Warning( error, logMessage );
		CompileWarnings = [new Compiler.Warning( error.Message, null, Compiler.Severity.Error )];
		CompileTime = null;
		_compileLog?.Refresh();
	}

	private void ClearLiveDebug()
	{
		_debugSampler.Clear();
		_liveDebug = null;
		_liveNodes = null;
		_liveLayers = null;
		_transitionTargets = null;
		_liveAnimationClips = [];
	}

	internal void CollectActiveAnimations( SceneModel model, List<(string Name, float Cycle, float Duration)> animations )
	{
		animations.Clear();

		if ( model is null )
			return;

		foreach ( var clip in _liveAnimationClips )
		{
			if ( !model.Animation.Debug.TryGetNode( clip.NodeIndex, out var info ) || !info.IsActive )
				continue;

			animations.Add( (System.IO.Path.GetFileNameWithoutExtension( clip.AnimationName ), info.NormalizedTime, info.Duration) );
		}
	}

	[EditorEvent.Frame]
	private void UpdateLiveNodes()
	{
		var model = _liveNodes is not null ? _preview?.ModelRenderer?.SceneModel : null;
		_debugSampler.BeginFrame( model );
		_liveNodeChanges.Clear();
		UpdateLiveNodes( _graphView, model );
		UpdateLiveNodes( _workspace?.SecondaryView, model );
		UpdateLiveNodes( (_pageCanvas as INodePage)?.EmbeddedGraphView, model );
		StateMachineCanvas?.UpdateLiveState();

		if ( _properties?.LiveDebugNode is { } debugNode )
			UpdateLiveNode( debugNode, model );
	}

	private void UpdateLiveNodes( View graphView, SceneModel model )
	{
		if ( graphView is null || !graphView.Visible )
			return;

		// Nothing to show and nothing stale to clear - the common idle case
		if ( model is null && graphView.LiveDebugCleared )
			return;

		graphView.LiveDebugCleared = model is null;

		foreach ( var item in graphView.Items )
		{
			if ( item is not NodeEditor.NodeUI ui || ui.Node is not Node node )
				continue;

			if ( UpdateLiveNode( node, model ) )
				ui.Update();
		}
	}

	private bool UpdateLiveNode( Node node, SceneModel model )
	{
		if ( _liveNodeChanges.TryGetValue( node, out var changed ) )
			return changed;

		var nodeIndex = -1;
		GraphDebug.NodeInfo info = default;
		var hasInfo = model is not null
			&& _liveNodes.TryGetValue( node.Identifier, out nodeIndex )
			&& _debugSampler.TryGetInfo( nodeIndex, out info );
		var syncTrackEvents = Array.Empty<GraphDebug.SyncEvent>();
		var syncEventID = "";
		var hasDebugValue = false;
		ValueDebugInfo debugValue = default;

		if ( hasInfo && info.IsActive && info.Kind == GraphValueKind.Pose && _properties?.LiveDebugNode == node )
			_debugSampler.ReadPose( nodeIndex, out syncTrackEvents, out syncEventID );
		else if ( hasInfo && info.IsActive && node.HasValueOutput && ValueDebug.IsSupported( info.Kind ) )
			hasDebugValue = _debugSampler.TryGetValue( nodeIndex, out debugValue );

		var forceInvalidHighlight = node is Nodes.TransitionNode { CanBeForced: false }
			&& _transitionTargets.TryGetValue( node.Identifier, out var transition )
			&& TryGetLiveStateInfo( transition.Machine, transition.Target, out var targetInfo )
			&& targetInfo.IsActive;

		changed = node.SetLiveDebugInfo( hasInfo, info, syncTrackEvents, syncEventID, hasDebugValue, debugValue, forceInvalidHighlight );

		if ( node is Nodes.Blend1DNode blend )
			changed |= UpdateBlend1DDebug( blend, model, nodeIndex, hasInfo && info.IsInitialized );
		else if ( node is Nodes.Blend2DNode blend2D )
			changed |= UpdateBlend2DDebug( blend2D, model, nodeIndex, hasInfo && info.IsInitialized );
		else if ( node is Nodes.StateMachineLayerNode layer )
			changed |= UpdateStateMachineLayerDebug( layer, model );

		_liveNodeChanges.Add( node, changed );
		return changed;
	}

	private bool UpdateBlend1DDebug( Nodes.Blend1DNode node, SceneModel model, int nodeIndex, bool isInitialized )
	{
		if ( model is null
			|| !isInitialized
			|| !model.Animation.Debug.TryGetBlend( nodeIndex, out var blend ) )
		{
			return node.SetLiveBlendDebug( false );
		}

		int FindPoint( int sourceNode )
		{
			if ( sourceNode < 0 )
				return -1;

			return node.Points.FindIndex( point =>
			{
				var output = point.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault();
				var source = output?.GetConnectedNode( output.Pose );
				return source is not null
					&& _liveNodes.TryGetValue( source.Identifier, out var pointNode )
					&& pointNode == sourceNode;
			} );
		}

		return node.SetLiveBlendDebug( true, FindPoint( blend.SourceNodeIndex0 ), FindPoint( blend.SourceNodeIndex1 ), blend.Weight );
	}

	private static bool UpdateBlend2DDebug( Nodes.Blend2DNode node, SceneModel model, int nodeIndex, bool isInitialized )
	{
		if ( model is null
			|| !isInitialized
			|| !model.Animation.Debug.TryGetBlendSpace2DParameter( nodeIndex, out var parameter ) )
		{
			return node.SetLiveBlendDebug( false );
		}

		return node.SetLiveBlendDebug( true, parameter );
	}

	private bool UpdateStateMachineLayerDebug( Nodes.StateMachineLayerNode node, SceneModel model )
	{
		if ( model is null
			|| _liveLayers is null
			|| !_liveLayers.TryGetValue( node.Identifier, out var layer )
			|| !model.Animation.Debug.TryGetLayerWeight( layer.NodeIndex, layer.LayerIndex, out var weight ) )
		{
			return node.SetLiveLayerWeight( false );
		}

		return node.SetLiveLayerWeight( true, weight );
	}

	internal bool TryGetLiveStateInfo( StateMachineNode node, StateMachineNode.State state, out GraphDebug.NodeInfo info )
	{
		info = default;

		if ( _liveDebug is null )
			return false;

		if ( !_liveDebug.TryGetValue( node.Identifier, out var machine ) || !machine.StateNodes.TryGetValue( state, out var nodeIndex ) )
			return false;

		return _debugSampler.TryGetInfo( nodeIndex, out info );
	}

	internal bool TryGetLiveStatePoseDebugInfo(
		StateMachineNode node,
		StateMachineNode.State state,
		out GraphDebug.NodeInfo info,
		out GraphDebug.SyncEvent[] syncTrackEvents,
		out string syncEventID )
	{
		info = default;
		syncTrackEvents = [];
		syncEventID = "";

		if ( _liveDebug is null )
			return false;

		if ( !_liveDebug.TryGetValue( node.Identifier, out var machine )
			|| !machine.StateNodes.TryGetValue( state, out var nodeIndex )
			|| !_debugSampler.TryGetInfo( nodeIndex, out info ) )
		{
			return false;
		}

		if ( info.IsActive && info.Kind == GraphValueKind.Pose )
			_debugSampler.ReadPose( nodeIndex, out syncTrackEvents, out syncEventID );

		return true;
	}

	internal bool TryGetLiveStateMachineState(
		StateMachineNode node,
		out StateMachineNode.State activeState,
		out StateMachineNode.State transitionSource,
		out Nodes.TransitionNode transition,
		out float transitionProgress )
	{
		activeState = null;
		transitionSource = null;
		transition = null;
		transitionProgress = 0.0f;

		if ( _liveDebug is null || _preview?.ModelRenderer?.SceneModel is not { } model )
			return false;

		if ( !_liveDebug.TryGetValue( node.Identifier, out var machine ) )
			return false;

		if ( !_debugSampler.TryGetInfo( machine.NodeIndex, out var machineInfo )
			|| !machineInfo.IsActive
			|| !machineInfo.IsInitialized )
			return false;

		var hasState = model.Animation.Debug.TryGetStateMachine( machine.NodeIndex, out var state );
		transitionProgress = state.TransitionProgress;
		if ( !hasState || state.ActiveStateIndex < 0
			|| state.ActiveStateIndex >= node.States.Count )
			return false;

		activeState = node.States[state.ActiveStateIndex];

		if ( state.TransitionNodeIndex >= 0 && state.TransitionSourceStateIndex >= 0 && state.TransitionSourceStateIndex < node.States.Count )
		{
			transitionSource = node.States[state.TransitionSourceStateIndex];
			transition = machine.TransitionNodes.FirstOrDefault( candidate => candidate.Value == state.TransitionNodeIndex ).Key;
		}

		return true;
	}
}
