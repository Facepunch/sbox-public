using System;

namespace Packages;

[TestClass]
public class PackageDownload
{
	[TestMethod]
	[DataRow( "facepunch.sandbox" )]
	[DataRow( "garry.grassworld" )]
	public async Task SingleDownload( string packageIdent )
	{
		// Find the package
		var package = await Package.FetchAsync( packageIdent, false );
		Assert.IsNotNull( package );

		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/package_download/{packageIdent}";

		if ( System.IO.Directory.Exists( dir ) )
			System.IO.Directory.Delete( dir, true );

		System.IO.Directory.CreateDirectory( dir );

		AssetDownloadCache.Initialize( dir );

		var filesystem = await package.Download();

		// We should have downloaded a bunch of stuff so this folder shouldn't be empty
		{
			var files = System.IO.Directory.EnumerateFiles( dir, "*", System.IO.SearchOption.AllDirectories ).ToArray();
			Assert.AreNotEqual( 0, files.Length );
		}

		// The returned filesystem should have files that we can do stuff with now
		{
			var files = filesystem.FindFile( "", "*", true ).ToArray();
			Assert.AreNotEqual( 0, files.Length );

			foreach ( var file in files )
			{
				Console.WriteLine( $"{file}" );

				var s = filesystem.ReadAllBytes( file ).ToArray();
			}
		}

		System.IO.Directory.Delete( dir, true );
	}

	[TestMethod]
	[DataRow( "facepunch.sandbox" )]
	[DataRow( "garry.grassworld" )]
	public async Task MultipleDownload( string packageIdent )
	{
		// Find the package
		var package = await Package.FetchAsync( packageIdent, false );
		Assert.IsNotNull( package );

		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/package_download";

		if ( System.IO.Directory.Exists( dir ) )
			System.IO.Directory.Delete( dir, true );

		System.IO.Directory.CreateDirectory( dir );

		AssetDownloadCache.Initialize( dir );

		_ = await package.Download();
		var filesystem = await package.Download();

		// We should have downloaded a bunch of stuff so this folder shouldn't be empty
		{
			var files = System.IO.Directory.EnumerateFiles( dir, "*", System.IO.SearchOption.AllDirectories ).ToArray();
			Assert.AreNotEqual( 0, files.Length );
		}

		// The returned filesystem should have files that we can do stuff with now
		{
			var files = filesystem.FindFile( "", "*", true ).ToArray();
			Assert.AreNotEqual( 0, files.Length );

			foreach ( var file in files )
			{
				Console.WriteLine( $"{file}" );
			}
		}

		System.IO.Directory.Delete( dir, true );
	}

	[TestMethod]
	[DataRow( "testingsomething.sandbox_escape__playground#82331" )]
	public async Task NoDlls( string packageIdent )
	{
		// Find the package
		var package = await Package.FetchAsync( packageIdent, false );
		Assert.IsNotNull( package );

		var dir = $"{Environment.CurrentDirectory}/.source2/test_download_cache/package_dll";

		if ( System.IO.Directory.Exists( dir ) )
			System.IO.Directory.Delete( dir, true );

		System.IO.Directory.CreateDirectory( dir );

		AssetDownloadCache.Initialize( dir );

		var filesystem = await package.Download();

		Assert.IsNotNull( filesystem );

		// We should have downloaded a bunch of stuff so this folder shouldn't be empty
		{
			var files = System.IO.Directory.EnumerateFiles( dir, "*", System.IO.SearchOption.AllDirectories ).ToArray();
			Assert.AreNotEqual( 0, files.Length );
		}

		// The returned filesystem should have files that we can do stuff with now
		{
			var files = filesystem.FindFile( "", "*", true ).ToArray();
			Assert.AreNotEqual( 0, files.Length );

			foreach ( var file in files )
			{
				Console.WriteLine( $"{file}" );

				Assert.IsFalse( file.EndsWith( ".dll" ) );

				var s = filesystem.ReadAllBytes( file ).ToArray();
			}
		}

		System.IO.Directory.Delete( dir, true );
	}

}
