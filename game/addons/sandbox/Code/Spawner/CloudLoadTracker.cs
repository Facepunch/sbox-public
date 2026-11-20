
/// <summary>
/// Tracks how many spawner payloads are loading on behalf of the local player, so the
/// <see cref="CloudLoadRing"/> shows on the client who asked for the spawn - not whoever happens
/// to be doing the loading (usually the host). Call <see cref="Track"/> with the requesting connection.
/// </summary>
public static class CloudLoadTracker
{
	static int _active;

	/// <summary>
	/// True while any spawner requested by the local player is still loading its payload.
	/// </summary>
	public static bool IsLoading => _active > 0;

	/// <summary>
	/// Show the loading ring for <paramref name="owner"/> until <paramref name="loading"/> completes.
	/// A null owner means the local connection.
	/// </summary>
	public static void Track( Task<bool> loading, Connection owner = null )
	{
		if ( loading is null || loading.IsCompleted ) return;

		if ( owner is null || owner == Connection.Local )
		{
			_active++;
			_ = Untrack( loading, null );
			return;
		}

		using ( Rpc.FilterInclude( owner ) ) Begin();
		_ = Untrack( loading, owner );
	}

	static async Task Untrack( Task<bool> loading, Connection owner )
	{
		try
		{
			await loading;
		}
		catch
		{
			// Failures are reported by the spawner; we only care that loading has stopped.
		}
		finally
		{
			if ( owner is null )
				_active--;
			else
				using ( Rpc.FilterInclude( owner ) ) End();
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void Begin() => _active++;

	[Rpc.Broadcast( NetFlags.HostOnly )]
	static void End() => _active = Math.Max( 0, _active - 1 );
}
