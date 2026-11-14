namespace Basalt.Shadows;

/// <summary>
/// Which casters that aren't static count as settled: neither moved nor changed how they cast for <see cref="Seconds"/>. The
/// sun's cached far cascade (<see cref="ShadowMapper.FarCascadeCache"/>) holds them with the static ones, so scenes that
/// mark nothing static still get it. A settled caster that moves or changes is drawn every frame again, and where it was and
/// is goes into <see cref="RenderWorld.SettledChanges"/>; after <see cref="MaxSettles"/> of those it never settles again, so
/// doors and lifts don't keep redrawing the cache.
/// </summary>
internal static class SettledCasters
{
	/// <summary>
	/// How long a caster stays put before it counts as settled.
	/// </summary>
	public const float Seconds = 2.0f;

	/// <summary>
	/// How many times a caster can move after settling before it never settles again.
	/// </summary>
	public const int MaxSettles = 2;

	/// <summary>
	/// A caster that never settles again.
	/// </summary>
	public const ulong Never = ulong.MaxValue;

	/// <summary>
	/// This frame, as a stamp of when a caster last moved: the engine frame plus one, so 0 is before every frame.
	/// </summary>
	public static ulong Now => (RenderHost.Current?.FrameCount ?? 0) + 1;

	/// <summary>
	/// The latest stamp a caster can have last moved at and count as settled now: the last frame at least
	/// <see cref="Seconds"/> old.
	/// </summary>
	public static ulong Stamp { get; private set; }

	static readonly Queue<(ulong Stamp, float Time)> frames = new();

	/// <summary>
	/// Once an engine frame, with its time.
	/// </summary>
	public static void Tick( float now )
	{
		frames.Enqueue( (Now, now) );
		while ( frames.Count > 0 && now - frames.Peek().Time >= Seconds )
			Stamp = frames.Dequeue().Stamp;
	}

	/// <summary>
	/// Whether a caster that last moved at <paramref name="moved"/> counts as settled now.
	/// </summary>
	public static bool Settled( ulong moved ) => moved <= Stamp;

	/// <summary>
	/// For tests: everything that moved before this frame counts as settled, or, with <paramref name="settled"/> false,
	/// nothing does until <see cref="Seconds"/> have passed again.
	/// </summary>
	internal static void Reset( bool settled = false )
	{
		frames.Clear();
		Stamp = settled ? Now - 1 : 0;
	}
}
