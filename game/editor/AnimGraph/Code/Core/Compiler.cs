using Editor.AnimGraph.Nodes;

namespace Editor.AnimGraph;

public sealed class Compiler
{
	public sealed record StateMachineDebug( int NodeIndex, IReadOnlyDictionary<StateMachineNode.State, int> StateNodes, IReadOnlyDictionary<TransitionNode, int> TransitionNodes );
	public sealed record LayerDebug( int NodeIndex, int LayerIndex );
	private sealed record CompiledStateMachine(
		GraphBuilder.Node Node,
		IReadOnlyDictionary<StateMachineNode.State, int> StateNodes,
		IReadOnlyDictionary<TransitionNode, GraphBuilder.Node> TransitionNodes );
	private sealed record CompiledLayer( GraphBuilder.Node Node, int LayerIndex );

	public enum Severity
	{
		Warning,
		Error
	}

	public sealed record Warning( string Message, Node Node, Severity Severity = Severity.Warning )
	{
		public override string ToString() => Message;

		public bool IsError => Severity == Severity.Error;
	}

	public sealed record Result(
		byte[] Definition,
		int RootNodeIndex,
		IReadOnlyList<Warning> Warnings,
		IReadOnlyDictionary<StateMachineNode, StateMachineDebug> StateMachines,
		IReadOnlyDictionary<StateMachineLayerNode, LayerDebug> StateMachineLayers,
		IReadOnlyDictionary<Node, int> NodeIndices )
	{
		public IReadOnlyList<string> Dependencies { get; init; } = [];
		public double ElapsedMilliseconds { get; init; }

		public IEnumerable<Warning> Errors => Warnings.Where( warning => warning.IsError );

		public bool Failed => Warnings.Any( warning => warning.IsError );
	}

	public static Result Compile( Data graph ) => Compile( graph, Variation.DefaultVariationID );

	public static Result Compile( Data graph, string variationID )
	{
		ArgumentNullException.ThrowIfNull( graph );
		variationID ??= Variation.DefaultVariationID;
		if ( graph.Version != 1 )
			throw new InvalidOperationException( $"Unsupported animation graph version '{graph.Version}'" );
		if ( graph.Variations.TryGetCaseCorrectVariationID( variationID ) is null )
			throw new ArgumentException( $"Unknown animation graph variation '{variationID}'", nameof( variationID ) );
		var timer = Sandbox.Diagnostics.FastTimer.StartNew();
		var result = new Compiler( graph, variationID ).Run();
		return result with { ElapsedMilliseconds = timer.ElapsedMilliSeconds };
	}

	private readonly Data _graph;
	private readonly GraphBuilder _builder = Graph.Builder;
	private readonly List<Warning> _warnings = new();
	private readonly List<string> _parameterNames = new();
	private readonly List<string> _virtualParameterIds = new();
	private readonly List<string> _externalGraphSlots = new();
	private readonly List<string> _externalPoseSlots = new();
	private readonly Dictionary<Node, GraphBuilder.Node> _emitted = new();
	private readonly HashSet<Node> _emitting = new();
	private readonly List<Node> _compiling = new();
	private readonly Dictionary<StateMachineNode, CompiledStateMachine> _stateMachines = new();
	private readonly Dictionary<StateMachineLayerNode, CompiledLayer> _stateMachineLayers = new();
	private readonly HashSet<Node> _synchronizedNodesChecked = new();
	private readonly Dictionary<ParameterNode, IParameter> _resolvedParameters = new();
	private readonly Dictionary<IParameter, GraphBuilder.Node> _controlParameterNodes = new();
	private readonly Dictionary<VirtualParameter, GraphBuilder.Node> _compiledVirtualParameters = new();
	private readonly HashSet<VirtualParameter> _compilingVirtualParameters = new();
	private readonly Dictionary<Node, (GraphBuilder.StateMachineBuilder.State, float, GraphBuilder.Node)> _transitionScopes = new();

	internal string VariationID { get; }

	internal VariationHierarchy VariationHierarchy => _graph?.Variations;

	internal float TransitionDuration { get; set; }
	internal GraphBuilder.StateMachineBuilder.State SourceState { get; set; }
	internal GraphBuilder.Node TransitionDurationOverride { get; set; }

	private Compiler( Data graph, string variationID )
	{
		_graph = graph;

		VariationID = graph?.Variations.TryGetCaseCorrectVariationID( variationID ) ?? Variation.DefaultVariationID;
	}

	internal GraphBuilder Builder => _builder;

