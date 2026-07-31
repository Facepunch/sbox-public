using System;
using System.Collections.Generic;
using System.Reflection;

namespace SystemTests;

/// <summary>
/// A cheat ConVar can only be changed while cheats are enabled, so nothing it was set to is
/// allowed to outlive that authority - otherwise you could enable cheats in your own game, turn
/// something on, and still have it running in somebody else's game.
/// </summary>
[TestClass]
[DoNotParallelize]
public class CheatConVarTest
{
	// Deliberately plain properties - ManagedCommand takes the attribute as a constructor
	// argument, so we don't need [ConVar] here and don't drag the codegen into these tests
	internal static float InitialisedCheat { get; set; } = 15000f;

	internal static bool InitialisedCheatBool { get; set; } = true;

	internal static float InitialisedReplicatedCheat { get; set; } = 15000f;

	internal static float ThrowingGetterCheat
	{
		get => throw new InvalidOperationException( "This getter always throws" );
		set { }
	}

	readonly List<string> registered = new();
	bool previousCheatsEnabled;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();

		previousCheatsEnabled = Game.CheatsEnabled;
	}

	[TestCleanup]
	public void Cleanup()
	{
		foreach ( var name in registered )
		{
			ConVarSystem.Members.Remove( name );
		}

		registered.Clear();

		Game.CheatsEnabled = previousCheatsEnabled;
	}

	T Register<T>( T command ) where T : Command
	{
		ConVarSystem.Members[command.Name] = command;
		registered.Add( command.Name );

		return command;
	}

	T Add<T>( T command ) where T : Command
	{
		registered.Add( command.Name );
		ConVarSystem.AddConVar( command );

		return command;
	}

	/// <summary>
	/// Resetting a cheat ConVar is only safe if we know what its default actually was. These are
	/// declared as properties with initialisers, so the CLR type default is the wrong answer -
	/// resetting r_terrain_displacement to default(bool) would turn terrain displacement off.
	/// </summary>
	[TestMethod]
	public void DefaultValue_ComesFromPropertyInitialiser()
	{
		var command = BuildManagedCommand( nameof( InitialisedCheat ), "test_cheat_initialised" );

		Assert.AreEqual( "15000", command.DefaultValue,
			"Default should be the property's initialised value, not the CLR type default" );
	}

	[TestMethod]
	public void DefaultValue_ComesFromPropertyInitialiser_Bool()
	{
		var command = BuildManagedCommand( nameof( InitialisedCheatBool ), "test_cheat_initialised_bool" );

		Assert.AreEqual( "True", command.DefaultValue,
			"Default should be the property's initialised value, not the CLR type default" );
	}

	/// <summary>
	/// Reading a replicated ConVar goes through the network string table rather than the property's
	/// own value, so we can't use it to work out a default - a client hotloading addon code while
	/// connected to a server with cheats on would otherwise capture the host's value as the default,
	/// and resetting to it would leave the cheat on. Fall back to the type default instead.
	/// </summary>
	[TestMethod]
	public void DefaultValue_IgnoresLiveValueForReplicatedConVar()
	{
		var command = BuildManagedCommand( nameof( InitialisedReplicatedCheat ), "test_cheat_replicated",
			ConVarFlags.Cheat | ConVarFlags.Replicated );

		Assert.AreEqual( "0", command.DefaultValue,
			"Replicated convars shouldn't take their default from a value that could have come off the network" );
	}

	/// <summary>
	/// Addon code owns these getters, so one throwing can't stop the ConVar registering at all.
	/// </summary>
	[TestMethod]
	public void DefaultValue_SurvivesAThrowingGetter()
	{
		var command = BuildManagedCommand( nameof( ThrowingGetterCheat ), "test_cheat_throwing_getter" );

		Assert.AreEqual( "0", command.DefaultValue, "A throwing getter should fall back to the type default" );
	}

	static ManagedCommand BuildManagedCommand( string propertyName, string convarName, ConVarFlags flags = ConVarFlags.Cheat )
	{
		var member = typeof( CheatConVarTest ).GetProperty( propertyName, BindingFlags.Static | BindingFlags.NonPublic );

		Assert.IsNotNull( member, $"Couldn't find test property {propertyName}" );

		var attribute = new ConVarAttribute( convarName, flags ) { Help = "Test convar" };

		return new ManagedCommand( typeof( CheatConVarTest ).Assembly, member, attribute );
	}

	[TestMethod]
	public void ResetCheatConVars_ResetsCheatVariables()
	{
		var cheat = Register( new TestConVar( "test_cheat_reset", "1", "0" ) );

		ConVarSystem.ResetCheatConVars();

		Assert.AreEqual( "0", cheat.Value );
	}

	[TestMethod]
	public void ResetCheatConVars_LeavesNonCheatVariablesAlone()
	{
		var normal = Register( new TestConVar( "test_normal_reset", "1", "0", isCheat: false ) );

		ConVarSystem.ResetCheatConVars();

		Assert.AreEqual( "1", normal.Value, "A ConVar that isn't a cheat should never be touched" );
	}

	/// <summary>
	/// Addon code owns these setters, so one of them throwing can't be allowed to abandon the
	/// reset and leave every other cheat switched on.
	/// </summary>
	[TestMethod]
	public void ResetCheatConVars_KeepsGoingWhenASetterThrows()
	{
		Register( new ThrowingConVar( "test_cheat_throws" ) );
		var cheat = Register( new TestConVar( "test_cheat_after_throw", "1", "0" ) );

		ConVarSystem.ResetCheatConVars();

		Assert.AreEqual( "0", cheat.Value, "A throwing setter must not leave other cheats enabled" );
	}

	/// <summary>
	/// Game assemblies register their ConVars long after a game was torn down, and cookies, the
	/// command line and hotloads can all bring a value back with them.
	/// </summary>
	[TestMethod]
	public void AddConVar_DropsRestoredCheatValueWhenCheatsAreOff()
	{
		Game.CheatsEnabled = false;

		var cheat = Add( new TestConVar( "test_cheat_restored", "0", "0" ) { SavedValue = "1" } );

		Assert.AreEqual( "0", cheat.Value, "A saved cheat value must not come back while cheats are off" );
	}

	[TestMethod]
	public void AddConVar_KeepsRestoredCheatValueWhenCheatsAreOn()
	{
		Game.CheatsEnabled = true;

		var cheat = Add( new TestConVar( "test_cheat_restored_allowed", "0", "0" ) { SavedValue = "1" } );

		Assert.AreEqual( "1", cheat.Value, "Cheats are on, so the saved value is allowed" );
	}

	[TestMethod]
	public void AddConVar_KeepsRestoredValueForNonCheatConVar()
	{
		Game.CheatsEnabled = false;

		var normal = Add( new TestConVar( "test_normal_restored", "0", "0", isCheat: false ) { SavedValue = "1" } );

		Assert.AreEqual( "1", normal.Value, "Non-cheat ConVars should still load their saved value" );
	}

	[TestMethod]
	public void SvCheatsTurningOff_ResetsCheatVariables()
	{
		var cheat = Register( new TestConVar( "test_cheat_sv_off", "1", "0" ) );

		Game.CheatsEnabled = false;
		ConVarSystem.InvokeConVarChanged( new TestConVar( "sv_cheats", "0", "False", isCheat: false ), "1" );

		Assert.AreEqual( "0", cheat.Value );
	}

	[TestMethod]
	public void SvCheatsTurningOn_LeavesCheatVariablesAlone()
	{
		var cheat = Register( new TestConVar( "test_cheat_sv_on", "1", "0" ) );

		Game.CheatsEnabled = true;
		ConVarSystem.InvokeConVarChanged( new TestConVar( "sv_cheats", "1", "False", isCheat: false ), "0" );

		Assert.AreEqual( "1", cheat.Value, "Cheats are on, nothing should be reset" );
	}

	/// <summary>
	/// Tearing a game down gives up cheat authority entirely - see Application.ClearGame.
	/// </summary>
	[TestMethod]
	public void ResetCheats_TurnsCheatsOffAndResetsCheatVariables()
	{
		var cheat = Register( new TestConVar( "test_cheat_teardown", "1", "0" ) );
		var svCheats = Register( new TestConVar( "sv_cheats", "1", "False", isCheat: false ) );

		ConVarSystem.ResetCheats();

		Assert.AreEqual( "False", svCheats.Value, "Cheats should go back off when a game is torn down" );
		Assert.AreEqual( "0", cheat.Value );
	}

	class TestConVar : Command
	{
		readonly string defaultValue;

		/// <summary>
		/// Pretends to have a value saved to a cookie, like a ConVar flagged Saved would.
		/// </summary>
		public string SavedValue { get; init; }

		public TestConVar( string name, string value, string defaultValue, bool isCheat = true )
		{
			Name = name;
			IsConCommand = false;
			IsCheat = isCheat;
			Value = value;

			this.defaultValue = defaultValue;
		}

		public override string DefaultValue => defaultValue;

		public override bool TryLoad( out string loaded )
		{
			loaded = SavedValue;

			return SavedValue is not null;
		}
	}

	class ThrowingConVar : Command
	{
		public ThrowingConVar( string name )
		{
			Name = name;
			IsConCommand = false;
			IsCheat = true;
		}

		public override string DefaultValue => "0";

		public override string Value
		{
			get => "1";
			set => throw new InvalidOperationException( "This setter always throws" );
		}
	}
}
