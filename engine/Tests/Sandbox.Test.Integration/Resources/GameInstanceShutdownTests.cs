using Sandbox.Engine;
using System;
using System.Reflection;

namespace ResourceTests;

[TestClass]
[DoNotParallelize]
public class GameInstanceShutdownTests
{
	private const string MarkerPath = "__game_instance_shutdown_marker.txt";
	private const string ResourcePath = "__game_instance_shutdown_probe.resource";

	private sealed class ShutdownProbeResource : GameResource
	{
		public bool WasDestroyed { get; private set; }
		public bool MountWasAvailableOnDestroy { get; private set; }

		protected override void OnDestroy()
		{
			WasDestroyed = true;
			MountWasAvailableOnDestroy = FileSystem.Mounted.FileExists( MarkerPath );
		}
	}

	private sealed class ShutdownGameInstance : GameInstance
	{
		public ShutdownGameInstance( PackageManager.ActivePackage package )
			: base( "__shutdown_resource_test", default )
		{
			activePackage = package;
		}
	}

	[TestMethod]
	public void ShutdownClearsResourcesBeforeUnmountingActivePackage()
	{
		var previousIsPlaying = Game.IsPlaying;
		var previousIsClosing = Game.IsClosing;
		var originalPackageTags = PackageManager.ActivePackages
			.ToDictionary( package => package, package => package.Tags.ToArray() );
		var holdTag = $"__shutdown_resource_test_{Guid.NewGuid():N}";

		var packageFiles = new MemoryFileSystem();
		var mountedFiles = new AggregateFileSystem();
		var testContext = new GlobalContext();

		try
		{
			// Shutdown removes the process-wide game/gamemenu tags. Keep any pre-existing
			// packages alive for this isolated test, then restore their exact tag sets.
			foreach ( var package in originalPackageTags.Keys )
			{
				package.Tags.Add( holdTag );
			}

			using ( new GlobalContext.GlobalContextScope( testContext ) )
			{
				try
				{
					testContext.FileMount = mountedFiles;
					testContext.UISystem = new UISystem();

					packageFiles.WriteAllText( MarkerPath, "mounted" );
					mountedFiles.Mount( packageFiles );

					var package = new PackageManager.ActivePackage();
					var fileSystemProperty = typeof( PackageManager.ActivePackage ).GetProperty(
						nameof( PackageManager.ActivePackage.FileSystem ),
						BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )
						?? throw new MissingMemberException( nameof( PackageManager.ActivePackage.FileSystem ) );
					fileSystemProperty.SetValue( package, packageFiles );

					var probe = new ShutdownProbeResource();
					probe.Register( ResourcePath );

					Assert.IsTrue( FileSystem.Mounted.FileExists( MarkerPath ) );
					Assert.AreSame( probe, ResourceLibrary.Get<ShutdownProbeResource>( ResourcePath ) );

					new ShutdownGameInstance( package ).Shutdown();

					Assert.IsTrue( probe.WasDestroyed,
						"GameInstance.Shutdown must clear game resources." );
					Assert.IsTrue( probe.MountWasAvailableOnDestroy,
						"Resources must be destroyed while active-package files are still mounted." );
					Assert.IsFalse( probe.IsValid );
					Assert.IsNull( ResourceLibrary.Get<ShutdownProbeResource>( ResourcePath ) );
					Assert.IsFalse( FileSystem.Mounted.FileExists( MarkerPath ),
						"Shutdown must still unmount the active-package filesystem." );
				}
				finally
				{
					testContext.Shutdown();
				}
			}
		}
		finally
		{
			Game.IsPlaying = previousIsPlaying;
			Game.IsClosing = previousIsClosing;

			foreach ( var (package, tags) in originalPackageTags )
			{
				package.Tags.Clear();
				package.Tags.UnionWith( tags );
			}

			if ( mountedFiles.IsValid )
			{
				mountedFiles.UnMount( packageFiles );
				mountedFiles.Dispose();
			}

			if ( packageFiles.IsValid )
			{
				packageFiles.Dispose();
			}
		}
	}
}
