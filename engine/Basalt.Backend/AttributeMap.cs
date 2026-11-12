using System.Threading;

namespace Basalt.Backend;

/// <summary>
/// Values by attribute token that any thread reads without taking a lock, while writers, one at a time under
/// <see cref="Gate"/>, change them. Open addressing in arrays at most half full, replaced whole when they'd fill, so a
/// reader holding the old ones still sees what they held; a value is written before its key, so a reader that finds a
/// key finds its value. Clearing empties the arrays in place and moves <see cref="epoch"/> on before and after, and a
/// read that overlapped it is made again under the lock.
/// </summary>
internal sealed class AttributeMap( object gate )
{
	sealed class Table( int capacity )
	{
		public readonly ulong[] Keys = new ulong[capacity];
		public readonly object[] Values = new object[capacity];
	}

	/// <summary>What writers hold, and what a read that overlapped a clear waits for.</summary>
	public object Gate { get; } = gate;

	Table table = new( 8 );
	int count;
	int epoch;

	/// <summary>Marks a slot in use, so key 0 is a key like any other.</summary>
	const ulong Used = 1UL << 32;

	public int Count => count;

	static int Start( uint key, int mask ) => (int)((key * 0x9E3779B1u) >> 8) & mask;

	public bool TryGet( uint key, out object value )
	{
		var before = Volatile.Read( ref epoch );
		if ( (before & 1) == 0 )
		{
			var found = Find( Volatile.Read( ref table ), key, out value );
			if ( Volatile.Read( ref epoch ) == before ) return found;
		}

		lock ( Gate ) return Find( table, key, out value );
	}

	static bool Find( Table table, uint key, out object value )
	{
		var keys = table.Keys;
		var mask = keys.Length - 1;
		var wanted = key | Used;
		for ( int i = Start( key, mask ); ; i = (i + 1) & mask )
		{
			var found = Volatile.Read( ref keys[i] );
			if ( found == wanted )
			{
				value = Volatile.Read( ref table.Values[i] );
				return true;
			}

			if ( found == 0 )
			{
				value = null;
				return false;
			}
		}
	}

	/// <summary>Set a value. Hold <see cref="Gate"/>.</summary>
	public void Set( uint key, object value )
	{
		var current = table;
		var keys = current.Keys;
		var mask = keys.Length - 1;
		var wanted = key | Used;
		for ( int i = Start( key, mask ); ; i = (i + 1) & mask )
		{
			if ( keys[i] == wanted )
			{
				Volatile.Write( ref current.Values[i], value );
				return;
			}

			if ( keys[i] != 0 ) continue;
			if ( (count + 1) * 2 > keys.Length )
			{
				Grow();
				Set( key, value );
				return;
			}

			Volatile.Write( ref current.Values[i], value );
			Volatile.Write( ref keys[i], wanted );
			count++;
			return;
		}
	}

	void Grow()
	{
		var old = table;
		var grown = new Table( old.Keys.Length * 2 );
		var mask = grown.Keys.Length - 1;
		for ( int j = 0; j < old.Keys.Length; j++ )
		{
			if ( old.Keys[j] == 0 ) continue;
			var i = Start( (uint)old.Keys[j], mask );
			while ( grown.Keys[i] != 0 ) i = (i + 1) & mask;
			grown.Keys[i] = old.Keys[j];
			grown.Values[i] = old.Values[j];
		}

		Volatile.Write( ref table, grown );
	}

	/// <summary>Remove every value. Hold <see cref="Gate"/>.</summary>
	public void Clear()
	{
		if ( count == 0 ) return;
		Interlocked.Increment( ref epoch );
		Array.Clear( table.Keys );
		Array.Clear( table.Values );
		count = 0;
		Interlocked.Increment( ref epoch );
	}

	/// <summary>Slots to go through with <see cref="At"/>. Hold <see cref="Gate"/> while going through them.</summary>
	public int Slots => table.Keys.Length;

	/// <summary>The key and value in a slot, false for an empty one. Hold <see cref="Gate"/>.</summary>
	public bool At( int slot, out uint key, out object value )
	{
		var current = table;
		key = (uint)current.Keys[slot];
		value = current.Values[slot];
		return current.Keys[slot] != 0;
	}
}
