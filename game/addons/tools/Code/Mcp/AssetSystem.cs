using System;
using System.Linq;
using System.Threading.Tasks;

namespace Editor.Mcp;

[McpToolset( "asset", "The asset system - search, inspect, compile, edit and preview the project's assets" )]
public static partial class AssetTools
{
	/// <summary>
	/// Search the asset system by keywords and type. An empty query lists every asset, so use limit and offset
	/// to page through. Returns relative asset paths - pass one to asset_info, asset_compile, asset_read or asset_dependencies.
	/// </summary>
	/// <param name="query">Space separated keywords. Every keyword must match the asset's path or one of its tags. Empty matches everything.</param>
	/// <param name="type">Filter to one asset type - a file extension (vmdl, vmat, prefab) or friendly name (Model, Material). See asset_types.</param>
	/// <param name="projectOnly">Only assets belonging to the currently open project, excluding engine content and cloud downloads.</param>
	/// <param name="unreferencedOnly">Only assets nothing else references - useful for finding unused assets. Note map files and code can use an asset without the asset system knowing.</param>
	/// <param name="compileFailedOnly">Only assets whose last compile failed.</param>
	/// <param name="uncompiledOnly">Only assets with no compiled version at all.</param>
	/// <param name="limit">How many results to return.</param>
	/// <param name="offset">How many results to skip, for paging.</param>
	[McpTool.ReadOnly( "asset_search" )]
	public static AssetPage AssetSearch( string query = "", string type = "", bool projectOnly = false,
		bool unreferencedOnly = false, bool compileFailedOnly = false, bool uncompiledOnly = false,
		[Sandbox.Range( 1, 500 )] int limit = 50, int offset = 0 )
	{
		offset = Math.Max( offset, 0 );

		var assets = AssetSystem.All;

		if ( !string.IsNullOrWhiteSpace( type ) )
		{
			var assetType = FindAssetType( type );
			assets = assets.Where( x => x.AssetType == assetType );
		}

		if ( projectOnly )
		{
			var root = Sandbox.Project.Current?.RootDirectory?.FullName.Replace( '\\', '/' )
				?? throw new Exception( "No project is currently open" );

			assets = assets.Where( x => x.AbsolutePath?.StartsWith( root, StringComparison.OrdinalIgnoreCase ) ?? false );
		}

		if ( unreferencedOnly )
		{
			assets = assets.Where( x => x.GetDependants( false ).Count == 0 );
		}

		if ( compileFailedOnly )
		{
			assets = assets.Where( x => x.IsCompileFailed );
		}

		if ( uncompiledOnly )
		{
			assets = assets.Where( x => !x.IsCompiled );
		}

		var keywords = query?.Split( ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ?? [];

		assets = assets.Where( x => keywords.All( k =>
			(x.Path?.Contains( k, StringComparison.OrdinalIgnoreCase ) ?? false) || x.Tags.Contains( k ) ) );

		var matches = assets.OrderBy( x => x.Path, StringComparer.OrdinalIgnoreCase ).ToArray();
		var page = matches.Skip( offset ).Take( limit ).ToArray();

		return new AssetPage
		{
			Total = matches.Length,
			Showing = page.Length,
			Offset = offset,
			Assets = page.Select( FoundAsset.From ).ToArray()
		};
	}

	/// <summary>
	/// Everything about one asset - name, type, source and compiled file paths, compile state, tags, cloud
	/// package, and how many assets it references and is used by. Explore those relationships with asset_dependencies.
	/// </summary>
	/// <param name="path">Asset path, as returned by asset_search. Absolute paths work too.</param>
	[McpTool.ReadOnly( "asset_info" )]
	public static AssetDetails AssetInfo( string path )
	{
		var asset = FindAsset( path );

		return new AssetDetails
		{
			Name = asset.Name,
			Path = asset.Path,
			Type = asset.AssetType?.FriendlyName,
			Extension = asset.AssetType?.FileExtension,
			IsGameResource = asset.AssetType?.IsGameResource ?? false,
			SourceFile = asset.GetSourceFile( true ),
			CompiledFile = asset.GetCompiledFile( true ),
			IsCompiled = asset.IsCompiled,
			IsCompiledAndUpToDate = asset.IsCompiled && asset.IsCompiledAndUpToDate,
			IsCompileFailed = asset.IsCompileFailed,
			CanRecompile = asset.CanRecompile,
			IsCloud = asset.IsCloud,
			IsTransient = asset.IsTransient,
			IsProcedural = asset.IsProcedural,
			IsTrivialChild = asset.IsTrivialChild,
			Package = asset.Package?.FullIdent,
			Tags = asset.Tags.GetAll().ToArray(),
			ReferenceCount = asset.GetReferences( false ).Count,
			DependantCount = asset.GetDependants( false ).Count,
			LastOpened = asset.LastOpened,
		};
	}

	/// <summary>
	/// Recompile an asset from its source file. Compile errors and warnings land in the console - check
	/// read_console after a failure.
	/// </summary>
	/// <param name="path">Asset path, as returned by asset_search.</param>
	/// <param name="full">Force a full recompile instead of an incremental one.</param>
	[McpTool( "asset_compile" )]
	public static AssetCompileResult AssetCompile( string path, bool full = false )
	{
		var asset = FindAsset( path );

		if ( !asset.CanRecompile )
			throw new Exception( $"'{asset.Path}' can't be recompiled - it has no source file (cloud and compiled-only assets can't compile)" );

		var success = asset.Compile( full ) && !asset.IsCompileFailed;

		return new AssetCompileResult
		{
			Success = success,
			Path = asset.Path,
			CompiledFile = asset.GetCompiledFile( true ),
			Hint = success ? null : "Compile failed - read_console will show the compiler's errors"
		};
	}

	/// <summary>
	/// Walk an asset's relationships - the assets it references (uses), the assets that depend on it, or the
	/// assets it was generated from. Deep recurses the whole chain, e.g. a model's materials and their textures.
	/// </summary>
	/// <param name="path">Asset path, as returned by asset_search.</param>
	/// <param name="relation">Which direction to walk.</param>
	/// <param name="deep">Recurse instead of returning only direct relationships.</param>
	/// <param name="type">Filter results to one asset type - a file extension or friendly name. See asset_types.</param>
	/// <param name="limit">How many results to return.</param>
	[McpTool.ReadOnly( "asset_dependencies" )]
	public static AssetList AssetDependencies( string path, AssetRelation relation = AssetRelation.References,
		bool deep = false, string type = "", [Sandbox.Range( 1, 500 )] int limit = 100 )
	{
		var asset = FindAsset( path );

		var related = relation switch
		{
			AssetRelation.References => asset.GetReferences( deep ),
			AssetRelation.Dependants => asset.GetDependants( deep ),
			AssetRelation.Parents => asset.GetParents( deep ),
			_ => throw new ArgumentOutOfRangeException( nameof( relation ) )
		};

		if ( !string.IsNullOrWhiteSpace( type ) )
		{
			var assetType = FindAssetType( type );
			related = related.Where( x => x.AssetType == assetType ).ToList();
		}

		return new AssetList
		{
			Path = asset.Path,
			Relation = relation.ToString(),
			Total = related.Count,
			Assets = related.Take( limit ).Select( FoundAsset.From ).ToArray()
		};
	}

	public enum AssetRelation
	{
		References,
		Dependants,
		Parents
	}

	/// <summary>
	/// Read a game resource asset's raw json - the actual data of a prefab, sound event, clothing definition
	/// or any other GameResource type. Doesn't work on binary assets like models and textures.
	/// </summary>
	/// <param name="path">Asset path, as returned by asset_search.</param>
	[McpTool.ReadOnly( "asset_read" )]
	public static object AssetRead( string path )
	{
		var asset = FindAsset( path );

		if ( asset.AssetType?.IsGameResource != true )
			throw new Exception( $"'{asset.Path}' is a {asset.AssetType?.FriendlyName} - only game resources contain readable json. Use asset_info for file paths instead." );

		return asset.ReadJson()
			?? throw new Exception( $"Couldn't read '{asset.Path}' - the file may be missing from disk" );
	}

	/// <summary>
	/// Overwrite a game resource asset's json and recompile it. Read the current json with asset_read first,
	/// modify it, and pass the whole document back - this replaces the entire file. Doesn't work on binary
	/// assets like models and textures.
	/// </summary>
	/// <param name="path">Asset path, as returned by asset_search.</param>
	/// <param name="json">The complete json document to write.</param>
	[McpTool( "asset_write" )]
	public static AssetWriteResult AssetWrite( string path, string json )
	{
		var asset = FindAsset( path );

		if ( asset.AssetType?.IsGameResource != true )
			throw new Exception( $"'{asset.Path}' is a {asset.AssetType?.FriendlyName} - only game resources can be written as json" );

		var source = asset.GetSourceFile( true );

		if ( string.IsNullOrWhiteSpace( source ) )
			throw new Exception( $"'{asset.Path}' has no source file to write - cloud and compiled-only assets are read only" );

		try
		{
			System.Text.Json.Nodes.JsonNode.Parse( json );
		}
		catch ( Exception e )
		{
			throw new Exception( $"That isn't valid json, nothing was written - {e.Message}" );
		}

		System.IO.File.WriteAllText( source, json );

		var success = asset.Compile( false ) && !asset.IsCompileFailed;

		return new AssetWriteResult
		{
			Success = success,
			Path = asset.Path,
			Hint = success ? null : "Wrote the file but the compile failed - read_console will show why, asset_read shows what's there now"
		};
	}

	/// <summary>
	/// Render an asset's preview thumbnail and return it as an image - see what a model, material or texture
	/// actually looks like.
	/// </summary>
	/// <param name="path">Asset path, as returned by asset_search.</param>
	[McpTool.ReadOnly( "asset_thumbnail" )]
	public static async Task<McpResult> AssetThumbnail( string path )
	{
		var asset = FindAsset( path );

		var thumb = await asset.RenderThumb();

		if ( thumb is null )
			throw new Exception( $"'{asset.Path}' ({asset.AssetType?.FriendlyName}) doesn't support preview thumbnails" );

		return McpResult.Image( thumb.GetPng(), "image/png" ).WithText( asset.Path );
	}

	/// <summary>
	/// The loose disk files behind an asset - the input files it's built from (.fbx, .tga, .png...), extra
	/// content and game files it ships with, and reference paths that couldn't be resolved to assets.
	/// </summary>
	/// <param name="path">Asset path, as returned by asset_search.</param>
	[McpTool.ReadOnly( "asset_files" )]
	public static AssetFilesResult AssetFiles( string path )
	{
		var asset = FindAsset( path );

		return new AssetFilesResult
		{
			Path = asset.Path,
			SourceFile = asset.GetSourceFile( true ),
			CompiledFile = asset.GetCompiledFile( true ),
			InputFiles = asset.GetInputDependencies().ToArray(),
			AdditionalContentFiles = asset.GetAdditionalContentFiles().ToArray(),
			AdditionalGameFiles = asset.GetAdditionalGameFiles().ToArray(),
			UnresolvedReferences = asset.GetUnrecognizedReferencePaths().ToArray(),
		};
	}

	/// <summary>
	/// Find the assets built from a loose disk file - which models use this .fbx, which textures use this .png.
	/// Matches the end of the path, so a filename is enough.
	/// </summary>
	/// <param name="file">File path or filename, e.g. chair.fbx</param>
	/// <param name="limit">How many results to return.</param>
	[McpTool.ReadOnly( "asset_find_by_file" )]
	public static AssetMatches AssetFindByFile( string file, [Sandbox.Range( 1, 500 )] int limit = 50 )
	{
		if ( string.IsNullOrWhiteSpace( file ) )
			throw new Exception( "File path is empty" );

		var normalized = file.Replace( '\\', '/' ).TrimStart( '/' );

		var matches = AssetSystem.All
			.Where( x => x.GetInputDependencies().Any( f => f?.Replace( '\\', '/' ).EndsWith( normalized, StringComparison.OrdinalIgnoreCase ) ?? false ) )
			.OrderBy( x => x.Path, StringComparer.OrdinalIgnoreCase )
			.ToArray();

		return new AssetMatches
		{
			Total = matches.Length,
			Assets = matches.Take( limit ).Select( FoundAsset.From ).ToArray()
		};
	}

	/// <summary>
	/// Every asset type the editor knows, with the file extensions and category of each. Use a type's
	/// extension or name to filter asset_search.
	/// </summary>
	[McpTool.ReadOnly( "asset_types" )]
	public static AssetTypeList AssetTypes()
	{
		return new AssetTypeList
		{
			Types = AssetType.All
				.OrderBy( x => x.Category )
				.ThenBy( x => x.FriendlyName )
				.Select( x => new AssetTypeInfo
				{
					Name = x.FriendlyName,
					Extension = x.FileExtension,
					AllExtensions = x.FileExtensions?.ToArray(),
					Category = x.Category,
					IsGameResource = x.IsGameResource,
				} ).ToArray()
		};
	}

	/// <summary>
	/// Create a new game resource asset - a prefab, sound event or any other GameResource type.
	/// Give starting json to fill it, or leave empty for a default. Fails if the asset already
	/// exists - asset_write edits existing ones.
	/// </summary>
	/// <param name="type">Asset type file extension, e.g. 'prefab' or 'sndevt'. See asset_types.</param>
	/// <param name="path">Where to create it, relative to the project's assets folder, e.g. 'weapons/pistol.prefab'.</param>
	/// <param name="json">Starting json document. Empty creates a default resource.</param>
	[McpTool( "create_asset" )]
	public static CreatedAsset CreateAsset( string type, string path, string json = "" )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new Exception( "Give a path relative to the project's assets folder" );

		var assetsPath = Sandbox.Project.Current?.GetAssetsPath()
			?? throw new Exception( "No project is currently open" );

		var absolute = System.IO.Path.Combine( assetsPath, path.Replace( '\\', '/' ).TrimStart( '/' ) );
		absolute = System.IO.Path.ChangeExtension( absolute, type.Trim().TrimStart( '.' ) );

		if ( System.IO.File.Exists( absolute ) )
			throw new Exception( $"'{path}' already exists - asset_write edits existing assets" );

		if ( !string.IsNullOrWhiteSpace( json ) )
		{
			try
			{
				System.Text.Json.Nodes.JsonNode.Parse( json );
			}
			catch ( Exception e )
			{
				throw new Exception( $"That isn't valid json, nothing was created - {e.Message}" );
			}
		}

		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( absolute ) );

