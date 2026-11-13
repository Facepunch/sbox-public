namespace Sandbox.Animation;

public sealed partial class GraphBuilder
{
	/// <summary>
	/// How a transition combines the root motion of its source and target states.
	/// </summary>
	[Expose]
	public enum RootMotionBlend
	{
		Blend = 0,
		Additive = 1,
		IgnoreSource = 2,
		IgnoreTarget = 3,
	}

	/// <summary>
	/// How a transition aligns the target state's playback time.
	/// </summary>
	[Expose]
	public enum TransitionTimeMode
	{
		None,
		Synchronized,
		MatchSourceTime,
	}

	/// <summary>
	/// Which part of the source sync event is matched when
	/// <see cref="TransitionTimeMode.MatchSourceTime"/> is used.
	/// </summary>
	[Expose]
	[Flags]
	public enum TransitionSyncMatch
	{
		None,
		EventIndex = 1,
		EventId = 2,
		EventPercentage = 4,
	}

	/// <summary>
	/// Timing and blending options for a state-machine transition.
	/// </summary>
	public sealed class TransitionSettings
	{
		public float Duration { get; init; } = 0.2f;
		public Node DurationOverride { get; init; }
		public float TimeOffset { get; init; }
		public Node TimeOffsetOverride { get; init; }
		public TransitionTimeMode TimeMode { get; init; }
		public TransitionSyncMatch SyncMatch { get; init; }
		public bool PreferClosestSyncEventId { get; init; }
		public bool MatchTimeInSeconds { get; init; }
		public bool OffsetTimeInSeconds { get; init; }
		public bool ClampDuration { get; init; }
		public Node TargetSyncId { get; init; }
		public Node StartBoneMask { get; init; }
		public float BoneMaskBlendPercentage { get; init; } = 0.33f;
		public Easing BlendEasing { get; init; } = GraphBuilder.Easing.Linear;
		public RootMotionBlend RootMotionMode { get; init; } = GraphBuilder.RootMotionBlend.Blend;
		public bool CanBeForced { get; init; }
	}

	/// <summary>
	/// Add a state machine. Add states wrapping pose nodes, wire them together with
	/// transitions, then use it anywhere a <see cref="Node"/> is expected. The first
	/// state added is the default state.
	/// </summary>
	public StateMachineBuilder StateMachine()
	{
		return new StateMachineBuilder( this );
	}

	private Node CreateStateMachineNode( StateMachineBuilder stateMachine )
	{
		return AddNode( NodeKind.Pose, native =>
		{
			if ( stateMachine.States.Count == 0 )
				throw new InvalidOperationException( "StateMachine needs at least one state - add states with AddState(...)." );
			if ( stateMachine.States.Count > StateMachineBuilder.MaxStates )
				throw new InvalidOperationException( $"StateMachine supports at most {StateMachineBuilder.MaxStates} states." );
			var definition = NativeEngine.GraphStateMachineNode.Add( native );
			var states = definition.GetStateDefinition();
			for ( var i = 0; i < stateMachine.States.Count; i++ )
			{
				var state = states.Add();
				state.m_stateNodeIdx = (short)Resolve( stateMachine.States[i] );
				state.m_entryConditionNodeIdx = (short)ResolveOptional( stateMachine.StateEntryConditions[i] );
				var transitions = state.GetTransitionDefinition();
				foreach ( var transition in stateMachine.Transitions.Where( t => t.From == i ) )
				{
					var route = transitions.Add();
					route.m_targetStateIdx = (short)transition.To;
					route.m_transitionNodeIdx = (short)Resolve( transition.TransitionNode );
					route.m_conditionNodeIdx = (short)Resolve( transition.Condition );
					route.m_canBeForced = transition.CanBeForced;
				}
			}

			definition.m_defaultStateIndex = (short)stateMachine.DefaultStateIndex;
			return definition;
		} );
	}

	/// <summary>
	/// A state machine being built, created by <see cref="StateMachine"/>.
	/// </summary>
	public sealed class StateMachineBuilder
	{
		public const int MaxStates = 64;
		/// <summary>
		/// A state within a state machine.
		/// </summary>
		public sealed class State
		{
			internal readonly StateMachineBuilder Owner;
			internal readonly GraphBuilder Builder;
			internal readonly Node Node;
			internal readonly int Index;
			internal State( StateMachineBuilder owner, GraphBuilder builder, Node node, int index )
			{
				Owner = owner;
				Builder = builder;
				Node = node;
				Index = index;
			}
		}

