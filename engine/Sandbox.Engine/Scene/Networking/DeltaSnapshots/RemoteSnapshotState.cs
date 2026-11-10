namespace Sandbox.Network;

/// <summary>
/// For an object another connection sends us snapshots of: the newest snapshot each slot's value came from,
/// so values from older snapshots that arrive late are ignored.
/// </summary>
internal class RemoteSnapshotState
{
	// One of these exists per (connection, object) and objects have few slots, so a flat array searched by key
	// beats a dictionary.
	private int[] _keys = [];
	private uint[] _snapshotIds = [];
	private int _count;

	public Guid ObjectId { get; init; }

	/// <summary>
	/// Whether the incoming snapshot id is newer than our last processed one. Ids are 32 bits so a slot can go unchanged
	/// for years of snapshots, and wrapping is handled by the signed difference.
	/// </summary>
	internal static bool IsNewer( uint newId, uint lastId ) => (int)(newId - lastId) > 0;

	/// <summary>
	/// A value for <paramref name="slot"/> arrived in <paramref name="snapshotId"/>. Returns whether it's newer than
	/// what we have, and if so remembers it.
	/// </summary>
	public bool Accept( int slot, uint snapshotId )
	{
		var index = _keys.AsSpan( 0, _count ).IndexOf( slot );

		if ( index < 0 )
		{
			if ( _count == _keys.Length )
			{
				var size = Math.Max( 4, _count * 2 );
				Array.Resize( ref _keys, size );
				Array.Resize( ref _snapshotIds, size );
			}

			index = _count++;
			_keys[index] = slot;
		}
		else if ( !IsNewer( snapshotId, _snapshotIds[index] ) )
		{
			return false;
		}

		_snapshotIds[index] = snapshotId;
		return true;
	}

	/// <summary>
	/// Forget a slot, its next value is accepted whatever snapshot it comes from.
	/// </summary>
	public void Remove( int slot )
	{
		var index = _keys.AsSpan( 0, _count ).IndexOf( slot );
		if ( index < 0 )
			return;

		_count--;
		_keys[index] = _keys[_count];
		_snapshotIds[index] = _snapshotIds[_count];
	}
}
