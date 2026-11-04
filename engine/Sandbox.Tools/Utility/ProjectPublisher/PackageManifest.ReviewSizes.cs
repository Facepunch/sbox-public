using System;
using System.Threading;

namespace Editor;

public partial class ProjectPublisher
{
	public partial class PackageManifest
	{
		/// <summary>Additional uncompressed bytes, estimated from existing compiler metadata.</summary>
		public sealed record PublishImpact( long DirectBytes, long TransitiveBytes, bool DirectUnknown, bool TransitiveUnknown )
		{
			public long? TotalBytes => DirectUnknown || TransitiveUnknown ? null : DirectBytes + TransitiveBytes;
		}

		internal sealed record ReviewSizeNode( Dictionary<string, long?> Files, string[] References, bool Incomplete = false,
			Dictionary<string, long?> RootFiles = null, bool IsAsset = true );

		readonly Dictionary<string, ReviewSizeNode> reviewSizeGraph = new( StringComparer.OrdinalIgnoreCase );
		readonly HashSet<string> reviewIncludedFiles = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, PublishImpact> reviewImpactCache = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, PublishImpact> reviewFootprintCache = new( StringComparer.OrdinalIgnoreCase );

		static string ReviewPath( string path ) => NormalizePublishPath( path ).TrimStart( '/' );

		async Task BuildReviewSizes( IEnumerable<Asset> assets, HashSet<string> included, CancellationToken cancel )
		{
			reviewIncludedFiles.UnionWith( included.Select( ReviewPath ) );
			var queue = new Queue<Asset>( assets );
			var visited = new HashSet<Asset>();
			while ( queue.TryDequeue( out var asset ) )
			{
				cancel.ThrowIfCancellationRequested();
				await YieldPreview( cancel );
				if ( !visited.Add( asset ) || !CanPublishFile( asset ) ) continue;
				var files = new Dictionary<string, long?>( StringComparer.OrdinalIgnoreCase );
				var rootFiles = new Dictionary<string, long?>( StringComparer.OrdinalIgnoreCase );
				void AddFileSize( string relative, string absolute )
				{
					if ( !string.IsNullOrEmpty( relative ) ) files[ReviewPath( relative )] = ReadReviewFileSize( absolute );
				}
				var compiled = asset.GetCompiledFile( false );
				AddFileSize( compiled, asset.GetCompiledFile( true ) );
				if ( asset.AssetType.HasDependencies && string.IsNullOrEmpty( compiled ) ) files[$"unknown:{ReviewPath( asset.Path )}"] = null;
				var source = asset.GetSourceFile( false );
				var assetPath = ReviewPath( asset.Path );
				reviewAssetAliases[assetPath] = assetPath;
				foreach ( var alias in new[] { asset.RelativePath, source, compiled }.Where( x => !string.IsNullOrWhiteSpace( x ) ) )
					reviewAssetAliases.TryAdd( ReviewPath( alias ), assetPath );
				if ( IncludeSourceFiles ) AddFileSize( source, asset.GetSourceFile( true ) );
				else if ( (string.IsNullOrEmpty( compiled ) || IsStandaloneSourceFile( asset )) && !asset.IsCompileFailed && !string.IsNullOrEmpty( source ) && LooseFileAllowed( source, false ) )
					rootFiles[ReviewPath( source )] = ReadReviewFileSize( asset.GetSourceFile( true ) );

				foreach ( var file in asset.GetAdditionalGameFiles() ) AddFileSize( file, FileSystem.Mounted.GetFullPath( file ) );
				foreach ( var file in asset.GetAdditionalContentFiles() )
					if ( (IncludeSourceFiles || file.EndsWith( ".rect", StringComparison.OrdinalIgnoreCase )) && AssetSystem.FindByPath( file ) is { } extra )
						AddFileSize( extra.RelativePath, extra.AbsolutePath );
				var inputReferences = new List<string>();
				if ( IncludeSourceFiles || asset.AssetType.IsGameResource )
					foreach ( var file in asset.GetInputDependencies() )
						if ( AssetSystem.FindByPath( file ) is { } input && (IncludeSourceFiles || input.AssetType == AssetType.ImageFile) )
						{
							var key = $"input:{ReviewPath( input.RelativePath )}";
							inputReferences.Add( key );
							reviewSizeGraph[key] = new( new( StringComparer.OrdinalIgnoreCase )
							{ [ReviewPath( input.RelativePath )] = ReadReviewFileSize( input.AbsolutePath ) }, [], IsAsset: false );
							// Runtime image assets are also collected by FindAssetsToPublish.
							if ( asset.AssetType.IsGameResource && input.AssetType == AssetType.ImageFile )
							{
								inputReferences.Add( ReviewPath( input.Path ) );
								queue.Enqueue( input );
							}
						}

				// Never compile excluded assets or generate thumbnails for this estimate.
				var references = asset.GetReferences( false ).Where( CanPublishFile ).ToArray();
				reviewSizeGraph[ReviewPath( asset.Path )] = new( files, references.Select( x => ReviewPath( x.Path ) ).Concat( inputReferences ).ToArray(),
					asset.AssetType.HasDependencies && (!asset.IsCompiled || asset.IsCompileFailed || string.IsNullOrEmpty( compiled )), rootFiles );
				foreach ( var reference in references ) queue.Enqueue( reference );
			}
			foreach ( var (path, size) in ExcludedAssetSizes )
				reviewSizeGraph.TryAdd( ReviewPath( path ), new( new( StringComparer.OrdinalIgnoreCase ) { [ReviewPath( path )] = size }, [], IsAsset: false ) );
			foreach ( var file in Assets )
				reviewSizeGraph.TryAdd( ReviewPath( file.Name ), new( new( StringComparer.OrdinalIgnoreCase ) { [ReviewPath( file.Name )] = file.Size }, [], IsAsset: false ) );
		}

