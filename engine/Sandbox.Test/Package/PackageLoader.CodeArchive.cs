namespace Packages;

partial class PackageLoader
{
	/// <summary>
	/// We should be able to download a package with cll files and compile them successfully
	/// </summary>
	[TestMethod]
	[DataRow( "facepunch.testbed" )]
	[DataRow( "softsplit.donut" )]
	[DataRow( "jeraldsjunk.carexplorer" )]
	//[DataRow( "facepunch.hc1" )]
	[DataRow( "carsonk.instagib" )]
	[DataRow( "facepunch.pool" )]
	[DataRow( "baks.func" )]
	[DataRow( "facepunch.sandbox" )]
	[DataRow( "facepunch.mazing" )] // todo - add hundreds of these

	public async Task CompileCodeArchive( string packageName )
	{
		using var packageLoader = new Sandbox.PackageLoader( "Test", GetType().Assembly );
		var enroller = packageLoader.CreateEnroller( "test-enroller" );

		var packageInfo = await Package.FetchAsync( packageName, false );

		await packageInfo.Revision.DownloadManifestAsync();

		// get all the code archives
		var codeArchives = packageInfo.Revision.Manifest.Files.Where( x => x.Path.EndsWith( ".cll" ) ).ToArray();
		Assert.AreNotEqual( 0, codeArchives.Length, "We have package files mounted" );

		using var group = new CompileGroup( packageName );

		foreach ( var file in codeArchives )
		{
			System.Console.WriteLine( $"Downloading {file.Url}" );

			// Download it successfully
			var bytes = await Sandbox.Utility.Web.GrabFile( file.Url, default );
			Assert.AreNotEqual( null, bytes );

			System.Console.WriteLine( $" .. finished {bytes.Length}b (expecting {file.Size}b)" );
			Assert.AreEqual( file.Size, bytes.Length );

			// Deserialize to a code archive
			var archive = new CodeArchive( bytes );

			// Create a compiler for it
			var compiler = group.GetOrCreateCompiler( archive.CompilerName );
			compiler.UpdateFromArchive( archive );
		}

		// Compile that bad boy
		await group.BuildAsync();

		System.Console.WriteLine( $"Build result: {group.BuildResult.Success}" );
		System.Console.WriteLine( $"{group.BuildResult.BuildDiagnosticsString()}" );

		// Should be successful
		Assert.IsTrue( group.BuildResult.Success );
	}
}
