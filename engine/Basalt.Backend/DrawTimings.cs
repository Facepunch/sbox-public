using System.Threading;

namespace Basalt.Backend;

/// <summary>
/// Model draws timed by <see cref="BackendDevice.TimeDraws"/>: the time spent binding and recording, split into draws that
/// changed material or mode (binds) and draws of the same material again. Safe to add to from several recording threads;
/// a bind is a draw whose material or mode differs from the previous draw in the same context.
/// </summary>
public sealed class DrawTimings
{
	/// <summary>Draws timed, and of those, binds.</summary>
	public long Draws, Binds;

	/// <summary>Stopwatch ticks in binds and in the other draws.</summary>
	public long BindTicks, DrawTicks;

	/// <summary>Of the draws, multi-draws' draws in all (an aggregate's fragments).</summary>
	public long IndirectDraws;

	/// <summary>
	/// Of the draws that weren't binds, those with other object attributes (an object's own, its lightmap or light probe)
	/// than the draw before, and the ticks in them: same material, other per-object data. Their ticks aren't in
	/// <see cref="DrawTicks"/>.
	/// </summary>
	public long ObjectChanges, ObjectChangeTicks;

	/// <summary>Draws that recorded nothing (no variant, no shader, or a way the backend can't draw yet), by material.</summary>
	public Dictionary<string, int> Failed { get; } = new();

	internal void Fail( string material )
	{
		lock ( Failed ) Failed[material ?? "(none)"] = Failed.GetValueOrDefault( material ?? "(none)" ) + 1;
	}

	internal void Add( long ticks, bool bind, bool drawn, bool objectChange = false )
	{
		if ( !drawn ) return;
		Interlocked.Increment( ref Draws );
		if ( bind )
		{
			Interlocked.Increment( ref Binds );
			Interlocked.Add( ref BindTicks, ticks );
		}
		else if ( objectChange )
		{
			Interlocked.Increment( ref ObjectChanges );
			Interlocked.Add( ref ObjectChangeTicks, ticks );
		}
		else
		{
			Interlocked.Add( ref DrawTicks, ticks );
		}
	}

	internal void AddIndirect( int draws ) => Interlocked.Add( ref IndirectDraws, draws );

	/// <summary>Nanoseconds a bind (a draw that changed material) took, on average.</summary>
	public double NanosecondsPerBind => Binds == 0 ? 0 : BindTicks * 1e9 / System.Diagnostics.Stopwatch.Frequency / Binds;

	/// <summary>Nanoseconds a draw of the same material and object attributes again took.</summary>
	public double NanosecondsPerRepeat => Draws == Binds + ObjectChanges ? 0 : DrawTicks * 1e9 / System.Diagnostics.Stopwatch.Frequency / (Draws - Binds - ObjectChanges);

	/// <summary>Nanoseconds a draw of the same material with other object attributes took.</summary>
	public double NanosecondsPerObjectChange => ObjectChanges == 0 ? 0 : ObjectChangeTicks * 1e9 / System.Diagnostics.Stopwatch.Frequency / ObjectChanges;

	/// <summary>Nanoseconds per draw over all of them.</summary>
	public double NanosecondsPerDraw => Draws == 0 ? 0 : (BindTicks + DrawTicks + ObjectChangeTicks) * 1e9 / System.Diagnostics.Stopwatch.Frequency / Draws;

	/// <summary>Start counting again (not while anything records).</summary>
	public void Reset()
	{
		Draws = Binds = BindTicks = DrawTicks = IndirectDraws = ObjectChanges = ObjectChangeTicks = 0;
		lock ( Failed ) Failed.Clear();
	}
}
