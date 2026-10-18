using Sandbox.Engine;

using Sandbox.Network;

internal class DeltaSnapshot : IObjectPoolEvent
{
	public static NetworkObjectPool<DeltaSnapshot> Pool { get; } = new();

	public void Release() => Pool.Return( this );

	void IObjectPoolEvent.OnRented()
	{
	}

	void IObjectPoolEvent.OnReturned()
	{
		Lookup.Clear();
		Entries.Clear();
	}

	/// <summary>
	/// A received slot value. It points into the message it came in, so it's only valid while that's being
	/// handled: copy it to keep it.
	/// </summary>
	public struct SnapshotDataEntry
	{
		public int Slot;
		public ReadOnlyMemory<byte> Value;
	}

	// Pooled, so these grow to the sizes actually used instead of starting at a fixed 128
	public readonly List<SnapshotDataEntry> Entries = new();

	/// <summary>
	/// Slot lookup for the entries.
	/// </summary>
	public readonly Dictionary<int, SnapshotDataEntry> Lookup = new();

	/// <summary>
	/// When the sender took it, on our clock.
	/// </summary>
	public double Time { get; set; }

	/// <summary>
	/// Add a serialized value to the specified slot.
	/// </summary>
	public void AddSerialized( int slot, ReadOnlyMemory<byte> value )
	{
		var entry = new SnapshotDataEntry
		{
			Slot = slot,
			Value = value
		};

		Entries.Add( entry );
		Lookup[slot] = entry;
	}

	/// <summary>
	/// Try to get a deserialized value from the specified slot.
	/// </summary>
	public bool TryGetValue<T>( int slot, out T value )
	{
		if ( Lookup.TryGetValue( slot, out var entry ) )
		{
			value = GlobalContext.Current.TypeLibrary.FromBytes<T>( entry.Value.Span );
			return true;
		}

		value = default;
		return false;
	}
}