		var asset = AssetSystem.CreateResource( type, absolute )
			?? throw new Exception( $"Couldn't create a '{type}' - asset_types lists the valid extensions" );

		if ( !string.IsNullOrWhiteSpace( json ) )
		{
			System.IO.File.WriteAllText( asset.GetSourceFile( true ), json );
			asset.Compile( false );
		}

		return new CreatedAsset
		{
			Path = asset.Path,
			SourceFile = asset.GetSourceFile( true ),
			IsCompileFailed = asset.IsCompileFailed,
			Hint = asset.IsCompileFailed ? "Compile failed - read_console shows why" : null
		};
	}

	/// <summary>An asset path and its type.</summary>
	public class FoundAsset
	{
		public string Path { get; set; }
		public string Type { get; set; }

		internal static FoundAsset From( Asset asset ) => new() { Path = asset.Path, Type = asset.AssetType?.FriendlyName };
	}

	/// <summary>A page of asset search results.</summary>
	public class AssetPage
	{
		/// <summary>How many assets matched in total, beyond this page.</summary>
		public int Total { get; set; }
		public int Showing { get; set; }
		public int Offset { get; set; }
		public FoundAsset[] Assets { get; set; }
	}

	/// <summary>Assets related to another asset.</summary>
	public class AssetList
	{
		public string Path { get; set; }
		public string Relation { get; set; }
		/// <summary>How many related assets exist, beyond what's shown.</summary>
		public int Total { get; set; }
		public FoundAsset[] Assets { get; set; }
	}

	/// <summary>Assets built from a disk file.</summary>
	public class AssetMatches
	{
		public int Total { get; set; }
		public FoundAsset[] Assets { get; set; }
	}

	/// <summary>Everything about one asset.</summary>
	public class AssetDetails
	{
		public string Name { get; set; }
		public string Path { get; set; }
		public string Type { get; set; }
		public string Extension { get; set; }
		public bool IsGameResource { get; set; }
		public string SourceFile { get; set; }
		public string CompiledFile { get; set; }
		public bool IsCompiled { get; set; }
		public bool IsCompiledAndUpToDate { get; set; }
		public bool IsCompileFailed { get; set; }
		public bool CanRecompile { get; set; }
		public bool IsCloud { get; set; }
		public bool IsTransient { get; set; }
		public bool IsProcedural { get; set; }
		public bool IsTrivialChild { get; set; }
		public string Package { get; set; }
		public string[] Tags { get; set; }
		public int ReferenceCount { get; set; }
		public int DependantCount { get; set; }
		public DateTime? LastOpened { get; set; }
	}

	/// <summary>How an asset compile went.</summary>
	public class AssetCompileResult
	{
		public bool Success { get; set; }
		public string Path { get; set; }
		public string CompiledFile { get; set; }
		public string Hint { get; set; }
	}

	/// <summary>How an asset write went.</summary>
	public class AssetWriteResult
	{
		public bool Success { get; set; }
		public string Path { get; set; }
		public string Hint { get; set; }
	}

	/// <summary>What create_asset made.</summary>
	public class CreatedAsset
	{
		public string Path { get; set; }
		public string SourceFile { get; set; }
		public bool IsCompileFailed { get; set; }
		public string Hint { get; set; }
	}

	/// <summary>The disk files behind an asset.</summary>
	public class AssetFilesResult
	{
		public string Path { get; set; }
		public string SourceFile { get; set; }
		public string CompiledFile { get; set; }
		public string[] InputFiles { get; set; }
		public string[] AdditionalContentFiles { get; set; }
		public string[] AdditionalGameFiles { get; set; }
		public string[] UnresolvedReferences { get; set; }
	}

	/// <summary>Every asset type the editor knows.</summary>
	public class AssetTypeList
	{
		public AssetTypeInfo[] Types { get; set; }
	}

	/// <summary>One asset type.</summary>
	public class AssetTypeInfo
	{
		public string Name { get; set; }
		/// <summary>The extension asset_search's type filter takes.</summary>
		public string Extension { get; set; }
		public string[] AllExtensions { get; set; }
		public string Category { get; set; }
		public bool IsGameResource { get; set; }
	}

	static Asset FindAsset( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new Exception( "Asset path is empty - find assets with asset_search" );

		var found = AssetSystem.FindByPath( path );
		if ( found is not null ) return found;

		var normalized = path.Replace( '\\', '/' ).TrimStart( '/' );

		var suggestions = AssetSystem.All
			.Where( x => x.Path?.EndsWith( normalized, StringComparison.OrdinalIgnoreCase ) ?? false )
			.Concat( AssetSystem.All.Where( x => x.Path?.Contains( normalized, StringComparison.OrdinalIgnoreCase ) ?? false ) )
			.Select( x => x.Path )
			.Distinct()
			.Take( 5 )
			.ToArray();

		var suggest = suggestions.Length > 0 ? $" Did you mean {string.Join( " or ", suggestions )}?" : " Find assets with asset_search.";

		throw new Exception( $"No asset at '{path}'.{suggest}" );
	}

	static AssetType FindAssetType( string type )
	{
		type = type.Trim().TrimStart( '.' );

		var found = AssetType.FromExtension( type.ToLowerInvariant() )
			?? AssetType.All.FirstOrDefault( x => string.Equals( x.FriendlyName, type, StringComparison.OrdinalIgnoreCase ) )
			?? AssetType.Find( type, true );

		return found ?? throw new Exception( $"Unknown asset type '{type}' - asset_types lists every extension and name" );
	}
}