	private Result Run()
	{
		BeginVariation();

		CollectParameters();
		CollectVirtualParameters();

		var output = _graph.Nodes.OfType<PoseResultNode>().FirstOrDefault();
		GraphBuilder.Node root;

		if ( output is null )
		{
			Error( "Graph has no output node" );
			root = ReferencePose();
		}
		else
		{
			root = EmitPose( output, output.Pose, "Graph Output" );
		}

		byte[] definition = [];
		var model = VariationHierarchy.GetVariation( VariationID ).TargetModelPath;
		if ( !_warnings.Any( warning => warning.IsError ) )
		{
			try
			{
				definition = _builder.Compile( root, model, VariationID );
			}
			catch ( Exception e ) when ( e is ArgumentException or InvalidOperationException )
			{
				Error( e.Message );
			}
		}
		return new Result(
			definition,
			root?.GetRuntimeIndex() ?? -1,
			_warnings,
			_stateMachines.ToDictionary(
				entry => entry.Key,
				entry => new StateMachineDebug(
					entry.Value.Node.GetRuntimeIndex(),
					entry.Value.StateNodes,
					entry.Value.TransitionNodes.ToDictionary( transition => transition.Key, transition => transition.Value.GetRuntimeIndex() ) ) ),
			_stateMachineLayers.ToDictionary(
				entry => entry.Key,
				entry => new LayerDebug( entry.Value.Node.GetRuntimeIndex(), entry.Value.LayerIndex ) ),
			_emitted.Where( entry => entry.Value is not null ).ToDictionary( entry => entry.Key, entry => entry.Value.GetRuntimeIndex() ) )
		{
			Dependencies = _builder.GetResourceReferences().Append( model ).Where( path => !string.IsNullOrWhiteSpace( path ) ).Distinct().ToArray()
		};
	}

	private void BeginVariation()
	{
		if ( _graph?.Variations.GetVariation( VariationID ) is not { } variation )
			return;

		if ( string.IsNullOrWhiteSpace( variation.TargetModelPath ) )
			Warn( $"Variation '{VariationID}' has no model" );

		if ( string.IsNullOrWhiteSpace( variation.TargetSkeletonPath ) )
			Error( $"Variation '{VariationID}' has no skeleton" );

		_builder.Skeleton = variation.TargetSkeletonPath;
	}

	private void CollectParameters()
	{
		foreach ( var parameter in _graph.Parameters )
		{
			if ( string.IsNullOrWhiteSpace( parameter.Name ) )
			{
				Warn( "A graph parameter has no name" );
				continue;
			}

			if ( _parameterNames.Contains( parameter.Name, StringComparer.OrdinalIgnoreCase ) )
			{
				Warn( $"Duplicate parameter '{parameter.Name}'" );
				continue;
			}

			_parameterNames.Add( parameter.Name );
			var node = AddControlParameter( parameter, describe: true );
			_controlParameterNodes[parameter] = node;
		}

		foreach ( var node in _graph.AllNodes.OfType<ParameterNode>() )
		{
			if ( string.IsNullOrWhiteSpace( node.ParameterName ) )
			{
				Warn( $"{node.DisplayInfo.Name} has no parameter name", node );
				_resolvedParameters[node] = AddImplicitParameter( node );
				continue;
			}

			var parameter = ParameterReferences.Resolve( _graph.AllParameters, node.ParameterIdentifier, node.ParameterName, node.ParameterType );

			if ( parameter is null )
			{
				Warn( $"'{node.ParameterName}' is not a graph parameter", node );
				_resolvedParameters[node] = AddImplicitParameter( node );
				continue;
			}

			if ( parameter.Type != node.ParameterType )
			{
				Warn( $"'{node.ParameterName}' node type doesn't match the parameter's type", node );
				_resolvedParameters[node] = AddImplicitParameter( node );
				continue;
			}

			_resolvedParameters[node] = parameter;
		}
	}

	private Parameter AddImplicitParameter( ParameterNode node )
	{
		if ( node.ParameterType == AnimParameterType.BoneMask || string.IsNullOrWhiteSpace( node.ParameterName ) )
			return null;

		var existing = _controlParameterNodes.Keys.OfType<Parameter>()
			.FirstOrDefault( parameter => string.Equals( parameter.Name, node.ParameterName, StringComparison.OrdinalIgnoreCase ) );
		if ( existing is not null )
		{
			if ( existing.Type == node.ParameterType )
				return existing;
			Error( $"Implicit parameter '{node.ParameterName}' conflicts with an existing parameter", node );
			return null;
		}

		var parameter = new Parameter
		{
			Identifier = node.ParameterIdentifier,
			Name = node.ParameterName ?? "",
			Type = node.ParameterType
		};

		_parameterNames.Add( node.ParameterName ?? "" );
		var parameterNode = AddControlParameter( parameter, describe: false );
		_controlParameterNodes[parameter] = parameterNode;
		return parameter;
	}

