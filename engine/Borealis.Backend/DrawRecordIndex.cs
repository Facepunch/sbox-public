using System.Linq;
using System.Numerics;
using System.Threading;

namespace Borealis.Backend;

/// <summary>What makes a draw record (<see cref="GpuDrivenTables.Draw"/>): it's made once per key.</summary>
internal readonly record struct DrawRecordKey( uint Geometry, uint Vertex, uint Pixel, uint Flags, uint FirstIndex, uint IndexCount, int BaseVertex, int IndexPage );

/// <summary>
/// Draw records by key, in flat arrays of structs: millions of them on a big map, which a dictionary of nodes kept as as many
/// objects for every full collection to walk. Found without a lock by every recording thread; added to and removed from
/// under a lock per shard of keys, so many new records at once (a map's first frames) don't wait on one. Record 0 is the
/// table's empty one and is never a value.
/// </summary>
internal sealed class DrawRecordIndex
{
	const int ShardBits = 4;
	const uint Removed = uint.MaxValue;

	struct Entry
	{
		public DrawRecordKey Key;
		public uint Value; // 0: empty; Removed: gone, still probed past; else the record, written after the key
	}

	sealed class Shard
	{
		// Replaced whole when it grows, so a reader holding the old one still sees every entry it had
		public Entry[] Entries = new Entry[64];
		public int Used, Live;
	}

	readonly Shard[] shards = Enumerable.Range( 0, 1 << ShardBits ).Select( _ => new Shard() ).ToArray();

	/// <summary>Records indexed.</summary>
	public int Count
	{
		get
		{
			var count = 0;
			foreach ( var shard in shards ) count += Volatile.Read( ref shard.Live );
			return count;
		}
	}

	// The record struct's hash combines fields by multiplying, so its low bits see only the fields' low bits: mixed (murmur3's
	// finaliser) before the shard and slot take them
	static int HashOf( in DrawRecordKey key )
	{
		var h = (uint)key.GetHashCode();
		h = (h ^ (h >> 16)) * 0x85ebca6bu;
		h = (h ^ (h >> 13)) * 0xc2b2ae35u;
		return (int)(h ^ (h >> 16));
	}

	/// <summary>The key's record, or 0.</summary>
	public uint Find( in DrawRecordKey key )
	{
		var hash = HashOf( key );
		return Find( Volatile.Read( ref shards[hash & ((1 << ShardBits) - 1)].Entries ), key, hash );
	}

	static uint Find( Entry[] entries, in DrawRecordKey key, int hash )
	{
		var mask = entries.Length - 1;
		for ( var i = (hash >>> ShardBits) & mask; ; i = (i + 1) & mask )
		{
			var value = Volatile.Read( ref entries[i].Value );
			if ( value == 0 ) return 0;
			if ( value != Removed && entries[i].Key == key ) return value;
		}
	}

	/// <summary>
	/// The key's record, or the one <paramref name="make"/> makes for it under its shard's lock (called once per key).
	/// <paramref name="made"/> says which.
	/// </summary>
	public uint FindOrAdd<TState>( in DrawRecordKey key, TState state, Func<TState, uint> make, out bool made )
	{
		made = false;
		var hash = HashOf( key );
		var shard = shards[hash & ((1 << ShardBits) - 1)];
		var found = Find( Volatile.Read( ref shard.Entries ), key, hash );
		if ( found != 0 ) return found;

		lock ( shard )
		{
			found = Find( shard.Entries, key, hash );
			if ( found != 0 ) return found;

			var value = make( state );
			if ( value == 0 || value == Removed ) throw new InvalidOperationException( $"Draw record {value} can't be indexed" );

			// Open addressing keeps under 70% of slots used, counting removed ones, which a growth drops
			if ( (shard.Used + 1) * 10 > shard.Entries.Length * 7 ) Rebuild( shard, shard.Live + 1 );
			Insert( shard.Entries, key, hash, value );
			shard.Used++;
			Volatile.Write( ref shard.Live, shard.Live + 1 );
			made = true;
			return value;
		}
	}

	static void Insert( Entry[] entries, in DrawRecordKey key, int hash, uint value )
	{
		var mask = entries.Length - 1;
		var i = (hash >>> ShardBits) & mask;
		while ( entries[i].Value != 0 ) i = (i + 1) & mask;
		entries[i].Key = key;
		Volatile.Write( ref entries[i].Value, value );
	}

	static void Rebuild( Shard shard, int live )
	{
		var length = 64;
		while ( live * 10 > length * 4 ) length *= 2;
		var entries = new Entry[length];
		foreach ( ref readonly var entry in shard.Entries.AsSpan() )
		{
			if ( entry.Value != 0 && entry.Value != Removed ) Insert( entries, entry.Key, HashOf( entry.Key ), entry.Value );
		}

		shard.Used = shard.Live;
		Volatile.Write( ref shard.Entries, entries );
	}

	/// <summary>Forget every record over the index page at <paramref name="slot"/> (its meshes have gone), adding them to <paramref name="removed"/> when it's given.</summary>
	public void RemovePage( int slot, List<uint> removed = null ) => Remove( slot, static ( slot, key ) => key.IndexPage == slot, removed );

	/// <summary>Forget every record naming one of <paramref name="materials"/>' records, which are never asked for again; how many.</summary>
	public long RemoveMaterials( IReadOnlySet<uint> materials )
	{
		// Almost every key names none of them: a bit per hash of the records turns those away without asking the set, which
		// a walk of millions of keys would do twice for each
		var bits = Math.Clamp( BitOperations.RoundUpToPowerOf2( (uint)materials.Count * 16 ), 4096u, 1u << 22 );
		var shift = 32 - BitOperations.Log2( bits );
		var filter = new ulong[bits / 64];
		foreach ( var record in materials )
		{
			var bit = (record * FilterMultiplier) >> shift;
			filter[bit >> 6] |= 1ul << (int)(bit & 63);
		}

		return Remove( (materials, filter, shift), static ( state, key ) => Names( state, key.Vertex ) || Names( state, key.Pixel ) );
	}

	const uint FilterMultiplier = 0x9E3779B1u;

	static bool Names( in (IReadOnlySet<uint> Materials, ulong[] Filter, int Shift) state, uint record )
	{
		var bit = (record * FilterMultiplier) >> state.Shift;
		return (state.Filter[bit >> 6] & (1ul << (int)(bit & 63))) != 0 && state.Materials.Contains( record );
	}

	long Remove<TState>( TState state, Func<TState, DrawRecordKey, bool> match, List<uint> into = null )
	{
		long total = 0;
		foreach ( var shard in shards )
		{
			lock ( shard )
			{
				var removed = 0;
				foreach ( ref var entry in shard.Entries.AsSpan() )
				{
					if ( entry.Value == 0 || entry.Value == Removed || !match( state, entry.Key ) ) continue;
					into?.Add( entry.Value );
					Volatile.Write( ref entry.Value, Removed );
					removed++;
				}

				if ( removed == 0 ) continue;
				total += removed;
				Volatile.Write( ref shard.Live, shard.Live - removed );
				if ( shard.Live * 10 < shard.Entries.Length && shard.Entries.Length > 64 ) Rebuild( shard, shard.Live );
			}
		}

		return total;
	}
}
