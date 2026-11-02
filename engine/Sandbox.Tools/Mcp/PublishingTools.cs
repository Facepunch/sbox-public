using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using static Editor.ProjectPublisher.PackageManifest;

namespace Editor.Mcp;

/// <summary>Agent review uses the same asset selection and analysis as the Publishing UI.</summary>
[McpToolset( "publishing", "Preview publishing rules, runtime-content warnings and dependency costs without saving project settings." )]
internal static class PublishingTools
{
	static readonly SemaphoreSlim previewGate = new( 1, 1 );

	[McpTool( "publishing_preview" )]
	[Description( "Build a publishing preview for the current editor project, optionally with proposed Resource Files rules and referenced-only mode. Does not save settings or upload. Like the UI preview, may compile included assets and write build caches. Returns counts, uncompressed bytes and a local JSON report path containing included/excluded files, dependency costs, source warnings, recommendations and estimate limitations. Read that report locally; compare separate baseline/proposal reports. UI edits are not read automatically: pass the rules copied from the UI. Compiled game code and final upload hashes are not included." )]
	public static async Task<object> Preview(
		[Description( "Complete Resource Files text, including ordered !exclusions. Null uses current project configuration; empty string previews no extra rules." )] string resources = null,
		[Description( "Override Only Publish Referenced Assets for this preview. Null uses current project configuration." )] bool? publishUsedAssetsOnly = null )
	{
		var project = Project.Current ?? throw new InvalidOperationException( "Open the project in the editor first." );
		if ( project.Config.AI?.EnableMcpCommunication == false )
			throw new InvalidOperationException( "MCP communication is disabled for this project." );
		var session = McpSession.Current ?? throw new InvalidOperationException( "Call publishing_preview through sbox-cli or MCP." );
		var cancel = session.Cancellation;
		var projectPath = project.ConfigFilePath;
		var options = new PublishOptions( publishUsedAssetsOnly ?? project.Config.PublishUsedAssetsOnly, resources ?? project.Config.Resources );
		await previewGate.WaitAsync( cancel );
		try
		{
			var manifest = new ProjectPublisher.PackageManifest();
			await manifest.BuildFromAssets( project, cancel: cancel, options: options, previewOnly: true );
			cancel.ThrowIfCancellationRequested();
			// Analysis is complete. Serialize the snapshot off the editor thread without flooding tool output.
			var reportPath = Path.Combine( session.ExchangeDirectory, $"publishing-{Guid.NewGuid():N}.json" );
			return await Task.Run( async () =>
			{
				var report = CreateReport( manifest, projectPath, options );
				await using var stream = File.Create( reportPath );
				await JsonSerializer.SerializeAsync( stream, report, new JsonSerializerOptions { WriteIndented = true }, cancel );
				return new
				{
					ReportPath = reportPath,
					report.Project,
					report.Options,
					report.IncludedCount,
					report.ExcludedCount,
					report.IncludedBytes,
					report.WarningCount,
					report.EstimateIssueCount,
					report.Errors
				};
			}, cancel );
		}
		finally
		{
			previewGate.Release();
		}
	}

	internal sealed record ReviewFile( string Path, long? Bytes, PublishImpact AdditionalImpact, DependencySummary Dependencies, string ExclusionRule, string ExclusionOverride );
	internal sealed record ReviewWarning( CodeAssetWarning Source, PublishImpact AdditionalImpact, DependencySummary Dependencies );
	internal sealed record Report( string Project, PublishOptions Options, int IncludedCount, int ExcludedCount, long IncludedBytes,
		int WarningCount, int EstimateIssueCount, string[] Errors, ReviewFile[] Included, ReviewFile[] Excluded,
		ScenePublishWarning[] Scenes, ReviewWarning[] CodeWarnings, string[] RecommendedRules, ExclusionOverride[] RequiredDespiteExclusions, EstimateIssue[] EstimateIssues );

	internal static Report CreateReport( ProjectPublisher.PackageManifest manifest, string project, PublishOptions options )
	{
		var issues = manifest.GetEstimateIssues().ToArray();
		return new Report( project, options, manifest.Assets.Count, manifest.ExcludedAssets.Count,
			manifest.Assets.Sum( x => (long)x.Size ), manifest.SceneWarnings.Count + manifest.CodeAssetWarnings.Count,
			issues.Length, manifest.Errors.ToArray(),
			manifest.Assets.Select( x => new ReviewFile( x.Name, (long)x.Size, null, manifest.GetDependencySummary( [x.Name] ),
				null, manifest.IncludedExclusionReasons.GetValueOrDefault( x.Name.TrimStart( '/' ) ) ) ).ToArray(),
			manifest.ExcludedAssets.Select( x => new ReviewFile( x, manifest.ExcludedAssetSizes.GetValueOrDefault( x ),
				manifest.GetPublishImpact( [x] ), manifest.GetDependencySummary( [x] ), manifest.ExcludedByRules.GetValueOrDefault( x ), null ) ).ToArray(),
			manifest.SceneWarnings.ToArray(), manifest.CodeAssetWarnings.Select( x => new ReviewWarning( x,
				manifest.GetPublishImpact( x.ExcludedPaths ), manifest.GetDependencySummary( x.ExcludedPaths ) ) ).ToArray(),
			manifest.RecommendedRules.ToArray(), manifest.ExclusionOverrides.ToArray(), issues );
	}
}
