using Sandbox.Utility;
using System.Text;
using System.Text.Json;

namespace Sandbox.Engine;

internal static class StartupProfiler
{
	const string Switch = "-startup-profile";

	public static bool Enabled => CommandLine.HasSwitch( Switch );

	/// <summary>
	/// When startup reached a point, measured from the process launching, rather than how long a stage took.
	/// </summary>
	static bool IsMilestone( string name ) => name is "TimeToWindow" or "TimeToMenu";

	public static void Write( Api.Events.EventRecord timing )
	{
		if ( !Enabled )
			return;

		var entries = timing.Data
			.Where( x => x.Value is int )
			.Select( x => new Entry( x.Key, (int)x.Value ) )
			.Where( x => x.Milliseconds > 0 )
			.ToArray();

		var total = entries.FirstOrDefault( x => x.Name == "Time" ).Milliseconds;
		var milestones = entries.Where( x => IsMilestone( x.Name ) ).OrderBy( x => x.Milliseconds ).ToArray();
		var stages = entries.Where( x => x.Name != "Time" && !IsMilestone( x.Name ) ).OrderByDescending( x => x.Milliseconds ).ToArray();
		var report = new Report( DateTime.UtcNow, Environment.ProcessPath, timing.Mode, timing.Version, total, milestones, stages );
		var json = JsonSerializer.Serialize( report, new JsonSerializerOptions { WriteIndented = true } );
		var markdown = BuildMarkdown( timing, milestones, stages, total );

		var data = FileSystem.Data ?? EngineFileSystem.Data;
		data.WriteAllText( "startup-profile.json", json );
		data.WriteAllText( "startup-profile.md", markdown );
		Log.Info( "Startup profile written to data/startup-profile.json and data/startup-profile.md" );
	}

	static string BuildMarkdown( Api.Events.EventRecord timing, Entry[] milestones, Entry[] stages, double total )
	{
		var builder = new StringBuilder();
		builder.AppendLine( "# Startup Profile" );
		builder.AppendLine();
		builder.AppendLine( $"- Generated: `{DateTime.UtcNow:O}`" );
		builder.AppendLine( $"- Process: `{Environment.ProcessPath}`" );
		builder.AppendLine( $"- Mode: `{timing.Mode}`" );
		builder.AppendLine( $"- Version: `{timing.Version}`" );
		builder.AppendLine( $"- Total measured startup time: **{total:0} ms**" );

		if ( milestones.Length > 0 )
		{
			builder.AppendLine();
			builder.AppendLine( "## Milestones" );
			builder.AppendLine();
			builder.AppendLine( "| Milestone | Since launch |" );
			builder.AppendLine( "| --- | ---: |" );

			foreach ( var entry in milestones )
			{
				builder.AppendLine( $"| `{entry.Name}` | {entry.Milliseconds:0} ms |" );
			}
		}

		builder.AppendLine();
		builder.AppendLine( "## Measured stages" );
		builder.AppendLine();
		builder.AppendLine( "| Stage | Time | Share |" );
		builder.AppendLine( "| --- | ---: | ---: |" );

		foreach ( var entry in stages )
		{
			var share = total > 0 ? entry.Milliseconds / total * 100 : 0;
			builder.AppendLine( $"| `{entry.Name}` | {entry.Milliseconds:0} ms | {share:0.0}% |" );
		}

		builder.AppendLine();
		builder.AppendLine( "The stages are ordered by measured duration. Use the largest rows as the next optimization targets; the total runs from the process launching until startup finished." );
		return builder.ToString();
	}

	readonly record struct Report( DateTime GeneratedUtc, string Process, string Mode, string Version, double TotalMilliseconds, Entry[] Milestones, Entry[] Stages );
	readonly record struct Entry( string Name, double Milliseconds );
}