		internal Node Node;
		internal Node LayerWeight;
		internal Node LayerBoneMask;
		internal Node LayerRootMotionWeight;
		internal int DefaultStateIndex;
		internal readonly List<Node> States = new();
		internal readonly List<Node> StateEntryConditions = new();
		internal readonly List<(int From, int To, Node TransitionNode, Node Condition, bool CanBeForced)> Transitions = new();
		private readonly GraphBuilder builder;
		internal StateMachineBuilder( GraphBuilder builder )
		{
			this.builder = builder;
		}

		[Expose]
		public enum TimedEventComparison
		{
			[Title( "≤" )]
			LessThanEqual,
			[Title( "≥" )]
			GreaterThanEqual,
		}

		/// <summary>
		/// Events and optional layer values for a state.
		/// </summary>
		public sealed class StateSettings
		{
			public bool IsOffState { get; init; }
			public bool UseActualElapsedTimeInStateForTimedEvents { get; init; }
			public IReadOnlyList<string> EntryEvents { get; init; } = [];
			public IReadOnlyList<string> FullyInStateEvents { get; init; } = [];
			public IReadOnlyList<string> ExitEvents { get; init; } = [];
			public IReadOnlyList<(string Id, float Time, TimedEventComparison Operator)> ElapsedEvents { get; init; } = [];
			public IReadOnlyList<(string Id, float Time, TimedEventComparison Operator)> RemainingEvents { get; init; } = [];
			public Node LayerWeight { get; init; }
			public Node LayerRootMotionWeight { get; init; }
			public Node LayerBoneMask { get; init; }
		}

		public void SetDefaultState( State state )
		{
			ValidateState( state, nameof( state ) );
			DefaultStateIndex = state.Index;
		}

		public Node Build()
		{
			if ( Node is not null )
				return Node;
			Node = builder.CreateStateMachineNode( this );
			return Node;
		}

		private void ValidateState( State state, string arg )
		{
			if ( state is null )
				throw new ArgumentException( $"The '{arg}' state is required but was not provided.", arg );
			if ( !ReferenceEquals( state.Owner, this ) )
				throw new ArgumentException( $"The '{arg}' state was not created by this state machine.", arg );
		}

		/// <summary>
		/// Add a state playing <paramref name="child"/>. The first state added is the default state.
		/// </summary>
		/// <param name="child">The pose the state plays.</param>
		/// <param name="entryEvents">Graph event IDs raised while transitioning into this state.</param>
		/// <param name="exitEvents">Graph event IDs raised while transitioning out of this state.</param>
		/// <param name="fullyInStateEvents">Graph event IDs raised while fully in this state.</param>
		public State AddState( Node child, IEnumerable<string> entryEvents = null, IEnumerable<string> exitEvents = null, IEnumerable<string> fullyInStateEvents = null ) => AddState( child, false, entryEvents, exitEvents, fullyInStateEvents );
		/// <summary>
		/// Add a state that produces no pose. Use an off state to disable an optional
		/// state-machine layer until another state is entered.
		/// </summary>
		public State AddOffState( IEnumerable<string> entryEvents = null, IEnumerable<string> exitEvents = null, IEnumerable<string> fullyInStateEvents = null ) => AddState( default, true, entryEvents, exitEvents, fullyInStateEvents );
		private State AddState( Node child, bool isOffState, IEnumerable<string> entryEvents, IEnumerable<string> exitEvents, IEnumerable<string> fullyInStateEvents )
		{
			if ( !isOffState )
				builder.RequirePose( child );
			var entry = entryEvents?.ToArray() ?? [];
			var exit = exitEvents?.ToArray() ?? [];
			var fully = fullyInStateEvents?.ToArray() ?? [];
			var state = builder.AddNode( NodeKind.Pose, native =>
			{
				var definition = NativeEngine.GraphStateNode.Add( native );
				definition.m_childNodeIdx = (short)(isOffState ? -1 : builder.Resolve( child ));
				definition.m_layerWeightNodeIdx = (short)builder.ResolveOptional( LayerWeight );
				definition.m_layerBoneMaskNodeIdx = (short)builder.ResolveOptional( LayerBoneMask );
				definition.m_layerRootMotionWeightNodeIdx = (short)builder.ResolveOptional( LayerRootMotionWeight );
				definition.m_isOffState = isOffState;
				if ( entry.Length > 0 )
				{
					var nativeEntryEvents = definition.GetEntryEvents();
					foreach ( var nativeValue in entry )
					{
						nativeEntryEvents.Add( native.InternID( IdString( nativeValue ) ) );
					}
				}

				if ( exit.Length > 0 )
				{
					var nativeExitEvents = definition.GetExitEvents();
					foreach ( var nativeValue in exit )
					{
						nativeExitEvents.Add( native.InternID( IdString( nativeValue ) ) );
					}
				}

				if ( fully.Length > 0 )
				{
					var nativeFullyInStateEvents = definition.GetFullyInStateEvents();
					foreach ( var nativeValue in fully )
					{
						nativeFullyInStateEvents.Add( native.InternID( IdString( nativeValue ) ) );
					}
				}

				return definition;
			} );
			States.Add( state );
			StateEntryConditions.Add( default );
			return new State( this, builder, state, States.Count - 1 );
		}

