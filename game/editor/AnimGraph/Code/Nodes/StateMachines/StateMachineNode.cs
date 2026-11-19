using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Plays one state graph at a time and blends to another when a transition condition becomes true. States can contain blend trees, nested state machines, or an off state.
/// <br/><br/><b>Use when:</b>&#160;Organizing animation into distinct modes with conditional transitions.
/// <br/><b>Important:</b>&#160;Entry overrides choose the first true state; otherwise the configured default, or first state, is used. Explicit conduits take priority over global transitions to the same target.
/// </summary>
[Title( "State Machine" ), Group( "State Machine" ), Icon( "hub" )]
public class StateMachineNode : Node
{
	public enum StateType
	{
		/// <summary>A regular state playing its blend tree graph.</summary>
		[Title( "Blend Tree" )]
		BlendTree,

		/// <summary>A state whose graph is a nested state machine.</summary>
		[Title( "State Machine" )]
		StateMachine,

		/// <summary>An inactive state producing no pose — used to turn layers off.</summary>
		Off,
	}

	/// <summary>
	/// An event ID emitted while in a state — on entry, while fully in, or on exit.
	/// </summary>
	public class StateEvent
	{
		[Property, KeyProperty, Title( "ID" )]
		public string Id { get; set; } = "";

		[Property]
		public bool IsEntry { get; set; } = true;

		[Property]
		public bool IsFullyInState { get; set; }

		[Property]
		public bool IsExit { get; set; }
	}

	/// <summary>
	/// An event ID emitted based on elapsed or remaining time in a state.
	/// </summary>
	public class TimedStateEvent
	{
		public enum EventType
		{
			[Title( "Time Elapsed" )]
			TimeElapsed,
			[Title( "Time Remaining" )]
			TimeRemaining,
		}

		[Property, KeyProperty, Title( "ID" )]
		public string Id { get; set; } = "";

		[Property]
		public EventType Type { get; set; } = EventType.TimeElapsed;

		[Property]
		public GraphBuilder.StateMachineBuilder.TimedEventComparison Operator { get; set; } = GraphBuilder.StateMachineBuilder.TimedEventComparison.GreaterThanEqual;

		/// <summary>
		/// Time in seconds to compare against.
		/// </summary>
		[Property]
		public float Time { get; set; }
	}

	/// <summary>
	/// A state owning its own pose graph, played while the state is active.
	/// </summary>
	public class State
	{
		[Property]
		public string Name { get; set; } = "State";

		[Property, Title( "Exit State" ), Description( "Allow the containing state machine to complete after this state's animation finishes." )]
		public bool IsExitState { get; set; }

		// Not editable in the property panel — type changes go through the context
		// menu conversions, which do the graph work. The canvas already shows the type.
		[Hide]
		public StateType Type { get; set; } = StateType.BlendTree;

		/// <summary>
		/// Event IDs emitted while in this state.
		/// </summary>
		[Property]
		public List<StateEvent> Events { get; set; } = new();

		/// <summary>
		/// Event IDs emitted based on time elapsed or remaining in this state.
		/// </summary>
		[Property]
		public List<TimedStateEvent> TimedEvents { get; set; } = new();

		[Property]
		public bool UseActualElapsedTimeInStateForTimedEvents { get; set; }

		[Hide]
		public List<string> TransitionOrder { get; set; } = new();

		[Hide]
		public Vector2 Position { get; set; }

		/// <summary>
		/// The state's pose graph, ending in a Graph Output node.
		/// </summary>
		[Hide]
		public Data Graph { get; set; } = Data.CreateSubGraph<PoseResultNode>();

		[Hide, JsonIgnore]
		public StateMachineNode NestedStateMachine
			=> Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault() is { } output
				? output.GetConnectedNode( output.Pose ) as StateMachineNode
				: null;

		/// <summary>
		/// Optional layer data — layer weight, root motion weight and bone mask
		/// used when this state machine drives a layer.
		/// </summary>
		[Hide]
		public Data LayerGraph { get; set; } = Data.CreateSubGraph<StateLayerDataNode>( GraphKind.LayerData );
	}

	/// <summary>
	/// An arrow between two states. Owns a graph holding one or more Transition
	/// nodes — each a separate way to take this conduit, with its own condition
	/// and blend settings.
	/// </summary>
	public class Conduit
	{
		[Hide]
		public string From { get; set; } = "";

		[Hide]
		public string To { get; set; } = "";

		/// <summary>
		/// The conduit's transitions and their conditions.
		/// </summary>
		[Hide]
		public Data Graph { get; set; } = Data.CreateSubGraph( GraphKind.TransitionConduit );

