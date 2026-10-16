using Sandbox;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Facepunch.CreateGameCache;

public static class Program
{
	const string CacheFolder = "gamecache";

	static DirectoryInfo CacheDirectory;

	static List<Task> tasks = new();

	public static async Task<int> Main( string[] args )
	{
		if ( args.Length != 1 || !Directory.Exists( args[0] ) )
		{
			Console.Error.WriteLine( "Usage: CreateGameCache <game-directory> (must be an existing directory)" );
			return 1;
		}

		var cachePath = Path.Combine( Path.GetFullPath( args[0] ), CacheFolder );
		CacheDirectory = new DirectoryInfo( cachePath );

		Console.WriteLine( $"Game cache directory: {CacheDirectory.FullName}" );
		CacheDirectory.Create();

		Sandbox.Api.Init();

		await FindAndInstallPackage( "type:model sort:popular org:facepunch", 200 );
		await FindAndInstallPackage( "type:model sort:spawns org:facepunch", 200 );

		await InstallPackage( "facepunch.sandbox" );
		await InstallPackage( "facepunch.construct" );
		await InstallPackage( "facepunch.flatgrass" );

		// Onboarding games from game/addons/menu/Code/MenuUI/Front/StarterShelf.razor.
		await InstallPackage( "jco.drill" );
		await InstallPackage( "glag.ex_zone" );
		await InstallPackage( "priceless.deliveryhopper" );
		await InstallPackage( "facepunch.blockparty" );

		await InstallPackage( "taxi.mow_the_lawn" );
		await InstallPackage( "kivin.goblingeddon" );

		await Task.WhenAll( tasks );
		return 0;
	}

	static async Task FindAndInstallPackage( string query, int max )
	{
		Console.WriteLine( $"{query}" );
		var result = await Package.FindAsync( query, max );
		if ( result.Packages is null || result.Packages.Length == 0 )
			throw new InvalidOperationException( $"No packages returned for game cache query: {query}" );

		foreach ( var package in result.Packages )
		{
			await InstallPackage( package.FullIdent );
		}
	}

	static async Task InstallPackage( string packageName )
	{
		Console.WriteLine( $"{packageName}" );

		var package = await Sandbox.Package.Fetch( packageName, false );
		if ( package?.Revision is null )
			throw new InvalidOperationException( $"No revision found for game cache package: {packageName}" );

		await package.Revision.DownloadManifestAsync();
		if ( package.Revision.Manifest?.Files is null )
			throw new InvalidOperationException( $"No manifest found for game cache package: {packageName}" );

		foreach ( var file in package.Revision.Manifest.Files )
		{
			tasks.Add( DownloadFile( file ) );
		}
	}

	// Only allow 16 downloads at a time
	static SemaphoreSlim throttler = new SemaphoreSlim( 16 );

	static async Task DownloadFile( Sandbox.ManifestSchema.File file )
	{
		await throttler.WaitAsync();

		try
		{
			string filename = AssetDownloadCache.CreateGameCacheFilename( file.Path, file.Crc );
			var path = Path.Combine( CacheDirectory.FullName, filename );

			if ( System.IO.File.Exists( path ) )
			{
				var info = new FileInfo( path );
				if ( info.Length == file.Size ) return;
			}

			Console.WriteLine( $"{file.Path}" );
			if ( !await Sandbox.Utility.Web.DownloadFile( file.Url, path, default, default ) )
				throw new IOException( $"Failed to download game cache file: {file.Path}" );

			if ( new FileInfo( path ).Length != file.Size )
				throw new IOException( $"Incorrect size for game cache file: {file.Path}" );
		}
		finally
		{
			throttler.Release();
		}
	}
}
