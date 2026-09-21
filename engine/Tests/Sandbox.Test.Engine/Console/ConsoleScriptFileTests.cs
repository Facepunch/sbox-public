using Sandbox.Engine;

namespace EngineTests;

[TestClass]
public class ConsoleScriptFileTests
{
	MemoryFileSystem mounted;
	MemoryFileSystem core;

	[TestInitialize]
	public void Initialize()
	{
		mounted = new MemoryFileSystem();
		core = new MemoryFileSystem();
	}

	[TestCleanup]
	public void Cleanup()
	{
		mounted.Dispose();
		core.Dispose();
	}

	[TestMethod]
	public void MountedScriptsOverrideCoreAndDefaultExtensionIsAdded()
	{
		mounted.WriteAllText( "cfg/test.cfg", "mounted" );
		core.WriteAllText( "cfg/test.cfg", "core" );
		core.WriteAllText( "cfg/fallback.cfg", "fallback" );
		Assert.AreEqual( "mounted", ConsoleScripts.Read( mounted, core, "test" ) );
		Assert.AreEqual( "fallback", ConsoleScripts.Read( mounted, core, "fallback.cfg" ) );
		Assert.AreEqual( "core", ConsoleScripts.Read( null, core, "test" ) );
		Assert.IsNull( ConsoleScripts.Read( mounted, core, "missing" ) );
	}

	[TestMethod]
	public void InvalidPathsAndOversizeFilesAreRejected()
	{
		var fs = mounted;
		fs.WriteAllText( "cfg/large.cfg", new string( 'x', 1024 * 1024 + 1 ) );
		fs.WriteAllText( "cfg/test.exe", "should not execute" );
		fs.WriteAllText( "cfg/test.txt", "should not execute" );
		foreach ( var file in new[] { "../test", "C:/test", "/test", "test.exe", "test.txt", "large.cfg" } )
			Assert.IsNull( ConsoleScripts.Read( fs, fs, file ), file );
	}
}