		[Hide, JsonIgnore]
		public IEnumerable<TransitionNode> Transitions => Graph?.Nodes.OfType<TransitionNode>() ?? [];

		/// <summary>
		/// Whether any transition in this conduit can actually fire.
		/// </summary>
		[Hide, JsonIgnore]
		public bool HasTransitions => Transitions.Any( t => t.Condition.IsValid );
	}

	[Property, Hide]
	public List<State> States { get; set; } = [new() { Name = "State 1" }];

	[Property, Hide]
	public List<Conduit> Conduits { get; set; } = new();

	/// <summary>
	/// Shared transitions into each target state. They are available from every
	/// other state unless an explicit conduit exists between the same two states.
	/// </summary>
	[Property, Hide]
	public Data GlobalGraph { get; set; } = Data.CreateSubGraph( GraphKind.GlobalTransitions );

	/// <summary>
	/// Conditions used to choose the state whenever this state machine initializes.
	/// The first true condition in state-list order wins; otherwise the default
	/// state is used.
	/// </summary>
	[Property, Hide]
	public Data EntryGraph { get; set; } = Data.CreateSubGraph<EntryStateOverrideConditionsNode>( GraphKind.EntryOverrides );

	/// <summary>
	/// The state the machine starts in. Falls back to the first state.
	/// </summary>
	[Hide]
	public string DefaultState { get; set; } = "";

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Pink;

	public override IEnumerable<Data> SubGraphs
	{
		get
		{
			foreach ( var state in States )
			{
				yield return state.Graph;

				if ( state.LayerGraph is not null )
					yield return state.LayerGraph;
			}

			foreach ( var conduit in Conduits )
				yield return conduit.Graph;

			if ( GlobalGraph is not null )
				yield return GlobalGraph;

			if ( EntryGraph is not null )
				yield return EntryGraph;
		}
	}

	public int DefaultStateIndex
	{
		get
		{
			var index = States.FindIndex( s => s.Name == DefaultState );
			return index >= 0 ? index : 0;
		}
	}

	[Hide, JsonIgnore]
	public EntryStateOverrideConditionsNode EntryConditions => EntryGraph?.Nodes.OfType<EntryStateOverrideConditionsNode>().FirstOrDefault();

	[Hide, JsonIgnore]
	public IEnumerable<GlobalTransitionNode> GlobalTransitions => GlobalGraph?.Nodes.OfType<GlobalTransitionNode>() ?? [];

	public readonly record struct TransitionRoute( TransitionNode Transition, string TargetState, bool IsGlobal );

	public IReadOnlyList<TransitionRoute> GetOrderedTransitions( State state )
	{
		if ( state is null )
			return [];

		var routes = new List<TransitionRoute>();
		var explicitTargets = new HashSet<string>();

		foreach ( var conduit in Conduits.Where( x => x.From == state.Name ) )
		{
			explicitTargets.Add( conduit.To );
			routes.AddRange( conduit.Transitions.Select( transition => new TransitionRoute( transition, conduit.To, false ) ) );
		}

		routes.AddRange( GlobalTransitions
			.Where( transition => transition.StateName != state.Name && !explicitTargets.Contains( transition.StateName ) )
			.Select( transition => new TransitionRoute( transition, transition.StateName, true ) ) );

		routes.RemoveAll( route => !route.Transition.Condition.IsValid );

		if ( state.TransitionOrder.Count == 0 )
			return routes;

		var order = state.TransitionOrder
			.Select( ( identifier, index ) => (identifier, index) )
			.DistinctBy( entry => entry.identifier )
			.ToDictionary( entry => entry.identifier, entry => entry.index );

		return routes
			.OrderBy( route => order.GetValueOrDefault( route.Transition.Identifier, int.MaxValue ) )
			.ToList();
	}

	public bool MoveTransition( State state, TransitionNode transition, int newIndex )
	{
		var identifiers = GetOrderedTransitions( state ).Select( route => route.Transition.Identifier ).ToList();
		var oldIndex = identifiers.IndexOf( transition.Identifier );

		if ( oldIndex < 0 )
			return false;

		newIndex = Math.Clamp( newIndex, 0, identifiers.Count - 1 );
		if ( oldIndex == newIndex )
			return false;

		identifiers.RemoveAt( oldIndex );
		identifiers.Insert( newIndex, transition.Identifier );
		state.TransitionOrder = identifiers;
		return true;
	}

