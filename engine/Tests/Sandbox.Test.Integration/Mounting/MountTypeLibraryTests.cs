using Sandbox.Engine;
using Sandbox.Internal;
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json.Nodes;

namespace MountingTests;

[Library]
public sealed class TestMountComponent : Component
{
	[JsonUpgrader( typeof( TestMountComponent ), 1 )]
	public static void Upgrader_v1( JsonObject json )
	{
	}
}

/// <summary>
/// Carries no attribute that would expose it, like a mount's file readers and other internals.
/// </summary>
public enum TestMountEnum
{
	Value
}

[TestClass]
public class MountTypeLibraryTests
{
	GlobalContext _previousGame;
	GlobalContext _previousMenu;

	[TestInitialize]
	public void Setup()
	{
		_previousGame = GlobalContext.Game;
		_previousMenu = GlobalContext.Menu;

		// Fresh libraries that don't already hold the test assembly
		GlobalContext.Game = new GlobalContext { TypeLibrary = new TypeLibrary() };
		GlobalContext.Menu = new GlobalContext { TypeLibrary = new TypeLibrary() };

		JsonUpgrader.UpdateUpgraders( GlobalContext.Game.TypeLibrary );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Sandbox.Mounting.Directory.RemoveTypeAssembly( typeof( TestMountComponent ).Assembly );

		GlobalContext.Game = _previousGame;
		GlobalContext.Menu = _previousMenu;

		// The upgraders were taken from the libraries above
		if ( _previousGame?.TypeLibrary is { } library )
			JsonUpgrader.UpdateUpgraders( library );
	}

	[TestMethod]
	public void EditorMountRegistrationExposesPrefabComponentsWithoutAGameDependency()
	{
		var builder = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( $"EditorMountTypes{Guid.NewGuid():N}" ), AssemblyBuilderAccess.Run );
		var module = builder.DefineDynamicModule( "main" );
		var mount = module.DefineType( $"EditorMount{Guid.NewGuid():N}", TypeAttributes.Public | TypeAttributes.Class, typeof( TestConsoleMount ) );
		mount.DefineDefaultConstructor( MethodAttributes.Public );
		mount.CreateType();
		var component = module.DefineType( "EditorMountComponent", TypeAttributes.Public | TypeAttributes.Class, typeof( Component ) );
		component.DefineDefaultConstructor( MethodAttributes.Public );
		component.SetCustomAttribute( new CustomAttributeBuilder( typeof( LibraryAttribute ).GetConstructor( Type.EmptyTypes ), [] ) );
		var componentType = component.CreateType();
		var assembly = componentType.Assembly;

		try
		{
			// The same registration used by ToolsDll's package enroller, without a game package dependency.
			Sandbox.Mounting.Directory.AddAssembly( assembly, exposeMountTypes: true );
			Assert.IsNotNull( GlobalContext.Game.TypeLibrary.GetType( componentType ) );
			Assert.IsNotNull( GlobalContext.Menu.TypeLibrary.GetType( componentType ) );

			var rebuilt = new TypeLibrary();
			Sandbox.Mounting.Directory.AddTypesTo( rebuilt );
			Assert.IsNotNull( rebuilt.GetType( componentType ) );
		}
		finally
		{
			Sandbox.Mounting.Directory.RemoveAssembly( assembly );
		}

		Assert.IsNull( GlobalContext.Game.TypeLibrary.GetType( componentType ) );
	}

	[TestMethod]
	public void MountTypesSurviveRebuildAndRemoval()
	{
		var assembly = typeof( TestMountComponent ).Assembly;
		var game = GlobalContext.Game.TypeLibrary;
		var menu = GlobalContext.Menu.TypeLibrary;

		Assert.IsNull( game.GetType( typeof( TestMountComponent ) ) );

		Sandbox.Mounting.Directory.AddTypeAssembly( assembly );

		Assert.IsNotNull( game.GetType( typeof( TestMountComponent ) ) );
		Assert.IsNotNull( menu.GetType( typeof( TestMountComponent ) ) );

		var rebuilt = new TypeLibrary();
		Sandbox.Mounting.Directory.AddTypesTo( rebuilt );
		Assert.IsNotNull( rebuilt.GetType( typeof( TestMountComponent ) ) );

		Sandbox.Mounting.Directory.RemoveTypeAssembly( assembly );

		Assert.IsNull( game.GetType( typeof( TestMountComponent ) ) );
		Assert.IsNull( menu.GetType( typeof( TestMountComponent ) ) );

		var afterRemoval = new TypeLibrary();
		Sandbox.Mounting.Directory.AddTypesTo( afterRemoval );
		Assert.IsNull( afterRemoval.GetType( typeof( TestMountComponent ) ) );
	}

	[TestMethod]
	public void TypesTheMountDoesNotExposeStayOut()
	{
		Sandbox.Mounting.Directory.AddTypeAssembly( typeof( TestMountEnum ).Assembly );

		Assert.IsNotNull( GlobalContext.Game.TypeLibrary.GetType( typeof( TestMountComponent ) ) );
		Assert.IsNull( GlobalContext.Game.TypeLibrary.GetType( typeof( TestMountEnum ) ) );
		Assert.IsNull( GlobalContext.Menu.TypeLibrary.GetType( typeof( TestMountEnum ) ) );

		var rebuilt = new TypeLibrary();
		Sandbox.Mounting.Directory.AddTypesTo( rebuilt );
		Assert.IsNull( rebuilt.GetType( typeof( TestMountEnum ) ) );
	}

	[TestMethod]
	public void UpgradersFollowTheMountTypes()
	{
		var assembly = typeof( TestMountComponent ).Assembly;

		Assert.IsFalse( Upgrades(), "No upgrader before the mount is enrolled" );

		Sandbox.Mounting.Directory.AddTypeAssembly( assembly );
		Assert.IsTrue( Upgrades(), "The mount's upgrader runs once it is enrolled" );

		Sandbox.Mounting.Directory.RemoveTypeAssembly( assembly );
		Assert.IsFalse( Upgrades(), "The upgrader is gone with the mount" );
	}

	static bool Upgrades()
	{
		var json = new JsonObject();
		JsonUpgrader.Upgrade( 0, json, typeof( TestMountComponent ) );
		return json.ContainsKey( "__version" );
	}
}
