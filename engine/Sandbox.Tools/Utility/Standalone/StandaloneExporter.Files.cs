using System;
using System.IO;

namespace Editor;

partial class StandaloneExporter
{
	private static string[] DllBlacklist = [
		// DLLs that *aren't* needed for the game to boot
		"assetsystem.dll",
		"Qt5Concurrent.dll",
		"Qt5Core.dll",
		"Qt5Gui.dll",
		"Qt5Widgets.dll",
		"rendersystemdx11.dll",
		"steamdatagram_gamecoordinator.dll",
		"toolframework2.dll",
		"qtadvanceddocking.dll",
		"toolscenenodes.dll",
		"steamclient64.dll",
		"propertyeditor.dll",
		"OpenImageDenoise.dll",
		"bakedlodbuilder.dll"
	];

	private static IEnumerable<string> GetDllFiles( string engineDir )
	{
		var nativeDlls = GetBlacklistedFiles( DllBlacklist, Path.Combine( engineDir, "bin", "win64" ) );
		var managedDlls = GetBlacklistedFiles( [], Path.Combine( engineDir, "bin", "managed" ) );

		var files = nativeDlls.Concat( managedDlls );

		return files.Where( x => x.EndsWith( ".dll" ) );
	}

	/// <summary>
	/// Core files that aren't compiled resources but are read at runtime.
	/// </summary>
	private static string[] CoreWhitelist = [
		"debug/*",

		// Loaded as an image by WaterOverlay
		"textures/water/splashwake1.png",

		// Interface
		"fonts/*.ttf",
		"styles/**/*",
		"ui/**/*",

		// Config files
		"cfg/*",
	];

	/// <summary>
	/// Core folders only the editor uses.
	/// </summary>
	private static readonly string[] CoreEditorFolders = ["tools", "templates", "maps"];

	/// <summary>
	/// Every compiled core resource, apart from the editor's, and the loose files the engine reads. The engine loads plenty
	/// of core resources by name - error assets, lighting lookups, physics surfaces, default effects and sounds - so
	/// nothing references them as assets.
	/// </summary>
	private IEnumerable<string> GetCoreFiles( string engineDir )
	{
		var core = Path.Combine( engineDir, "core" );

		var compiled = Directory.EnumerateFiles( core, "*_c", SearchOption.AllDirectories )
			.Where( x => !CoreEditorFolders.Contains( Path.GetRelativePath( core, x ).Split( Path.DirectorySeparatorChar )[0], StringComparer.OrdinalIgnoreCase ) );

		return compiled.Concat( GetWhitelistedFiles( CoreWhitelist, core ) ).Distinct( StringComparer.OrdinalIgnoreCase ).ToList();
	}

	/// <summary>
	/// Resources outside core that engine code loads by name, so nothing references them as assets, and what in the
	/// export means the game needs them.
	/// </summary>
	private static readonly (string UsedBy, string[] Resources)[] ImpliedResources =
	[
		// Dressing a human adds default underwear
		("models/citizen_human/human.vmdl", [Dresser.DefaultUnderwearPath, Dresser.DefaultBraPath]),
	];

	/// <summary>
	/// The resources outside core that engine code loads by name for what the export has: what core surfaces reference
	/// (impact sounds, decals, prefabs), and <see cref="ImpliedResources"/>. Core's own are shipped already.
	/// </summary>
	private IEnumerable<CodeResource> GetImpliedResources( string engineDir, IEnumerable<string> coreFiles )
	{
		var core = Path.GetFullPath( Path.Combine( engineDir, "core" ) ) + Path.DirectorySeparatorChar;
		var exported = PackageManifest.Assets.Select( x => x.Name.Replace( '\\', '/' ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );

		var assets = coreFiles.Where( x => x.EndsWith( ".surface_c", StringComparison.OrdinalIgnoreCase ) )
			.Concat( ImpliedResources.Where( x => exported.Contains( x.UsedBy + "_c" ) ).SelectMany( x => x.Resources ) );

		foreach ( var path in assets )
		{
			var asset = AssetSystem.FindByPath( path );
			if ( asset is null )
			{
				Logger.Warning( $"Couldn't find {path}, it and its references won't be exported" );
				continue;
			}

			foreach ( var reference in asset.GetReferences( true ).Prepend( asset ) )
			{
				var relativePath = reference.GetCompiledFile( false )?.TrimStart( '/', '\\' );
				var absolutePath = reference.GetCompiledFile( true );

				if ( string.IsNullOrEmpty( relativePath ) || string.IsNullOrEmpty( absolutePath ) )
					continue;

				if ( Path.GetFullPath( absolutePath ).StartsWith( core, StringComparison.OrdinalIgnoreCase ) )
					continue;

				yield return new CodeResource( relativePath, absolutePath );
			}
		}
	}

	private static IEnumerable<string> GetBlacklistedFiles( string[] blacklist, string absoluteDirectory )
	{
		var allFiles = new List<string>();

		foreach ( var file in Directory.GetFiles( absoluteDirectory ) )
		{
			var fileName = Path.GetFileName( file );

			if ( blacklist != null )
				if ( blacklist.Contains( fileName ) )
					continue;

			allFiles.Add( file );
		}

		return allFiles;
	}

	private IEnumerable<string> GetWhitelistedFiles( string[] whitelist, string absoluteDirectory )
	{
		var allFiles = new List<string>();
		foreach ( var entry in whitelist )
		{
			var normalizedEntry = entry.Replace( '/', Path.DirectorySeparatorChar );

			if ( entry.Contains( "**" ) )
			{
				// Handle recursive directory patterns
				var parts = normalizedEntry.Split( new[] { "**" }, 2, StringSplitOptions.RemoveEmptyEntries );
				var baseDirectory = parts[0].TrimEnd( Path.DirectorySeparatorChar );
				var filePattern = parts[1].TrimStart( Path.DirectorySeparatorChar );

				var fullPath = Path.Combine( absoluteDirectory, baseDirectory );
				try
				{
					if ( Directory.Exists( fullPath ) )
					{
						allFiles.AddRange( Directory.GetFiles( fullPath, filePattern, SearchOption.AllDirectories ) );
					}
					else
					{
						Log.Warning( $"Directory not found: {fullPath}" );
					}
				}
				catch ( DirectoryNotFoundException )
				{
					Log.Warning( $"{entry} doesn't exist" );
				}
			}
			else
			{
				// Handle non-recursive patterns
				var directory = Path.GetDirectoryName( normalizedEntry ) ?? ".";
				var filePattern = Path.GetFileName( normalizedEntry );

				var fullPath = Path.Combine( absoluteDirectory, directory );
				try
				{
					if ( Directory.Exists( fullPath ) )
					{
						allFiles.AddRange( Directory.GetFiles( fullPath, filePattern, SearchOption.TopDirectoryOnly ) );
					}
					else
					{
						Logger.Warning( $"Directory not found: {fullPath}" );
					}
				}
				catch ( DirectoryNotFoundException )
				{
					Logger.Warning( $"{entry} doesn't exist" );
				}
			}
		}

		return allFiles;
	}
}
