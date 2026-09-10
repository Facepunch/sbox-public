namespace Sandbox.CodeUpgrader;

/// <summary>
/// A global list of rules, so we can avoid doubling up on diagnostic descriptors
/// </summary>
static class Diagnostics
{
	public static DiagnosticDescriptor BroadcastAttribute = Warning( "SBOX001", "Replace [Broadcast] with [Rpc.Broadcast]", "The [Broadcast] attribute has been moved to [Rpc.Broadcast] to make it more discoverable." );
	public static DiagnosticDescriptor AuthorityAttribute = Warning( "SBOX002", "Replace [Authority] with [Rpc.Owner]", "The [Authority] attribute has been moved to [Rpc.Owner] and [Rpc.Host]." );
	public static DiagnosticDescriptor GpuBuffer = Warning( "SBOX003", "Replace ComputeBuffer with GpuBuffer", "The ComputeBuffer class has been moved to GpuBuffer." );
	public static DiagnosticDescriptor HostSyncAttribute = Warning( "SBOX004", "Replace [HostSync] with [Sync( SyncFlags.FromHost )]", "The [HostSync] attribute has been merged with [Sync] to make functionality expandable with SyncFlags." );
	public static DiagnosticDescriptor SyncQuery = Warning( "SBOX005", "Replace Query with SyncFlags.Query in [Sync]", "[Sync] attributes should have SyncFlags.Query set instead of the Query property." );
	public static DiagnosticDescriptor ConCmdAttribute = Warning( "SBOX006", "Make ConCmd Method Static", "[ConCmd] methods need to be static to function." );
	public static DiagnosticDescriptor ConVarAttribute = Warning( "SBOX007", "Make ConVar Property Static", "[ConVar] properties need to be static to function." );
	public static DiagnosticDescriptor GenericStaticMembersUnsupported = Warning( "SB3000", "Add [SkipHotload] to Static Member in Generic Type", "Static members in generic types won't be processed during hotloads, so should be explicitly marked with [SkipHotload]" );

	const string MigrationHelp = "https://sbox.game/dev/doc/networking/host-migration/";

	public static DiagnosticDescriptor ConnectionStored = Warning( "SB3002", "Connection stored in a field", "'{0}' holds a Connection that is not synced, so it is lost on host migration. Mark it [Sync], or store Connection.Id and look the connection up when you need it.", "Networking", MigrationHelp );
	public static DiagnosticDescriptor UnsyncedTimer = Warning( "SB3003", "Timer is not synced", "Host code reads '{0}' but it is not synced, so it resets on host migration. Mark it [Sync].", "Networking", MigrationHelp );
	public static DiagnosticDescriptor HostInvoke = Warning( "SB3004", "Host Invoke is lost on host migration", "This Invoke is scheduled by host code, so it dies with the host and nobody else runs it. Keep the deadline in a [Sync] TimeUntil and act on it in OnUpdate instead.", "Networking", MigrationHelp );
	public static DiagnosticDescriptor SyncWrittenAfterAwait = Warning( "SB3005", "Synced value written after an await", "'{0}' is written after an await in {1}. If the host leaves before then, the rest never runs and what was set before stays set. Drive this from a [Sync] TimeUntil in OnUpdate instead.", "Networking", MigrationHelp );
	public static DiagnosticDescriptor HostAsync = Warning( "SB3006", "Async host logic is not migrated", "This async operation runs from host-only code. Its pending work is not transferred when the host leaves. Keep progress in synced state and resume it on the new host, or drive timed logic from a [Sync] TimeUntil in OnUpdate.", "Networking", MigrationHelp );

	static DiagnosticDescriptor Warning( string id, string title, string message, string category = "Refactoring", string helpLink = null )
	{
		return new DiagnosticDescriptor(
			id: id,
			title: title,
			messageFormat: message,
			category: category,
			defaultSeverity: DiagnosticSeverity.Warning,
			isEnabledByDefault: true,
			helpLinkUri: helpLink );
	}
}
