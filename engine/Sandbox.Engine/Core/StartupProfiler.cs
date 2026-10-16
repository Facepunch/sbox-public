using Sandbox.Utility;
using System.Text;
using System.Text.Json;

namespace Sandbox.Engine;

internal static class StartupProfiler
{
	const string Switch = "-startup-profile";

	public static bool Enabled => CommandLine.HasSwitch( Switch );

	public static void Write( Api.Events.EventRecord timing )
	{
		if ( !Enabled )
			return;

		var entries = timing.Data
			.Where( x => x.Value is int )
			.Select( x => new Entry( x.Key, (int)x.Value ) )
			.Where( x => x.Milliseconds > 0 )
			.OrderByDescending( x => x.Milliseconds )
			.ToArray();

		var total = entries.FirstOrDefault( x => x.Name == "Time" ).Milliseconds;
		var report = new Report( DateTime.UtcNow, Environment.ProcessPath, timing.Mode, timing.Version, total, entries );
		var json = JsonSerializer.Serialize( report, new JsonSerializerOptions { WriteIndented = true } );
		var markdown = BuildMarkdown( timing, entries, total );

		var data = FileSystem.Data ?? EngineFileSystem.Data;
		data.WriteAllText( "startup-profile.json", json );
		data.WriteAllText( "startup-profile.md", markdown );
		Log.Info( "Startup profile written to data/startup-profile.json and data/startup-profile.md" );
	}

	static string BuildMarkdown( Api.Events.EventRecord timing, Entry[] entries, double total )
	{
		var builder = new StringBuilder();
		builder.AppendLine( "# Startup Profile" );
		builder.AppendLine();
		builder.AppendLine( $"- Generated: `{DateTime.UtcNow:O}`" );
		builder.AppendLine( $"- Process: `{Environment.ProcessPath}`" );
		builder.AppendLine( $"- Mode: `{timing.Mode}`" );
		builder.AppendLine( $"- Version: `{timing.Version}`" );
		builder.AppendLine( $"- Total measured startup time: **{total:0} ms**" );
		builder.AppendLine();
		builder.AppendLine( "## Measured stages" );
		builder.AppendLine();
		builder.AppendLine( "| Stage | Time | Share |" );
		builder.AppendLine( "| --- | ---: | ---: |" );

		foreach ( var entry in entries.Where( x => x.Name != "Time" ) )
		{
			var share = total > 0 ? entry.Milliseconds / total * 100 : 0;
			builder.AppendLine( $"| `{entry.Name}` | {entry.Milliseconds:0} ms | {share:0.0}% |" );
		}

		builder.AppendLine();
		builder.AppendLine( "The stages are ordered by measured duration. Use the largest rows as the next optimization targets; values are captured from the same process and do not include time before the managed bootstrap timer starts." );
		return builder.ToString();
	}

	readonly record struct Report( DateTime GeneratedUtc, string Process, string Mode, string Version, double TotalMilliseconds, Entry[] Stages );
	readonly record struct Entry( string Name, double Milliseconds );
}
