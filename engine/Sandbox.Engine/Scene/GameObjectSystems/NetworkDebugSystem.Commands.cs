using System.Text;

namespace Sandbox;

sealed partial class NetworkDebugSystem
{
	[ConCmd( "net_diag_dump", ConVarFlags.Protected )]
	public static void NetworkDiag( string arg = "" )
	{
		var system = NetworkDebugSystem.Current;
		if ( system is null )
		{
			Log.Warning( "NetworkDebugSystem is not active." );
			return;
		}

		if ( arg.Equals( "reset", StringComparison.OrdinalIgnoreCase ) )
		{
			system.Reset();
			Log.Info( "Network diagnostics stats have been reset." );
			return;
		}

		if ( system.InboundStats is null && system.OutboundStats is null
		  && system.SyncVarInboundStats is null && system.SyncVarOutboundStats is null )
		{
			Log.Info( "No network diagnostics data collected yet." );
			return;
		}

		var timestamp = DateTime.Now.ToString( "yyyyMMdd_HHmmss" );
		var elapsed = system.TrackingElapsed.TotalSeconds;
		var sb = new StringBuilder();

		sb.AppendLine( $"=== Network Diagnostics ({elapsed:0.0}s) ===" );

		if ( system.OutboundStats is { Count: > 0 } )
		{
			sb.AppendLine();
			sb.Append( BuildStatsTable( "RPC Outbound [to all connections]", system.OutboundStats, elapsed ) );
		}

		if ( system.InboundStats is { Count: > 0 } )
		{
			sb.AppendLine();
			sb.Append( BuildStatsTable( "RPC Inbound [from all connections]", system.InboundStats, elapsed ) );
		}

		if ( system.ConnectionStats is { Count: > 0 } )
		{
			sb.AppendLine();
			sb.Append( BuildConnectionTable( "RPC Inbound [per connection]", system.ConnectionStats, elapsed ) );
		}

		if ( system.SyncVarOutboundStats is { Count: > 0 } )
		{
			sb.AppendLine();
			sb.Append( BuildStatsTable( "Sync Vars Outbound [all connections]", system.SyncVarOutboundStats, elapsed ) );
		}

		if ( system.SyncVarInboundStats is { Count: > 0 } )
		{
			sb.AppendLine();
			sb.Append( BuildStatsTable( "Sync Vars Inbound [all connections]", system.SyncVarInboundStats, elapsed ) );
		}

		if ( system.SyncVarConnectionStats is { Count: > 0 } )
		{
			sb.AppendLine();
			sb.Append( BuildConnectionTable( "Sync Vars Outbound [per connection]", system.SyncVarConnectionStats, elapsed ) );
		}

		var content = sb.ToString();
		var filename = $"network_diag_{timestamp}.txt";

		foreach ( var line in content.Split( '\n' ) ) Log.Info( line );

		FileSystem.Data.WriteAllText( filename, content );
		Log.Info( $"Written to {filename}" );
		Log.Info( "Run 'network_diag reset' to clear accumulated stats." );
	}

	private static string BuildStatsTable( string label, Dictionary<string, NetworkDebugSystem.MessageStats> stats, double elapsedSeconds )
	{
		var sb = new StringBuilder();
		var sorted = stats.OrderByDescending( kv => kv.Value.TotalBytes ).ToList();
		var totalBytes = sorted.Sum( kv => kv.Value.TotalBytes );
		var totalCalls = sorted.Sum( kv => kv.Value.TotalCalls );
		var callRate = elapsedSeconds > 0 ? totalCalls / elapsedSeconds : 0;
		var kbRate = elapsedSeconds > 0 ? totalBytes / 1024.0 / elapsedSeconds : 0;

		sb.AppendLine( $"{label}: {sorted.Count} types, {totalCalls:N0} calls ({callRate:0.0}/s), {totalBytes / 1024f:0.0} KB ({kbRate:0.0} KB/s)" );
		sb.AppendLine( $"\t{"#",-4} {"Method",-50} {"Calls/s",8} {"KB/s",8} {"Avg B",7} {"Peak B",7} {"% Total",9}" );
		sb.AppendLine( $"\t{new string( '-', 4 )} {new string( '-', 50 )} {new string( '-', 8 )} {new string( '-', 8 )} {new string( '-', 7 )} {new string( '-', 7 )} {new string( '-', 9 )}" );

		var rank = 0;
		foreach ( var (name, stat) in sorted.Take( 30 ) )
		{
			rank++;
			var pct = totalBytes > 0 ? stat.TotalBytes / (float)totalBytes * 100f : 0f;
			var statCallRate = elapsedSeconds > 0 ? stat.TotalCalls / elapsedSeconds : 0;
			var statKbRate = elapsedSeconds > 0 ? stat.TotalBytes / 1024.0 / elapsedSeconds : 0;
			sb.AppendLine( $"\t{rank,-4} {name,-50} {statCallRate,7:0.0}/s {statKbRate,6:0.00} KB/s {stat.BytesPerMessage,6} B {stat.PeakBytes,6} B {pct,8:0.0}%" );
		}

		return sb.ToString();
	}

	private static string BuildConnectionTable( string label, Dictionary<Guid, Dictionary<string, NetworkDebugSystem.MessageStats>> connectionStats, double elapsedSeconds )
	{
		var sb = new StringBuilder();
		sb.AppendLine( $"{label}:" );

		var sorted = connectionStats
			.Select( kv => (Id: kv.Key, Stats: kv.Value, TotalBytes: kv.Value.Values.Sum( s => s.TotalBytes )) )
			.OrderByDescending( x => x.TotalBytes )
			.ToList();

		foreach ( var (id, stats, totalBytes) in sorted )
		{
			var conn = Connection.All.FirstOrDefault( c => c.Id == id );
			var connName = conn?.DisplayName ?? id.ToString()[..8];
			var totalCalls = stats.Values.Sum( s => s.TotalCalls );
			var kbRate = elapsedSeconds > 0 ? totalBytes / 1024.0 / elapsedSeconds : 0;

			sb.AppendLine( $"\t{connName,-30} {totalCalls,8:N0} calls   {totalBytes / 1024f,8:0.0} KB   {kbRate,6:0.00} KB/s" );

			foreach ( var (name, stat) in stats.OrderByDescending( kv => kv.Value.TotalBytes ).Take( 5 ) )
			{
				var pct = totalBytes > 0 ? stat.TotalBytes / (float)totalBytes * 100f : 0f;
				var parts = name.Split( '.' );
				var shortName = parts.Length >= 2 ? $"{parts[^2]}.{parts[^1]}" : name;
				sb.AppendLine( $"\t\t{shortName,-48} {stat.TotalCalls,6}x   {stat.TotalBytes / 1024f,6:0.0} KB   {pct,5:0.0}%" );
			}
		}

		return sb.ToString();
	}
}
