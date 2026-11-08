using System;
using System.Text.Json.Serialization;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		public sealed record ExclusionOverride( string Path, string Rule, string Reason, long Size );
		[JsonIgnore] public List<ExclusionOverride> ExclusionOverrides { get; } = new();
		[JsonIgnore] public Dictionary<string, string> ExcludedByRules { get; } = new( StringComparer.OrdinalIgnoreCase );
		[JsonIgnore] public Dictionary<string, string> IncludedExclusionReasons { get; } = new( StringComparer.OrdinalIgnoreCase );
		readonly HashSet<string> configuredPublishPaths = new( StringComparer.OrdinalIgnoreCase );

		internal static string ExclusionOverrideReason( IEnumerable<string> paths, IEnumerable<string> configured )
		{
			var normalized = paths.Select( NormalizePublishPath ).ToArray();
			if ( normalized.Intersect( configured.Select( NormalizePublishPath ), StringComparer.OrdinalIgnoreCase ).Any() )
				return "Referenced by a configured scene or project setting.";
			if ( normalized.Any( IsResourcesPath ) ) return "Included by the Resources folder.";
			return "Required by included content or automatic publishing rules.";
		}

		internal static IEnumerable<CodeAssetWarning> RespectRuleExclusions( IEnumerable<CodeAssetWarning> warnings, IReadOnlyDictionary<string, string> exclusions )
		{
			foreach ( var warning in warnings )
			{
				var rules = warning.ExcludedPaths.Where( exclusions.ContainsKey ).Select( x => exclusions[x] ).Distinct().ToArray();
				yield return rules.Length == 0 ? warning : warning with { Recommended = false, ExclusionRule = string.Join( ", ", rules ) };
			}
		}
	}
}
