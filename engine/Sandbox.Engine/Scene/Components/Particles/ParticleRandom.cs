namespace Sandbox;

/// <summary>
/// The random numbers particles are made from: <see cref="Random.Shared"/>, so effects differ every run, unless
/// <see cref="SetSeed"/> has seeded them, for render tests, which need the same particles every run. Seeded numbers come
/// out in the same order when they're drawn in the same order: emitting is on the main thread, but what particles draw
/// while they update (collisions, attractors) is on worker threads, so it's thread safe there but not repeatable.
/// </summary>
internal static class ParticleRandom
{
	public static Random Shared { get; private set; } = Random.Shared;

	/// <summary>
	/// Make particles from a sequence seeded with <paramref name="seed"/>, or from <see cref="Random.Shared"/> again when null.
	/// </summary>
	public static void SetSeed( int? seed )
	{
		Shared = seed is { } value ? new Locked( value ) : Random.Shared;
	}

	/// <summary>
	/// A seeded random whose draws can't interleave, since worker threads draw from it too.
	/// </summary>
	sealed class Locked( int seed ) : Random
	{
		readonly Random random = new( seed );
		readonly System.Threading.Lock gate = new();

		public override int Next() { lock ( gate ) return random.Next(); }
		public override int Next( int maxValue ) { lock ( gate ) return random.Next( maxValue ); }
		public override int Next( int minValue, int maxValue ) { lock ( gate ) return random.Next( minValue, maxValue ); }
		public override long NextInt64() { lock ( gate ) return random.NextInt64(); }
		public override long NextInt64( long maxValue ) { lock ( gate ) return random.NextInt64( maxValue ); }
		public override long NextInt64( long minValue, long maxValue ) { lock ( gate ) return random.NextInt64( minValue, maxValue ); }
		public override double NextDouble() { lock ( gate ) return random.NextDouble(); }
		public override float NextSingle() { lock ( gate ) return random.NextSingle(); }
		public override void NextBytes( byte[] buffer ) { lock ( gate ) random.NextBytes( buffer ); }
		public override void NextBytes( Span<byte> buffer ) { lock ( gate ) random.NextBytes( buffer ); }
		protected override double Sample() { lock ( gate ) return random.NextDouble(); }
	}
}
