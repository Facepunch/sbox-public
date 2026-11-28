using System.Collections.Concurrent;
using System.Threading;

namespace Sandbox.SteamApi;

/// <summary>
/// Reads a Steam client call result when its completion callback arrives.
/// </summary>
internal static class SteamCalls
{
	static readonly ConcurrentDictionary<ulong, Action<bool>> pending = new();

	internal static async Task<T> GetResultAsync<T>( ulong call, Func<ulong, T> readResult, CancellationToken token = default, T fallback = default )
	{
		if ( call == 0 || !SteamClient.IsValid ) return fallback;
		token.ThrowIfCancellationRequested();

		var session = SteamClient.Session;
		var completion = new TaskCompletionSource<T>( TaskCreationOptions.RunContinuationsAsynchronously );

		void Finish( bool ready )
		{
			try
			{
				// Read Steam's result during dispatch. User continuations run after we return to Steam.
				completion.SetResult( ready && SteamClient.IsValid && SteamClient.Session == session ? readResult( call ) : fallback );
			}
			catch ( Exception e )
			{
				completion.SetException( e );
			}
		}

		var entry = new KeyValuePair<ulong, Action<bool>>( call, Finish );
		if ( !pending.TryAdd( call, entry.Value ) )
			throw new InvalidOperationException( "This Steam call already has a waiter." );

		try
		{
			using var registration = token.Register( () =>
			{
				if ( pending.TryRemove( entry ) )
					completion.SetCanceled( token );
			} );

			// The SDK may already have dispatched the callback before we registered the waiter.
			var status = SteamClient.IsValid && SteamClient.Session == session ? SteamNative.Utils.GetCallStatus( call ) : Result.NoConnection;
			if ( status != Result.Pending && pending.TryRemove( entry ) )
				Finish( status == Result.OK );

			return await completion.Task.ConfigureAwait( false );
		}
		finally
		{
			pending.TryRemove( entry );
		}
	}

	internal static void Complete( ulong call )
	{
		if ( pending.TryRemove( call, out var finish ) )
			finish( true );
	}

	internal static void Shutdown()
	{
		foreach ( var entry in pending )
		{
			if ( pending.TryRemove( entry ) )
				entry.Value( false );
		}
	}
}
