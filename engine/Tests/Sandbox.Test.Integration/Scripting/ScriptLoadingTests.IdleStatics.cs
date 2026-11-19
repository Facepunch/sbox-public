using System;
using System.Collections.Generic;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Editing one script while another script's component is alive, but has never run its RPC,
	/// leaves the scripting context usable. This is the edit-while-playing case.
	/// </summary>
	[TestMethod]
	[DataRow( "body" )]
	[DataRow( "shape" )]
	[DataRow( "new file" )]
	public void EditingScriptsKeepsIdleRpcComponentsUsable( string edit )
	{
		const string idle = """
			public class IdleRpc : Component
			{
				public int Calls;
				[Rpc.Host] public void Use() { Calls++; }
			}
			""";
		const string other = """
			public class OtherRpc : Component
			{
				public int Calls;
				public int Read() { return 1; }
				[Rpc.Host] public void Go() { Calls++; }
			}
			""";
		var loaded = Game.Scripting.LoadFiles( new Dictionary<string, string> { ["idle.csx"] = idle, ["other.csx"] = other } );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );

		scene = new Scene();
		using var scope = scene.Push();
		var gameObject = new GameObject( scene );
		var idleComponent = gameObject.Components.Create( Game.TypeLibrary.GetType( "IdleRpc" ) );
		var otherComponent = gameObject.Components.Create( Game.TypeLibrary.GetType( "OtherRpc" ) );
		// Hotload walks the scripting state before the components, as it does from the engine's own statics.
		reloadRoots = [Game.Scripting, idleComponent, otherComponent];

		var edited = edit switch
		{
			"body" => Game.Scripting.LoadFile( "other.csx", other.Replace( "return 1;", "return 2;" ) ),
			"shape" => Game.Scripting.LoadFile( "other.csx", other.Replace( "public int Read()", "public int Extra() { return 3; } public int Read()" ) ),
			_ => Game.Scripting.LoadFile( "third.csx", "public class ThirdRpc : Component { [Rpc.Host] public void Go() { } }" )
		};
		Assert.IsTrue( edited.Success, string.Join( "\n", edited.Diagnostics ) );
		Assert.IsFalse( Game.Scripting.RequiresReload );

		idleComponent = (Component)reloadRoots[1];
		var type = idleComponent.GetType();
		type.GetMethod( "Use" ).CreateDelegate<Action>( idleComponent )();
		Assert.AreEqual( 1, type.GetField( "Calls" ).GetValue( idleComponent ) );

		otherComponent = (Component)reloadRoots[2];
		otherComponent.GetType().GetMethod( "Go" ).CreateDelegate<Action>( otherComponent )();
		Assert.AreEqual( 1, otherComponent.GetType().GetField( "Calls" ).GetValue( otherComponent ) );
	}

	/// <summary>
	/// A static initializer that reads the type library sees its own type: on a first load, for a type
	/// added by an edit, and for an existing type that is edited, whether or not it has been used yet.
	/// </summary>
	[TestMethod]
	[DataRow( "first load" )]
	[DataRow( "added by edit" )]
	[DataRow( "edited before use" )]
	[DataRow( "edited after use" )]
	public void StaticInitializersSeeRegisteredScriptTypes( string when )
	{
		const string probe = """
			public class LazyProbe : Component
			{
				static readonly object Described = Game.TypeLibrary.GetType( "LazyProbe" );
				public bool Has() { return Described != null; }
			}
			""";
		const string other = """
			public class LazyOther : Component
			{
				public int Calls;
			}
			""";

		scene = new Scene();
		using var scope = scene.Push();
		var gameObject = new GameObject( scene );
		Component probed = null;

		if ( when == "added by edit" )
		{
			var loaded = Game.Scripting.LoadFiles( new Dictionary<string, string> { ["other.csx"] = other } );
			Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );
			reloadRoots = [Game.Scripting, gameObject.Components.Create( Game.TypeLibrary.GetType( "LazyOther" ) )];

			var edited = Game.Scripting.LoadFile( "probe.csx", probe );
			Assert.IsTrue( edited.Success, string.Join( "\n", edited.Diagnostics ) );
		}
		else
		{
			var loaded = Game.Scripting.LoadFiles( new Dictionary<string, string> { ["probe.csx"] = probe, ["other.csx"] = other } );
			Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );
		}

		if ( when is "edited before use" or "edited after use" )
		{
			if ( when == "edited after use" )
			{
				probed = gameObject.Components.Create( Game.TypeLibrary.GetType( "LazyProbe" ) );
				Assert.AreEqual( true, probed.GetType().GetMethod( "Has" ).Invoke( probed, [] ) );
			}
			reloadRoots = [Game.Scripting, gameObject.Components.Create( Game.TypeLibrary.GetType( "LazyOther" ) ), probed];

			var edited = Game.Scripting.LoadFile( "probe.csx", probe.Replace( "public bool Has()", "public int Extra() { return 3; } public bool Has()" ) );
			Assert.IsTrue( edited.Success, string.Join( "\n", edited.Diagnostics ) );
			probed = (Component)reloadRoots[2];
		}

		Assert.IsFalse( Game.Scripting.RequiresReload );
		probed ??= gameObject.Components.Create( Game.TypeLibrary.GetType( "LazyProbe" ) );
		Assert.AreEqual( true, probed.GetType().GetMethod( "Has" ).Invoke( probed, [] ) );
		Assert.AreEqual( Game.TypeLibrary.GetType( "LazyProbe" ).TargetType, probed.GetType() );
	}
}
