namespace Sandbox.Services;

/// <summary>
/// Telling Steam how long workshop items are played for - it shows on their pages, and in the creator's stats.
/// Our backend's told from the same heartbeat, but Steam only hears it from us. It stops tracking everything
/// when the game quits.
/// </summary>
internal static partial class Ugc
{
	/// <summary>
	/// The items we've told Steam we're playing
	/// </summary>
	static readonly HashSet<ulong> playing = new();

	/// <summary>
	/// These are the items being played now - Steam stops counting the rest, and starts on any new ones. 0s are skipped.
	/// </summary>
	internal static void SetPlaying( params ReadOnlySpan<ulong> ids )
	{
		ThreadSafe.AssertIsMainThread();

		List<ulong> stopped = null;

		foreach ( var id in playing )
		{
			if ( !ids.Contains( id ) )
				(stopped ??= new()).Add( id );
		}

		if ( stopped is not null )
		{
			foreach ( var id in stopped )
			{
				playing.Remove( id );
				NativeEngine.CUgcSubscriptions.StopPlaytimeTracking( id );
			}
		}

		foreach ( var id in ids )
		{
			if ( id != 0 && playing.Add( id ) )
				NativeEngine.CUgcSubscriptions.StartPlaytimeTracking( id );
		}
	}
}
