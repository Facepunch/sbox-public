namespace Sandbox;

/// <summary>
/// Configurable options when spawning a networked object.
/// </summary>
public struct NetworkSpawnOptions()
{
	/// <summary>
	/// The default network spawn options.
	/// </summary>
	public static readonly NetworkSpawnOptions Default = new();

	/// <summary>
	/// <inheritdoc cref="GameObject.NetworkAccessor.NetworkOrphaned"/>
	/// </summary>
	public NetworkOrphaned? OrphanedMode { get; set; }

	/// <summary>
	/// <inheritdoc cref="GameObject.NetworkAccessor.OwnerTransfer"/>
	/// </summary>
	public OwnerTransfer? OwnerTransfer { get; set; }

	/// <summary>
	/// <inheritdoc cref="GameObject.NetworkAccessor.Flags"/>
	/// </summary>
	public NetworkFlags? Flags { get; set; }

	/// <summary>
	/// <inheritdoc cref="GameObject.NetworkAccessor.AlwaysTransmit"/>
	/// </summary>
	public bool? AlwaysTransmit { get; set; }

	/// <summary>
	/// Should this networked object start enabled?
	/// </summary>
	public bool StartEnabled { get; set; } = true;

	/// <summary>
	/// Who should be the owner of this networked object?
	/// </summary>
	public Connection Owner { get; set; }
}
