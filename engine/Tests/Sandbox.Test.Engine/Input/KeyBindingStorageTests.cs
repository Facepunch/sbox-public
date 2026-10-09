using NativeEngine;
using Sandbox.Engine;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class KeyBindingStorageTests
{
	MemoryFileSystem _files;
	KeyBindings.Storage _storage;

	[TestInitialize]
	public void Initialize()
	{
		KeyBindings.Reset();
		_files = new MemoryFileSystem();
		_storage = new KeyBindings.Storage( _files, 123 );
	}

	[TestCleanup]
	public void Cleanup()
	{
		_files.Dispose();
		KeyBindings.Reset();
	}

	[TestMethod]
	public void MissingBindingsUseDefaultsAndCanBeSavedAndReloaded()
	{
		KeyBindings.LoadDefaults( """{"bindings":{"w":"default"}}""" );
		Assert.AreEqual( KeyBindings.LoadResult.Missing, _storage.Initialize() );
		Assert.AreEqual( "default", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.IsFalse( _files.FileExists( "core/cfg/user_keys_123.json" ) );
		KeyBindings.Bind( "w", "custom" );
		Assert.IsTrue( _storage.Save() );
		KeyBindings.Reset();
		Assert.AreEqual( KeyBindings.LoadResult.Loaded, _storage.Load() );
		Assert.AreEqual( "custom", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
	}

	[TestMethod]
	[DataRow( "{broken" )]
	[DataRow( "{\"bindings\":{\"W\":\"first\",\"w\":\"second\"}}" )]
	public void FailedStartupUsesDefaultsWithoutOverwritingUserBindings( string invalid )
	{
		KeyBindings.LoadDefaults( """{"bindings":{"w":"default"}}""" );
		_files.WriteAllText( "core/cfg/user_keys_123.json", invalid );
		Assert.AreEqual( KeyBindings.LoadResult.Failed, _storage.Initialize() );
		Assert.AreEqual( "default", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.IsFalse( _storage.Save() );
		Assert.AreEqual( invalid, _files.ReadAllText( "core/cfg/user_keys_123.json" ) );
	}

	[TestMethod]
	[DataRow( true, 123u )]
	[DataRow( false, 0u )]
	public void MigrationPreservesLegacyBindings( bool hasSteamUser, uint expectedAccount )
	{
		var account = KeyBindings.ResolveAccountId( hasSteamUser, 76561197960265851 );
		Assert.AreEqual( expectedAccount, account );
		var storage = new KeyBindings.Storage( _files, account );
		var path = $"core/cfg/user_keys_{account}";
		const string legacy = "\"config\" { \"bindings\" { \"w\" \"legacy\" } }";
		_files.WriteAllText( path + ".vcfg", legacy );
		Assert.AreEqual( KeyBindings.LoadResult.Migrated, storage.Initialize() );
		Assert.AreEqual( "legacy", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.IsTrue( _files.FileExists( path + ".json" ) );
		Assert.AreEqual( legacy, _files.ReadAllText( path + ".vcfg" ) );

		// Retained legacy files must not replace newer settings on the next launch.
		KeyBindings.Bind( "w", "updated" );
		Assert.IsTrue( storage.Save() );
		Assert.AreEqual( KeyBindings.LoadResult.Loaded, storage.Initialize() );
		Assert.AreEqual( "updated", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
	}
}