	private GraphBuilder.Node AddControlParameter( Parameter parameter, bool describe )
	{
		return _builder.AddControlParameter(
			parameter.Name,
			parameter.Type switch
			{
				AnimParameterType.Bool => GraphBuilder.NodeKind.Bool,
				AnimParameterType.ID => GraphBuilder.NodeKind.Id,
				AnimParameterType.Float => GraphBuilder.NodeKind.Float,
				AnimParameterType.Vector => GraphBuilder.NodeKind.Vector,
				AnimParameterType.Target => GraphBuilder.NodeKind.Target,
				_ => throw new InvalidOperationException( $"Unsupported control parameter type: {parameter.Type}" ),
			},
			new GraphBuilder.ControlParameterSettings
			{
				Describe = describe,
				Min = parameter.HasRange ? parameter.Min : 0f,
				Max = parameter.HasRange ? parameter.Max : 0f,
				Options = parameter.Options,
				DefaultFloat = parameter.RuntimeDefaultFloat,
				DefaultBool = parameter.RuntimeDefaultBool,
				DefaultID = parameter.RuntimeDefaultID,
				DefaultVector = parameter.RuntimeDefaultVector,
			} );
	}

	private void CollectVirtualParameters()
	{
		foreach ( var parameter in _graph.VirtualParameters )
		{
			if ( string.IsNullOrWhiteSpace( parameter.Name ) )
			{
				Warn( "A virtual parameter has no name" );
				continue;
			}

			if ( _parameterNames.Contains( parameter.Name, StringComparer.OrdinalIgnoreCase )
				|| _virtualParameterIds.Contains( parameter.Name, StringComparer.OrdinalIgnoreCase ) )
			{
				Warn( $"Duplicate parameter '{parameter.Name}'" );
				continue;
			}

			var node = CompileVirtualParameter( parameter );
			_virtualParameterIds.Add( parameter.Name );
			if ( parameter.Lifetime == VirtualParameterLifetime.Graph )
				_builder.BindVirtualParameter( parameter.Name, node );
		}
	}

	private GraphBuilder.Node CompileVirtualParameter( VirtualParameter parameter )
	{
		if ( _compiledVirtualParameters.TryGetValue( parameter, out var node ) )
			return node;

		if ( !_compilingVirtualParameters.Add( parameter ) )
		{
			Warn( $"Virtual parameter '{parameter.Name}' is part of a cycle" );
			return ConstDefault( ParameterLinkType( parameter.Type ) );
		}

		var result = parameter.Graph.Nodes.OfType<VirtualParameterResultNode>().FirstOrDefault();
		var source = result?.Inputs.FirstOrDefault()?.ConnectedOutput?.Node as Node;

		if ( source is null )
		{
			Warn( $"Virtual parameter '{parameter.Name}' has no result connected" );
			node = ConstDefault( ParameterLinkType( parameter.Type ) );
		}
		else if ( result.Inputs.First().Type != ParameterLinkType( parameter.Type ) )
		{
			Error( $"Virtual parameter '{parameter.Name}' has an incompatible result", result );
			node = ConstDefault( ParameterLinkType( parameter.Type ) );
		}
		else
		{
			node = Emit( source );
		}

		_compilingVirtualParameters.Remove( parameter );
		_compiledVirtualParameters[parameter] = node;
		return node;
	}

	internal GraphBuilder.Node EmitParameterReference( ParameterNode node )
	{
		if ( !_resolvedParameters.TryGetValue( node, out var parameter ) )
			return ConstDefault( node.Outputs.First().Type );

		if ( parameter is null )
			return ConstDefault( node.Outputs.First().Type );

		if ( parameter is VirtualParameter virtualParameter )
			return CompileVirtualParameter( virtualParameter );

		if ( _controlParameterNodes.TryGetValue( parameter, out var controlNode ) )
			return controlNode;

		Warn( $"Control parameter '{parameter.Name}' could not be compiled", node );
		return ConstDefault( node.Outputs.First().Type );
	}

