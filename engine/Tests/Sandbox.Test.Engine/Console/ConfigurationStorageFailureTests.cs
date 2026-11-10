using Sandbox.Engine;
using System;
using System.IO;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class ConfigurationStorageFailureTests
{
	string _directory;
	LocalFileSystem _files;

	[TestInitialize]
	public void Initialize()
	{
		_directory = Path.Combine( Path.GetTempPath(), $"sbox-config-test-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( _directory );
		_files = new LocalFileSystem( _directory );
		KeyBindings.Reset();
	}

	[TestCleanup]
	public void Cleanup()
	{
		_files.Dispose();
		Directory.Delete( _directory, recursive: true );
		KeyBindings.Reset();
	}

	[TestMethod]
	public void FailedBindingReplacementPreservesOriginal()
	{
		if ( !OperatingSystem.IsWindows() ) Assert.Inconclusive( "Windows sharing violations are required." );
		const string path = "core/cfg/user_keys_123.json";
		_files.WriteAllText( path, "{}" );
		var storage = new KeyBindings.Storage( _files, 123 );
		Assert.AreEqual( KeyBindings.LoadResult.Loaded, storage.Load() );
		KeyBindings.Bind( "w", "changed" );
		using ( File.Open( Path.Combine( _directory, path ), FileMode.Open, FileAccess.ReadWrite, FileShare.None ) )
		{
			Assert.IsFalse( storage.Save() );
		}
		Assert.AreEqual( "{}", _files.ReadAllText( path ) );
		Assert.AreEqual( 0, _files.FindFile( "core/cfg", "*.tmp" ).Count() );
	}

	[TestMethod]
	public void LockedMachineConfigIsNeitherReplacedNorQuarantined()
	{
		if ( !OperatingSystem.IsWindows() ) Assert.Inconclusive( "Windows sharing violations are required." );
		const string path = "cfg/machine_convars.json";
		_files.WriteAllText( path, "{broken" );
		using var storage = new ConsoleConfig.VariableStorage( _files );
		using ( File.Open( Path.Combine( _directory, path ), FileMode.Open, FileAccess.ReadWrite, FileShare.None ) )
		{
			Assert.IsFalse( storage.Initialize( [] ) );
			Assert.IsFalse( ConsoleConfig.SaveVariables( _files, [] ) );
		}
		Assert.IsFalse( storage.Save( [] ) );
		Assert.AreEqual( "{broken", _files.ReadAllText( path ) );
		Assert.IsFalse( _files.FileExists( path + ".bad" ) );
		Assert.AreEqual( 0, _files.FindFile( "cfg", "*.tmp" ).Count() );
	}
}
