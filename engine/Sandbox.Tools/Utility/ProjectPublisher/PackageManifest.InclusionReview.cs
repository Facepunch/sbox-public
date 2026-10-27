using System;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		readonly Dictionary<string, string[]> inclusionReasons = new( StringComparer.OrdinalIgnoreCase );

		/// <summary>Preview-only explanations, including direct referrers along transitive dependency chains.</summary>
		public string[] GetInclusionReasons( IEnumerable<string> paths ) => paths.SelectMany( path =>
			inclusionReasons.GetValueOrDefault( ReviewPath( path ), [] ) ).Distinct().Order( StringComparer.OrdinalIgnoreCase ).ToArray();

		void BuildInclusionReview()
		{
			var reasons = CalculateInclusionReasons( Assets.Select( x => x.Name ), reviewSizeGraph, reviewAssetAliases,
				configuredPublishPaths, publishingOptions.Resources, publishingOptions.PublishUsedAssetsOnly );
			foreach ( var (path, entries) in reasons ) inclusionReasons[path] = entries;
		}

		internal static Dictionary<string, string[]> CalculateInclusionReasons( IEnumerable<string> files,
			IReadOnlyDictionary<string, ReviewSizeNode> graph, IReadOnlyDictionary<string, string> aliases,
			IEnumerable<string> configuredPaths, string rules, bool referencedOnly )
		{
			var included = files.Select( ReviewPath ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			string Canonical( string path ) => aliases.GetValueOrDefault( ReviewPath( path ), ReviewPath( path ) );
			var logical = included.Select( Canonical ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var configured = configuredPaths.Select( Canonical ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var matcher = new ResourceRuleMatcher( rules, legacySyntax: !referencedOnly );
			var pathsByAsset = aliases.GroupBy( x => x.Value, StringComparer.OrdinalIgnoreCase )
				.ToDictionary( x => x.Key, x => x.Select( a => a.Key ).ToArray(), StringComparer.OrdinalIgnoreCase );
			var reasons = new Dictionary<string, HashSet<string>>( StringComparer.OrdinalIgnoreCase );
			void Add( string path, string reason )
			{
				if ( !reasons.TryGetValue( path, out var entries ) ) reasons[path] = entries = new( StringComparer.OrdinalIgnoreCase );
				entries.Add( reason );
			}

			foreach ( var path in logical )
			{
				var paths = pathsByAsset.GetValueOrDefault( path, [path] );
				if ( configured.Contains( path ) ) Add( path, "Referenced by a configured scene or project setting." );
				if ( referencedOnly && paths.Any( IsResourcesPath ) ) Add( path, "Included by the Resources/ folder." );
				if ( matcher.LastMatch( paths.Select( NormalizePublishPath ) ) is { Exclude: false } rule ) Add( path, $"Included by rule: {rule.Text}" );
				if ( !referencedOnly && aliases.ContainsKey( path ) ) Add( path, "Included because all project assets are published." );
				if ( path.StartsWith( "fonts/", StringComparison.OrdinalIgnoreCase ) ) Add( path, "Fonts are included automatically." );
				if ( path.StartsWith( "localization/", StringComparison.OrdinalIgnoreCase ) ) Add( path, "Localization files are included automatically." );
				if ( path.StartsWith( "projectsettings/", StringComparison.OrdinalIgnoreCase ) ) Add( path, "Project settings are included automatically." );
			}

			// Only included parents can explain inclusion. Excluded scenes may also reference these assets.
			foreach ( var (path, node) in graph.Where( x => logical.Contains( x.Key ) ) )
			{
				foreach ( var reference in node.References )
				{
					var target = Canonical( reference.StartsWith( "input:", StringComparison.Ordinal ) ? reference[6..] : reference );
					if ( logical.Contains( target ) && !string.Equals( path, target, StringComparison.OrdinalIgnoreCase ) )
						Add( target, $"Referenced by {path}" );
				}
				foreach ( var file in node.Files.Keys.Concat( node.RootFiles?.Keys ?? Enumerable.Empty<string>() ) )
				{
					if ( included.Contains( file ) && !string.Equals( Canonical( file ), path, StringComparison.OrdinalIgnoreCase ) )
						Add( Canonical( file ), $"Runtime file for {path}" );
				}
			}

			var result = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase );
			foreach ( var file in included )
			{
				var path = Canonical( file );
				if ( file.EndsWith( ".t.png", StringComparison.OrdinalIgnoreCase ) && included.Contains( file[..^6] ) )
					Add( path, $"Thumbnail for {Canonical( file[..^6] )}" );
				result[file] = reasons.TryGetValue( path, out var entries ) ? entries.Order( StringComparer.OrdinalIgnoreCase ).ToArray()
					: ["Automatically packaged runtime content."];
			}
			return result;
		}
	}
}
