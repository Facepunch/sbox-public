using Editor;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Standalone;

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
		config.TargetDir = Path.Combine( Path.GetTempPath(), "sboxexportest" );
		config.AppId = 480; // SpaceWar AppID

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
				await process.WaitForExitAsync();

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
