using System;
using Editor.MapDoc;

namespace Editor.MapEditor;

public static partial class Hammer
{
	/// <summary>
	/// Called just before anything is saved to a map file.
	/// This is the ideal place to manipulate the map document just before save.
	/// </summary>
	internal static void PreSaveMap( MapDocument mapDoc )
	{
	}

	/// <summary>
	/// Called after the map has been successfully written to disk.
	/// </summary>
	internal static void MapAssetSaved( int assetIndex, MapDocument map )
	{
		var mapAsset = AssetSystem.Get( (uint)assetIndex );
		Assert.NotNull( mapAsset );

		var referencedAssets = mapAsset.GetReferences( false )
			.Where( a => a.Package != null )
			.ToArray();
		var referencedPackageVersions = referencedAssets
			.Where( a => a.Package.Revision != null )
			.Select( a => $"{a.Package.FullIdent}#{a.Package.Revision.VersionId}" );
		var referencedRuntimePackages = mapAsset.GetReferences( true )
			.Where( a => a.Package != null )
			.Where( a => !string.Equals( a.Package.TypeName, "game", StringComparison.OrdinalIgnoreCase ) )
			.Select( a => a.Package.Revision != null
				? $"{a.Package.FullIdent}#{a.Package.Revision.VersionId}"
				: a.Package.FullIdent );

		var usedPackages = GetUsedPackages( map );

		//
		// References that we need to download to edit this map
		// These will be re-downloaded when opening the editor
		// if it doesn't already have them downloaded.
		//
		{
			//
			// Collect all packages our assets use and save them as metadata, so we can make sure we have them downloaded before opening.
			// Keep any manual references the user already added and merge in the automatic ones.
			//
			var editorReferences = mapAsset.Publishing.ProjectConfig.EditorReferences ?? [];
			mapAsset.Publishing.ProjectConfig.EditorReferences = MergePackageReferences(
				editorReferences,
				referencedPackageVersions,
				usedPackages.Where( x => x.TypeName == "game" ).Select( x => x.FullIdent )
			).ToList();
		}

		//
		// References to addons that this map needs to run. We try to work this out by looking at the entities
		// used and then looking at the package they're from, and if the package is an addon we use it. We assume
		// that if they're referencing a specific game then the map will always be run with that game.
		//
		{
			var packageReferences = mapAsset.Publishing.ProjectConfig.PackageReferences ?? [];
			mapAsset.Publishing.ProjectConfig.PackageReferences = MergePackageReferences(
				packageReferences,
				referencedRuntimePackages,
				usedPackages.Where( x => x.TypeName == "addon" ).Select( x => x.FullIdent )
			).ToList();
		}


		// Save the updated publish config
		mapAsset.MetaData.Set( "publish", mapAsset.Publishing );
	}

	internal static string[] MergePackageReferences( params IEnumerable<string>[] referenceSets )
	{
		return CloudAsset.DeduplicateReferences( referenceSets.Where( x => x != null ).SelectMany( x => x ) );
	}

	/// <summary>
	/// Get a list of packages used in this map, based primarily on the entities used.
	/// </summary>
	static Package[] GetUsedPackages( MapDocument map )
	{
		var usedEntityClasses = map.World.Children.OfType<MapEntity>().Select( e => e.ClassName ).Distinct();

		List<Package> usedPackges = new();
		foreach ( var className in usedEntityClasses )
		{
			var managedClass = GameData.EntityClasses.Where( c => c.Name == className ).FirstOrDefault();
			if ( managedClass == null || managedClass.Package == null ) continue;

			usedPackges.Add( managedClass.Package );
		}

		return usedPackges.Distinct().ToArray();
	}

	/// <summary>
	/// Called after loading a map file from disk, after the MapDoc is created but before the world is loaded.
	/// This makes it the perfect place to resolve any dependent assets or game data, but not the place
	/// to interact with the world in any meaningful way.
	/// </summary>
	internal static void PostLoadMap( MapDocument mapDoc )
	{
		// tony: This used to house loading all references, but we do this on project startup now, so seemed useless
	}
}
