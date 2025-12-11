using Sandbox.Network;

namespace Sandbox;

/// <summary>
/// A <see cref="NetworkTable{Transform}"/> containing <see cref="Transform">Transforms</see> but each component of the transform
/// is added to a <see cref="DeltaSnapshot"/>.
/// </summary>
internal class NetworkTransforms : NetworkTable<Transform>
{
	private Dictionary<int, (int Position, int Rotation, int Scale)> _componentHashes { get; set; } = new();

	private void UpdateComponentHashes( int key )
	{
		if ( _componentHashes.ContainsKey( key ) )
			return;

		var subSlot = $"{_parentSlot}.{key}";

		_componentHashes[key] = (
			$"{subSlot}.position".FastHash(),
			$"{subSlot}.rotation".FastHash(),
			$"{subSlot}.scale".FastHash()
		);
	}

	private readonly SnapshotValueCache _snapshotCache = new();

	protected override void WriteSnapshot( int slot, LocalSnapshotState state )
	{
		foreach ( var (key, transform) in Table )
		{
			UpdateComponentHashes( key );

			var hashes = _componentHashes[key];

			state.AddCached( _snapshotCache, hashes.Position, transform.Position, true );
			state.AddCached( _snapshotCache, hashes.Rotation, transform.Rotation, true );
			state.AddCached( _snapshotCache, hashes.Scale, transform.Scale, true );
		}
	}

	protected override void ReadSnapshot( int slot, DeltaSnapshot snapshot )
	{
		foreach ( var key in Keys )
		{
			UpdateComponentHashes( key );

			var hashes = _componentHashes[key];
			var transform = Get( key );
			var didTransformChange = false;

			if ( snapshot.TryGetValue<Vector3>( hashes.Position, out var position ) )
			{
				transform.Position = position;
				didTransformChange = true;
			}

			if ( snapshot.TryGetValue<Rotation>( hashes.Rotation, out var rotation ) )
			{
				transform.Rotation = rotation;
				didTransformChange = true;
			}

			if ( snapshot.TryGetValue<Vector3>( hashes.Scale, out var scale ) )
			{
				transform.Scale = scale;
				didTransformChange = true;
			}

			if ( !didTransformChange )
				continue;

			Table[key] = transform;
			Serialized[key] = Game.TypeLibrary.ToBytes( transform );
		}
	}

	protected override void OnValueChanged( int slot, Transform value )
	{
		UpdateComponentHashes( slot );
	}

	protected override void OnCleared()
	{
		_componentHashes.Clear();
		_snapshotCache.Clear();
	}

	protected override void OnKeyRemoved( int key )
	{
		if ( _componentHashes.TryGetValue( key, out var hashes ) )
		{
			_snapshotCache.Remove( hashes.Position );
			_snapshotCache.Remove( hashes.Rotation );
			_snapshotCache.Remove( hashes.Scale );
		}

		_componentHashes.Remove( key );
	}

	protected override void OnInit( int slot )
	{
		// NetworkTable might be reinitialized under a new parent slot(?!) - invalidate _componentHashes so the new parent slot is used
		_componentHashes.Clear();
		_snapshotCache.Clear();

		foreach ( var key in Keys )
		{
			UpdateComponentHashes( key );
		}
	}
}
