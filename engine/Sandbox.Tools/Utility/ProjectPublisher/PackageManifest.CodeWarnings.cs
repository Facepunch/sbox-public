using Microsoft.CodeAnalysis;
using Project = Sandbox.Project;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		public record ScenePublishWarning( string Path, bool Missing );

		public record CodeAssetPath( string Pattern, string SourceFile, int Line, string Usage = null, bool Recursive = true, bool Recommended = false, string SourceLine = null );
		public record CodeAssetWarning( string Pattern, string SourceFile, int Line, string[] ExcludedPaths, string Usage = null, bool Recommended = false, string ExclusionRule = null, string SourceLine = null );

		// Review information belongs to the editor, not the uploaded manifest.
		[JsonIgnore] public List<ScenePublishWarning> SceneWarnings { get; } = new();
		[JsonIgnore] public List<string> ExcludedAssets { get; } = new();
		[JsonIgnore] public Dictionary<string, long?> ExcludedAssetSizes { get; } = new( StringComparer.OrdinalIgnoreCase );
		[JsonIgnore] public List<CodeAssetWarning> CodeAssetWarnings { get; } = new();

		async Task BuildAssetReview( Project project, List<Asset> candidates, CancellationToken cancel, IEnumerable<Asset> publishedAssets )
		{
			var included = Assets.Select( x => NormalizePublishPath( x.Name ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var includedSizes = Assets.ToDictionary( x => NormalizePublishPath( x.Name ), x => (long)x.Size, StringComparer.OrdinalIgnoreCase );
			var rules = new ResourceRuleMatcher( publishingOptions.Resources, legacySyntax: !publishingOptions.PublishUsedAssetsOnly );
			var excluded = new Dictionary<string, string[]>( StringComparer.OrdinalIgnoreCase );
			var availablePaths = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var includedPaths = new HashSet<string>( included, StringComparer.OrdinalIgnoreCase );
			var assetSources = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var localCandidates = candidates.ToHashSet();
			// Configured scenes and dependencies can come from installed packages outside the project.
			foreach ( var asset in candidates.Concat( publishedAssets ).Distinct() )
			{
				cancel.ThrowIfCancellationRequested();
				await YieldPreview( cancel );
				var paths = AssetPaths( asset ).Where( x => !string.IsNullOrWhiteSpace( x ) ).Select( NormalizePublishPath ).Distinct().ToArray();
				foreach ( var path in paths ) assetSources.Add( path );
				availablePaths.UnionWith( paths );
				if ( paths.Any( included.Contains ) )
				{
					includedPaths.UnionWith( paths );
					if ( publishingOptions.PublishUsedAssetsOnly && rules.HasExclusions && rules.LastMatch( paths ) is { Exclude: true } rule )
					{
						var reason = ExclusionOverrideReason( paths, configuredPublishPaths );
						var publishedPaths = paths.Where( included.Contains ).Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
						ExclusionOverrides.Add( new( asset.Path.TrimStart( '/' ), rule.Text, reason, publishedPaths.Sum( x => includedSizes[x] ) ) );
						foreach ( var path in publishedPaths ) IncludedExclusionReasons[path.TrimStart( '/' )] = $"{rule.Text} overridden: {reason}";
					}
				}
				if ( !localCandidates.Contains( asset ) || !CanPublishFile( asset ) || paths.Any( included.Contains ) ) continue;
				var key = asset.Path.TrimStart( '/' );
				excluded[key] = paths;
				if ( rules.LastMatch( paths ) is { Exclude: true } exclusion ) ExcludedByRules[key] = exclusion.Text;
				// Read metadata only; never compile excluded assets just to display their size.
				if ( IsPreview ) ExcludedAssetSizes[key] = ReadReviewFileSize( asset.GetCompiledFile( true ) );
			}

			// Include loose runtime files in the review too, but don't list source meshes or compiled sidecars.
			var assetRoots = AllAssetPaths( project ).ToArray();
			await Task.Run( () =>
			{
				foreach ( var root in assetRoots.Where( Directory.Exists ) )
				{
					foreach ( var file in Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ) )
					{
						cancel.ThrowIfCancellationRequested();
						var path = NormalizePublishPath( Path.GetRelativePath( root, file ) );
						availablePaths.Add( path );
						if ( !ResourceFileAllowed( path ) || included.Contains( path ) || assetSources.Contains( path ) ) continue;
						excluded[path.TrimStart( '/' )] = [path];
						if ( rules.LastMatch( path ) is { Exclude: true } exclusion ) ExcludedByRules[path.TrimStart( '/' )] = exclusion.Text;
						if ( IsPreview ) ExcludedAssetSizes[path.TrimStart( '/' )] = ReadReviewFileSize( file );
					}
				}
			}, cancel );

			if ( publishingOptions.PublishUsedAssetsOnly && rules.HasExclusions )
			{
				foreach ( var file in Assets )
				{
					cancel.ThrowIfCancellationRequested();
					await YieldPreview( cancel );
					var path = NormalizePublishPath( file.Name );
					// Asset aliases were evaluated together above. Rechecking a compiled filename alone
					// would incorrectly report an override after a later source-path inclusion.
					if ( assetSources.Contains( path ) || rules.LastMatch( path ) is not { Exclude: true } rule ) continue;
					var reason = ExclusionOverrideReason( [path], configuredPublishPaths );
					ExclusionOverrides.Add( new( file.Name, rule.Text, reason, file.Size ) );
					IncludedExclusionReasons[path.TrimStart( '/' )] = $"{rule.Text} overridden: {reason}";
				}
			}

			ExcludedAssets.AddRange( excluded.Keys.Order( StringComparer.OrdinalIgnoreCase ) );
			SceneWarnings.AddRange( FindSceneWarnings( GetConfiguredScenePaths( project.Config ), included, ExcludedAssets.Where( x => !ExcludedByRules.ContainsKey( x ) ),
				path => AssetSystem.FindByPath( path ) is not null || FileSystem.Mounted.FileExists( path ) ) );
			if ( IsPreview )
			{
				// Included rows need the full graph too, even with stripping disabled or no exclusions.
				await BuildReviewSizes( candidates.Concat( publishedAssets ).Distinct(), included, cancel );
				BuildInclusionReview();
				var dependencySets = new[] { Assets.Select( x => x.Name ).ToArray(), ExcludedAssets.ToArray() };
				// Match the UI's type grouping and warm Included/Excluded separately.
				var groupExtensions = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
				foreach ( var type in AssetType.All )
					foreach ( var extension in type.FileExtensions.Append( type.FileExtension ) )
						if ( !string.IsNullOrWhiteSpace( extension ) ) groupExtensions.TryAdd( extension.TrimStart( '.' ), type.FileExtension );
				string GroupKey( string path )
				{
					var extension = Path.GetExtension( path ).TrimStart( '.' ).ToLowerInvariant();
					if ( extension.EndsWith( "_c" ) ) extension = extension[..^2];
					return groupExtensions.TryGetValue( extension, out var type ) ? $"asset:{type}" : $"file:{extension}";
				}
				await Task.Run( () =>
				{
					foreach ( var path in dependencySets.SelectMany( x => x ) )
					{
						cancel.ThrowIfCancellationRequested();
						GetDependencySummary( [path] );
					}
					foreach ( var group in dependencySets.SelectMany( paths => paths.GroupBy( GroupKey ) ) )
					{
						cancel.ThrowIfCancellationRequested();
						GetDependencySummary( group );
					}
				}, cancel );
			}
			if ( !publishingOptions.PublishUsedAssetsOnly || excluded.Count == 0 ) return;

			var references = new List<CodeAssetPath>();
			var resourceTypes = GetResourceTypePatterns( AssetType.All.Select( x => (x.ResourceType, x.FileExtension) ) );
			var codeRoots = AllCodePaths( project ).Distinct().Where( Directory.Exists ).ToArray();
			var codeCache = codeReferenceCaches.GetValue( project, _ => new CodeReferenceCache() );
			var typeSignature = string.Join( "\n", resourceTypes.OrderBy( x => x.Key, StringComparer.Ordinal ).Select( x => x.Key + ":" + string.Join( ",", x.Value ) ) );
			await Task.Run( () =>
			{
				foreach ( var root in codeRoots )
				{
					foreach ( var file in Directory.EnumerateFiles( root, "*.cs", SearchOption.AllDirectories ) )
					{
						cancel.ThrowIfCancellationRequested();
						var relative = Path.GetRelativePath( root, file ).Replace( '\\', '/' );
						if ( relative.Split( '/' ).Any( x => x.StartsWith( '.' ) || x.Equals( "obj", StringComparison.OrdinalIgnoreCase )
							|| x.Equals( "bin", StringComparison.OrdinalIgnoreCase ) || x.Equals( "Editor", StringComparison.OrdinalIgnoreCase ) || x.Equals( "unittest", StringComparison.OrdinalIgnoreCase ) ) ) continue;
						if ( relative.EndsWith( ".g.cs", StringComparison.OrdinalIgnoreCase ) ) continue;
						references.AddRange( codeCache.Analyze( file, File.ReadAllText( file ), typeSignature, resourceTypes, cancel ) );
					}
				}
				CodeAssetWarnings.AddRange( RespectRuleExclusions( FindCodeAssetWarnings( references, excluded ), ExcludedByRules ) );
				// Uploads need the warnings, not recommendations or UI size/dependency caches.
				if ( !IsPreview ) return;
				RecommendedRules = CompactRecommendedRules( RecommendedIncludes, availablePaths, includedPaths );
				// Warm the UI's graph caches in the same background pass.
				foreach ( var path in ExcludedAssets )
				{
					cancel.ThrowIfCancellationRequested();
					GetPublishImpact( [path] );
				}
				foreach ( var warning in CodeAssetWarnings )
				{
					cancel.ThrowIfCancellationRequested();
					GetPublishImpact( warning.ExcludedPaths );
					GetDependencySummary( warning.ExcludedPaths );
				}
				foreach ( var group in ExcludedAssets.GroupBy( Path.GetExtension, StringComparer.OrdinalIgnoreCase ) )
				{
					cancel.ThrowIfCancellationRequested();
					GetPublishImpact( group );
				}
				GetPublishImpact( ExcludedAssets );
			}, cancel );
			if ( CodeAssetWarnings.Count > 0 && !IsPreview )
				Log.Warning( $"{CodeAssetWarnings.Count:n0} code references may load excluded files. Review them in Publishing settings or the publish wizard." );
		}

		internal static long? ReadReviewFileSize( string path )
		{
			if ( string.IsNullOrEmpty( path ) ) return null;
			try
			{
				var info = new FileInfo( path );
				return info.Exists ? info.Length : null;
			}
			catch ( IOException ) { return null; }
			catch ( UnauthorizedAccessException ) { return null; }
		}

		/// <summary>
		/// Best-effort diagnostics only. Interpolations become wildcards; expressions are never evaluated.
		/// Looking at syntax rather than text avoids treating comments or escaped quotes as paths.
		/// </summary>
		internal static IEnumerable<CodeAssetPath> FindCodeAssetPaths( string code, string sourceFile, CancellationToken cancel = default, IReadOnlyDictionary<string, string[]> resourceTypes = null )
		{
			var tree = CSharpSyntaxTree.ParseText( code, cancellationToken: cancel );
			var root = tree.GetRoot( cancel );
			var lines = tree.GetText( cancel ).Lines;
			if ( resourceTypes is not null )
			{
				foreach ( var call in root.DescendantNodes().OfType<InvocationExpressionSyntax>() )
				{
					if ( call.Expression is not MemberAccessExpressionSyntax member
						|| member.Name is not GenericNameSyntax { Identifier.ValueText: "GetAll" } method
						|| method.TypeArgumentList.Arguments.Count != 1 ) continue;
					var receiver = member.Expression.ToString().Replace( "global::", "" );
					if ( receiver is not ("ResourceLibrary" or "Sandbox.ResourceLibrary" or "Game.Resources" or "Sandbox.Game.Resources") ) continue;
					var typeName = method.TypeArgumentList.Arguments[0].ToString().Replace( "global::", "" );
					if ( !resourceTypes.TryGetValue( typeName, out var patterns ) ) continue;
					var folder = "";
					var recursive = true;
					var arguments = call.ArgumentList.Arguments;
					var recursiveArgument = arguments.FirstOrDefault( x => x.NameColon?.Name.Identifier.ValueText == "recursive" );
					recursiveArgument ??= arguments.Count > 1 ? arguments[1] : null;
					if ( recursiveArgument?.Expression.IsKind( SyntaxKind.FalseLiteralExpression ) == true ) recursive = false;
					if ( call.ArgumentList.Arguments.Count > 0 )
					{
						var folderArgument = arguments.FirstOrDefault( x => x.NameColon?.Name.Identifier.ValueText == "filepath" ) ?? arguments[0];
						if ( folderArgument.Expression is not LiteralExpressionSyntax folderLiteral
							|| !folderLiteral.IsKind( SyntaxKind.StringLiteralExpression ) ) continue;
						folder = folderLiteral.Token.ValueText.Replace( '\\', '/' ).Trim( '/' );
						if ( folder.Length > 0 ) folder += "/";
					}
					// GetAll enumerates cached resources. Filters, lookups and generic pickers do not declare
					// that every matching asset must ship, even when the type and folder are known.
					foreach ( var pattern in patterns )
						yield return new CodeAssetPath( folder + pattern, sourceFile,
							call.GetLocation().GetLineSpan().StartLinePosition.Line + 1, $"GetAll<{typeName}>()", recursive, SourceLine: lines.GetLineFromPosition( call.SpanStart ).ToString().Trim() );
				}
			}
			foreach ( var node in root.DescendantNodes().Where( x => x is LiteralExpressionSyntax or InterpolatedStringExpressionSyntax ) )
			{
				cancel.ThrowIfCancellationRequested();
				if ( node.Ancestors().Any( x => x is InterpolatedStringExpressionSyntax || x is BinaryExpressionSyntax binary && binary.IsKind( SyntaxKind.AddExpression ) ) ) continue;
				string pattern = node switch
				{
					LiteralExpressionSyntax literal when literal.IsKind( SyntaxKind.StringLiteralExpression ) => literal.Token.ValueText,
					InterpolatedStringExpressionSyntax interpolation => string.Concat( interpolation.Contents.Select( x =>
						x is InterpolatedStringTextSyntax text ? text.TextToken.ValueText : "*" ) ),
					_ => null
				};
				if ( !IsCodeAssetPattern( pattern ) ) continue;
				var resourceDefault = ResourceDefaultUsage( node, pattern );
				yield return new CodeAssetPath( NormalizePublishPath( pattern ).TrimStart( '/' ), sourceFile,
					node.GetLocation().GetLineSpan().StartLinePosition.Line + 1, Usage: resourceDefault,
					Recommended: node is LiteralExpressionSyntax && pattern.IndexOfAny( ['*', '?'] ) < 0,
					SourceLine: lines.GetLineFromPosition( node.SpanStart ).ToString().Trim() );
			}
		}

		static string ResourceDefaultUsage( SyntaxNode node, string path )
		{
			if ( node is not LiteralExpressionSyntax || node.Parent is not EqualsValueClauseSyntax initializer
				|| path.IndexOfAny( ['*', '?'] ) >= 0 ) return null;
			SyntaxList<AttributeListSyntax> attributes;
			string name;
			if ( initializer.Parent is PropertyDeclarationSyntax property )
			{
				attributes = property.AttributeLists;
				name = property.Identifier.ValueText;
			}
			else if ( initializer.Parent is VariableDeclaratorSyntax variable && variable.Parent?.Parent is FieldDeclarationSyntax field )
			{
				attributes = field.AttributeLists;
				name = variable.Identifier.ValueText;
			}
			else return null;
			foreach ( var attribute in attributes.SelectMany( x => x.Attributes ) )
			{
				var type = attribute.Name.ToString().Split( '.' ).Last();
				var arguments = attribute.ArgumentList?.Arguments;
				if ( arguments is null ) continue;
				var extension = type switch
				{
					"ResourceSelect" or "ResourceSelectAttribute" => arguments.Value.FirstOrDefault( x => x.NameEquals?.Name.Identifier.ValueText == "Extension" )?.Expression,
					"ResourceType" or "ResourceTypeAttribute" => arguments.Value.FirstOrDefault()?.Expression,
					_ => null
				};
				if ( extension is LiteralExpressionSyntax text && text.IsKind( SyntaxKind.StringLiteralExpression )
					&& string.Equals( Path.GetExtension( path ).TrimStart( '.' ), text.Token.ValueText.TrimStart( '.' ), StringComparison.OrdinalIgnoreCase ) )
					return $"Default {name}: {path}";
			}
			return null;
		}

		static bool IsCodeAssetPattern( string pattern )
		{
			if ( string.IsNullOrWhiteSpace( pattern ) || pattern.IndexOfAny( [':', '"', '\r', '\n'] ) >= 0 ) return false;
			if ( pattern.Replace( '\\', '/' ).Split( '/' ).Contains( ".." ) ) return false;
			var extension = Path.GetExtension( pattern );
			if ( string.IsNullOrWhiteSpace( Path.GetFileNameWithoutExtension( pattern.Replace( '\\', '/' ) ) ) ) return false;
			if ( extension.Length < 2 || extension.IndexOfAny( ['*', '?', '/', '\\', ' '] ) >= 0 ) return false;
			var wildcard = pattern.IndexOfAny( ['*', '?'] );
			// An interpolation with no fixed prefix is too broad to infer an intended path.
			return wildcard < 0 || pattern[..wildcard].Any( char.IsLetterOrDigit );
		}

		internal static IEnumerable<ScenePublishWarning> FindSceneWarnings( IEnumerable<string> configured, IEnumerable<string> included,
			IEnumerable<string> excluded, Func<string, bool> exists )
		{
			var includedPaths = included.Select( NormalizePublishPath ).ToHashSet( StringComparer.OrdinalIgnoreCase );
			var scenes = excluded.Where( x => x.EndsWith( ".scene", StringComparison.OrdinalIgnoreCase ) )
				.Select( NormalizePublishPath ).Distinct( StringComparer.OrdinalIgnoreCase )
				.ToDictionary( x => x, _ => false, StringComparer.OrdinalIgnoreCase );
			foreach ( var path in configured.Where( IsReferencePath ) )
			{
				var normalized = NormalizePublishPath( path );
				if ( includedPaths.Contains( normalized ) || includedPaths.Contains( normalized + "_c" ) ) continue;
				scenes[normalized] = !exists( path );
			}
			return scenes.OrderByDescending( x => x.Value ).ThenBy( x => x.Key, StringComparer.OrdinalIgnoreCase )
				.Select( x => new ScenePublishWarning( x.Key.TrimStart( '/' ), x.Value ) );
		}

		internal static IEnumerable<CodeAssetWarning> FindCodeAssetWarnings( IEnumerable<CodeAssetPath> references, IReadOnlyDictionary<string, string[]> excluded )
		{
			// Normalize aliases once; literal paths use a lookup instead of rescanning every asset.
			var entries = excluded.Select( x => (x.Key, Paths: x.Value.Select( NormalizePublishPath ).Distinct().ToArray()) ).ToArray();
			var exact = entries.SelectMany( x => x.Paths.Select( path => (Path: path, x.Key) ) )
				.ToLookup( x => x.Path, x => x.Key, StringComparer.OrdinalIgnoreCase );
			foreach ( var reference in references.OrderByDescending( x => x.Recommended ).DistinctBy( x => (x.Pattern.ToLowerInvariant(), x.Recursive) ) )
			{
				var pattern = NormalizePublishPath( reference.Pattern );
				var directory = Path.GetDirectoryName( pattern );
				var matches = pattern.IndexOfAny( ['*', '?'] ) < 0
					? exact[pattern].Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ).ToArray()
					: entries.Where( x => x.Paths.Any( path => path.WildcardMatch( pattern )
						&& (reference.Recursive || string.Equals( Path.GetDirectoryName( path ), directory, StringComparison.OrdinalIgnoreCase )) ) )
						.Select( x => x.Key ).Order( StringComparer.OrdinalIgnoreCase ).ToArray();
				if ( matches.Length > 0 )
					yield return new CodeAssetWarning( reference.Pattern, reference.SourceFile, reference.Line, matches, reference.Usage, reference.Recommended, SourceLine: reference.SourceLine );
			}
		}

		internal static Dictionary<string, string[]> GetResourceTypePatterns( IEnumerable<(Type Type, string Extension)> types )
		{
			var result = new Dictionary<string, HashSet<string>>( StringComparer.Ordinal );
			foreach ( var (type, extension) in types )
			{
				if ( type is null || string.IsNullOrWhiteSpace( extension ) ) continue;
				var parents = new List<Type>( type.GetInterfaces() );
				for ( var parent = type; parent is not null && parent != typeof( object ); parent = parent.BaseType ) parents.Add( parent );
				foreach ( var parent in parents )
				{
					foreach ( var name in new[] { parent.Name, parent.FullName?.Replace( '+', '.' ) }.Where( x => x is not null ) )
					{
						if ( !result.TryGetValue( name, out var patterns ) ) result[name] = patterns = new();
						patterns.Add( "*." + extension.TrimStart( '.' ) );
					}
				}
			}
			return result.ToDictionary( x => x.Key, x => x.Value.Order().ToArray() );
		}

	}
}