		/// <summary>Counts shared files once, and omits files already in this publish manifest.</summary>
		public PublishImpact GetPublishImpact( IEnumerable<string> paths )
			=> GetReviewSize( paths, reviewImpactCache, reviewIncludedFiles );

		/// <summary>Direct and transitive size of files in this publish manifest. Shared files count once.</summary>
		public PublishImpact GetPublishFootprint( IEnumerable<string> paths )
			=> GetReviewSize( paths, reviewFootprintCache, [], reviewIncludedFiles );

		PublishImpact GetReviewSize( IEnumerable<string> paths, Dictionary<string, PublishImpact> cache, IEnumerable<string> included, IReadOnlySet<string> filesToCount = null )
		{
			var roots = paths.Select( ReviewPath ).Select( x => reviewAssetAliases.GetValueOrDefault( x, x ) )
				.Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ).ToArray();
			var key = string.Join( '\n', roots );
			if ( !cache.TryGetValue( key, out var impact ) )
				cache[key] = impact = CalculatePublishImpact( roots, reviewSizeGraph, included, filesToCount );
			return impact;
		}

		internal static PublishImpact CalculatePublishImpact( IEnumerable<string> paths, IReadOnlyDictionary<string, ReviewSizeNode> graph, IEnumerable<string> included, IReadOnlySet<string> filesToCount = null )
		{
			var roots = paths.Distinct( StringComparer.OrdinalIgnoreCase ).ToArray();
			var seenFiles = new HashSet<string>( included, StringComparer.OrdinalIgnoreCase );
			var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			long direct = 0, transitive = 0;
			bool directUnknown = false, transitiveUnknown = false;
			void Count( Dictionary<string, long?> files, ref long total, ref bool unknown )
			{
				if ( files is null ) return;
				foreach ( var (path, size) in files )
				{
					if ( filesToCount is not null && !filesToCount.Contains( path ) ) continue;
					if ( !seenFiles.Add( path ) ) continue;
					if ( size is { } bytes ) total += bytes;
					else unknown = true;
				}
			}
			// Count every selected root before walking dependencies, even if one root references another.
			foreach ( var path in roots )
			{
				if ( !graph.TryGetValue( path, out var node ) ) { directUnknown = true; continue; }
				Count( node.Files, ref direct, ref directUnknown );
				Count( node.RootFiles, ref direct, ref directUnknown );
			}
			var queue = new Queue<string>( roots );
			while ( queue.TryDequeue( out var path ) )
			{
				if ( !visited.Add( path ) ) continue;
				if ( !graph.TryGetValue( path, out var node ) ) { transitiveUnknown = true; continue; }
				Count( node.Files, ref transitive, ref transitiveUnknown );
				transitiveUnknown |= node.Incomplete;
				foreach ( var reference in node.References ) queue.Enqueue( reference );
			}
			return new( direct, transitive, directUnknown, transitiveUnknown );
		}

