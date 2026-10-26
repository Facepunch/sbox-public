using System.Runtime.InteropServices;
using Sandbox.Engine;

namespace Sandbox.Network;

internal class SnapshotValueCache
{
	private struct Cached
	{
		public int Hash;
		public byte[] Bytes;
	}

	// The network object's own slots are small numbers and looked up for every object every tick
	private readonly Cached[] _direct = new Cached[8];
	private Dictionary<int, Cached> _bySlot;

	private ref Cached Get( int slot )
	{
		if ( (uint)slot < (uint)_direct.Length )
			return ref _direct[slot];

		_bySlot ??= new();
		return ref CollectionsMarshal.GetValueRefOrAddDefault( _bySlot, slot, out _ );
	}

	/// <summary>
	/// Get cached bytes from the specified value if they exist. If the value is different,
	/// then re-serialize and cache again.
	/// </summary>
	public byte[] GetCached<T>( int slot, in T value, out bool isEqual )
	{
		var hash = value?.GetHashCode() ?? 0;
		ref var cached = ref Get( slot );

		if ( cached.Bytes is not null && cached.Hash == hash )
		{
			isEqual = true;
			return cached.Bytes;
		}

		isEqual = false;

		var bs = ByteStream.Create( 64 );

		try
		{
			GlobalContext.Current.TypeLibrary.ToBytes( value, ref bs );
			var span = bs.ToSpan();

			// Values like transforms keep their size, write over the previous bytes instead of allocating. Only
			// the snapshot state shares this array and it hashes the new bytes, it doesn't keep the old ones.
			if ( cached.Bytes is not null && cached.Bytes.Length == span.Length )
				span.CopyTo( cached.Bytes );
			else
				cached.Bytes = span.ToArray();

			cached.Hash = hash;
			return cached.Bytes;
		}
		finally
		{
			bs.Dispose();
		}
	}

	public void Remove( int slot )
	{
		if ( (uint)slot < (uint)_direct.Length )
			_direct[slot] = default;
		else
			_bySlot?.Remove( slot );
	}

	public void Clear()
	{
		Array.Clear( _direct );
		_bySlot?.Clear();
	}
}