	/// <summary>
	/// Keeps the auto-managed global transition nodes and entry override pins
	/// in sync with the state list. Call after adding, removing or renaming states.
	/// </summary>
	public void SyncStates()
	{
		var names = States.Select( s => s.Name ).ToList();

		GlobalGraph ??= Data.CreateSubGraph( GraphKind.GlobalTransitions );
		EntryGraph ??= Data.CreateSubGraph<EntryStateOverrideConditionsNode>( GraphKind.EntryOverrides );
		GlobalGraph.SetRoot( Root );
		EntryGraph.SetRoot( Root );

		foreach ( var state in States )
		{
			state.LayerGraph ??= Data.CreateSubGraph<StateLayerDataNode>( GraphKind.LayerData );
			state.LayerGraph.SetRoot( Root );
		}

		foreach ( var conduit in Conduits )
		{
			conduit.Graph ??= Data.CreateSubGraph( GraphKind.TransitionConduit );
			conduit.Graph.SetRoot( Root );
		}

		foreach ( var orphan in GlobalTransitions.Where( g => !names.Contains( g.StateName ) ).ToList() )
			GlobalGraph.RemoveNode( orphan );

		var existing = GlobalTransitions.Select( g => g.StateName ).ToHashSet();

		foreach ( var name in names.Where( n => !existing.Contains( n ) ) )
			GlobalGraph.AddNode( new GlobalTransitionNode { StateName = name } );

		var i = 0;
		foreach ( var name in names )
		{
			if ( GlobalTransitions.FirstOrDefault( g => g.StateName == name ) is { } node )
				node.Position = new Vector2( 400, i++ * 250 );
		}

		EntryConditions?.SyncStates( names );
	}

	/// <summary>
	/// Renames a state everywhere it's referenced by name.
	/// </summary>
	public void RenameState( string oldName, string newName )
	{
		foreach ( var conduit in Conduits )
		{
			if ( conduit.From == oldName ) conduit.From = newName;
			if ( conduit.To == oldName ) conduit.To = newName;
		}

		if ( GlobalTransitions.FirstOrDefault( g => g.StateName == oldName ) is { } global )
		{
			global.StateName = newName;
			global.Update();
		}

		EntryConditions?.RenameState( oldName, newName );

		if ( DefaultState == oldName )
			DefaultState = newName;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		if ( States.Count == 0 )
		{
			compiler.Warn( "State Machine has no states" );
			return compiler.ReferencePose();
		}

		if ( States.Count > GraphBuilder.StateMachineBuilder.MaxStates )
		{
			compiler.Error( $"State Machine supports at most {GraphBuilder.StateMachineBuilder.MaxStates} states" );
			return default;
		}

		if ( States.Select( s => s.Name ).Distinct().Count() != States.Count )
			compiler.Warn( "State Machine has duplicate state names - transitions will use the first match" );

		var stateMachine = compiler.Builder.StateMachine();
		var compiledStates = States.Select( state => EmitState( compiler, stateMachine, state ) ).ToList();
		var transitionNodes = new Dictionary<TransitionNode, CompiledTransition>();

		for ( var stateIdx = 0; stateIdx < States.Count; stateIdx++ )
		{
			var state = States[stateIdx];

			foreach ( var conduit in Conduits.Where( conduit => conduit.From == state.Name && States.All( target => target.Name != conduit.To ) ) )
				compiler.Warn( $"Conduit '{conduit.From} → {conduit.To}' targets an unknown state" );

			foreach ( var route in GetOrderedTransitions( state ) )
			{
				var targetIdx = States.FindIndex( candidate => candidate.Name == route.TargetState );
				if ( targetIdx < 0 )
				{
					if ( route.IsGlobal )
						compiler.Warn( $"Global transition targets an unknown state '{route.TargetState}'" );

					continue;
				}

				var transition = EmitTransition(
					compiler,
					stateMachine,
					route.Transition,
					compiledStates[targetIdx],
					compiledStates[stateIdx],
					transitionNodes );

				stateMachine.AddTransition(
					compiledStates[stateIdx].State,
					compiledStates[targetIdx].State,
					transition.Node,
					new GraphBuilder.StateMachineBuilder.TransitionRouteSettings
					{
						Condition = transition.Condition,
						CanBeForced = route.Transition.CanBeForced,
					} );
			}

			if ( EntryConditions is { } entry )
			{
				var entryCondition = compiler.EmitValue( entry, entry.ConditionFor( state.Name ) );
				if ( entryCondition is not null )
					stateMachine.SetEntryCondition( compiledStates[stateIdx].State, entryCondition );
			}
		}

		stateMachine.SetDefaultState( compiledStates[DefaultStateIndex].State );
		var emitted = stateMachine.Build();

		compiler.RegisterStateMachine(
			this,
			emitted,
			States.Select( ( state, index ) => (state, index) ).ToDictionary( entry => entry.state, entry => compiledStates[entry.index].NodeIndex ),
			transitionNodes.ToDictionary( entry => entry.Key, entry => entry.Value.Node ),
			EmitExitCondition( compiler, compiledStates ) );
		return emitted;
	}