		/// <summary>Unique transitive assets, including ones already published. Selected roots are not counted.</summary>
		public sealed record DependencySummary( int Count, bool Incomplete );
		readonly Dictionary<string, string> reviewAssetAliases = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, DependencySummary> reviewDependencyCache = new( StringComparer.OrdinalIgnoreCase );

		public DependencySummary GetDependencySummary( IEnumerable<string> paths )
		{
			var roots = paths.Select( ReviewPath ).Select( x => reviewAssetAliases.GetValueOrDefault( x, x ) )
				.Distinct( StringComparer.OrdinalIgnoreCase ).Order( StringComparer.OrdinalIgnoreCase ).ToArray();
			var key = string.Join( '\n', roots );
			if ( !reviewDependencyCache.TryGetValue( key, out var summary ) )
				reviewDependencyCache[key] = summary = CalculateDependencySummary( roots, reviewSizeGraph );
			return summary;
		}

		internal static DependencySummary CalculateDependencySummary( IEnumerable<string> paths, IReadOnlyDictionary<string, ReviewSizeNode> graph )
		{
			var roots = paths.ToHashSet( StringComparer.OrdinalIgnoreCase );
			var visited = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var queue = new Queue<string>( roots );
			var count = 0;
			var incomplete = false;
			while ( queue.TryDequeue( out var path ) )
			{
				if ( !visited.Add( path ) ) continue;
				if ( !graph.TryGetValue( path, out var node ) )
				{
					// The reference itself is known, even if its outgoing references are unavailable.
					if ( !roots.Contains( path ) ) count++;
					incomplete = true;
					continue;
				}
				if ( node.IsAsset && !roots.Contains( path ) ) count++;
				incomplete |= node.Incomplete;
				foreach ( var reference in node.References ) queue.Enqueue( reference );
			}
			return new( count, incomplete );
		}

		public sealed record EstimateIssue( string Path, string Reason );

		/// <summary>The actual gaps behind incomplete estimates, reported once for the whole preview.</summary>
		public IReadOnlyList<EstimateIssue> GetEstimateIssues() => FindEstimateIssues( reviewSizeGraph );

		internal static IReadOnlyList<EstimateIssue> FindEstimateIssues( IReadOnlyDictionary<string, ReviewSizeNode> graph )
		{
			var issues = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
			foreach ( var (path, node) in graph )
			{
				if ( node.Incomplete ) issues.TryAdd( path, "Dependency metadata unavailable" );
				foreach ( var reference in node.References )
					if ( !graph.ContainsKey( reference ) ) issues.TryAdd( reference, "Dependency metadata unavailable" );
				foreach ( var (file, size) in node.Files.Concat( node.RootFiles ?? [] ) )
					if ( size is null ) issues.TryAdd( file.StartsWith( "unknown:", StringComparison.Ordinal ) ? file[8..] : file, "File size unavailable" );
			}
			return issues.OrderBy( x => x.Key, StringComparer.OrdinalIgnoreCase ).Select( x => new EstimateIssue( x.Key, x.Value ) ).ToArray();
		}
	}
}
