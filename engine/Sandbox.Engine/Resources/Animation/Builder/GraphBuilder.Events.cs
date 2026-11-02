namespace Sandbox.Animation;

public sealed partial class GraphBuilder
{
	/// <summary>
	/// A phase of the walk cycle, baked into animations from their footstep events.
	/// A foot is "down" from its contact until halfway to the next contact, then the
	/// next foot is "passing" until it lands.
	/// </summary>
	[Expose]
	public enum FootPhase
	{
		LeftFootDown = 0,
		LeftFootPassing = 1,
		RightFootDown = 2,
		RightFootPassing = 3,
		/// <summary>Either right foot down or left foot passing.</summary>
		LeftPhase = 4,
		/// <summary>Either left foot down or right foot passing.</summary>
		RightPhase = 5,
		/// <summary>A section marked up as having no clear foot events.</summary>
		None = 6,
	}

	/// <summary>
	/// When several sampled events match, which one wins.
	/// </summary>
	[Expose]
	public enum EventPriority
	{
		HighestWeight,
		HighestPercentageThrough,
		[Hide, Obsolete( "Use HighestPercentageThrough." )]
		HighestProgress = HighestPercentageThrough,
	}

	/// <summary>
	/// How multiple event conditions are combined.
	/// </summary>
	[Expose]
	public enum EventMatch
	{
		/// <summary>Any one matching event satisfies the condition.</summary>
		Or,
		/// <summary>Every listed event must be sampled in the same update.</summary>
		And,
		[Hide, Obsolete( "Use Or." )]
		Any = Or,
		[Hide, Obsolete( "Use And." )]
		All = And,
	}

	/// <summary>
	/// Which kinds of sampled events an event query searches.
	/// </summary>
	[Expose]
	public enum EventSearch
	{
		SearchAll,
		OnlySearchGraphEvents,
		OnlySearchAnimEvents,
		[Hide, Obsolete( "Use SearchAll." )]
		All = SearchAll,
		[Hide, Obsolete( "Use OnlySearchGraphEvents." )]
		GraphEventsOnly = OnlySearchGraphEvents,
		[Hide, Obsolete( "Use OnlySearchAnimEvents." )]
		AnimEventsOnly = OnlySearchAnimEvents,
	}

	/// <summary>
	/// The type of graph event to match in <c>GraphEventCondition</c>.
	/// </summary>
	[Expose]
	public enum GraphEventType
	{
		Entry,
		FullyInState,
		Exit,
		Timed,
		Generic,
		Any,
	}

	/// <summary>
	/// How a <c>SyncEventCondition</c> compares the current sync event index.
	/// </summary>
	[Expose]
	public enum SyncEventTrigger
	{
		ExactlyAtEventIndex,
		GreaterThanEqualToEventIndex,
		[Hide, Obsolete( "Use ExactlyAtEventIndex." )]
		ExactIndex = ExactlyAtEventIndex,
		[Hide, Obsolete( "Use GreaterThanEqualToEventIndex." )]
		AtOrAfterIndex = GreaterThanEqualToEventIndex,
	}

	/// <summary>
	/// What <c>CurrentSyncEvent</c> reports.
	/// </summary>
	[Expose]
	public enum SyncEventInfo
	{
		IndexAndPercentage,
		IndexOnly,
		PercentageOnly,
	}

	/// <summary>
	/// The permission carried by a transition event, from most to least permissive.
	/// </summary>
	[Expose]
	public enum TransitionRule
	{
		AnyAllowed,
		FullyAllowed,
		ConditionallyAllowed,
		Blocked,
	}

	public sealed class EventQuerySettings
	{
		private bool? limitSearchToSourceState;
		public StateMachineBuilder.State SourceState { get; init; }
		public bool LimitSearchToSourceState { get => limitSearchToSourceState ?? SourceState is not null; init => limitSearchToSourceState = value; }
		public bool IgnoreInactiveEvents { get; init; }
	}

	public sealed class CurveEventSettings
	{
		public string EventId { get; init; }
		public float DefaultValue { get; init; }
		public Node DefaultValueNode { get; init; }
		public EventPriority Priority { get; init; } = EventPriority.HighestWeight;
		public bool LimitSearchToSourceState { get; init; }
		public bool IgnoreInactiveEvents { get; init; }
	}

	// EventConditionRules bit positions
	private static uint EventBaseFlags( bool limitToSourceState, bool ignoreInactive ) => (limitToSourceState ? 1u << 0 : 0u) | (ignoreInactive ? 1u << 1 : 0u);
	private static uint EventPriorityFlag( EventPriority priority ) => priority == EventPriority.HighestWeight ? 1u << 2 : 1u << 3;
	private static uint EventMatchFlag( EventMatch match ) => match == EventMatch.Or ? 1u << 4 : 1u << 5;
	private static uint EventSearchFlag( EventSearch search ) => search switch
	{
		EventSearch.OnlySearchGraphEvents => 1u << 6,
		EventSearch.OnlySearchAnimEvents => 1u << 7,
		_ => 1u << 8,
	};
	private Node ValidateSourceState( StateMachineBuilder.State sourceState )
	{
		if ( sourceState is null )
			return default;
		if ( !ReferenceEquals( sourceState.Builder, this ) )
			throw new ArgumentException( "The source state was created by a different GraphBuilder.", nameof( sourceState ) );
		return sourceState.Node;
	}

