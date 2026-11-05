namespace Sandbox.Animation;

/// <summary>
/// Connects graphs to named External Graph nodes in the current graph.
/// Parent control parameters are not copied into external graphs, and this API does not expose
/// child parameter writes. Child control parameters use their authored defaults.
/// Connections are runtime-only and are lost when the animation controller is recreated.
/// Recording playback also discards live connections; reconnect them after playback stops if needed.
/// </summary>
public sealed class GraphExternalSlots
{
	/// <summary>
	/// Timing of the slot's last evaluated pose. Normalized time is 0-1; a looping clip wraps.
	/// IsActive indicates whether the slot contributed to the last graph update.
	/// </summary>
	public readonly record struct PlaybackInfo( float Duration, float NormalizedTime, bool IsActive );

	private readonly AnimationAccessor _animation;

	internal GraphExternalSlots( AnimationAccessor animation ) => _animation = animation;

	/// <summary>Whether the running graph declares this slot. Does not search referenced child graphs.</summary>
	public bool Contains( string slot )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( slot );
		var controller = _animation.Controller;
		return !controller.IsNull && controller.HasExternalGraphSlot( slot );
	}

	/// <summary>Whether a graph is connected, even if its branch is currently inactive.</summary>
	public bool IsConnected( string slot )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( slot );
		var controller = _animation.Controller;
		return !controller.IsNull && controller.IsExternalGraphSlotConnected( slot );
	}

	/// <summary>
	/// Connect a graph to an empty slot, starting its playback. The graph must use the same skeleton
	/// and must not declare external pose slots. Throws if the slot or controller is unavailable,
	/// the slot is occupied, the graph is incompatible, or recording playback is active.
	/// Each connection has its own instance. Disconnect first to replace or restart it.
	/// </summary>
	public void Connect( string slot, Graph graph )
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentException.ThrowIfNullOrWhiteSpace( slot );
		ArgumentNullException.ThrowIfNull( graph );
		if ( !graph.IsValid || graph.IsError )
			throw new ArgumentException( "The external graph must be loaded and valid.", nameof( graph ) );

		var controller = _animation.Controller;
		ValidateSlot( controller, slot );
		if ( controller.IsExternalGraphSlotConnected( slot ) )
			throw new InvalidOperationException( $"External graph slot '{slot}' is occupied. Disconnect it before connecting another graph." );
		if ( !controller.ConnectExternalGraph( slot, graph.native ) )
			throw new InvalidOperationException( $"Cannot connect external graph '{graph.Name}' to slot '{slot}'. It must use the parent graph's skeleton and cannot contain external pose slots." );
	}

	/// <summary>
	/// Disconnect the graph and restore the slot's fallback. An already empty slot is unchanged.
	/// Throws if the slot or controller is unavailable, or recording playback is active.
	/// While recording or retaining recorded data, disconnected instances and their resources stay
	/// alive until the recording is cleared or replaced, or the controller is destroyed.
	/// </summary>
	public void Disconnect( string slot )
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentException.ThrowIfNullOrWhiteSpace( slot );
		var controller = _animation.Controller;
		ValidateSlot( controller, slot );
		if ( !controller.DisconnectExternalGraph( slot ) )
			throw new InvalidOperationException( $"Cannot disconnect external graph slot '{slot}'." );
	}

	/// <summary>
	/// Read the connected slot's last evaluated timing, not a wall-clock timer.
	/// Returns false when no graph is connected. An inactive slot does not advance independently.
	/// </summary>
	public bool TryGetPlaybackInfo( string slot, out PlaybackInfo info )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( slot );
		info = default;
		var controller = _animation.Controller;
		if ( controller.IsNull || !controller.GetExternalGraphTiming( slot, out var duration, out var time, out var active ) )
			return false;

		info = new( duration, time, active != 0 );
		return true;
	}

	private static void ValidateSlot( NativeEngine.AnimationGraph2Controller controller, string slot )
	{
		if ( controller.IsNull )
			throw new InvalidOperationException( "No animation graph is running." );
		if ( !controller.HasExternalGraphSlot( slot ) )
			throw new ArgumentException( $"The animation graph has no external slot named '{slot}'.", nameof( slot ) );
		if ( controller.IsRecordingPlaybackActive() )
			throw new InvalidOperationException( "External graph slots cannot be changed during recording playback." );
	}
}
