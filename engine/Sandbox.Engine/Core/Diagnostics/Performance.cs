using static Sandbox.Diagnostics.PerformanceStats;

namespace Sandbox.Diagnostics;

public static unsafe class Performance
{
	/// <summary>
	/// Record a frame state section in PerformanceStats
	/// </summary>
	public static ScopeSection Scope( string title )
	{
		if ( Application.IsUnitTest ) return default;
		return Timings.Get( title ).Scope();
	}

	/// <summary>
	/// This exists to allow the creation of performance scopes without
	/// </summary>
	public ref struct ScopeSection
	{
		internal Timings Source;
		internal FastTimer Timer;
		// Snapshot of total GC pause ticks at scope open so we can subtract
		// GC pause time from this scope's elapsed (keeping per-system times accurate).
		// -1 on non-main threads where attribution is unreliable.
		internal long GcPauseTicksAtStart;

		public void Dispose()
		{
			Source?.ScopeFinished( this );
		}
	}
}
