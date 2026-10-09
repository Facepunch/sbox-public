using System;
using System.IO;
using System.Text;

namespace Editor;

/// <summary>
/// Creates .vmat files by matching texture files on disk against the filename suffixes a shader
/// declares for its texture inputs ("_color"/"_normal" etc)
/// </summary>
internal static class MaterialGenerator
{
	internal const string DefaultShader = "shaders/complex.shader";

	/// <summary>
	/// A group of texture files that share a directory and a base name, keyed by their suffix..
	/// For example awesome_mat_color.png + awesome_mat_normal.png becomes one set named "awesome_mat"
	/// </summary>
	internal sealed class TextureSet
	{
		public string Directory { get; init; }

		public string BaseName { get; init; }

		public Dictionary<string, Asset> BySuffix { get; init; }
	}

	/// <summary>
	/// Maps a texture file suffix to the shader variable that consumes it,
	/// returns null if the shader couldn't be loaded
	/// </summary>
	internal static Dictionary<string, string> GetSuffixMap( string shaderPath )
	{
		var shader = Shader.Load( shaderPath );
		if ( shader is null || !shader.IsValid )
		{
			Log.Warning( $"Material Generator: couldn't load shader {shaderPath}" );
			return null;
		}

		List<Shader.VariableDescription> variables;

		try
		{
			variables = shader.Schema?.Variables;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Material Generator: couldn't read schema for {shaderPath} - {e.Message}" );
			return null;
		}

		if ( variables is null )
			return null;

		var map = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		var bySuffix = variables
			.Where( x => !string.IsNullOrWhiteSpace( x.TextureEnding ) && !string.IsNullOrWhiteSpace( x.Name ) )
			.GroupBy( x => x.TextureEnding, StringComparer.OrdinalIgnoreCase );

		foreach ( var group in bySuffix )
		{
			var name = group
				.Select( x => x.Name )
				.OrderBy( x => x.Length )
				.ThenBy( x => x, StringComparer.OrdinalIgnoreCase )
				.First();

			map[group.Key] = name;
		}

		return map;
	}

	/// <summary>
	/// Find every group of suffixed texture files at or below root directory
	/// </summary>
	internal static List<TextureSet> DiscoverTextureSets( string rootDirectory, IEnumerable<string> suffixes )
	{
		var result = new List<TextureSet>();

		if ( string.IsNullOrWhiteSpace( rootDirectory ) || suffixes is null )
			return result;

		var ordered = suffixes
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderByDescending( x => x.Length )
			.ToArray();

		if ( ordered.Length == 0 )
			return result;

		var root = rootDirectory.NormalizeFilename( false );
		if ( !root.EndsWith( '/' ) )
			root += "/";

		// grouped by directory + base name, case insensitive
		var byKey = new Dictionary<(string Directory, string BaseName), TextureSet>();

		var images = AssetSystem.All
			.Where( IsImage )
			.OrderBy( x => x.AbsolutePath, StringComparer.OrdinalIgnoreCase );

		foreach ( var asset in images )
		{
			var absolute = asset.AbsolutePath.NormalizeFilename( false );
			if ( !absolute.StartsWith( root, StringComparison.Ordinal ) )
				continue;

			// parsed from the un-normalized name so the material keeps the casing the artist used
			if ( !TrySplitSuffix( Path.GetFileNameWithoutExtension( asset.AbsolutePath ), ordered, out var baseName, out var suffix ) )
				continue;

			// the lowercased path is only ever used for grouping - the set keeps the real one, so
			// writing the material works on case-sensitive filesystems too
			var key = (Path.GetDirectoryName( absolute ).NormalizeFilename( false ), baseName.ToLowerInvariant());

			if ( !byKey.TryGetValue( key, out var set ) )
			{
				set = new TextureSet
				{
					Directory = Path.GetDirectoryName( asset.AbsolutePath ).NormalizeFilename( false, false ),
					BaseName = baseName,
					BySuffix = new Dictionary<string, Asset>( StringComparer.OrdinalIgnoreCase ),
				};

				byKey[key] = set;
				result.Add( set );
			}

			set.BySuffix.TryAdd( suffix, asset );
		}

		return result;
	}

