using Sandbox.Engine;
using System;
using System.IO;

namespace ScriptingTests;

/// <summary>
/// Scripts use the same scoped storage contracts as compiled game code.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptFileSystemTests
{
	GlobalContext previous;
	GlobalContext previousGame;
	MemoryFileSystem storage;
	MemoryFileSystem content;
	AggregateFileSystem mounted;
	BaseFileSystem organization;
	BaseFileSystem data;

	/// <summary>
	/// Creates separate game, organization and mounted-content scopes.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		storage = new MemoryFileSystem();
		storage.CreateDirectory( "organization/game" );
		storage.CreateDirectory( "organization/other-game" );
		organization = storage.CreateSubSystem( "organization" );
		data = organization.CreateSubSystem( "game" );
		content = new MemoryFileSystem();
		content.WriteAllText( "content.txt", "mounted content" );
		mounted = new AggregateFileSystem();
		mounted.Mount( content );
		GlobalContext.Current = GlobalContext.Game = new GlobalContext
		{
			TypeLibrary = SharedTypeLibrary.ScriptingWithSystem,
			FileData = data,
			FileOrg = organization,
			FileMount = mounted
		};
	}

	/// <summary>
	/// Releases the isolated storage and restores the original game context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		mounted.Dispose();
		content.Dispose();
		data.Dispose();
		organization.Dispose();
		storage.Dispose();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// The exact LL-001 existence probe can read the active game's data.
	/// </summary>
	[TestMethod]
	public void ScriptsCanQueryGameData()
	{
		Assert.IsFalse( Run<bool>( "return FileSystem.Data.FileExists(\"best.txt\");" ) );
		data.WriteAllText( "best.txt", "42" );
		Assert.IsTrue( Run<bool>( "return FileSystem.Data.FileExists(\"best.txt\");" ) );
	}

	/// <summary>
	/// Saved files survive reopening both the physical filesystem and the scripting context.
	/// </summary>
	[TestMethod]
	public void ScriptsCanPersistDataAcrossContexts()
	{
		var directory = Path.Combine( Path.GetTempPath(), $"sbox-script-storage-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( directory );

		try
		{
			using ( new GlobalContext.GlobalContextScope( new GlobalContext
			{
				TypeLibrary = SharedTypeLibrary.ScriptingWithSystem
			} ) )
			{
				var previousGame = GlobalContext.Game;
				GlobalContext.Game = GlobalContext.Current;
				var disk = new LocalFileSystem( directory );
				GlobalContext.Current.FileData = disk;
				try
				{
					Assert.IsTrue( Run<bool>( """
						FileSystem.Data.WriteAllText("best.txt", "42");
						return FileSystem.Data.FileExists("best.txt");
						""" ) );
				}
				finally
				{
					GlobalContext.Current.Shutdown();
					GlobalContext.Game = previousGame;
					disk.Dispose();
				}
			}

			using ( new GlobalContext.GlobalContextScope( new GlobalContext
			{
				TypeLibrary = SharedTypeLibrary.ScriptingWithSystem
			} ) )
			{
				var previousGame = GlobalContext.Game;
				GlobalContext.Game = GlobalContext.Current;
				var disk = new LocalFileSystem( directory );
				GlobalContext.Current.FileData = disk;
				try
				{
					Assert.AreEqual( "42", Run<string>( "return FileSystem.Data.ReadAllText(\"best.txt\");" ) );
				}
				finally
				{
					GlobalContext.Current.Shutdown();
					GlobalContext.Game = previousGame;
					disk.Dispose();
				}
			}
		}
		finally
		{
			File.Delete( Path.Combine( directory, "best.txt" ) );
			Directory.Delete( directory );
		}
	}

	/// <summary>
	/// JSON, directory enumeration and returned sub-filesystems use their native contracts.
	/// </summary>
	[TestMethod]
	public void ScriptsCanUseJsonAndSubFileSystems()
	{
		Assert.AreEqual( 42, Run<int>( """
			FileSystem.Data.CreateDirectory("records");
			var records = FileSystem.Data.CreateSubSystem("records");
			records.WriteJson<int>("best.json", 42);
			int count = 0;
			foreach (var file in records.FindFile("/", "*.json"))
			{
				count += 1;
			}
			return count == 1 ? records.ReadJson<int>("best.json") : -1;
			""" ) );

		Assert.AreEqual( "42", storage.ReadAllText( "organization/game/records/best.json" ) );
	}

	/// <summary>
	/// Data and organization storage stay separate from mounted content and other games.
	/// </summary>
	[TestMethod]
	public void ScriptsUseTheirAssignedScopes()
	{
		Assert.AreEqual( "mounted content", Run<string>( """
			FileSystem.Data.WriteAllText("best.txt", "game best");
			FileSystem.OrganizationData.WriteAllText("shared.txt", "organization shared");
			return FileSystem.Mounted.ReadAllText("content.txt");
			""" ) );

		Assert.AreEqual( "game best", storage.ReadAllText( "organization/game/best.txt" ) );
		Assert.AreEqual( "organization shared", storage.ReadAllText( "organization/shared.txt" ) );
		Assert.IsFalse( storage.FileExists( "organization/other-game/best.txt" ) );
		Assert.IsFalse( Run<bool>( "return FileSystem.Data.FileExists(\"shared.txt\");" ) );
		Assert.IsFalse( Run<bool>( "return FileSystem.Mounted.FileExists(\"best.txt\");" ) );
	}

	/// <summary>
	/// Scripts can create an independent in-memory filesystem through the public factory.
	/// </summary>
	[TestMethod]
	public void ScriptsCanCreateMemoryFileSystems()
	{
		Assert.AreEqual( "temporary", Run<string>( """
			var memory = FileSystem.CreateMemoryFileSystem();
			memory.WriteAllText("temporary.txt", "temporary");
			return memory.ReadAllText("temporary.txt");
			""" ) );

		Assert.IsFalse( data.FileExists( "temporary.txt" ) );
	}

	/// <summary>
	/// Exposing scoped storage does not grant host-file constructors or private filesystem controls.
	/// </summary>
	[TestMethod]
	[DataRow( "return System.IO.File.ReadAllText(\"outside.txt\");" )]
	[DataRow( "return new System.IO.FileStream(path, 2);" )]
	[DataRow( "return new Sandbox.LocalFileSystem(\"C:/\");" )]
	[DataRow( "return new BaseFileSystem();" )]
	[DataRow( "return EngineFileSystem.Root;" )]
	[DataRow( "return FileSystem.Data.system;" )]
	[DataRow( "FileSystem.Data.Mount(null);" )]
	[DataRow( "FileSystem.Data.Dispose();" )]
	public void ScriptsCannotAccessHostFileSystems( string source )
	{
		var path = Path.Combine( Path.GetTempPath(), $"sbox-script-denied-{Guid.NewGuid():N}.tmp" );
		var script = Game.Scripting.CreateScript( source );
		if ( script.HasArgument( "path" ) )
		{
			script.SetArgument( "path", path );
		}

		try
		{
			Assert.IsFalse( script.Run(), source );
			Assert.IsNotNull( script.LastError, source );
		}
		finally
		{
			File.Delete( path );
		}
	}

	/// <summary>
	/// Filesystem paths remain rooted in the game scope when scripts attempt to traverse above it.
	/// </summary>
	[TestMethod]
	[DataRow( "../outside.txt" )]
	[DataRow( "/../../outside.txt" )]
	[DataRow( "C:/outside.txt" )]
	public void ScriptsCannotWriteOutsideDataScope( string path )
	{
		var script = Game.Scripting.CreateScript( "FileSystem.Data.WriteAllText(path, \"outside\");" );
		Assert.IsFalse( script.With( "path", path ).Run() );
		Assert.IsNotNull( script.LastError );
		Assert.IsFalse( storage.FileExists( "outside.txt" ) );
		Assert.IsFalse( storage.FileExists( "organization/outside.txt" ) );
	}

	T Run<T>( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		var result = script.RunAndReturn<T>();
		Assert.IsNull( script.LastError, script.LastError?.Message );
		return result;
	}
}
