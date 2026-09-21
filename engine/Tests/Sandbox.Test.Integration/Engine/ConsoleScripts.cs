using NativeEngine;
using Sandbox.Engine;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class ConsoleScriptCommandTests
{
	MemoryFileSystem _scripts;
	BaseFileSystem _previousMount;
	string _savedBindings;

	[TestInitialize]
	public void Initialize()
	{
		_scripts = new MemoryFileSystem();
		_previousMount = GlobalContext.Current.FileMount;
		GlobalContext.Current.FileMount = _scripts;
		_savedBindings = KeyBindings.Write();
	}

	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.FileMount = _previousMount;
		_scripts.Dispose();
		KeyBindings.Read( _savedBindings );
	}

	[TestMethod]
	[DataRow( "exec" )]
	[DataRow( "bind" )]
	[DataRow( "unbind" )]
	[DataRow( "unbindall" )]
	[DataRow( "binddefaults" )]
	[DataRow( "writekeybindings" )]
	[DataRow( "host_writeconfig" )]
	public void GameCannotRunProtectedCommands( string name )
	{
		using var scope = GlobalContext.GameScope();
		Assert.IsFalse( Game.IsMenu );
		Assert.IsInstanceOfType<ManagedCommand>( ConVarSystem.Find( name ) );
		var exception = Assert.ThrowsException<System.Exception>( () => ConsoleSystem.Run( name, "F10", "changed" ) );
		Assert.AreEqual( $"Can't run '{name}'", exception.Message );
		Assert.AreEqual( _savedBindings, KeyBindings.Write() );
	}

	[TestMethod]
	public void OnlyExecRemainsRegistered()
	{
		Assert.IsInstanceOfType<ManagedCommand>( ConVarSystem.Find( "exec" ) );
		Assert.IsInstanceOfType<ManagedCommand>( ConVarSystem.Find( "host_writeconfig" ) );
		foreach ( var name in new[] { "execifexists", "exec_async", "exec_async_wait", "sleep", "run_perftest" } )
			Assert.IsNull( ConVarSystem.Find( name ), name );
	}

	[TestMethod]
	public void ExecPreservesNestedOrderAndQuotedSemicolons()
	{
		_scripts.WriteAllText( "cfg/parent.cfg", "bind F10 before\nexec child\nbind F11 \"after; quoted\"" );
		_scripts.WriteAllText( "cfg/child.cfg", "bind F10 child" );
		ConVarSystem.Run( "exec parent MOD" );
		Assert.AreEqual( "child", KeyBindings.GetBinding( ButtonCode.KEY_F10 ) );
		Assert.AreEqual( "after; quoted", KeyBindings.GetBinding( ButtonCode.KEY_F11 ) );
	}

	[TestMethod]
	public void RecursiveScriptsStopAndSubsequentExecStillWorks()
	{
		_scripts.WriteAllText( "cfg/recursive.cfg", "exec recursive\nbind F10 resumed" );
		ConVarSystem.Run( "exec recursive" );
		Assert.AreEqual( "resumed", KeyBindings.GetBinding( ButtonCode.KEY_F10 ) );

		_scripts.WriteAllText( "cfg/recursive.cfg", "bind F10 recovered" );
		ConVarSystem.Run( "exec recursive" );
		Assert.AreEqual( "recovered", KeyBindings.GetBinding( ButtonCode.KEY_F10 ) );
	}
}
