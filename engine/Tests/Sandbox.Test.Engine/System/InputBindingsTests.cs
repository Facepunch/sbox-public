using System;
using System.Collections.Generic;
using System.IO;

namespace SystemTests;

/// <summary>
/// Tests for the public input rebinding surface on <see cref="Input"/> (GetBind, GetAllBinds,
/// SetBind, ResetBinds), backed by the internal BindCollection / InputBinds system.
/// </summary>
// These tests drive global engine state (Application.GameIdent, InputBinds.Collections,
// Input.InputSettings), so they must not run concurrently with each other or other tests.
[DoNotParallelize]
[TestClass]
public class InputBindingsTests
{
	// A unique game ident so we don't collide with the real "common" collection or any
	// other engine state in parallel tests.
	const string TestGameIdent = "test.bindings.game";

	// A throw-away, per-test folder underneath the .source2 root the test assembly seeds.
	const string ConfigFolder = ".source2/InputBindingsTest";

	[TestInitialize]
	public void Setup()
	{
		// Initialise an isolated, throw-away config filesystem. The bind system touches
		// EngineFileSystem.Config when it (re)loads a bind collection, so it must exist.
		// LocalFileSystem requires the root directory to already exist on disk.
		Directory.CreateDirectory( ConfigFolder );

		EngineFileSystem.Initialize( ConfigFolder, true );
		EngineFileSystem.InitializeConfigFolder();

		// Point the engine at a throw-away game and give it a small known action set.
		Application.GameIdent = TestGameIdent;

		var settings = new InputSettings
		{
			Actions = new List<InputAction>
			{
				new InputAction( "jump", "space", title: "Jump" ),
				new InputAction( "attack1", "mouse1", gamepadCode: GamepadCode.RightTrigger, title: "Attack" ),
				new InputAction( "move", "w" )
			}
		};

		// Push the action definitions through the same path the engine uses so the
		// bind collection is populated with defaults.
		Input.ReadConfig( settings );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Application.GameIdent = null;
		EngineFileSystem.Shutdown();
	}

	[TestMethod]
	public void GetBind_ReturnsDefaultKey_WhenNothingIsBound()
	{
		Assert.AreEqual( "space", Input.GetBind( "jump" ) );
		Assert.AreEqual( "mouse1", Input.GetBind( "attack1" ) );
		Assert.AreEqual( "w", Input.GetBind( "move" ) );
	}

	[TestMethod]
	public void SetBind_ThenGetBind_ReturnsTheBoundCombo()
	{
		Input.SetBind( "jump", "e", 0 );

		Assert.AreEqual( "e", Input.GetBind( "jump" ) );
	}

	[TestMethod]
	public void SetBind_ToNull_ClearsTheSlot()
	{
		Input.SetBind( "jump", "e", 0 );
		Input.SetBind( "jump", null, 0 );

		// Cleared slot falls back to the action's declared default.
		Assert.AreEqual( "space", Input.GetBind( "jump" ) );
	}

	[TestMethod]
	public void SetBind_WritesToTheRequestedSlot()
	{
		Input.SetBind( "attack1", "mouse2", 1 );

		Assert.AreEqual( "mouse1", Input.GetBind( "attack1", 0 ) );
		Assert.AreEqual( "mouse2", Input.GetBind( "attack1", 1 ) );
	}

	[TestMethod]
	public void GetBind_FallsBackAcrossSlotsAndDefault()
	{
		// Slot 1 never has a declared default, so an unbound slot 1 is null.
		Assert.IsNull( Input.GetBind( "jump", 1 ) );

		// Fallback to the declared default for slot 0 still applies.
		Assert.AreEqual( "space", Input.GetBind( "jump", 0 ) );
	}

	[TestMethod]
	public void ResettingBinds_RestoresDefaults()
	{
		Input.SetBind( "jump", "e", 0 );
		Input.ResetBinds();

		Assert.AreEqual( "space", Input.GetBind( "jump" ) );
	}

	[TestMethod]
	public void GetAllBinds_ContainsEveryActionWithTwoSlots()
	{
		Input.SetBind( "attack1", "mouse2", 1 );

		var binds = Input.GetAllBinds();

		Assert.AreEqual( 3, binds.Count );
		CollectionAssert.AreEqual( new[] { "space", null }, binds["jump"] );
		CollectionAssert.AreEqual( new[] { "mouse1", "mouse2" }, binds["attack1"] );
		CollectionAssert.AreEqual( new[] { "w", null }, binds["move"] );
	}

	[TestMethod]
	public void GetBind_IsCaseInsensitive()
	{
		Assert.AreEqual( "space", Input.GetBind( "JUMP" ) );
	}
}
