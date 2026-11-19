using System;
using System.Collections.Generic;
using System.Linq;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Generator failures identify the original companion file, not its synthetic declaration path.
	/// </summary>
	[TestMethod]
	[DataRow( "broken.csx" )]
	[DataRow( "../broken" )]
	public void CodeGenerationErrorsKeepOriginalLocations( string path )
	{
		const string source = "\nclass Invalid { public int = ; }";
		var result = Game.Scripting.LoadFiles( new Dictionary<string, string>
		{
			["network.csx"] = "class NetworkMarker : Component { [Sync] public int Value { get; set; } }",
			[path] = source
		} );
		Assert.IsFalse( result.Published );
		var error = result.Diagnostics.First( error => error.Code == "SCR_CODEGEN" );
		Assert.AreEqual( path, error.FilePath );
		Assert.AreEqual( 2, error.Line );
		Assert.AreEqual( source.IndexOf( '=' ), error.Start );
		Assert.AreEqual( error.Start, error.Column );
	}

	/// <summary>
	/// Large authored scripts keep startup and function locations without duplicating their padding.
	/// </summary>
	[TestMethod]
	public void NetworkingGenerationDoesNotDuplicateSourcePadding()
	{
		var source = "/*" + new string( ' ', 40_000 ) + "*/\n" + """
			class LargeNet : Component { [Sync] public int Value { get; set; } }
			[Global] int LargeStarts = 0;
			LargeStarts++;
			int ReadLarge() { return LargeStarts; }
			""";
		var loaded = Game.Scripting.LoadFile( "large.csx", source );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );
		Assert.AreEqual( 1, Game.Scripting.Run<int>( "return ReadLarge();" ) );
		var location = Game.TypeLibrary.GetType( "LargeNet" ).GetAttribute<Sandbox.Internal.SourceLocationAttribute>();
		Assert.AreEqual( "large.csx", location.Path );
		Assert.AreEqual( 2, location.Line );
		var failed = Game.Scripting.LoadFile( "large.csx", source + "\nthrow new Exception(\"original line\");" );
		Assert.IsTrue( failed.Published );
		var error = failed.Diagnostics.Single();
		Assert.AreEqual( "large.csx", error.FilePath, error.ToString() );
		Assert.AreEqual( 6, error.Line );
	}

	/// <summary>
	/// Large authored files load and run with or without networking code generation.
	/// </summary>
	[TestMethod]
	[DataRow( "" )]
	[DataRow( "[Sync]" )]
	public void LargeAuthoredFilesLoadWithAndWithoutNetworkingGeneration( string attribute )
	{
		var source = "/*" + new string( ' ', 70_000 ) + $$"""
			*/ class LargeAuthoredComponent : Component { {{attribute}} public int Value { get; set; } }
			[Global] int LargeAuthoredValue = 42;
			""";
		var result = Game.Scripting.LoadFile( "large-authored.csx", source );
		Assert.IsTrue( result.Success, string.Join( "\n", result.Diagnostics ) );
		Assert.IsTrue( result.Published );
		Assert.IsNotNull( Game.TypeLibrary.GetType( "LargeAuthoredComponent" ) );
		Assert.AreEqual( 42, Game.Scripting.Globals.Get<int>( "LargeAuthoredValue" ) );
	}

	/// <summary>
	/// Qualified attribute trivia does not bypass the RPC wrapper's recipient filter.
	/// </summary>
	[TestMethod]
	[DataRow( "Rpc.Broadcast" )]
	[DataRow( "Rpc . Broadcast" )]
	[DataRow( "Sandbox /* namespace */ . Rpc . Broadcast" )]
	public void NetworkingAttributeTriviaPreservesRpcFiltering( string attribute )
	{
		var source = $$"""
			public class FilteredScriptRpc : Component
			{
				public int Calls;
				[{{attribute}}] public void Call() { Calls++; }
			}
			""";
		var loaded = Game.Scripting.LoadFile( "filtered-rpc.csx", source );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );
		scene = new Scene();
		var description = Game.TypeLibrary.GetType( "FilteredScriptRpc" );
		var component = new GameObject( scene ).Components.Create( description );
		var type = component.GetType();
		var call = type.GetMethod( "Call" ).CreateDelegate<Action>( component );

		using ( Rpc.FilterExclude( _ => true ) )
		{
			call();
		}

		Assert.AreEqual( 0, type.GetField( "Calls" ).GetValue( component ) );
		call();
		Assert.AreEqual( 1, type.GetField( "Calls" ).GetValue( component ) );
	}

	/// <summary>
	/// Named source passes through the addon generator before publication, preserving startup and real native callbacks.
	/// </summary>
	[TestMethod]
	[DataRow( "Broadcast" )]
	[DataRow( "Host" )]
	[DataRow( "Owner" )]
	public void GeneratedSyncAndRpcRunThroughNamedFileLoading( string rpcMode )
	{
		var source = $$"""
			[Global] int GeneratedStarts = 0;
			GeneratedStarts++;
			public class GeneratedNetworkComponent : Component
			{
				/// <summary>
				/// Replicated count.
				/// </summary>
				[Sync] public int Value { get; set; }
				public Guid LastCaller { get; set; }
				[Rpc.{{rpcMode}}] public void Add(int amount) { Value += amount; LastCaller = Rpc.Caller.Id; }
			}
			""";
		var loaded = Game.Scripting.LoadFile( "generated-network.csx", source );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "GeneratedStarts" ) );

		scene = new Scene();
		var description = Game.TypeLibrary.GetType( "GeneratedNetworkComponent" );
		var component = new GameObject( scene ).Components.Create( description );
		var type = component.GetType();
		var property = type.GetProperty( "Value" );
		Assert.IsNotNull( property.GetCustomAttributes( typeof( SyncAttribute ), false ).SingleOrDefault() );
		Assert.AreEqual( "Replicated count.", ((DescriptionAttribute)property.GetCustomAttributes( typeof( DescriptionAttribute ), false ).Single()).Value );
		var setter = property.SetMethod.CreateDelegate<Action<int>>( component );
		var getter = property.GetMethod.CreateDelegate<Func<int>>( component );
		var rpc = type.GetMethod( "Add" ).CreateDelegate<Action<int>>( component );
		setter( 10 );
		rpc( 5 );
		Assert.AreEqual( 15, getter() );
		Assert.AreEqual( Connection.Local.Id, type.GetProperty( "LastCaller" ).GetValue( component ) );

		var command = Game.Scripting.CreateScript( "Target.Add(7); return Target.Value;" );
		command.SetArgument( "Target", component );
		Assert.AreEqual( 22, command.RunAndReturn<int>() );
		Assert.IsNull( command.LastError, command.LastError?.Message );

		var unchanged = Game.Scripting.LoadFile( "generated-network.csx", source );
		Assert.IsTrue( unchanged.Success, string.Join( "\n", unchanged.Diagnostics ) );
		Assert.IsFalse( unchanged.Published );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "GeneratedStarts" ) );
		Assert.AreEqual( 22, getter() );

		reloadRoots = [component];
		var edited = Game.Scripting.LoadFile( "generated-network.csx", source.Replace( "Value += amount;", "Value += amount * 2;" ) );
		Assert.IsTrue( edited.Success, string.Join( "\n", edited.Diagnostics ) );
		component = (Component)reloadRoots[0];
		property = component.GetType().GetProperty( "Value" );
		getter = property.GetMethod.CreateDelegate<Func<int>>( component );
		rpc = component.GetType().GetMethod( "Add" ).CreateDelegate<Action<int>>( component );
		rpc( 1 );
		Assert.AreEqual( 24, getter() );
		Assert.AreEqual( 2, Game.Scripting.Globals.Get<int>( "GeneratedStarts" ) );

		var rejected = Game.Scripting.LoadFile( "generated-network.csx", source.Replace( "Value += amount;", "Value += ;" ) );
		Assert.IsFalse( rejected.Success );
		Assert.IsFalse( rejected.Published );
		rpc( 1 );
		Assert.AreEqual( 26, getter() );
		Assert.AreEqual( 2, Game.Scripting.Globals.Get<int>( "GeneratedStarts" ) );

		var unrelated = Game.Scripting.LoadFile( "other.csx", "int OtherValue() { return 1; }" );
		Assert.IsTrue( unrelated.Success, string.Join( "\n", unrelated.Diagnostics ) );
		component = (Component)reloadRoots[0];
		getter = component.GetType().GetProperty( "Value" ).GetMethod.CreateDelegate<Func<int>>( component );
		rpc = component.GetType().GetMethod( "Add" ).CreateDelegate<Action<int>>( component );
		rpc( 1 );
		Assert.AreEqual( 28, getter() );
		Assert.AreEqual( 2, Game.Scripting.Globals.Get<int>( "GeneratedStarts" ) );

		Assert.IsTrue( Game.Scripting.LoadFiles( new Dictionary<string, string>() ).Success );
		Assert.IsNull( Game.TypeLibrary.GetType( "GeneratedNetworkComponent" ) );
	}

	/// <summary>
	/// Generating one file preserves script-only binding and unchanged startup in the rest of the module.
	/// </summary>
	[TestMethod]
	[DataRow( "companion.csx" )]
	[DataRow( "companion" )]
	[DataRow( "C:/scripts/companion.csx" )]
	[DataRow( "../companion.csx" )]
	public void NetworkingGenerationPreservesOtherScriptFiles( string companionPath )
	{
		var sources = new Dictionary<string, string>
		{
			["startup"] = "[Global] int CompanionStarts = 0; CompanionStarts++;",
			[companionPath] = "int GetSeed() { return 3; } public class Companion { public int Read() { return GetSeed(); } }",
			["network.csx"] = """
				#define NETWORK_TEST
				#if NETWORK_TEST
				public class GeneratedPeer : Component
				{
					[Sync] public int Value { get; set; }
					public int Read() { return new Companion().Read(); }
				}
				#endif
				"""
		};
		var loaded = Game.Scripting.LoadFiles( sources );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );
		Assert.AreEqual( 3, Game.Scripting.Run<int>( "return new Companion().Read();" ) );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "CompanionStarts" ) );
		var location = Game.TypeLibrary.GetType( "GeneratedPeer" ).GetAttribute<Sandbox.Internal.SourceLocationAttribute>();
		Assert.AreEqual( "network.csx", location.Path );
		Assert.AreEqual( 3, location.Line );

		sources["network.csx"] = sources["network.csx"].Replace( "new Companion().Read()", "new Companion().Read() + 1" );
		var edited = Game.Scripting.LoadFiles( sources );
		Assert.IsTrue( edited.Success, string.Join( "\n", edited.Diagnostics ) );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "CompanionStarts" ) );
	}
}
