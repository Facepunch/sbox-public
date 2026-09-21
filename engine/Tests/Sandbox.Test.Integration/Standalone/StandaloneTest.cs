using Editor;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace StandaloneTests;

/// <summary>
/// Tests for the Standalone export pipeline
/// </summary>
[TestClass]
public class StandaloneTest
{
	[TestInitialize]
	public void TestInitialize()
	{
		Project.Clear();
	}

	[TestCleanup]
	public void TestCleanup()
	{
		Project.Clear();
	}

	[TestMethod]
	public async Task ExportAndRun()
	{
		var config = new ExportConfig();
		var tempFolderName = Guid.NewGuid().ToString();
		config.TargetDir = Path.Combine( Path.GetTempPath(), tempFolderName, "sboxexportest" );
		config.AppId = 480; // SpaceWar AppID
		config.TargetIcon = "core/tools/images/hammer/appicon.ico"; // the wizard always offers one, so exercise the icon path

		try
		{
			var project = Project.AddFromFile( "unittest/addons/spacewars/.sbproj" );
			config.Project = project;
			config.ExecutableName = project.Package.Ident;

			CleanOutput( config );

			//
			// Build the project
			//
			{
				var exporter = await StandaloneExporter.FromConfig( config );
				await exporter.Run();
			}

			//
			// The game is the standalone launcher's apphost with the game's identity in its PE
			// resources; the launcher assembly lives in bin/managed with the rest of the engine.
			// Nothing about the game's identity is a loose file in assets/.
			//
			{
				var executable = Path.Combine( config.TargetDir, $"{config.ExecutableName}.exe" );
				Assert.IsTrue( File.Exists( executable ), "Export is missing its executable" );

				foreach ( var file in new[] { "sbox-standalone.dll", "sbox-standalone.runtimeconfig.json" } )
				{
					Assert.IsTrue( File.Exists( Path.Combine( config.TargetDir, "bin", "managed", file ) ), $"Export is missing bin/managed/{file}" );
				}

				Assert.AreEqual( 1, Directory.GetFiles( config.TargetDir ).Length, $"Only the executable should sit in the export root, found: {string.Join( ", ", Directory.GetFiles( config.TargetDir ).Select( Path.GetFileName ) )}" );

				var assets = Path.Combine( config.TargetDir, Standalone.GamePath );
				Assert.IsFalse( File.Exists( Path.Combine( assets, ".sbproj" ) ), "Export shouldn't have a loose .sbproj" );
				Assert.IsFalse( File.Exists( Path.Combine( assets, "standalone.manifest.json" ) ), "Export shouldn't have a loose manifest" );

				// Only compiled assemblies ship - never the code archive (the game's source) or doc xml
				var binFiles = Directory.GetFiles( Path.Combine( assets, ".bin" ) ).Select( Path.GetFileName ).ToArray();
				Assert.IsTrue( binFiles.Length > 0, ".bin should contain the game's assemblies" );
				CollectionAssert.AreEquivalent( binFiles.Where( f => f.EndsWith( ".dll" ) ).ToArray(), binFiles, $".bin should only contain assemblies, has: {string.Join( ", ", binFiles )}" );

				// What Windows shows for the exe (Explorer, Defender, SmartScreen) is the game, not the template it was made from
				var versionInfo = FileVersionInfo.GetVersionInfo( executable );
				Assert.AreEqual( project.Config.Title, versionInfo.ProductName, "Executable's version info should name the game" );
				Assert.AreEqual( project.Config.Title, versionInfo.FileDescription, "Executable's version info should name the game" );
				Assert.IsFalse( (versionInfo.OriginalFilename ?? "").Contains( "sbox-standalone" ), "Executable still carries the template's version info" );

				// Resource names are UTF-16 in the PE resource directory, RCDATA contents are our UTF-8 JSON
				var image = File.ReadAllBytes( executable );
				foreach ( var resource in new[] { Standalone.ManifestResourceName, Standalone.ProjectConfigResourceName } )
				{
					Assert.IsTrue( image.AsSpan().IndexOf( System.Text.Encoding.Unicode.GetBytes( resource ) ) >= 0, $"Executable has no {resource} resource" );
				}
				Assert.IsTrue( image.AsSpan().IndexOf( System.Text.Encoding.UTF8.GetBytes( $"\"Ident\":\"{project.Config.Ident}\"" ) ) >= 0, "Executable's manifest doesn't name the game" );
			}

			//
			// Run the exported project, make sure it launches and exits cleanly
			//
			{
				using var process = new Process
				{
					StartInfo = new ProcessStartInfo
					{
						FileName = Path.Combine( config.TargetDir, $"{config.ExecutableName}.exe" ),
						WorkingDirectory = config.TargetDir,
						Arguments = "-headless -test-standalone",
						RedirectStandardError = true,
						UseShellExecute = false,
						CreateNoWindow = true
					}
				};

				// forward error output so we know why it's failed
				process.ErrorDataReceived += ( sender, e ) =>
				{
					if ( e.Data != null )
						Console.Error.WriteLine( e.Data );
				};

				bool success = process.Start();
				Assert.IsTrue( success, "Failed to start standalone exe" );

				process.BeginErrorReadLine();

				// A hung exe would otherwise stall the whole serial suite - kill it after a generous timeout
				using var timeout = new CancellationTokenSource( TimeSpan.FromMinutes( 5 ) );

				try
				{
					await process.WaitForExitAsync( timeout.Token );
				}
				catch ( OperationCanceledException )
				{
					process.Kill( entireProcessTree: true );
					Assert.Fail( "Standalone exe didn't exit within 5 minutes - killed the process" );
				}

				int exitCode = process.ExitCode;
				Assert.AreEqual( 0, exitCode, $"Process exited with code {exitCode}" );
			}
		}
		finally
		{
			CleanOutput( config );
		}
	}

	private void CleanOutput( ExportConfig config )
	{
		if ( config?.TargetDir is null || !Directory.Exists( config.TargetDir ) )
			return;

		try
		{
			Directory.Delete( config.TargetDir, true );
		}
		catch ( IOException )
		{
			Thread.Sleep( 500 );
			Directory.Delete( config.TargetDir, true );
		}
		catch ( UnauthorizedAccessException )
		{
			Thread.Sleep( 500 );
			Directory.Delete( config.TargetDir, true );
		}
	}

}
