using System.IO;
using System.Text.RegularExpressions;

namespace Editor;

static class NativeAssetProcessor
{
	/// <summary>
	/// Determine what packages this material requires, and save the list to metadata so we know what to download in future
	/// </summary>
	internal static void OnAssetChanged( Asset asset )
	{
		if ( asset.AssetType != AssetType.Material && asset.AssetType != AssetType.Model )
			return;

		if ( !File.Exists( asset.AbsoluteSourcePath ) )
			return;

		if ( asset.IsCloud )
			return;

		// scan from source file contents directly
		// (not using asset dependency graph so it works even if compile failed)
		string contents = File.ReadAllText( asset.AbsoluteSourcePath );
		var strings = Regex.Matches( contents, @"""([^""\\]+\.[a-zA-Z0-9.]+)""" ); // stuff in quotes with .ext

		HashSet<string> packageReferences = new HashSet<string>();
		for ( int i = 0; i < strings.Count; i++ )
		{
			var path = strings[i].Groups[1].Value;
			var assetRef = AssetSystem.FindByPath( path );
			if ( assetRef is null ) continue;

			var package = assetRef.Package;
			if ( package is null && assetRef.IsTrivialChild )
			{
				// possibly a generated texture. work out if the input's from a package, because we'll need that
				foreach ( var inputPath in assetRef.GetInputDependencies() )
				{
					var input = AssetSystem.FindByPath( inputPath );
					if ( input is null ) continue;

					if ( input.Package is not null )
					{
						package = input.Package;
						break;
					}
				}
			}

			if ( package is null ) continue;
			if ( package == asset.Package ) continue;

			packageReferences.Add( package.GetVersionedIdent() );
		}

		if ( !packageReferences.Any() && asset.Publishing.ProjectConfig.EditorReferences is null )
			return; // no packages and no change, avoid writing empty metadata

		asset.Publishing.ProjectConfig.EditorReferences = packageReferences.ToList();
		asset.MetaData?.Set( "publish", asset.Publishing );
	}
}
