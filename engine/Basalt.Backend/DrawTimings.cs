namespace Basalt.Backend;

/// <summary>
/// Model draws timed by <see cref="BackendDevice.TimeDraws"/>: the time spent binding and recording, split into draws that
/// changed material or mode (binds) and draws of the same material again.
/// </summary>
public sealed class DrawTimings
{
	/// <summary>Draws timed, and of those, binds.</summary>
	public long Draws, Binds;

	/// <summary>Stopwatch ticks in binds and in the other draws.</summary>
	public long BindTicks, DrawTicks;

	/// <summary>Of the draws, multi-draws' draws in all (an aggregate's fragments).</summary>
	public long IndirectDraws;

	/// <summary>Draws that recorded nothing (no variant, no shader, or a way the backend can't draw yet), by material.</summary>
	public Dictionary<string, int> Failed { get; } = new();

	internal void Fail( string material )
	{
		lock ( Failed ) Failed[material ?? "(none)"] = Failed.GetValueOrDefault( material ?? "(none)" ) + 1;
	}

	internal void Add( long ticks, bool bind, bool drawn )
	{
		if ( !drawn ) return;
		Draws++;
		if ( bind )
		{
			Binds++;
			BindTicks += ticks;
		}
		else
		{
			DrawTicks += ticks;
		}
	}

	/// <summary>Nanoseconds a bind (a draw that changed material) took, on average.</summary>
	public double NanosecondsPerBind => Binds == 0 ? 0 : BindTicks * 1e9 / System.Diagnostics.Stopwatch.Frequency / Binds;

	/// <summary>Nanoseconds a draw of the same material again took.</summary>
	public double NanosecondsPerRepeat => Draws == Binds ? 0 : DrawTicks * 1e9 / System.Diagnostics.Stopwatch.Frequency / (Draws - Binds);

	/// <summary>Nanoseconds per draw over all of them.</summary>
	public double NanosecondsPerDraw => Draws == 0 ? 0 : (BindTicks + DrawTicks) * 1e9 / System.Diagnostics.Stopwatch.Frequency / Draws;

	/// <summary>Start counting again.</summary>
	public void Reset()
	{
		Draws = Binds = BindTicks = DrawTicks = IndirectDraws = 0;
		lock ( Failed ) Failed.Clear();
	}
}
