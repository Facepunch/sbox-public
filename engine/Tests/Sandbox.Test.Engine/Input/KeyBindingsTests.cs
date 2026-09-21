using NativeEngine;
using Sandbox.Engine;
using System.Text.Json.Nodes;

namespace EngineTests;

[TestClass, DoNotParallelize]
public class KeyBindingsTests
{
	[TestInitialize] public void Initialize() => KeyBindings.Reset();
	[TestCleanup] public void Cleanup() => KeyBindings.Reset();

	[TestMethod]
	public void UserOverridesAndExplicitUnbindsSurviveSaving()
	{
		KeyBindings.LoadDefaults( """{"bindings":{"w":"+forward","a":"+left"}}""" );
		KeyBindings.Read( """{"bindings":{"W":"<unbound>","future_key":"keep_me"},"version":"preserve"}""" );
		Assert.AreEqual( "", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.AreEqual( "+left", KeyBindings.GetBinding( ButtonCode.KEY_A ) );
		KeyBindings.Bind( "b", "say", "hello world" );
		KeyBindings.Unbind( "c" );
		var json = KeyBindings.Write();
		var user = JsonNode.Parse( json, new JsonNodeOptions { PropertyNameCaseInsensitive = true } );
		var saved = user["bindings"];
		Assert.AreEqual( "", saved["w"].GetValue<string>() );
		Assert.AreEqual( "say hello world", saved["b"].GetValue<string>() );
		Assert.IsNull( saved["a"] );
		Assert.IsNull( saved["c"] );
		Assert.AreEqual( "keep_me", saved["future_key"].GetValue<string>() );
		Assert.AreEqual( "preserve", user["version"].GetValue<string>() );
		KeyBindings.Read( json );
		Assert.AreEqual( "", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.AreEqual( "say hello world", KeyBindings.GetBinding( ButtonCode.KEY_B ) );
	}

	[TestMethod]
	public void DefaultResetDiscardsOldOverridesAndConfig()
	{
		KeyBindings.LoadDefaults( """{"bindings":{"w":"+forward"}}""" );
		KeyBindings.Read( """{"bindings":{"w":"+jump"},"old":"data"}""" );
		KeyBindings.ResetToDefaults();
		Assert.AreEqual( "+forward", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		var saved = JsonNode.Parse( KeyBindings.Write() );
		Assert.IsNull( saved["old"] );
		Assert.AreEqual( 0, saved["bindings"].AsObject().Count );
	}

	[TestMethod]
	public void DefaultsMergeAndUnbindAllOverridesThem()
	{
		KeyBindings.LoadDefaults( """{"bindings":{"w":"+forward","a":"+left"}}""" );
		KeyBindings.LoadDefaults( """{"bindings":{"W":"+jump"}}""" );
		KeyBindings.Read( "{}" );
		Assert.AreEqual( "+jump", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.AreEqual( "+left", KeyBindings.GetBinding( ButtonCode.KEY_A ) );
		KeyBindings.UnbindAll();
		KeyBindings.Read( KeyBindings.Write() );
		Assert.AreEqual( "", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.AreEqual( "", KeyBindings.GetBinding( ButtonCode.KEY_A ) );
	}

	[TestMethod]
	public void LegacyBindingsMigrateToJson()
	{
		KeyBindings.LoadDefaults( """{"bindings":{"w":"+forward","a":"+left"}}""" );
		var migrated = KeyBindings.ReadLegacy( """
			"config" { "bindings" { "w" "<unbound>" "b" "say hello" "future_key" "keep_me" } "version" "preserve" }
			""" );
		Assert.IsTrue( migrated );
		var saved = KeyBindings.Write();
		var document = JsonNode.Parse( saved );
		Assert.AreEqual( "", document["bindings"]["w"].GetValue<string>() );
		Assert.AreEqual( "keep_me", document["bindings"]["future_key"].GetValue<string>() );
		Assert.AreEqual( "preserve", document["version"].GetValue<string>() );
		Assert.IsTrue( KeyBindings.Read( saved ) );
		Assert.AreEqual( "", KeyBindings.GetBinding( ButtonCode.KEY_W ) );
		Assert.AreEqual( "+left", KeyBindings.GetBinding( ButtonCode.KEY_A ) );
		Assert.AreEqual( "say hello", KeyBindings.GetBinding( ButtonCode.KEY_B ) );
	}

	[TestMethod]
	public void InvalidJsonDoesNotReplaceBindings()
	{
		KeyBindings.Bind( "a", "first" );
		Assert.IsFalse( KeyBindings.Read( "{broken" ) );
		Assert.IsFalse( KeyBindings.Read( "null" ) );
		Assert.AreEqual( "first", KeyBindings.GetBinding( ButtonCode.KEY_A ) );
	}

	[TestMethod]
	public void InvalidKeysCannotChangeBindings()
	{
		KeyBindings.Bind( "a", "first" );
		KeyBindings.Bind( "not_a_key", "second" );
		KeyBindings.Unbind( "aux-1" );
		KeyBindings.SetBinding( (ButtonCode)int.MaxValue, "invalid" );
		Assert.AreEqual( "first", KeyBindings.GetBinding( ButtonCode.KEY_A ) );
		Assert.IsNull( KeyBindings.GetBinding( ButtonCode.BUTTON_CODE_INVALID ) );
	}
}
