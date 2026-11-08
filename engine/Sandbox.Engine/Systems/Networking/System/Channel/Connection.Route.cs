using Sandbox.Network;

namespace Sandbox;

public abstract partial class Connection
{
	/// <summary>
	/// Messages to this peer go up to the host, which passes them on. For peers we have no direct route to, and
	/// clients in a lobby until their direct route works.
	/// </summary>
	internal virtual bool RoutesThroughHost => !HasDirectRoute && Id != Guid.Empty && System is { IsHost: false } && !IsHost;

	/// <summary>
	/// Can the transport reach this peer directly, see <see cref="OpenDirectRoute"/>.
	/// </summary>
	internal bool HasDirectRoute { get; private set; } = true;

	// Unreliable state doesn't need the reliable handover barrier; keep it flowing via the host.
	internal bool RelayFor( NetFlags flags ) => RoutesThroughHost ||
		(System is { IsHost: false } && !IsHost && _directRouteBarrier is not null && (flags & NetFlags.Reliable) == 0);

	TaskCompletionSource<byte[][]> _directRouteBarrier;
	Guid _directRouteToken;
	RealTimeSince _timeSinceDirectRouteOpened;
	RealTimeUntil _timeUntilDirectRetry;

	/// <summary>
	/// An unanswered <see cref="DirectRouteMsg"/> (an older peer, or a host that left) falls back to relaying after this.
	/// </summary>
	const float DirectRouteAckTimeout = 5;
	const float DirectRouteRetryDelay = 30;

	/// <summary>
	/// The transport can reach this peer directly now. What we sent through the host may still be on its way, so new
	/// messages wait until the peer says it got everything up to here.
	/// </summary>
	internal void OpenDirectRoute()
	{
		if ( HasDirectRoute || HasPendingSends || _timeUntilDirectRetry > 0 )
			return;

		var relayed = RoutesThroughHost;

		// The host passes it on after everything we sent through it before
		if ( relayed )
		{
			_directRouteToken = Guid.NewGuid();
			SendMessage( new DirectRouteMsg { Token = _directRouteToken }, NetFlags.Reliable );
		}

		HasDirectRoute = true;

		if ( !relayed )
			return;

		_directRouteBarrier = new();
		_timeSinceDirectRouteOpened = 0;
		QueueSend( _directRouteBarrier.Task, NetFlags.Reliable );
	}

	/// <summary>
	/// The direct route broke, or doesn't work yet. Messages go through the host until <see cref="OpenDirectRoute"/>.
	/// Reliable messages already handed to Steam can still be lost or arrive out of order with relayed messages
	/// when a session breaks. Recovering those requires message acknowledgements and deduplication across routes.
	/// </summary>
	internal void CloseDirectRoute()
	{
		HasDirectRoute = false;
		ReleaseDirectRouteBarrier();
	}

	/// <summary>
	/// The peer got our <see cref="DirectRouteMsg"/>, and everything we sent through the host before it.
	/// </summary>
	internal void OnDirectRouteAck( Guid token )
	{
		if ( _directRouteBarrier is not null && token == _directRouteToken )
			ReleaseDirectRouteBarrier();
	}

	void ReleaseDirectRouteBarrier()
	{
		_directRouteBarrier?.TrySetResult( [] );
		_directRouteBarrier = null;
	}

	void CheckDirectRouteBarrier()
	{
		// When either endpoint is the host, the old host's ack may never arrive and relaying is unnecessary.
		if ( System is { IsHost: true } || IsHost )
		{
			ReleaseDirectRouteBarrier();
			return;
		}

		if ( _directRouteBarrier is not null && _timeSinceDirectRouteOpened > DirectRouteAckTimeout )
		{
			// Without an ack the relayed messages may still be in flight. Keep them ordered through the host.
			CloseDirectRoute();
			_timeUntilDirectRetry = DirectRouteRetryDelay;
		}
	}
}
