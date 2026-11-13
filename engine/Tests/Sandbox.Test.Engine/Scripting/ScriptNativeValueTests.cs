using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ScriptingTests;

/// <summary>
/// Native returned values use the engine's approved metadata, including its visibility restrictions.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptNativeValueTests
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
	/// Guid formatting binds through the real catalog instead of falling back to System.Object.
	/// </summary>
	[TestMethod]
	[DataRow( "return id.ToString();", "00112233-4455-6677-8899-aabbccddeeff" )]
	[DataRow( "return id.ToString(\"N\");", "00112233445566778899aabbccddeeff" )]
	public void GuidFormattingMatchesNative( string source, string expected )
	{
		var script = Game.Scripting.CreateScript( source );
		script.SetArgument( "id", new Guid( "00112233-4455-6677-8899-aabbccddeeff" ) );
		Assert.IsTrue( script.Compile(), script.LastError?.ToString() );
		Assert.AreEqual( expected, script.RunAndReturn<string>(), script.LastError?.ToString() );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Both outcomes of each Guid equality operation agree with native C#.
	/// </summary>
	[TestMethod]
	[DataRow( "return id == other;", true )]
	[DataRow( "return id != other;", false )]
	[DataRow( "return id.Equals(other);", true )]
	public void GuidEqualityMatchesNative( string source, bool sameResult )
	{
		var id = new Guid( "00112233-4455-6677-8899-aabbccddeeff" );
		var script = Game.Scripting.CreateScript( source );
		foreach ( var other in new[] { id, Guid.Empty } )
		{
			script.SetArgument( "id", id );
			script.SetArgument( "other", other );
			Assert.IsTrue( script.Compile(), script.LastError?.ToString() );
			Assert.AreEqual( id == other ? sameResult : !sameResult, script.RunAndReturn<bool>(), script.LastError?.ToString() );
			Assert.IsNull( script.LastError );
		}
	}

	/// <summary>
	/// Uses the actual engine vector override, not System.Numerics or a nonexistent format overload.
	/// </summary>
	[TestMethod]
	public void EngineVectorFormattingMatchesNative()
	{
		var position = new Vector3( 1.25f, -2.5f, 3.75f );
		var script = Game.Scripting.CreateScript( "return position.ToString();" );
		script.SetArgument( "position", position );
		Assert.IsTrue( script.Compile(), script.LastError?.ToString() );
		Assert.AreEqual( position.ToString(), script.RunAndReturn<string>(), script.LastError?.ToString() );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Common value operations do not expose reflection.
	/// </summary>
	[TestMethod]
	[DataRow( "return id.GetType();" )]
	public void GuidMemberRestrictionsRemainInForce( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		script.SetArgument( "id", Guid.Empty );
		Assert.IsFalse( script.Run(), source );
		Assert.IsNotNull( script.LastError );
	}

	/// <summary>
	/// Span-based Guid APIs stay outside Breen's object-value calling convention.
	/// </summary>
	[TestMethod]
	public void GuidSpanMembersAreNotSuppliedToBreen()
	{
		var members = new List<MemberInfo>();
		new TypeLibraryScriptResolver( Game.TypeLibrary ).GetMembers( typeof( Guid ), members );
		Assert.IsTrue( typeof( Guid ).GetMethods().Any( method => method.Name == "TryFormat" ) );
		Assert.IsFalse( members.Any( member => member.Name == "TryFormat" ) );
	}
}