	private GraphBuilder.Node EmitExitCondition( Compiler compiler, IReadOnlyList<CompiledState> compiledStates )
	{
		var exits = States.Select( ( state, index ) => (state, compiled: compiledStates[index]) )
			.Where( entry => entry.state.IsExitState )
			.Select( entry =>
			{
				var completed = compiler.Builder.StateCompletedCondition( entry.compiled.State, 0.0f, default, true );
				return entry.compiled.ExitCondition is { } childExit
					? compiler.Builder.And( completed, childExit )
					: completed;
			} ).ToArray();

		return exits.Length > 0 ? compiler.Builder.Or( exits ) : default;
	}


	private readonly record struct CompiledState( GraphBuilder.StateMachineBuilder.State State, int NodeIndex, GraphBuilder.Node ExitCondition );

	private readonly record struct CompiledTransition( GraphBuilder.Node Node, GraphBuilder.Node Condition );


	private CompiledTransition EmitTransition(
		Compiler compiler,
		GraphBuilder.StateMachineBuilder stateMachine,
		TransitionNode transition,
		CompiledState targetState,
		CompiledState sourceState,
		Dictionary<TransitionNode, CompiledTransition> cache )
	{
		if ( !cache.TryGetValue( transition, out var compiled ) )
		{
			var durationOverride = compiler.EmitValue( transition, transition.DurationOverride );
			var timeOffsetOverride = compiler.EmitValue( transition, transition.TimeOffsetOverride );

			var boneMask = compiler.EmitValue( transition, transition.StartBoneMask );
			if ( boneMask is not null && transition.BoneMaskBlendInTimePercentage <= 0 )
			{
				compiler.Warn( $"Transition '{transition.TransitionName}' has a start bone mask but no bone mask blend in time" );
				boneMask = default;
			}

			GraphBuilder.Node syncId = default;
			if ( transition.TargetSyncID.IsValid )
			{
				if ( transition.TimeMatch is >= TimeMatchMode.MatchSyncEventID and <= TimeMatchMode.MatchClosestSyncEventIDAndPercentage )
					syncId = compiler.EmitValue( transition, transition.TargetSyncID );
				else
					compiler.Warn( $"Transition '{transition.TransitionName}' has a target sync ID but its time match mode doesn't use one" );
			}

			var previousDuration = compiler.TransitionDuration;
			var previousState = compiler.SourceState;
			var previousExitCondition = compiler.SourceStateExitCondition;
			var previousDurationOverride = compiler.TransitionDurationOverride;

			compiler.TransitionDuration = transition.Duration;


			compiler.SourceState = transition is GlobalTransitionNode ? null : sourceState.State;
			compiler.SourceStateExitCondition = transition is GlobalTransitionNode ? default : sourceState.ExitCondition;
			compiler.TransitionDurationOverride = durationOverride;

			var condition = compiler.EmitValue( transition, transition.Condition );
			if ( condition is null )
				condition = compiler.ConstBool( false );

			compiler.TransitionDuration = previousDuration;
			compiler.SourceState = previousState;
			compiler.SourceStateExitCondition = previousExitCondition;
			compiler.TransitionDurationOverride = previousDurationOverride;

			var transitionNode = stateMachine.CreateTransition(
				targetState.State,
				new GraphBuilder.TransitionSettings
				{
					Duration = transition.Duration,
					DurationOverride = durationOverride,
					TimeOffset = transition.TimeOffset,
					TimeOffsetOverride = timeOffsetOverride,
					TimeMode = transition.TimeMatch switch
					{
						TimeMatchMode.Synchronized => GraphBuilder.TransitionTimeMode.Synchronized,
						TimeMatchMode.None or TimeMatchMode.OffsetTimeInSeconds => GraphBuilder.TransitionTimeMode.None,
						_ => GraphBuilder.TransitionTimeMode.MatchSourceTime,
					},
					SyncMatch = transition.TimeMatch switch
					{
						TimeMatchMode.MatchSourceSyncEventIndex => GraphBuilder.TransitionSyncMatch.EventIndex,
						TimeMatchMode.MatchSourceSyncEventPercentage => GraphBuilder.TransitionSyncMatch.EventPercentage,
						TimeMatchMode.MatchSourceSyncEventIndexAndPercentage => GraphBuilder.TransitionSyncMatch.EventIndex | GraphBuilder.TransitionSyncMatch.EventPercentage,
						TimeMatchMode.MatchSyncEventID or TimeMatchMode.MatchClosestSyncEventID => GraphBuilder.TransitionSyncMatch.EventId,
						TimeMatchMode.MatchSyncEventIDAndPercentage or TimeMatchMode.MatchClosestSyncEventIDAndPercentage => GraphBuilder.TransitionSyncMatch.EventId | GraphBuilder.TransitionSyncMatch.EventPercentage,
						_ => GraphBuilder.TransitionSyncMatch.None,
					},
					PreferClosestSyncEventId = transition.TimeMatch is TimeMatchMode.MatchClosestSyncEventID or TimeMatchMode.MatchClosestSyncEventIDAndPercentage,
					MatchTimeInSeconds = transition.TimeMatch == TimeMatchMode.MatchTimeInSeconds,
					OffsetTimeInSeconds = transition.TimeMatch == TimeMatchMode.OffsetTimeInSeconds,
					ClampDuration = transition.ClampDurationToSource,
					TargetSyncId = syncId,
					StartBoneMask = boneMask,
					BoneMaskBlendPercentage = transition.BoneMaskBlendInTimePercentage,
					BlendEasing = transition.BlendWeightEasing,
					RootMotionMode = transition.RootMotionBlend,
				} );

			compiled = new( transitionNode, condition );
			cache[transition] = compiled;
			compiler.SetEmitted( transition, transitionNode );
		}

		return compiled;
	}


