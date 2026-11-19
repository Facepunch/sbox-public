using System;
using System.IO;
using System.Reflection;
using Sandbox.MovieMaker;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class SensitiveConVarTests
{
	[TestMethod]
	[DataRow( typeof( ScreenshotService ), "screenshot_prefix" )]
	[DataRow( typeof( Sandbox.Diagnostics.Profiler ), "profiler" )]
	public void GameCodeCantReach( Type type, string name )
	{
		var attribute = type.GetMembers( BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic )
			.Select( x => x.GetCustomAttribute<ConVarAttribute>() )
			.FirstOrDefault( x => x?.Name == name );

		Assert.IsTrue( (attribute.Flags & ConVarFlags.Protected) != 0, name );
	}

	[TestMethod]
	public void ScreenshotFilenameStaysInFolder()
	{
		var previous = ScreenshotService.ScreenshotPrefix;
		ScreenshotService.ScreenshotPrefix = "../../../evil";
		var path = ScreenCaptureUtility.GenerateScreenshotFilename( "png" );
		ScreenshotService.ScreenshotPrefix = previous;

		Assert.AreEqual( "screenshots", Path.GetDirectoryName( path ) );
	}

	[TestMethod]
	[DataRow( "../../../evil" )]
	[DataRow( "C:\\Users\\Public\\evil" )]
	public void MovieSaveStaysInFolder( string fileName )
	{
		Assert.AreEqual( "movies", Path.GetDirectoryName( MovieConsoleCommands.GetMovieFilePath( fileName ) ) );
	}
}
