namespace Borealis.Shadows;

/// <summary>
/// Which casters that aren't static count as settled: neither moved nor changed how they cast for <see cref="Seconds"/>. The
/// sun's cached far cascade (<see cref="ShadowMapper.FarCascadeCache"/>) holds them with the static ones, so scenes that
/// mark nothing static still get it. A settled caster that moves or changes is drawn every frame again, and where it was and
/// is goes into <see cref="RenderWorld.SettledChanges"/>. After <see cref="MaxSettles"/> of those it's restless: it settles
/// only after <see cref="RestlessSeconds"/>, so doors and lifts don't keep redrawing the cache, and once it has it starts
/// over.
/// </summary>
internal static class SettledCasters
{
	/// <summary>
	/// How long a caster stays put before it counts as settled.
	/// </summary>
	public static float Seconds { get; set; } = DefaultSeconds;

	/// <summary>
	/// How long a restless caster stays put before it counts as settled.
	/// </summary>
	public static float RestlessSeconds { get; set; } = DefaultRestlessSeconds;

	public const float DefaultSeconds = 2.0f;
	public const float DefaultRestlessSeconds = 30.0f;

	/// <summary>
	/// How many times a caster can move after settling before it's restless.
	/// </summary>
	public const int MaxSettles = 2;

	/// <summary>
	/// Marks a restless caster's stamp.
	/// </summary>
	public const ulong Restless = 1ul << 63;

	// Frames this process has counted, one a tick: stamps keep counting up whatever render host or world is current
	static ulong ticks;

	/// <summary>
	/// This frame, as a stamp of when a caster last moved: counted by <see cref="Tick"/>, from 1.
	/// </summary>
	public static ulong Now => ticks + 1;

	/// <summary>
	/// The latest stamp a caster can have last moved at and count as settled now: the last frame at least
	/// <see cref="Seconds"/> old.
	/// </summary>
	public static ulong Stamp { get; private set; }

	/// <summary>
	/// <see cref="Stamp"/> for restless casters: the last frame at least <see cref="RestlessSeconds"/> old.
	/// </summary>
	public static ulong RestlessStamp { get; private set; }

	static readonly Queue<(ulong Stamp, float Time)> frames = new();
	static readonly Queue<(ulong Stamp, float Time)> restlessFrames = new();

	/// <summary>
	/// Once an engine frame, with its time.
	/// </summary>
	public static void Tick( float now )
	{
		frames.Enqueue( (Now, now) );
		restlessFrames.Enqueue( (Now, now) );
		ticks++;

		while ( frames.Count > 0 && now - frames.Peek().Time >= Seconds )
			Stamp = frames.Dequeue().Stamp;
		while ( restlessFrames.Count > 0 && now - restlessFrames.Peek().Time >= RestlessSeconds )
			RestlessStamp = restlessFrames.Dequeue().Stamp;
	}

	/// <summary>
	/// Whether a caster that last moved at <paramref name="moved"/> counts as settled now.
	/// </summary>
	public static bool Settled( ulong moved ) => SettledBy( moved, Stamp, RestlessStamp );

	/// <summary>
	/// Whether a caster that last moved at <paramref name="moved"/> counted as settled when the stamps were these.
	/// </summary>
	public static bool SettledBy( ulong moved, ulong stamp, ulong restlessStamp )
		=> (moved & Restless) != 0 ? (moved & ~Restless) <= restlessStamp : moved <= stamp;

	/// <summary>
	/// For tests: everything that moved before this frame counts as settled, restless casters too, or, with
	/// <paramref name="settled"/> false, nothing does until the times have passed again.
	/// </summary>
	internal static void Reset( bool settled = false )
	{
		frames.Clear();
		restlessFrames.Clear();
		Stamp = RestlessStamp = settled ? Now - 1 : 0;
	}
}
