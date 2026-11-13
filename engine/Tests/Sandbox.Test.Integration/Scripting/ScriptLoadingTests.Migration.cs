using System;
using System.Collections.Generic;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	private sealed class MigrationReferences
	{
		internal Component First;
		internal Component Second;
		internal List<Component> Shared;
		internal Dictionary<string, Component> Dictionary;
		internal Func<int> Read;
		internal Func<int> DescribedRead;
		internal Script Command;
	}

	private MigrationReferences migration;

	/// <summary>
	/// Assembly replacement migrates ordinary object fields, delegates, stores and shared cycles.
	/// The fixture is retained through watched static fields, like game and editor state.
	/// </summary>
	[TestMethod]
	public void AssemblyReplacementMigratesComponentGraphs()
	{
		var resource = LoadSource( "migration.csx", """
			class MigrationComponent : Component
			{
				[Property] public int Value { get; set; } = 7;
				[Property] public Component Other { get; set; }
				public int Starts;
				public int Read() { return Value; }
				protected override void OnStart() { Starts++; }
				protected override void OnUpdate() { Value++; }
			}
			""" );
		scene = new Scene();
		using var scope = scene.Push();
		var type = Game.TypeLibrary.GetType( "MigrationComponent" );
		migration = new();
		migration.First = scene.CreateObject().Components.Create( type );
		migration.Second = scene.CreateObject().Components.Create( type );
		type.SetValue( migration.First, "Value", 21 );
		type.SetValue( migration.First, "Other", migration.Second );
		type.SetValue( migration.Second, "Other", migration.First );
		migration.Shared = [migration.First, migration.Second, migration.First];
		migration.Dictionary = new() { ["first"] = migration.First, ["second"] = migration.Second };
		migration.Read = migration.First.GetType().GetMethod( "Read" ).CreateDelegate<Func<int>>( migration.First );
		migration.DescribedRead = type.GetMethod( "Read" ).CreateDelegate<Func<int>>( migration.First );
		Game.Scripting.Globals.Set<Component>( "RetainedComponent", migration.First );
		migration.Command = Game.Scripting.CreateScript( "[Global] Component RetainedComponent = null; return RetainedComponent.Read();" );
		Assert.AreEqual( 21, migration.Command.RunAndReturn<int>(), migration.Command.LastError?.ToString() );
		migration.First.InternalUpdate();
		var original = migration.First;
		LoadSource( "migration.csx", """
			class MigrationComponent : Component
			{
				[Property] public int Value { get; set; } = 99;
				[Property] public Component Other { get; set; }
				public int Starts;
				[Property] public int Added { get; set; } = 8;
				public int Read() { return Value * 2; }
				protected override void OnStart() { Starts++; }
				protected override void OnUpdate() { Value += 10; }
			}
			""" );
		Assert.IsNull( resource.LastError, resource.LastError?.ToString() );
		Assert.AreNotSame( original, migration.First );
		Assert.AreSame( migration.Second, type.GetValue( migration.First, "Other" ) );
		Assert.AreSame( migration.First, type.GetValue( migration.Second, "Other" ) );
		Assert.AreSame( migration.First, migration.Shared[0] );
		Assert.AreSame( migration.First, migration.Shared[2] );
		Assert.AreSame( migration.First, migration.Dictionary["first"] );
		Assert.AreSame( migration.Second, migration.Dictionary["second"] );
		Assert.AreEqual( 22, type.GetValue( migration.First, "Value" ) );
		Assert.AreEqual( 44, migration.Read() );
		Assert.AreEqual( 44, migration.DescribedRead() );
		Assert.AreSame( migration.First, Game.Scripting.Globals.Get<Component>( "RetainedComponent" ) );
		Assert.AreEqual( 44, migration.Command.RunAndReturn<int>(), migration.Command.LastError?.ToString() );
		Assert.AreEqual( 8, type.GetValue( migration.First, "Added" ) );
		migration.First.InternalUpdate();
		Assert.AreEqual( 32, type.GetValue( migration.First, "Value" ) );
		Assert.AreEqual( 1, type.GetValue( migration.First, "Starts" ) );
	}

}
