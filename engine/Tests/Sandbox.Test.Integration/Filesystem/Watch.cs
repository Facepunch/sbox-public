using System;

namespace FilesystemTests;

public partial class FileSystemTest
{
	/// <summary>
	/// Polls <see cref="Sandbox.FileWatch.Tick"/> for up to <paramref name="timeoutSeconds"/> seconds
	/// until <paramref name="changeDetected"/> returns true. FileWatch debounces changes for a short
	/// period after the last filesystem event, so a single Tick straight after a file operation is
	/// not guaranteed to deliver callbacks.
	/// </summary>
	private static async Task<bool> TickUntilChangeDetected( Func<bool> changeDetected, float timeoutSeconds = 5f )
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();

		while ( sw.Elapsed.TotalSeconds < timeoutSeconds )
		{
			Sandbox.FileWatch.Tick();

			if ( changeDetected() )
				return true;

			await Task.Delay( 50 );
		}

		return false;
	}

	[TestMethod]
	public async Task WatchPhysical()
	{
		Sandbox.EngineFileSystem.Root.CreateDirectory( "/WatchFolder" );

		using ( var watcher = Sandbox.EngineFileSystem.Root.Watch( "/WatchFolder/*" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w => detectedChanges = true;

			await Task.Delay( 100 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.EngineFileSystem.Root.WriteAllText( "/WatchFolder/hello.txt", "test" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}
	}

	[TestMethod]
	public async Task WatchMemory()
	{
		Sandbox.EngineFileSystem.Temporary.CreateDirectory( "/WatchFolder" );

		using ( var watcher = Sandbox.EngineFileSystem.Temporary.Watch( "/WatchFolder/*" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w => detectedChanges = true;

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/hello.txt", "test" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}

		Sandbox.EngineFileSystem.Temporary.DeleteDirectory( "/WatchFolder", true );
	}


	[TestMethod]
	public async Task WatchSingleFile()
	{
		Sandbox.EngineFileSystem.Temporary.CreateDirectory( "/WatchFolder" );

		using ( var watch = Sandbox.EngineFileSystem.Temporary.Watch( "/WatchFolder/hello.txt" ) )
		{
			bool detectedChanges = false;
			watch.OnChanges += w => detectedChanges = true;

			Sandbox.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/not watching.txt", "test" );
			Sandbox.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/not watching.txt", "test" );

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.EngineFileSystem.Temporary.WriteAllText( "/WatchFolder/hello.txt", "test" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}

		Sandbox.EngineFileSystem.Temporary.DeleteDirectory( "/WatchFolder", true );
	}

	[TestMethod]
	public async Task WatchMounted()
	{
		var fs = new Sandbox.AggregateFileSystem();
		fs.UnMountAll();

		var blue = fs.CreateAndMount( Sandbox.EngineFileSystem.Root, "Addons/Blue" );
		var red = fs.CreateAndMount( Sandbox.EngineFileSystem.Root, "Addons/Red" );
		var green = fs.CreateAndMount( Sandbox.EngineFileSystem.Root, "Addons/Green" );

		Sandbox.EngineFileSystem.Root.CreateDirectory( "Addons/Blue/UI" );
		System.IO.File.WriteAllText( Sandbox.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "efwfwefweweffwe" );

		using ( var watcher = fs.Watch( "/UI/hello.txt" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w =>
			{
				Console.WriteLine( $"{string.Join( ",", w.Changes )} has changes" );
				detectedChanges = true;
			};

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			System.IO.File.WriteAllText( Sandbox.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "ergergregerg" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}
	}

	[TestMethod]
	public async Task WatchMountedCaseInsensitivePath()
	{
		var fs = new Sandbox.AggregateFileSystem();
		fs.UnMountAll();

		// Wrong-case mount paths: SubFileSystem(isCaseSensitive:false) must strip the base
		// prefix case-insensitively when routing physical change events back to virtual paths,
		// otherwise the watcher silently drops events on Linux.
		fs.CreateAndMount( Sandbox.EngineFileSystem.Root, "addons/blue" );

		Sandbox.EngineFileSystem.Root.CreateDirectory( "Addons/Blue/UI" );
		System.IO.File.WriteAllText( Sandbox.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "initial" );

		using ( var watcher = fs.Watch( "/UI/hello.txt" ) )
		{
			bool detectedChanges = false;

			watcher.OnChanges += w =>
			{
				Console.WriteLine( $"{string.Join( ",", w.Changes )} has changes" );
				detectedChanges = true;
			};

			await Task.Delay( 200 );

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			Sandbox.FileWatch.Tick();

			Assert.IsFalse( detectedChanges, "No changes made yet - should be false" );

			System.IO.File.WriteAllText( Sandbox.EngineFileSystem.Root.GetFullPath( "Addons/Blue/UI/hello.txt" ), "changed" );

			Assert.IsFalse( detectedChanges, "Changes must not be delivered before a Tick" );

			Assert.IsTrue( await TickUntilChangeDetected( () => detectedChanges ), "Callbacks should have been triggered now" );
		}
	}
}