	internal GraphBuilder.Node EmitControlParameterReference(
		Node owner,
		string identifier,
		string name,
		AnimParameterType type )
	{
		var parameter = ParameterReferences.Resolve( _graph.Parameters, identifier, name, type );

		if ( parameter is null )
		{
			Warn( $"'{name}' is not a control parameter", owner );
			return null;
		}

		if ( parameter.Type != type )
		{
			Warn( $"'{parameter.Name}' parameter type doesn't match the selector's type", owner );
			return null;
		}

		if ( _controlParameterNodes.TryGetValue( parameter, out var controlNode ) )
			return controlNode;

		Warn( $"Control parameter '{parameter.Name}' could not be compiled", owner );
		return null;
	}

	private static Type ParameterLinkType( AnimParameterType type ) => type switch
	{
		AnimParameterType.Bool => typeof( BoolLink ),
		AnimParameterType.ID => typeof( IDLink ),
		AnimParameterType.Float => typeof( FloatLink ),
		AnimParameterType.Vector => typeof( VectorLink ),
		AnimParameterType.Target => typeof( TargetLink ),
		AnimParameterType.BoneMask => typeof( BoneMaskLink ),
		_ => typeof( FloatLink ),
	};

	internal GraphBuilder.Node Emit( Node node )
	{
		if ( node is ITransitionScopedNode )
			CheckTransitionScope( node );

		if ( _emitted.TryGetValue( node, out var cached ) )
			return cached;

		if ( !_emitting.Add( node ) )
		{
			Warn( $"{node.DisplayInfo.Name} is part of a cycle", node );
			return Fallback( node );
		}

		_compiling.Add( node );
		try
		{
			var emitted = node.Compile( this );
			_emitted[node] = emitted;
			return emitted;
		}
		catch ( Exception e ) when ( e is ArgumentException or InvalidOperationException )
		{
			Error( e.Message, node );
			return Fallback( node );
		}
		finally
		{
			_compiling.RemoveAt( _compiling.Count - 1 );
			_emitting.Remove( node );
		}
	}

	private void CheckTransitionScope( Node node )
	{
		var scope = (SourceState, TransitionDuration, TransitionDurationOverride);

		if ( !_transitionScopes.TryGetValue( node, out var previous ) )
		{
			_transitionScopes[node] = scope;
			return;
		}

		if ( !previous.Equals( scope ) )
			Error( $"{node.DisplayInfo.Name} is used by more than one transition - give each transition its own node", node );
	}

	internal bool RegisterExternalGraphSlot( string slotId, Node node )
	{
		return RegisterExternalSlot( _externalGraphSlots, slotId, node );
	}

	internal bool RegisterExternalPoseSlot( string slotId, Node node )
	{
		return RegisterExternalSlot( _externalPoseSlots, slotId, node );
	}

	private bool RegisterExternalSlot( List<string> slots, string slotId, Node node )
	{
		if ( string.IsNullOrWhiteSpace( slotId ) )
		{
			Warn( $"{node.DisplayInfo.Name} has no slot name", node );
			return false;
		}

		if ( slots.Contains( slotId, StringComparer.OrdinalIgnoreCase ) )
		{
			Warn( $"Duplicate external slot name '{slotId}'", node );
			return false;
		}

		slots.Add( slotId );
		return true;
	}

	internal GraphBuilder.Node EmitPose( Node owner, NodeInput input, string title = null, bool synchronized = false )
	{
		if ( owner.GetConnectedNode( input ) is { } node )
		{
			var output = node.Outputs.FirstOrDefault( x => x.Identifier == input.Output );
			if ( output?.Type != typeof( PoseLink ) )
			{
				Warn( $"{title ?? owner.DisplayInfo.Name} is connected to a non-pose output", owner );
				return ReferencePose();
			}

			if ( synchronized )
				WarnSynchronizedReverse( node );

			return Emit( node );
		}

		Warn( $"{title ?? owner.DisplayInfo.Name} has no pose connected", owner );
		return ReferencePose();
	}

	private void WarnSynchronizedReverse( Node node )
	{
		var pending = new Stack<Node>();
		pending.Push( node );

		while ( pending.TryPop( out var current ) )
		{
			if ( !_synchronizedNodesChecked.Add( current ) )
				continue;

			if ( current is AnimationClipNode clip && clip.GetConnectedNode( clip.PlayInReverse ) is not null )
				Warn( "Play In Reverse is ignored because this clip is driven by a synchronized blend or layer; use an unsynchronized branch or layer", clip );

			if ( current is GroupInputNode { ValueType: GraphBuilder.NodeKind.Pose } groupInput
				&& groupInput.Graph is Data { Owner: PoseGroupNode group }
				&& group.GetConnectedNode( groupInput.Source ) is { } groupSource )
				pending.Push( groupSource );

			foreach ( var input in current.Inputs.Where( input => input.Type == typeof( PoseLink ) ) )
			{
				if ( input.ConnectedOutput?.Node is Node source )
					pending.Push( source );
			}

			foreach ( var graph in current.SubGraphs.Where( graph => graph.Kind == GraphKind.BlendTree ) )
			{
				foreach ( var output in graph.Nodes.OfType<PoseResultNode>() )
				{
					if ( output.GetConnectedNode( output.Pose ) is { } source )
						pending.Push( source );
				}
			}
		}
	}

