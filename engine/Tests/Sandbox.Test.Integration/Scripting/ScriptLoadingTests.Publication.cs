using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Game scripts register in the game and editor catalogs and supply fresh local stores for each load.
	/// </summary>
	[TestMethod]
	public void ScriptPublicationUsesTheOwningCatalog()
	{
		var scripting = context.Scripting;
		const string source = "class CallbackType {} [Local] int Count = 0; Count++; [Global] int Latest = 0; Latest = Count;";
		Assert.IsTrue( scripting.Load( source ), scripting.LastError?.ToString() );
		Assert.IsTrue( scripting.Load( source ), scripting.LastError?.ToString() );
		Assert.AreEqual( 1, scripting.Globals.Get<int>( "Latest" ) );
		Assert.IsNotNull( context.TypeLibrary.GetType( "CallbackType" ) );
		Assert.IsNotNull( TypeLibrary.Editor.GetType( "CallbackType" ) );
		context.Reset();
		Assert.IsNull( context.TypeLibrary.GetType( "CallbackType" ) );
		Assert.IsNull( TypeLibrary.Editor.GetType( "CallbackType" ) );
	}

	/// <summary>
	/// Registers the host callback probe in both catalogs and clears its counters.
	/// </summary>
	private void AddRegistrationProbes()
	{
		ScriptRegistrationProbeAttribute.Unregistrations = 0;
		ScriptRegistrationProbeAttribute.FailRegistration = false;
		context.TypeLibrary.AddAssembly( typeof( ScriptRegistrationProbeAttribute ).Assembly, false );
		TypeLibrary.Editor.AddAssembly( typeof( ScriptRegistrationProbeAttribute ).Assembly, false );
	}

	/// <summary>
	/// Registration callback failures stop script dispatch until reset and leave no script types after cleanup.
	/// </summary>
	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void RegistrationFailureRequiresContextReset( bool reload )
	{
		AddRegistrationProbes();
		const string source = "[ScriptRegistrationProbe] class NotificationComponent : Component { } [Global] int Started = 0; Started++;";
		var scripting = Game.Scripting;
		if ( reload )
		{
			Assert.IsTrue( scripting.Load( source, "notification.csx" ), scripting.LastError?.ToString() );
		}

		ScriptRegistrationProbeAttribute.FailRegistration = true;
		var replacement = reload ? source.Replace( "Component { }", "Component { public int Added; }" ) : source;
		Assert.IsFalse( scripting.Load( replacement, "notification.csx" ) );
		StringAssert.Contains( scripting.LastError.Message, "Registration probe failure." );
		Assert.ThrowsException<InvalidOperationException>( () => scripting.Run<int>( "return 42;" ) );
		Assert.ThrowsException<InvalidOperationException>( () => scripting.Load( source, "notification.csx" ) );

		context.Reset();
		Assert.IsNull( Game.TypeLibrary.GetType( "NotificationComponent" ) );
		Assert.IsNull( TypeLibrary.Editor.GetType( "NotificationComponent" ) );
		Assert.AreEqual( reload ? 3 : 1, ScriptRegistrationProbeAttribute.Unregistrations );

		ScriptRegistrationProbeAttribute.FailRegistration = false;
		Assert.IsTrue( Game.Scripting.Load( source, "notification.csx" ), Game.Scripting.LastError?.ToString() );
		Assert.AreEqual( 1, Game.Scripting.Globals.Get<int>( "Started" ) );
	}

	/// <summary>
	/// A name occupied only in the editor rejects publication without replacing or unregistering that editor type.
	/// </summary>
	[TestMethod]
	public void EditorOnlyTypeConflictsAreRejectedBeforePublication()
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "EditorScriptCollisionFixture" ), AssemblyBuilderAccess.RunAndCollect );
		var occupiedType = assembly.DefineDynamicModule( "Fixture" ).DefineType( "EditorOnlyScriptComponent", TypeAttributes.Public ).CreateType();
		TypeLibrary.Editor.AddAssembly( assembly, true );
		var original = TypeLibrary.Editor.GetType( occupiedType );

		files.WriteAllText( "collision.csx", "class EditorOnlyScriptComponent : Component { }" );
		Assert.IsFalse( Game.Scripting.Load( files.ReadAllText( "collision.csx" ), "collision.csx" ) );
		var resource = Game.Scripting;
		Assert.IsNotNull( resource.LastError );
		Assert.IsNull( Game.TypeLibrary.GetType( "EditorOnlyScriptComponent" ) );
		Assert.AreSame( original, TypeLibrary.Editor.GetType( "EditorOnlyScriptComponent" ) );
		context.Reset();
		Assert.AreSame( original, TypeLibrary.Editor.GetType( "EditorOnlyScriptComponent" ) );
		Assert.IsTrue( original.IsValid );
	}

	/// <summary>
	/// Serializer caches use current CLR properties and catalog identities survive a context restart.
	/// </summary>
	[TestMethod]
	public void BytePackUsesReloadedMembersAndStableTypeIdentities()
	{
		const string source = "class ScriptData { public int Value { get; set; } = 3; }";
		LoadSource( "data.csx", source );
		var type = Game.TypeLibrary.GetType( "ScriptData" );
		var instance = type.Create<object>();
		var roots = reloadRoots = new object[] { instance };
		var initial = Game.TypeLibrary.ToBytes( instance );
		Assert.AreEqual( 3, type.GetValue( Game.TypeLibrary.FromBytes<object>( initial ), "Value" ) );

		const string edited = "class ScriptData { public int Value { get; set; } = 9; public string Added { get; set; } = \"new\"; }";
		LoadSource( "data.csx", edited );
		var bytes = Game.TypeLibrary.ToBytes( roots[0] );
		var restored = Game.TypeLibrary.FromBytes<object>( bytes );
		Assert.AreEqual( 3, type.GetValue( restored, "Value" ) );
		Assert.AreEqual( "new", type.GetValue( restored, "Added" ) );
		var identity = type.Identity;

		context.Reset();
		LoadSource( "data.csx", edited );
		var replacement = Game.TypeLibrary.GetType( "ScriptData" );
		Assert.AreEqual( identity, replacement.Identity );
		var persisted = Game.TypeLibrary.FromBytes<object>( bytes );
		Assert.AreEqual( 3, replacement.GetValue( persisted, "Value" ) );
		Assert.AreEqual( "new", replacement.GetValue( persisted, "Added" ) );
	}
}
