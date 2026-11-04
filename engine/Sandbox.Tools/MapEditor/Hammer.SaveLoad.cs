using Editor.MapDoc;
using System;

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

		var usedPackages = GetUsedPackages( map );

		//
		// References that we need to download to edit this map
		// These will be re-downloaded when opening the editor
		// if it doesn't already have them downloaded.
		//
		{
			mapAsset.Publishing.ProjectConfig.EditorReferences = new();

			//
			// Collect all packages our assets use and save them as metadata, so we can make sure we have them downloaded before opening
			//
			var packages = mapAsset.GetReferences( false ).Where( a => a.Package != null ).Select( a => $"{a.Package.FullIdent}#{a.Package.Revision.VersionId}" ).Distinct();
			mapAsset.Publishing.ProjectConfig.EditorReferences.AddRange( packages.ToList() );

			//
			// Add any games we're referencing. We'll want to re-reference them when opening the map
			//
			mapAsset.Publishing.ProjectConfig.EditorReferences.AddRange( usedPackages.Where( x => x.TypeName == "game" ).Select( x => x.FullIdent ) );

			//
			// Lowercase and remove duplicates
			//
			mapAsset.Publishing.ProjectConfig.EditorReferences = mapAsset.Publishing.ProjectConfig.EditorReferences.Select( x => x.ToLowerInvariant() ).Distinct().OrderBy( x => x ).ToList();
		}

		//
		// References to addons that this map needs to run. We try to work this out by looking at the entities
		// used and then looking at the package they're from, and if the package is an addon we use it. We assume
		// that if they're referencing a specific game then the map will always be run with that game.
		//
		UpdatePackageReferences( mapAsset.Publishing, usedPackages.Where( x => x.TypeName == "addon" ).Select( x => x.FullIdent ) );


		// Save the updated publish config
		mapAsset.MetaData.Set( "publish", mapAsset.Publishing );
	}

	internal static void UpdatePackageReferences( Asset.PublishSettings settings, IEnumerable<string> addonReferences )
	{
		var config = settings.ProjectConfig;

		// Only remove references we previously added. Older maps have no provenance, so keep their
		// existing references rather than guessing which ones the user configured explicitly.
		var manualReferences = (config.PackageReferences ?? [])
			.Except( settings.HammerAutoPackageReferences ?? [], StringComparer.OrdinalIgnoreCase )
			.Select( x => x.ToLowerInvariant() ).ToArray();

		// Don't claim an existing manual reference just because an entity also needs that addon.
		var automaticReferences = addonReferences.Select( x => x.ToLowerInvariant() )
			.Except( manualReferences ).OrderBy( x => x ).ToArray();

		config.PackageReferences = manualReferences.Concat( automaticReferences ).OrderBy( x => x ).ToList();
		settings.HammerAutoPackageReferences = automaticReferences;
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
