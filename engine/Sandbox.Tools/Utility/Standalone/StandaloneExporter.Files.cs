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

	private static string[] CoreWhitelist = [
		// Shaders
		"shaders/**/*.shader_c",
		
		// Dev textures, models (contains error assets etc.)
		"textures/dev/**/*.vtex_c",
		"models/dev/**/*.vmdl_c",
		"models/dev/**/*.vmat_c",
		"models/dev/**/*.vtex_c",
		"textures/**/*.vtex_c",
		"debug/*",

		// Default materials
		"materials/**/*.vmat_c",
		"materials/**/*.vtex_c",
		"dev/helper/**/*.vmat_c",
		"dev/helper/**/*.vtex_c",
		"dev/vgui/**/*.vmat_c",
		"dev/*.vtex_c",
		
		// Splash screen
		"materials/startup_background.vtex_c",

		// Surfaces - loaded at runtime without anything referencing them, physics and
		// ModelBuilder fall back to surfaces/default.surface. Their references are added too.
		"surfaces/*.surface_c",

		// Interface
		"fonts/*.ttf",
		"styles/**/*",
		"ui/**/*",

		// Config files
		"cfg/*",
	];

	private IEnumerable<string> GetCoreFiles( string engineDir )
	{
		return GetWhitelistedFiles( CoreWhitelist, Path.Combine( engineDir, "core" ) );
	}

	/// <summary>
	/// The compiled files the given surfaces reference (impact prefabs, footstep sounds, decal textures).
	/// Nothing in the game references these directly, so they'd never be picked up otherwise. Some live
	/// outside of core (generated textures are transients), so they're keyed by resource path.
	/// </summary>
	private static IEnumerable<CodeResource> GetSurfaceReferences( IEnumerable<string> files )
	{
		foreach ( var file in files )
		{
			if ( !file.EndsWith( ".surface_c", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var asset = AssetSystem.FindByPath( file );
			if ( asset is null )
			{
				Logger.Warning( $"Surface '{file}' isn't in the asset system, its references won't be exported" );
				continue;
			}

			foreach ( var reference in asset.GetReferences( true ) )
			{
				var absolutePath = reference.GetCompiledFile( true );
				var relativePath = reference.GetCompiledFile( false );

				if ( string.IsNullOrEmpty( absolutePath ) || string.IsNullOrEmpty( relativePath ) )
					continue;

				yield return new CodeResource( relativePath.TrimStart( '/', '\\' ), absolutePath );
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