	internal static TextureSet FindSetForSlot( List<TextureSet> sets, string slotName )
	{
		if ( sets is null || string.IsNullOrWhiteSpace( slotName ) )
			return null;

		var name = Path.GetFileNameWithoutExtension( slotName.NormalizeFilename( false ) );
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		// pick whatever shows up first, user must make sure that they don't have any texture duplicates in model's folder or subfolders
		return sets.FirstOrDefault( x => string.Equals( x.BaseName, name, StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// Get the material for a texture set, creating it next to the textures if it doesn't exist yet
	/// </summary>
	internal static string GetOrCreateMaterial( TextureSet set, string shaderPath, Dictionary<string, string> suffixMap )
	{
		if ( set is null || suffixMap is null )
			return null;

		var targetPath = Path.Combine( set.Directory, $"{set.BaseName}.vmat" );

		// make sure that we never generate a material if one already exists
		if ( !File.Exists( targetPath ) )
		{
			var contents = BuildMaterialText( set, shaderPath, suffixMap );
			if ( contents is null )
				return null;

			try
			{
				File.WriteAllText( targetPath, contents );
			}
			catch ( Exception e )
			{
				Log.Warning( $"MaterialGenerator: couldn't write '{targetPath}' - {e.Message}" );
				return null;
			}
		}

		var asset = AssetSystem.RegisterFile( targetPath );
		if ( asset is null )
		{
			Log.Warning( $"MaterialGenerator: couldn't register '{targetPath}'" );
			return null;
		}

		return asset.Path;
	}

	static string BuildMaterialText( TextureSet set, string shaderPath, Dictionary<string, string> suffixMap )
	{
		var features = new List<string>();
		var textures = new List<string>();

		// i really-really dislike this, but metalness texture is optional in complex & simple shaders, 
		// so if texture set has a valid metalness texture, make sure to force enable F_METALNESS_TEXTURE if user selects these shaders
		var shaderName = Path.GetFileNameWithoutExtension( shaderPath );
		var forceMetalnessFeature = shaderName.Equals( "complex", StringComparison.OrdinalIgnoreCase ) || shaderName.Equals( "simple", StringComparison.OrdinalIgnoreCase );

		foreach ( var (suffix, asset) in set.BySuffix.OrderBy( x => x.Key, StringComparer.OrdinalIgnoreCase ) )
		{
			if ( !suffixMap.TryGetValue( suffix, out var variable ) )
				continue;

			textures.Add( $"\t{variable} \"{asset.RelativePath}\"" );

			if ( forceMetalnessFeature && suffix.Equals( "metal", StringComparison.OrdinalIgnoreCase ) )
				features.Add( "\tF_METALNESS_TEXTURE 1" );
		}

		if ( textures.Count == 0 )
			return null;

		var builder = new StringBuilder();
		builder.AppendLine( "Layer0" );
		builder.AppendLine( "{" );
		builder.AppendLine( $"\tshader \"{shaderPath}\"" );
		builder.AppendLine();

		if ( features.Count > 0 )
		{
			foreach ( var feature in features )
				builder.AppendLine( feature );

			builder.AppendLine();
		}

		foreach ( var texture in textures )
			builder.AppendLine( texture );

		builder.AppendLine( "}" );

		return builder.ToString();
	}

	static bool TrySplitSuffix( string stem, string[] orderedSuffixes, out string baseName, out string suffix )
	{
		foreach ( var candidate in orderedSuffixes )
		{
			// needs at least one character of base name before the underscore
			if ( stem.Length <= candidate.Length + 1 )
				continue;

			if ( stem[stem.Length - candidate.Length - 1] != '_' )
				continue;

			if ( !stem.EndsWith( candidate, StringComparison.OrdinalIgnoreCase ) )
				continue;

			baseName = stem.Substring( 0, stem.Length - candidate.Length - 1 );
			suffix = candidate;
			return true;
		}

		baseName = null;
		suffix = null;
		return false;
	}

	static bool IsImage( Asset asset )
	{
		if ( AssetType.ImageFile is null )
			return false;

		return asset?.AbsolutePath is not null && asset.AssetType == AssetType.ImageFile;
	}

}