	internal GraphBuilder.Node EmitMask( Node owner, NodeInput input, string title = null )
	{
		if ( owner.GetConnectedNode( input ) is { } node )
		{
			var output = node.Outputs.FirstOrDefault( output => output.Identifier == input.Output );
			if ( output?.Type != typeof( BoneMaskLink ) )
			{
				Warn( $"{title ?? owner.DisplayInfo.Name} is connected to a non-bone-mask output", owner );
				return FixedWeightBoneMask( 1.0f );
			}

			return Emit( node );
		}

		Warn( $"{title ?? owner.DisplayInfo.Name} has no bone mask connected", owner );
		return FixedWeightBoneMask( 1.0f );
	}

	internal GraphBuilder.Node EmitValue( Node owner, NodeInput input )
	{
		if ( owner.GetConnectedNode( input ) is not { } node )
			return default;

		var output = node.Outputs.FirstOrDefault( output => output.Identifier == input.Output );
		var isCompatible = owner.Inputs
			.OfType<AnimPlugIn>()
			.Any( candidate => candidate.Info.GetInput( owner ) == input && candidate.Type == output?.Type );

		if ( isCompatible )
			return Emit( node );

		Warn( $"{owner.DisplayInfo.Name} has an incompatible value connection", owner );
		return default;
	}

	internal GraphBuilder.Node ConstFloat( float value ) => _builder.Constant( value );
	internal GraphBuilder.Node ConstBool( bool value ) => _builder.Constant( value );
	internal GraphBuilder.Node ConstID( string id = null ) => _builder.ConstantId( id );
	internal GraphBuilder.Node ConstVector( Vector3 value ) => _builder.Constant( value );
	internal GraphBuilder.Node NullTarget() => _builder.NullTarget();
	internal GraphBuilder.Node ReferencePose() => _builder.ReferencePose();
	internal GraphBuilder.Node FixedWeightBoneMask( float weight ) => _builder.BoneMask( weight );

	private GraphBuilder.Node Fallback( Node node )
	{
		var outputType = node.Outputs.OfType<AnimPlugOut>().FirstOrDefault()?.Type;
		return outputType is null ? ReferencePose() : ConstDefault( outputType );
	}

	internal GraphBuilder.Node ConstDefault( Type linkType )
	{
		if ( linkType == typeof( FloatLink ) ) return ConstFloat( 0.0f );
		if ( linkType == typeof( BoolLink ) ) return ConstBool( false );
		if ( linkType == typeof( IDLink ) ) return ConstID();
		if ( linkType == typeof( VectorLink ) ) return ConstVector( Vector3.Zero );
		if ( linkType == typeof( TargetLink ) ) return NullTarget();
		if ( linkType == typeof( BoneMaskLink ) ) return FixedWeightBoneMask( 1.0f );
		return ReferencePose();
	}

	internal void SetEmitted( Node node, GraphBuilder.Node emitted ) => _emitted[node] = emitted;
	internal void RegisterLayerDebug( StateMachineLayerNode node, GraphBuilder.Node emitted, int layerIndex ) => _stateMachineLayers[node] = new( emitted, layerIndex );
	internal void RegisterStateMachine(
		StateMachineNode node,
		GraphBuilder.Node emitted,
		IReadOnlyDictionary<StateMachineNode.State, int> states,
		IReadOnlyDictionary<TransitionNode, GraphBuilder.Node> transitions )
		=> _stateMachines[node] = new( emitted, states, transitions );

	internal void Warn( string message, Node node = null ) => AddMessage( message, node, Severity.Warning );

	internal void Error( string message, Node node = null ) => AddMessage( message, node, Severity.Error );

	private void AddMessage( string message, Node node, Severity severity )
	{
		if ( _warnings.All( w => w.Message != message || w.Severity != severity ) )
			_warnings.Add( new Warning( message, node ?? (_compiling.Count > 0 ? _compiling[^1] : null), severity ) );
	}
}