		public State AddState( Node child, StateSettings settings )
		{
			ArgumentNullException.ThrowIfNull( settings );
			if ( !settings.IsOffState )
				builder.RequirePose( child );
			builder.Optional( settings.LayerWeight, NodeKind.Float );
			builder.Optional( settings.LayerRootMotionWeight, NodeKind.Float );
			builder.Optional( settings.LayerBoneMask, NodeKind.BoneMask );
			var entryEvents = settings.EntryEvents ?? [];
			var fullyInStateEvents = settings.FullyInStateEvents ?? [];
			var exitEvents = settings.ExitEvents ?? [];
			var elapsedEvents = settings.ElapsedEvents ?? [];
			var remainingEvents = settings.RemainingEvents ?? [];
			var hasLayerData = settings.LayerWeight is not null || settings.LayerRootMotionWeight is not null || settings.LayerBoneMask is not null;
			var state = builder.AddNode( NodeKind.Pose, native =>
			{
				var definition = NativeEngine.GraphStateNode.Add( native );
				definition.m_childNodeIdx = (short)(settings.IsOffState ? -1 : builder.Resolve( child ));
				definition.m_isOffState = settings.IsOffState;
				definition.m_useActualElapsedTimeInStateForTimedEvents = settings.UseActualElapsedTimeInStateForTimedEvents;
				if ( entryEvents.Count > 0 )
				{
					var nativeEntryEvents = definition.GetEntryEvents();
					foreach ( var nativeValue in entryEvents )
					{
						nativeEntryEvents.Add( native.InternID( IdString( nativeValue ) ) );
					}
				}

				if ( fullyInStateEvents.Count > 0 )
				{
					var nativeFullyInStateEvents = definition.GetFullyInStateEvents();
					foreach ( var nativeValue in fullyInStateEvents )
					{
						nativeFullyInStateEvents.Add( native.InternID( IdString( nativeValue ) ) );
					}
				}

				if ( exitEvents.Count > 0 )
				{
					var nativeExitEvents = definition.GetExitEvents();
					foreach ( var nativeValue in exitEvents )
					{
						nativeExitEvents.Add( native.InternID( IdString( nativeValue ) ) );
					}
				}

				if ( elapsedEvents.Count > 0 )
				{
					var nativeTimedElapsedEvents = definition.GetTimedElapsedEvents();
					foreach ( var e in elapsedEvents )
					{
						var nativeItem = nativeTimedElapsedEvents.Add();
						nativeItem.SetID( native.InternID( IdString( e.Id ) ) );
						nativeItem.m_timeValue = e.Time;
						nativeItem.SetComparisonOperator( (int)e.Operator );
					}
				}

				if ( remainingEvents.Count > 0 )
				{
					var nativeTimedRemainingEvents = definition.GetTimedRemainingEvents();
					foreach ( var e in remainingEvents )
					{
						var nativeItem = nativeTimedRemainingEvents.Add();
						nativeItem.SetID( native.InternID( IdString( e.Id ) ) );
						nativeItem.m_timeValue = e.Time;
						nativeItem.SetComparisonOperator( (int)e.Operator );
					}
				}

				if ( hasLayerData )
				{
					definition.m_layerWeightNodeIdx = (short)builder.ResolveOptional( settings.LayerWeight );
					definition.m_layerRootMotionWeightNodeIdx = (short)builder.ResolveOptional( settings.LayerRootMotionWeight );
					definition.m_layerBoneMaskNodeIdx = (short)builder.ResolveOptional( settings.LayerBoneMask );
				}

				return definition;
			} );
			States.Add( state );
			StateEntryConditions.Add( default );
			return new State( this, builder, state, States.Count - 1 );
		}

