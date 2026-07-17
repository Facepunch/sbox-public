using Editor;
using System;

namespace EditorTests;

/// <summary>
/// Hosts shortcut methods for <see cref="EditorShortcutsTest"/> to discover through
/// the editor type library. Deliberately not a Widget, so widget-scoped shortcuts
/// get promoted to window scope during registration.
/// </summary>
public class ShortcutTestHost
{
	[Shortcut( "shortcut-test.save_all-things", "ctrl+shift+t", ShortcutType.Application )]
	public static void SaveAllThings() { }

	[Shortcut( "shortcut-test.find", "ctrl+k" )]
	public static void Find() { }

	[Shortcut( "orphan-shortcut", "F9" )]
	public static void Orphan() { }
}

[TestClass]
[DoNotParallelize]
public class EditorShortcutsTest
{
	/// <summary>
	/// Rebuild the registry before every test so each one starts from a clean slate.
	/// Key overrides persist through EditorPreferences, which outside the editor falls
	/// back to an in-memory cookie jar - nothing here touches real editor settings.
	/// </summary>
	[TestInitialize]
	public void TestInitialize()
	{
		EditorShortcuts.RegisterShortcuts();
	}

	/// <summary>
	/// Registration should discover shortcut methods from the editor type library.
	/// </summary>
	[TestMethod]
	public void RegisterFindsShortcutMethods()
	{
		Assert.IsNotNull( EditorShortcuts.Entries.FirstOrDefault( x => x.Identifier == "shortcut-test.save_all-things" ) );
		Assert.IsNotNull( EditorShortcuts.Entries.FirstOrDefault( x => x.Identifier == "shortcut-test.find" ) );
		Assert.IsNotNull( EditorShortcuts.Entries.FirstOrDefault( x => x.Identifier == "orphan-shortcut" ) );
	}

	/// <summary>
	/// Key binds get normalized to uppercase, the default keys stay as declared on the
	/// attribute, and display keys are title-cased for menus.
	/// </summary>
	[TestMethod]
	public void KeysAreNormalized()
	{
		Assert.AreEqual( "CTRL+K", EditorShortcuts.GetKeys( "shortcut-test.find" ) );
		Assert.AreEqual( "ctrl+k", EditorShortcuts.GetDefaultKeys( "shortcut-test.find" ) );
		Assert.AreEqual( "Ctrl+K", EditorShortcuts.GetDisplayKeys( "shortcut-test.find" ) );
		Assert.AreEqual( "Ctrl+Shift+T", EditorShortcuts.GetDisplayKeys( "shortcut-test.save_all-things" ) );
	}

	/// <summary>
	/// Group and name are derived from the dotted identifier, with dashes and
	/// underscores becoming title-cased words.
	/// </summary>
	[TestMethod]
	public void NameAndGroupDeriveFromIdentifier()
	{
		var entry = EditorShortcuts.Entries.First( x => x.Identifier == "shortcut-test.save_all-things" );

		Assert.AreEqual( "Shortcut Test", entry.Group );
		Assert.AreEqual( "Save All Things", entry.Name );
	}

	/// <summary>
	/// Identifiers without a dot fall back to the "Other" group.
	/// </summary>
	[TestMethod]
	public void UngroupedIdentifierFallsBackToOtherGroup()
	{
		var entry = EditorShortcuts.Entries.First( x => x.Identifier == "orphan-shortcut" );

		Assert.AreEqual( "Other", entry.Group );
		Assert.AreEqual( "Orphan Shortcut", entry.Name );
	}

	/// <summary>
	/// Widget-scoped shortcuts on types that aren't widgets should be promoted to
	/// window scope, while explicit scopes are left alone.
	/// </summary>
	[TestMethod]
	public void StaticShortcutOnNonWidgetIsPromotedToWindow()
	{
		var promoted = EditorShortcuts.Entries.First( x => x.Identifier == "shortcut-test.find" );
		var application = EditorShortcuts.Entries.First( x => x.Identifier == "shortcut-test.save_all-things" );

		Assert.AreEqual( ShortcutType.Window, promoted.Attribute.Type );
		Assert.AreEqual( ShortcutType.Application, application.Attribute.Type );
	}

	/// <summary>
	/// Pressing and releasing a key should track held state on every entry whose bind
	/// ends with that key.
	/// </summary>
	[TestMethod]
	public void PressAndReleaseTrackHeldState()
	{
		Assert.IsFalse( EditorShortcuts.IsDown( "shortcut-test.find" ) );

		EditorShortcuts.Press( "K" );

		Assert.IsTrue( EditorShortcuts.IsDown( "shortcut-test.find" ) );
		Assert.IsFalse( EditorShortcuts.IsDown( "shortcut-test.save_all-things" ) );

		EditorShortcuts.Release( "K" );

		Assert.IsFalse( EditorShortcuts.IsDown( "shortcut-test.find" ) );
	}

	/// <summary>
	/// <see cref="EditorShortcuts.ReleaseAll"/> should clear held state everywhere.
	/// </summary>
	[TestMethod]
	public void ReleaseAllClearsHeldState()
	{
		EditorShortcuts.Press( "K" );
		EditorShortcuts.Press( "T" );

		EditorShortcuts.ReleaseAll();

		Assert.IsFalse( EditorShortcuts.Entries.Any( x => x.IsDown ) );
	}

	/// <summary>
	/// Lookups for identifiers that don't exist should fall back gracefully.
	/// </summary>
	[TestMethod]
	public void UnknownIdentifierFallsBack()
	{
		Assert.AreEqual( "", EditorShortcuts.GetKeys( "does.not.exist" ) );
		Assert.AreEqual( "", EditorShortcuts.GetDisplayKeys( "does.not.exist" ) );
		Assert.AreEqual( "", EditorShortcuts.GetDefaultKeys( "does.not.exist" ) );
		Assert.IsFalse( EditorShortcuts.IsDown( "does.not.exist" ) );
	}

	/// <summary>
	/// Rebinding a shortcut stores an override that survives re-registration, and
	/// setting it back to the default removes the override again.
	/// </summary>
	[TestMethod]
	public void KeyOverrideRoundTrip()
	{
		var entry = EditorShortcuts.Entries.First( x => x.Identifier == "shortcut-test.find" );

		try
		{
			entry.Keys = " alt+j ";

			Assert.AreEqual( "ALT+J", EditorShortcuts.GetKeys( "shortcut-test.find" ) );
			Assert.AreEqual( "Alt+J", EditorShortcuts.GetDisplayKeys( "shortcut-test.find" ) );
			Assert.IsTrue( EditorPreferences.ShortcutOverrides.ContainsKey( "shortcut-test.find" ) );

			EditorShortcuts.RegisterShortcuts();

			Assert.AreEqual( "ALT+J", EditorShortcuts.GetKeys( "shortcut-test.find" ) );
		}
		finally
		{
			var rebound = EditorShortcuts.Entries.First( x => x.Identifier == "shortcut-test.find" );
			rebound.Keys = "ctrl+k";
		}

		Assert.AreEqual( "CTRL+K", EditorShortcuts.GetKeys( "shortcut-test.find" ) );
		Assert.IsFalse( EditorPreferences.ShortcutOverrides.ContainsKey( "shortcut-test.find" ) );
	}
}