	private CompiledState EmitState( Compiler compiler, GraphBuilder.StateMachineBuilder stateMachine, State state )
	{
		var isOff = state.Type == StateType.Off;

		var entry = state.Events.Where( e => e.IsEntry && !string.IsNullOrWhiteSpace( e.Id ) ).ToList();
		var fullyIn = state.Events.Where( e => e.IsFullyInState && !string.IsNullOrWhiteSpace( e.Id ) ).ToList();
		var exit = state.Events.Where( e => e.IsExit && !string.IsNullOrWhiteSpace( e.Id ) ).ToList();

		var elapsed = state.TimedEvents.Where( e => e.Type == TimedStateEvent.EventType.TimeElapsed && !string.IsNullOrWhiteSpace( e.Id ) ).ToList();
		var remaining = state.TimedEvents.Where( e => e.Type == TimedStateEvent.EventType.TimeRemaining && !string.IsNullOrWhiteSpace( e.Id ) ).ToList();

		var layer = state.LayerGraph?.Nodes.OfType<StateLayerDataNode>().FirstOrDefault();
		var compiledState = stateMachine.AddState(
			isOff ? default : EmitStateGraph( compiler, state ),
			new GraphBuilder.StateMachineBuilder.StateSettings
			{
				IsOffState = isOff,
				UseActualElapsedTimeInStateForTimedEvents = state.UseActualElapsedTimeInStateForTimedEvents,
				EntryEvents = entry.Select( e => e.Id ).ToList(),
				FullyInStateEvents = fullyIn.Select( e => e.Id ).ToList(),
				ExitEvents = exit.Select( e => e.Id ).ToList(),
				ElapsedEvents = elapsed.Select( e => (e.Id, e.Time, e.Operator) ).ToList(),
				RemainingEvents = remaining.Select( e => (e.Id, e.Time, e.Operator) ).ToList(),
				LayerWeight = layer is null ? default : compiler.EmitValue( layer, layer.LayerWeight ),
				LayerRootMotionWeight = layer is null ? default : compiler.EmitValue( layer, layer.RootMotionWeight ),
				LayerBoneMask = layer is null ? default : compiler.EmitValue( layer, layer.BoneMask ),
			} );

		var exitCondition = state.NestedStateMachine is { } nested
			? compiler.GetStateMachineExitCondition( nested )
			: default;
		return new( compiledState, compiledState.GetRuntimeIndex(), exitCondition );
	}


	private static GraphBuilder.Node EmitStateGraph( Compiler compiler, State state )
	{
		if ( state.Graph?.Nodes.OfType<PoseResultNode>().FirstOrDefault() is not { } output )
		{
			compiler.Warn( $"State '{state.Name}' has no output node" );
			return compiler.ReferencePose();
		}

		return compiler.EmitPose( output, output.Pose, $"State '{state.Name}'" );
	}

}