		public void SetEntryCondition( State state, Node condition )
		{
			ValidateState( state, nameof( state ) );
			builder.Optional( condition, NodeKind.Bool, nameof( condition ) );
			StateEntryConditions[state.Index] = condition;
		}

		/// <summary>
		/// Add a transition between two states, taken when <paramref name="condition"/> is true.
		/// </summary>
		public Node AddTransition( State from, State to, Node condition, TransitionSettings settings )
		{
			ValidateState( from, nameof( from ) );
			ValidateState( to, nameof( to ) );
			builder.Require( condition, NodeKind.Bool );
			var transition = CreateTransition( to, settings );
			AddTransition( from, to, transition, new TransitionRouteSettings { Condition = condition, CanBeForced = settings.CanBeForced, } );
			return transition;
		}

		/// <summary>
		/// Create a transition node without adding a route, allowing it to be shared by multiple source states.
		/// </summary>
		public Node CreateTransition( State target, TransitionSettings settings )
		{
			ValidateState( target, nameof( target ) );
			ArgumentNullException.ThrowIfNull( settings );
			builder.Optional( settings.DurationOverride, NodeKind.Float );
			builder.Optional( settings.TimeOffsetOverride, NodeKind.Float );
			builder.Optional( settings.TargetSyncId, NodeKind.Id );
			builder.Optional( settings.StartBoneMask, NodeKind.BoneMask );
			uint flags = 0;
			if ( settings.ClampDuration )
				flags |= 1u << 0;
			if ( settings.TimeMode == TransitionTimeMode.Synchronized )
				flags |= 1u << 1;
			if ( settings.TimeMode == TransitionTimeMode.MatchSourceTime )
				flags |= 1u << 2;
			if ( (settings.SyncMatch & TransitionSyncMatch.EventIndex) != 0 )
				flags |= 1u << 3;
			if ( (settings.SyncMatch & TransitionSyncMatch.EventId) != 0 )
				flags |= 1u << 4;
			if ( (settings.SyncMatch & TransitionSyncMatch.EventPercentage) != 0 )
				flags |= 1u << 5;
			if ( settings.PreferClosestSyncEventId )
				flags |= 1u << 6;
			if ( settings.MatchTimeInSeconds )
				flags |= 1u << 7;
			if ( settings.OffsetTimeInSeconds )
				flags |= 1u << 8;
			var transition = builder.AddNode( NodeKind.Pose, native =>
			{
				var definition = NativeEngine.GraphTransitionNode.Add( native );
				definition.m_targetStateNodeIdx = (short)builder.Resolve( target.Node );
				definition.m_durationOverrideNodeIdx = (short)builder.ResolveOptional( settings.DurationOverride );
				definition.m_timeOffsetOverrideNodeIdx = (short)builder.ResolveOptional( settings.TimeOffsetOverride );
				definition.SetBlendWeightEasingOp( (int)settings.BlendEasing );
				definition.SetRootMotionBlend( (int)settings.RootMotionMode );
				definition.m_duration = settings.Duration;
				definition.m_timeOffset = settings.TimeOffset;
				definition.m_transitionOptions = flags;
				definition.m_targetSyncIDNodeIdx = (short)builder.ResolveOptional( settings.TargetSyncId );
				definition.m_startBoneMaskNodeIdx = (short)builder.ResolveOptional( settings.StartBoneMask );
				definition.m_boneMaskBlendInTimePercentage = settings.BoneMaskBlendPercentage;
				return definition;
			} );
			return transition;
		}

		/// <summary>
		/// Add a route using an existing transition node.
		/// </summary>
		public sealed class TransitionRouteSettings
		{
			public Node Condition { get; init; }
			public bool CanBeForced { get; init; }
		}

		public void AddTransition( State from, State to, Node transition, TransitionRouteSettings settings )
		{
			ArgumentNullException.ThrowIfNull( settings );
			ValidateState( from, nameof( from ) );
			ValidateState( to, nameof( to ) );
			builder.RequirePose( transition, nameof( transition ) );
			builder.Require( settings.Condition, NodeKind.Bool );
			Transitions.Add( (from.Index, to.Index, transition, settings.Condition, settings.CanBeForced) );
		}

