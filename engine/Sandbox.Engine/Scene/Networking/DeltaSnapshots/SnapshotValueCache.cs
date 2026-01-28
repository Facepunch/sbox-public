using System.Runtime.InteropServices;
using Sandbox.Engine;

namespace Sandbox.Network;

internal class SnapshotValueCache
{
	private readonly Dictionary<int, byte[]> _serialized = new();
	private readonly Dictionary<int, int> _hashCache = new();

	/// <summary>
	/// Get cached bytes from the specified value if they exist. If the value is different,
	/// then re-serialize and cache again.
	/// </summary>
	public byte[] GetCached<T>( int slot, in T value, out bool isEqual )
	{
		var hash = value?.GetHashCode() ?? 0;

		ref var cachedHash = ref CollectionsMarshal.GetValueRefOrAddDefault( _hashCache, slot, out bool exists );

		if ( exists && cachedHash == hash )
		{
			isEqual = true;
			return _serialized[slot];
		}

		var bytes = GlobalContext.Current.TypeLibrary.ToBytes( value );
		_serialized[slot] = bytes;

		cachedHash = hash;
		isEqual = false;

		return bytes;
	}

	public void Remove( int slot )
	{
		_serialized.Remove( slot );
		_hashCache.Remove( slot );
	}

	public void Clear()
	{
		_serialized.Clear();
		_hashCache.Clear();
	}
}
