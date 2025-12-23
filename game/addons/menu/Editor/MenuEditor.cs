using System;
using System.IO;

public static class MenuEditorSystem
{
	[Menu( "Editor", "Menu Project/Copy Generated Assets" )]
	public static void CopyGeneratedAssets()
	{
		var targetFolder = Project.Current.RootDirectory + "/Transients";

		System.IO.Directory.CreateDirectory( targetFolder );

		Directory.GetFiles( targetFolder ).ToList().ForEach( File.Delete );
		Directory.GetDirectories( targetFolder ).ToList().ForEach( d => Directory.Delete( d, true ) );

		foreach ( var asset in AssetSystem.All )
		{
			if ( !ShouldCopy( asset ) ) continue;

			CopyAsset( asset, targetFolder );
		}

		System.IO.File.WriteAllText( targetFolder + "/.gitignore", "!*_c\n" );
	}

	[Menu( "Editor", "Menu Project/Rebuild All Clothing Icons" )]
	public static void RebuildClothingIcons()
	{
		_ = Editor.ClothingIconControlWidget.SaveAllIcons();
	}

	/// <summary>
	/// Copy an asset relative to the targetRoot. Will also copy dependant assets, if they are not in the project folder.
	/// </summary>
	static void CopyAsset( Asset asset, string targetRoot )
	{
		if ( IsInProjectFolder( asset ) ) return;

		// Make sure we only copy compiled stuff, Asset.RelativePath will use a source path if one exists!
		var assetPath = asset.GetCompiledFile();
		if ( assetPath == null ) return;

		var dir = targetRoot + "/" + System.IO.Path.GetDirectoryName( assetPath );
		System.IO.Directory.CreateDirectory( dir );

		var target = targetRoot + "/" + assetPath;

		if ( System.IO.File.Exists( target ) )
			return;

		System.IO.File.Copy( asset.GetCompiledFile( true ), target );

		foreach ( var dependant in asset.GetReferences( false ) )
		{
			CopyAsset( dependant, targetRoot );
		}
	}

	/// <summary>
	/// We want to copy transient and cloud files that have project assets that depend on them.
	/// </summary>
	private static bool ShouldCopy( Asset asset )
	{
		// nothing relies on this, don't bother
		if ( asset.GetDependants( false ).Where( IsInProjectFolder ).Count() == 0 ) return false;
		if ( !asset.IsCompiled ) return false;

		// is a transient asset
		if ( asset.IsTransient )
		{
			return true;
		}

		// is a cloud asset
		if ( asset.IsCloud )
		{
			return true;
		}

		return false;
	}

	/// <summary>
	/// Is this asset in the project folder (not the cloud, or transient folder)
	/// </summary>
	static bool IsInProjectFolder( Asset asset )
	{
		if ( asset.IsCloud ) return false;
		if ( asset.IsProcedural ) return false;
		if ( asset.IsTransient ) return false;

		return true;
	}
}