		/// <summary>
		/// A condition that is true when <paramref name="state"/> has nearly played through -
		/// within <paramref name="transitionDuration"/> seconds of its end.
		/// </summary>
		public Node Completed( State state, float transitionDuration = 0.0f ) => Completed( state, transitionDuration, default );
		public Node Completed( State state, float transitionDuration, Node transitionDurationOverride )
			=> Completed( state, transitionDuration, transitionDurationOverride, false );

		public Node Completed( State state, float transitionDuration, Node transitionDurationOverride, bool requireFullyInState )
		{
			ValidateState( state, nameof( state ) );
			builder.Optional( transitionDurationOverride, NodeKind.Float, nameof( transitionDurationOverride ) );
			return builder.AddNode( NodeKind.Bool, native =>
			{
				var definition = NativeEngine.GraphStateCompletedConditionNode.Add( native );
				definition.m_sourceStateNodeIdx = (short)builder.Resolve( States[state.Index] );
				definition.m_transitionDurationOverrideNodeIdx = (short)builder.ResolveOptional( transitionDurationOverride );
				definition.m_transitionDuration = transitionDuration;
				definition.m_requireFullyInState = requireFullyInState;
				return definition;
			} );
		}

		/// <summary>
		/// What a time condition measures on a state.
		/// </summary>
		[Expose]
		public enum TimeComparisonType
		{
			PercentageThroughState,
			PercentageThroughSyncEvent,
			[Title( "Elapsed Time (Seconds)" )]
			ElapsedTime,
			[Title( "Time In State (Seconds)" )]
			TimeInState,
			[Hide, Obsolete( "Use ElapsedTime." )]
			CurrentTime = ElapsedTime,
		}

		/// <summary>
		/// How a time condition compares against its value.
		/// </summary>
		[Expose]
		public enum TimeComparison
		{
			[Title( "<" )]
			LessThan = 0,
			[Title( "≤" )]
			LessThanEqual,
			[Title( ">" )]
			GreaterThan,
			[Title( "≥" )]
			GreaterThanEqual,
		}

		/// <summary>
		/// A condition on a state's timing - e.g. true once a state is more than 80%
		/// through, useful for gating a transition.
		/// </summary>
		public sealed class TimeConditionSettings
		{
			public TimeComparisonType Type { get; init; }
			public TimeComparison Comparison { get; init; }
			public float Value { get; init; }
			public Node InputValue { get; init; }
		}

		public Node TimeCondition( State state, TimeConditionSettings settings )
		{
			ArgumentNullException.ThrowIfNull( settings );
			ValidateState( state, nameof( state ) );
			builder.Optional( settings.InputValue, NodeKind.Float );
			return builder.AddNode( NodeKind.Bool, native =>
			{
				var definition = NativeEngine.GraphTimeConditionNode.Add( native );
				definition.m_sourceStateNodeIdx = (short)builder.Resolve( States[state.Index] );
				definition.m_inputValueNodeIdx = (short)builder.ResolveOptional( settings.InputValue );
				definition.m_comparand = settings.Value;
				definition.SetType( (int)settings.Type );
				definition.SetOperator( (int)settings.Comparison );
				return definition;
			} );
		}

		public static implicit operator Node( StateMachineBuilder stateMachine ) => stateMachine.Build();
	}

	public Node StateCompletedCondition( StateMachineBuilder.State sourceState, float transitionDuration, Node transitionDurationOverride )
		=> StateCompletedCondition( sourceState, transitionDuration, transitionDurationOverride, false );

	public Node StateCompletedCondition( StateMachineBuilder.State sourceState, float transitionDuration, Node transitionDurationOverride, bool requireFullyInState )
	{
		ArgumentNullException.ThrowIfNull( sourceState );
		if ( !ReferenceEquals( sourceState.Builder, this ) )
			throw new ArgumentException( "The source state was created by a different GraphBuilder.", nameof( sourceState ) );
		return sourceState.Owner.Completed( sourceState, transitionDuration, transitionDurationOverride, requireFullyInState );
	}

	public Node TimeCondition( StateMachineBuilder.State sourceState, StateMachineBuilder.TimeConditionSettings settings )
	{
		ArgumentNullException.ThrowIfNull( sourceState );
		if ( !ReferenceEquals( sourceState.Builder, this ) )
			throw new ArgumentException( "The source state was created by a different GraphBuilder.", nameof( sourceState ) );
		return sourceState.Owner.TimeCondition( sourceState, settings );
	}
}
