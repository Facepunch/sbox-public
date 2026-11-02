using System;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		internal sealed record ResourceRule( string Pattern, bool Exclude, int Index )
		{
			public bool IsWildcard => Pattern.IndexOfAny( ['*', '?'] ) >= 0;
			public string Text => (Exclude ? "!" : "") + Pattern.TrimStart( '/' );
		}

		internal static ResourceRule ParseResourceRule( string text, int index = 0, bool legacySyntax = false )
		{
			text = text?.Trim();
			if ( string.IsNullOrEmpty( text ) || text.StartsWith( "//" ) ) return null;
			// Existing projects treat #, ! and trailing slashes literally until they opt in.
			if ( legacySyntax ) return new( NormalizePublishPath( text ), false, index );
			if ( text.StartsWith( '#' ) ) return null;
			var exclude = text.StartsWith( '!' );
			if ( exclude ) text = text[1..].TrimStart();
			if ( string.IsNullOrWhiteSpace( text ) ) return null;
			text = text.Replace( '\\', '/' );
			if ( text.EndsWith( '/' ) ) text += "*";
			return new( NormalizePublishPath( text ), exclude, index );
		}

		internal static ResourceRule[] ParseResourceRules( string script, bool legacySyntax = false ) => (script ?? "").Split( '\n' )
			.Select( ( line, index ) => ParseResourceRule( line, index, legacySyntax ) ).Where( x => x is not null ).ToArray();

		/// <summary>Last matching rule wins; exact paths retain constant-time lookup.</summary>
		internal sealed class ResourceRuleMatcher
		{
			readonly Dictionary<string, ResourceRule> exact = new( StringComparer.OrdinalIgnoreCase );
			readonly List<ResourceRule> wildcards = new();
			public bool HasExclusions { get; private set; }
			public ResourceRuleMatcher( string rules, bool legacySyntax = false )
			{
				foreach ( var rule in ParseResourceRules( rules, legacySyntax ) )
				{
					if ( rule.IsWildcard ) wildcards.Add( rule );
					else exact[rule.Pattern] = rule;
					HasExclusions |= rule.Exclude;
				}
			}
			public ResourceRule LastMatch( string normalizedPath )
			{
				var last = exact.GetValueOrDefault( normalizedPath );
				for ( var i = wildcards.Count - 1; i >= 0; i-- )
				{
					var rule = wildcards[i];
					if ( last is not null && rule.Index <= last.Index ) break;
					if ( normalizedPath.WildcardMatch( rule.Pattern ) ) return rule;
				}
				return last;
			}
			public ResourceRule LastMatch( IEnumerable<string> normalizedPaths )
			{
				ResourceRule last = null;
				foreach ( var path in normalizedPaths )
				{
					var match = LastMatch( path );
					if ( match is not null && (last is null || match.Index > last.Index) ) last = match;
				}
				return last;
			}
			public bool Matches( string normalizedPath ) => LastMatch( normalizedPath ) is { Exclude: false };
			public bool Matches( IEnumerable<string> normalizedPaths ) => LastMatch( normalizedPaths ) is { Exclude: false };
		}
	}
}
