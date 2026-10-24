namespace Sandbox;

/// <summary>
/// A network parent that can be woken when one of its synced values changes.
/// </summary>
internal interface INetworkWakeable
{
	/// <summary>
	/// Wake the parent so changes are included in its next network snapshot.
	/// </summary>
	void MarkDirty();
}
