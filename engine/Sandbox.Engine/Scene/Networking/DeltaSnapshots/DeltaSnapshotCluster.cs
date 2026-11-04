/// <summary>
/// Snapshots of several objects sent in one message, see <see cref="Sandbox.Network.DeltaSnapshotSystem"/>.
/// </summary>
internal static class DeltaSnapshotCluster
{
	/// <summary>
	/// The maximum encoded size (in bytes) of a single snapshot cluster. Clusters are sent unreliably, and Steam drops
	/// the whole message when any of its packets is lost, so this keeps each one in a single packet. Steam's default
	/// MTU_DataSize, the largest message it won't fragment on an encrypted connection: 1300 byte packets, less 52 for
	/// its headers, 16 for the encryption tag and 100 it reserves for acks and stats.
	/// </summary>
	[ConVar( "net_max_cluster_size" )]
	public static int MaxSize { get; set; } = 1132;
}
