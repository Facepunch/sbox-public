using System;
using System.Text.Json.Serialization;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		/// <summary>Exact excluded files mentioned by literal paths in code, unless explicitly excluded by a rule.</summary>
		[JsonIgnore]
		public string[] RecommendedIncludes => CodeAssetWarnings.Where( x => x.Recommended )
			.SelectMany( x => x.ExcludedPaths ).Distinct( StringComparer.OrdinalIgnoreCase )
			.Order( StringComparer.OrdinalIgnoreCase ).ToArray();

		[JsonIgnore] public string[] RecommendedRules { get; private set; } = [];

		// Group by extension and folder. A subtree can collapse only when all its other files are already included.
		internal static string[] CompactRecommendedRules( IEnumerable<string> recommended, IEnumerable<string> available, IEnumerable<string> included )
		{
			static string Normalize( string path ) => NormalizePublishPath( path ).TrimStart( '/' );
			var targets = recommended.Select( Normalize ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var allowed = included.Select( Normalize ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var types = new Dictionary<string, RecommendationFolder>( StringComparer.OrdinalIgnoreCase );
			foreach ( var path in available.Select( Normalize ).Concat( targets ).Distinct( StringComparer.OrdinalIgnoreCase ) )
			{
				var extension = System.IO.Path.GetExtension( path );
				if ( extension.Length < 2 || extension.IndexOfAny( ['*', '?'] ) >= 0 ) continue;
				if ( !types.TryGetValue( extension, out var folder ) ) types[extension] = folder = new();
				var needed = targets.Contains( path );
				var unsafeMatch = !needed && !allowed.Contains( path );
				var parts = path.Split( '/' );
				for ( var i = 0; ; i++ )
				{
					if ( needed ) folder.Needed++;
					if ( unsafeMatch ) folder.Unsafe++;
					if ( i == parts.Length - 1 ) break;
					if ( !folder.Children.TryGetValue( parts[i], out var child ) ) folder.Children[parts[i]] = child = new();
					folder = child;
				}
				if ( needed ) folder.Files.Add( path );
			}
			var rules = new List<string>();
			foreach ( var (extension, root) in types ) Visit( root, "", extension );
			return rules.Order( StringComparer.OrdinalIgnoreCase ).ToArray();

			void Visit( RecommendationFolder folder, string prefix, string extension )
			{
				if ( folder.Needed == 0 ) return;
				var children = folder.Children.Where( x => x.Value.Needed > 0 ).ToArray();
				// Prefer the narrowest folder, even when a project-wide type wildcard would be safe today.
				if ( folder.Files.Count == 0 && children.Length == 1 )
				{
					Visit( children[0].Value, prefix + children[0].Key + "/", extension );
					return;
				}
				if ( folder.Unsafe == 0 && folder.Needed >= 2 )
				{
					rules.Add( prefix + "*" + extension );
					return;
				}
				rules.AddRange( folder.Files );
				foreach ( var (name, child) in children ) Visit( child, prefix + name + "/", extension );
			}
		}

		sealed class RecommendationFolder
		{
			public int Needed;
			public int Unsafe;
			public List<string> Files { get; } = new();
			public Dictionary<string, RecommendationFolder> Children { get; } = new( StringComparer.OrdinalIgnoreCase );
		}

		public sealed record ResourceRuleCleanup( string Rules, int Removed );

		/// <summary>Remove provably redundant rules, preserving the matched file set, comments and order.</summary>
		public static ResourceRuleCleanup CleanResourceRules( string rules )
		{
			var lines = (rules ?? "").Split( '\n' );
			var parsed = lines.Select( ( line, index ) => ParseResourceRule( line, index ) ).ToArray();
			if ( parsed.Any( x => x?.Exclude == true ) )
			{
				// Earlier identical patterns are shadowed by the last one. Avoid broader coverage
				// cleanup across exclusions: an intervening rule may make an inclusion significant.
				var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
				var redundant = new HashSet<int>();
				for ( var i = parsed.Length - 1; i >= 0; i-- )
					if ( parsed[i] is { } rule && !seen.Add( rule.Pattern ) ) redundant.Add( i );
				return new( redundant.Count == 0 ? rules : string.Join( "\n", lines.Where( ( _, i ) => !redundant.Contains( i ) ) ), redundant.Count );
			}
			var patterns = parsed.Select( x => x?.Pattern ).ToArray();
			var removed = new HashSet<int>();
			var unique = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			for ( var i = 0; i < patterns.Length; i++ )
			{
				if ( patterns[i] is { } pattern && !unique.Add( pattern ) ) removed.Add( i );
			}
			var wildcards = Enumerable.Range( 0, patterns.Length )
				.Where( i => patterns[i]?.IndexOfAny( ['*', '?'] ) >= 0 && !removed.Contains( i ) ).ToArray();
			for ( var i = 0; i < patterns.Length; i++ )
			{
				var pattern = patterns[i];
				if ( pattern is null || removed.Contains( i ) ) continue;
				foreach ( var j in wildcards )
				{
					if ( i == j || removed.Contains( j ) ) continue;
					var other = patterns[j];
					// Never treat a wildcard rule as a literal matching another wildcard.
					var covered = pattern.IndexOfAny( ['*', '?'] ) < 0
						? pattern.WildcardMatch( other )
						: other.EndsWith( '*' ) && other[..^1].IndexOfAny( ['*', '?'] ) < 0
							&& pattern.StartsWith( other[..^1], StringComparison.OrdinalIgnoreCase );
					if ( !covered ) continue;
					removed.Add( i );
					break;
				}
			}
			return new( removed.Count == 0 ? rules : string.Join( "\n", lines.Where( ( _, i ) => !removed.Contains( i ) ) ), removed.Count );
		}

		/// <summary>Save publishing rules from editor addons without forcing a code compilation.</summary>
		public static void SetResourceRules( Project project, string rules )
		{
			project.Config.Resources = rules;
			project.Save();
		}

		/// <summary>Add reviewed rules as a single edit, without rescanning existing rules for every file.</summary>
		public static string AddResourceRules( string rules, IEnumerable<string> additions )
		{
			var parsed = ParseResourceRules( rules );
			var lastByPattern = new Dictionary<string, ResourceRule>( StringComparer.OrdinalIgnoreCase );
			foreach ( var rule in parsed ) lastByPattern[rule.Pattern] = rule;
			var lastInclude = parsed.LastOrDefault( x => !x.Exclude )?.Index ?? -1;
			var lastExclude = parsed.LastOrDefault( x => x.Exclude )?.Index ?? -1;
			var index = parsed.LastOrDefault()?.Index + 1 ?? 0;
			var added = new List<string>();
			foreach ( var text in additions )
			{
				var rule = ParseResourceRule( text, index++ );
				var rawPattern = text?.Trim();
				if ( rawPattern?.StartsWith( '!' ) == true ) rawPattern = rawPattern[1..].TrimStart();
				if ( rule is null || text.IndexOfAny( ['\r', '\n'] ) >= 0 || rawPattern.IndexOfAny( [':', '"'] ) >= 0
					|| rawPattern.Replace( '\\', '/' ).Split( '/' ).Contains( ".." ) || rawPattern.Trim( '/', '\\' ).Length == 0 || rawPattern.StartsWith( '!' ) )
					throw new ArgumentException( "Enter one relative file path or wildcard, optionally prefixed with !." );
				// An exclusion can match a different asset alias. Only skip an earlier identical
				// rule when no opposite rule follows it; a manual Include must be able to override it.
				if ( lastByPattern.TryGetValue( rule.Pattern, out var same ) && same.Exclude == rule.Exclude
					&& same.Index > (rule.Exclude ? lastInclude : lastExclude) ) continue;
				lastByPattern[rule.Pattern] = rule;
				if ( rule.Exclude ) lastExclude = rule.Index;
				else lastInclude = rule.Index;
				added.Add( rule.Text );
			}
			if ( added.Count == 0 ) return rules;
			var newline = (rules ?? "").Contains( "\r\n" ) ? "\r\n" : "\n";
			return (string.IsNullOrWhiteSpace( rules ) ? "" : rules.TrimEnd() + newline) + string.Join( newline, added );
		}
	}
}
