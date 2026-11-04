namespace Sandbox;

/// <summary>
/// Coalesce repeated Steam notifications for the same party, including notifications
/// arriving just after the player accepts or dismisses its prompt.
/// </summary>
internal sealed class LobbyInviteFilter
{
	readonly Dictionary<ulong, double> _received = new();
	const double RepeatWindow = 30;

	internal bool TryReceive( ulong lobby, double now )
	{
		foreach ( var expired in _received.Where( x => now - x.Value >= RepeatWindow ).Select( x => x.Key ).ToArray() )
			_received.Remove( expired );

		return _received.TryAdd( lobby, now );
	}
}
