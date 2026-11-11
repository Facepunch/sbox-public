using Sandbox.Engine;
using System;

namespace ScriptingTests;

/// <summary>
/// Catch declarations resolve exception types without exposing unapproved members.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptExceptionTests
{
	GlobalContext previous;
	GlobalContext previousGame;

	/// <summary>
	/// Uses the normal engine and system catalog without registering test helper types.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
	}

	/// <summary>
	/// Disposes the scripting context and restores the previous context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// A catch can name the base exception without registering its reflective member surface.
	/// </summary>
	[TestMethod]
	[DataRow( "Exception" )]
	[DataRow( "System.Exception" )]
	public void ExceptionCatchTypesAreRecognized( string typeName )
	{
		var script = Game.Scripting.CreateScript( $"try {{ throw failure; }} catch ({typeName} error) {{ return 7; }}" );
		script.SetArgument( "failure", new InvalidOperationException( "Expected" ) );
		Assert.IsTrue( script.Compile(), script.LastError?.ToString() );
		Assert.AreEqual( 7, script.RunAndReturn<int>(), script.LastError?.ToString() );
		Assert.IsNull( script.LastError );
		Assert.IsNull( Game.TypeLibrary.GetType( typeof( Exception ) ) );
	}

	/// <summary>
	/// Resolving a catch type does not grant access to constructors or unapproved exception members.
	/// </summary>
	[TestMethod]
	[DataRow( "return new System.Exception();" )]
	[DataRow( "return failure.TargetSite;" )]
	[DataRow( "return failure.GetType();" )]
	public void ExceptionMembersStillRequireApproval( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		if ( script.HasArgument( "failure" ) )
		{
			script.SetArgument( "failure", new InvalidOperationException( "Expected" ) );
		}

		Assert.IsFalse( script.Run(), source );
		Assert.IsNotNull( script.LastError );
	}
}