	/// <summary>
	/// True while the currently sampled animations are in the given <see cref="FootPhase"/> -
	/// e.g. gate a transition so it's only taken when a specific foot is planted.
	/// </summary>
	public Node FootstepCondition( FootPhase phase, EventQuerySettings settings = null )
	{
		settings ??= new();
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphFootEventConditionNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetPhaseCondition( (int)phase );
			definition.SetRules( EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) );
			return definition;
		} );
	}

	/// <summary>
	/// The percentage (0-1) through the currently sampled foot event of the given
	/// <see cref="FootPhase"/>, or -1 when none matches.
	/// </summary>
	public Node FootstepPercentage( FootPhase phase, EventPriority priority = EventPriority.HighestWeight, EventQuerySettings settings = null )
	{
		settings ??= new();
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFootstepEventPercentageThroughNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetPhaseCondition( (int)phase );
			definition.SetRules( EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) | EventPriorityFlag( priority ) );
			return definition;
		} );
	}

	/// <summary>
	/// The sync event ID carried by the highest-priority sampled foot event.
	/// </summary>
	public Node FootstepEventId( EventPriority priority = EventPriority.HighestWeight, EventQuerySettings settings = null )
	{
		settings ??= new();
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphFootstepEventIDNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetRules( EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) | EventPriorityFlag( priority ) );
			return definition;
		} );
	}

	/// <summary>
	/// True when the sampled events contain the given IDs, combined by <paramref name="match"/>.
	/// </summary>
	public Node IdEventCondition( IEnumerable<string> eventIds, EventMatch match = EventMatch.Or, EventSearch search = EventSearch.SearchAll, EventQuerySettings settings = null )
	{
		settings ??= new();
		var ids = eventIds?.Where( id => !string.IsNullOrWhiteSpace( id ) ).ToArray() ?? [];
		if ( ids.Length == 0 )
			throw new ArgumentException( "IdEventCondition needs at least one event ID.", nameof( eventIds ) );
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		var flags = EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) | EventMatchFlag( match ) | EventSearchFlag( search );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphIDEventConditionNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetRules( flags );
			var nativeEventIDs = definition.GetEventIDs();
			foreach ( var nativeValue in ids )
			{
				nativeEventIDs.Add( native.InternID( IdString( nativeValue ) ) );
			}

			return definition;
		} );
	}

	/// <summary>
	/// The ID of the highest-priority sampled ID animation event, or
	/// <paramref name="defaultValue"/> when none qualifies.
	/// </summary>
	public Node IdEvent( string defaultValue = null, EventPriority priority = EventPriority.HighestWeight, EventQuerySettings settings = null )
	{
		settings ??= new();
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphIDEventNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetRules( EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) | EventPriorityFlag( priority ) );
			definition.SetDefaultValue( native.InternID( IdString( defaultValue ) ) );
			return definition;
		} );
	}

	/// <summary>
	/// The percentage (0-1) through the sampled ID animation event matching
	/// <paramref name="eventId"/>, or -1 when none is sampled.
	/// </summary>
	public Node IdEventPercentage( string eventId, EventPriority priority = EventPriority.HighestWeight, EventQuerySettings settings = null )
	{
		settings ??= new();
		if ( string.IsNullOrWhiteSpace( eventId ) )
			throw new ArgumentException( "IdEventPercentage needs an event ID.", nameof( eventId ) );
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphIDEventPercentageThroughNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetRules( EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) | EventPriorityFlag( priority ) );
			definition.SetEventID( native.InternID( IdString( eventId ) ) );
			return definition;
		} );
	}

	/// <summary>
	/// True when the sampled graph events match the given (ID, type) conditions,
	/// combined by <paramref name="match"/>.
	/// </summary>
	public Node GraphEventCondition( IEnumerable<(string EventId, GraphEventType Type)> conditions, EventMatch match = EventMatch.Or, EventQuerySettings settings = null )
	{
		settings ??= new();
		var list = conditions?.Where( c => !string.IsNullOrWhiteSpace( c.EventId ) ).ToArray() ?? [];
		if ( list.Length == 0 )
			throw new ArgumentException( "GraphEventCondition needs at least one condition with an event ID.", nameof( conditions ) );
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		var flags = EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) | EventMatchFlag( match );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphGraphEventConditionNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetRules( flags );
			var nativeConditions = definition.GetConditions();
			foreach ( var c in list )
			{
				var nativeItem = nativeConditions.Add();
				nativeItem.SetEventID( native.InternID( IdString( c.EventId ) ) );
				nativeItem.SetEventTypeCondition( (int)c.Type );
			}

			return definition;
		} );
	}

	/// <summary>
	/// Samples the curve from float curve events on the currently playing animations,
	/// returning the curve value at the event's current percentage-through.
	/// </summary>
	public Node CurveEvent( CurveEventSettings settings = null )
	{
		settings ??= new();
		Optional( settings.DefaultValueNode, NodeKind.Float );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphFloatCurveEventNode.Add( native );
			definition.m_defaultValueNodeIdx = (short)ResolveOptional( settings.DefaultValueNode );
			definition.SetMatchEventID( native.InternID( IdString( settings.EventId ) ) );
			definition.m_defaultValue = settings.DefaultValue;
			definition.SetRules( EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) | EventPriorityFlag( settings.Priority ) );
			return definition;
		} );
	}

	/// <summary>
	/// Tests sampled transition animation events against a permission rule, optionally
	/// filtering by a specific marker ID.
	/// </summary>
	public Node TransitionEventCondition( TransitionRule rule = TransitionRule.AnyAllowed, string markerId = null, EventQuerySettings settings = null )
	{
		settings ??= new();
		var sourceState = ValidateSourceState( settings.SourceState );
		Optional( sourceState, NodeKind.Pose, nameof( sourceState ) );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphTransitionEventConditionNode.Add( native );
			definition.SetRequireRuleID( native.InternID( IdString( markerId ) ) );
			definition.SetRules( EventBaseFlags( settings.LimitSearchToSourceState, settings.IgnoreInactiveEvents ) );
			definition.m_sourceStateNodeIdx = (short)ResolveOptional( sourceState );
			definition.SetRuleCondition( (int)rule );
			return definition;
		} );
	}

	/// <summary>
	/// True when the source state's current sync event index matches
	/// <paramref name="syncEventIndex"/>. Only meaningful as a state-machine transition condition.
	/// </summary>
	public Node SyncEventCondition( StateMachineBuilder.State sourceState, int syncEventIndex, SyncEventTrigger trigger = SyncEventTrigger.ExactlyAtEventIndex )
	{
		ArgumentOutOfRangeException.ThrowIfNegative( syncEventIndex );
		var sourceStateNode = ValidateSourceState( sourceState );
		if ( sourceStateNode is null )
			throw new ArgumentException( "A source state is required.", nameof( sourceState ) );
		return SyncEventCondition( sourceStateNode, syncEventIndex, trigger );
	}

	private Node SyncEventCondition( Node sourceState, int syncEventIndex, SyncEventTrigger trigger )
	{
		ValidateOwner( sourceState, nameof( sourceState ) );
		return AddNode( NodeKind.Bool, native =>
		{
			var definition = NativeEngine.GraphSyncEventIndexConditionNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)Resolve( sourceState );
			definition.SetTriggerMode( (int)trigger );
			definition.m_syncEventIdx = syncEventIndex;
			return definition;
		} );
	}

	/// <summary>
	/// The ID of the source state's current sync event. Only meaningful as a
	/// state-machine transition condition.
	/// </summary>
	public Node CurrentSyncEventId( StateMachineBuilder.State sourceState )
	{
		var sourceStateNode = ValidateSourceState( sourceState );
		if ( sourceStateNode is null )
			throw new ArgumentException( "A source state is required.", nameof( sourceState ) );
		return CurrentSyncEventId( sourceStateNode );
	}

	private Node CurrentSyncEventId( Node sourceState )
	{
		ValidateOwner( sourceState, nameof( sourceState ) );
		return AddNode( NodeKind.Id, native =>
		{
			var definition = NativeEngine.GraphCurrentSyncEventIDNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)Resolve( sourceState );
			return definition;
		} );
	}

	/// <summary>
	/// The source state's current sync position, as an event index, a percentage
	/// through that event, or both. Only meaningful as a state-machine transition condition.
	/// </summary>
	public Node CurrentSyncEvent( StateMachineBuilder.State sourceState, SyncEventInfo info = SyncEventInfo.IndexAndPercentage )
	{
		var sourceStateNode = ValidateSourceState( sourceState );
		if ( sourceStateNode is null )
			throw new ArgumentException( "A source state is required.", nameof( sourceState ) );
		return CurrentSyncEvent( sourceStateNode, info );
	}

	private Node CurrentSyncEvent( Node sourceState, SyncEventInfo info )
	{
		ValidateOwner( sourceState, nameof( sourceState ) );
		return AddNode( NodeKind.Float, native =>
		{
			var definition = NativeEngine.GraphCurrentSyncEventNode.Add( native );
			definition.m_sourceStateNodeIdx = (short)Resolve( sourceState );
			definition.SetInfoType( (int)info );
			return definition;
		} );
	}
}
