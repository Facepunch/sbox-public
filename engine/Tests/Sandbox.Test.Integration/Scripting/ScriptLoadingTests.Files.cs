using Sandbox.Engine;
using System;
using System.Collections.Generic;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Repeated scans share one watcher per resource type and dispatch in their owning context.
	/// Environment reset clears watchers before resetting the context and reloading resources.
	/// </summary>
	[TestMethod]
	public void ResourceWatcherUsesOwningContextAndRearmsAfterEnvironmentReset()
	{
		const string path = "watched.csx";
		files.WriteAllText( path, "[Global] int WatchStarts = 0; WatchStarts++; class WatchedComponent : Component { }" );
		ResourceLoader.LoadAllGameResource( files );
		ResourceLoader.LoadAllGameResource( files );
		Assert.AreEqual( 2, Game.Scripting.Globals.Get<int>( "WatchStarts" ) );

		files.AddChangedFile( path );
		FileWatch.TimeSinceLastChange = 1;
		using ( new GlobalContext.GlobalContextScope( previous ) )
		{
			FileWatch.Tick();
			Assert.AreSame( previous, GlobalContext.Current );
		}

		Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
		Assert.AreEqual( 3, Game.Scripting.Globals.Get<int>( "WatchStarts" ) );

		ResourceLoader.Clear();
		context.Reset();
		files.AddChangedFile( path );
		FileWatch.TimeSinceLastChange = 1;
		FileWatch.Tick();

		Assert.IsNull( Game.TypeLibrary.GetType( "WatchedComponent" ) );
		Assert.AreEqual( 0, Game.Scripting.Globals.Get( "WatchStarts", 0 ) );

		ResourceLoader.LoadAllGameResource( files );
		files.AddChangedFile( path );
		FileWatch.TimeSinceLastChange = 1;
		FileWatch.Tick();
		Assert.AreEqual( 2, Game.Scripting.Globals.Get<int>( "WatchStarts" ) );
	}
	/// <summary>
	/// The resource watcher executes refreshed script source when a mounted file changes on disk.
	/// </summary>
	[TestMethod]
	public void MountedFileWatcherReloadsScriptSource()
	{
		var path = $"script_reload_{Guid.NewGuid():N}.csx";
		var absolutePath = System.IO.Path.Combine( Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" ), "core", path );
		var mounted = new LocalFileSystem( System.IO.Path.GetDirectoryName( absolutePath ) );
		context.FileMount = mounted;
		try
		{
			System.IO.File.WriteAllText( absolutePath, "[Global] int WatchValue = 0; WatchValue = 1;" );
			Sandbox.Tasks.AsyncPump.RunSynchronously( () => ResourceLoader.LoadAllGameResourceAsync( mounted,
				preEnumeratedFiles: System.Threading.Tasks.Task.FromResult( new List<string> { path } ) ) );
			var scriptResource = Resource.Load( typeof( ScriptResource ), path ) as ScriptResource;
			Assert.IsNotNull( scriptResource );
			var resource = Game.Scripting;
			Assert.IsNull( resource.LastError, resource.LastError?.ToString() );
			Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "WatchValue" ) );

			System.IO.File.WriteAllText( absolutePath, "[Global] int WatchValue = 0; WatchValue = 2;" );
			var timer = System.Diagnostics.Stopwatch.StartNew();
			while ( Game.Scripting.Globals.Get<int>( "WatchValue" ) != 2 && timer.Elapsed.TotalSeconds < 5 )
			{
				FileWatch.TimeSinceLastChange = 1;
				// Frame ticks can run in a different context from the mount whose file changed.
				using ( new GlobalContext.GlobalContextScope( previous ) )
				{
					FileWatch.Tick();
				}
				System.Threading.Thread.Sleep( 10 );
			}
			Assert.IsNull( resource.LastError, resource.LastError?.ToString() );
			Assert.AreEqual( 2, Game.Scripting.Globals.Get<int>( "WatchValue" ) );
			Assert.AreSame( scriptResource, ResourceLibrary.Get<ScriptResource>( path ) );
			Assert.AreEqual( mounted.ReadAllText( path ), scriptResource.Source );
		}
		finally
		{
			context.FileMount = files;
			mounted.Dispose();
			System.IO.File.Delete( absolutePath );
			System.IO.File.Delete( absolutePath + ".meta" );
		}
	}

}
