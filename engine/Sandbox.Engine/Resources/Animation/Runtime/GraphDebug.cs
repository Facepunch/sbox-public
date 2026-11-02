namespace Sandbox.Animation;

/// <summary>Live graph inspection. Node indices refer to the currently compiled graph; invalid indices return false.</summary>
public sealed class GraphDebug
{
	[Flags]
	public enum NodeFlags
	{
		None = 0,
		Active = 1 << 0,
		Initialized = 1 << 1,
		Valid = 1 << 2
	}

	public readonly record struct NodeInfo(
		NodeFlags Flags,
		GraphValueKind Kind,
		float Duration,
		float NormalizedTime,
		float PreviousNormalizedTime,
		int SyncEventCount,
		int SyncEventIndex,
		float SyncEventProgress )
	{
		public bool IsActive => (Flags & NodeFlags.Active) != 0;
		public bool IsInitialized => (Flags & NodeFlags.Initialized) != 0;
		public bool IsValid => (Flags & NodeFlags.Valid) != 0;
	}

	public readonly record struct NodeValue(
		bool Bool,
		float Float,
		StringToken Id,
		Vector3 Vector,
		Transform TargetTransform,
		bool IsTargetSet,
		bool IsBoneTarget,
		StringToken TargetBoneId );

	public readonly record struct SyncEvent( StringToken Id, float StartTime, float Duration );

	public readonly record struct StateMachineState(
		int ActiveStateIndex,
		int TransitionSourceStateIndex,
		int TransitionNodeIndex,
		float TransitionProgress );

	public readonly record struct BlendState( int SourceNodeIndex0, int SourceNodeIndex1, float Weight );

	private readonly AnimationAccessor _graph;
	internal GraphDebug( AnimationAccessor graph ) => _graph = graph;

	private NativeEngine.AnimationGraph2Controller GetController( int nodeIndex )
		=> nodeIndex is >= 0 and <= short.MaxValue ? _graph.Controller : default;

	public bool IsNodeActive( int nodeIndex )
	{
		var controller = GetController( nodeIndex );
		return !controller.IsNull && controller.IsGraphNodeActive( nodeIndex );
	}

	/// <summary>Read a named float channel from the graph's evaluated output pose.</summary>
	public bool TryGetFloatChannel( string set, string channel, out float value )
	{
		value = 0;
		var controller = _graph.Controller;
		return !controller.IsNull && controller.GetFloatChannelValue( set, channel, out value );
	}

	public bool TryGetNode( int nodeIndex, out NodeInfo info )
	{
		info = default;
		var controller = GetController( nodeIndex );
		if ( controller.IsNull || !controller.GetNodeDebugInfo(
			nodeIndex, out var flags, out var kind, out var duration, out var time, out var previousTime,
			out var eventCount, out var eventIndex, out var eventProgress ) )
			return false;

		info = new( (NodeFlags)flags, (GraphValueKind)kind, duration, time, previousTime, eventCount, eventIndex, eventProgress );
		return true;
	}

	public bool TryGetValue( int nodeIndex, out NodeValue value )
	{
		value = default;
		var controller = GetController( nodeIndex );
		if ( controller.IsNull || !controller.GetNodeDebugValue(
			nodeIndex, out var boolValue, out var floatValue, out var idValue, out var vectorValue,
			out var targetTransform, out var targetFlags, out var targetBoneId ) )
			return false;

		value = new( boolValue != 0, floatValue, new( idValue ), vectorValue, targetTransform,
			(targetFlags & 1) != 0, (targetFlags & 2) != 0, new( targetBoneId ) );
		return true;
	}

	public string GetIdName( StringToken id )
	{
		var controller = _graph.Controller;
		return controller.IsNull ? "" : controller.GetDebugIDName( id );
	}

	public bool TryGetSyncEvent( int nodeIndex, int eventIndex, out SyncEvent syncEvent )
	{
		syncEvent = default;
		var controller = GetController( nodeIndex );
		if ( controller.IsNull || !controller.GetSyncTrackEvent( nodeIndex, eventIndex, out var id, out var startTime, out var duration ) )
			return false;

		syncEvent = new( new( id ), startTime, duration );
		return true;
	}

	public bool TryGetStateMachine( int nodeIndex, out StateMachineState state )
	{
		state = new( -1, -1, -1, 0f );
		var controller = GetController( nodeIndex );
		if ( controller.IsNull || !controller.GetStateMachineTransitionState(
			nodeIndex, out var activeState, out var sourceState, out var transition, out var progress ) )
			return false;

		state = new( activeState, sourceState, transition, progress );
		return true;
	}

	public bool TryGetSelectedOption( int nodeIndex, out int optionIndex )
	{
		optionIndex = -1;
		var controller = GetController( nodeIndex );
		return !controller.IsNull && controller.GetSelectorState( nodeIndex, out optionIndex );
	}

	public bool TryGetTransitionProgress( int nodeIndex, out float progress )
	{
		progress = 0f;
		var controller = GetController( nodeIndex );
		return !controller.IsNull && controller.GetTransitionProgressPercentage( nodeIndex, out progress );
	}

	public bool TryGetBlend( int nodeIndex, out BlendState state )
	{
		state = new( -1, -1, 0f );
		var controller = GetController( nodeIndex );
		if ( controller.IsNull || !controller.GetParameterizedBlendDebugInfo(
			nodeIndex, out var source0, out var source1, out var weight ) )
			return false;

		state = new( source0, source1, weight );
		return true;
	}

	public bool TryGetBlendSpace2DParameter( int nodeIndex, out Vector2 parameter )
	{
		parameter = default;
		var controller = GetController( nodeIndex );
		return !controller.IsNull && controller.GetBlend2DParameter( nodeIndex, out parameter );
	}

	public bool TryGetLayerWeight( int nodeIndex, int layerIndex, out float weight )
	{
		weight = 0f;
		var controller = GetController( nodeIndex );
		return !controller.IsNull && controller.GetLayerWeight( nodeIndex, layerIndex, out weight );
	}

	public bool TryGetIkWeight( int nodeIndex, out float weight )
	{
		weight = 0f;
		var controller = GetController( nodeIndex );
		return !controller.IsNull && controller.GetIKWeight( nodeIndex, out weight );
	}

	public bool TryGetReferencedGraphStatus( int nodeIndex, out bool hasInstance )
	{
		hasInstance = false;
		var controller = GetController( nodeIndex );
		if ( controller.IsNull || !controller.HasDebuggableGraphInstance( nodeIndex, out var value ) )
			return false;

		hasInstance = value != 0;
		return true;
	}
}
