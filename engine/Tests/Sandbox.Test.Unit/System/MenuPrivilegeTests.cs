using Microsoft.AspNetCore.Components;
using Sandbox.Engine;
using Sandbox.Internal;
using System;

namespace SystemTests;

[TestClass]
[DoNotParallelize]
public class MenuPrivilegeTests
{
	static bool RanProtectedCommand;

	TypeLibrary gameLibrary;
	TypeLibrary menuLibrary;
	TypeLibrary previousGameLibrary;
	TypeLibrary previousMenuLibrary;

	[TestInitialize]
	public void Setup()
	{
		ThreadSafe.MarkMainThread();

		previousGameLibrary = GlobalContext.Game.TypeLibrary;
		previousMenuLibrary = GlobalContext.Menu.TypeLibrary;

		gameLibrary = new TypeLibrary();
		menuLibrary = new TypeLibrary();

		GlobalContext.Game.TypeLibrary = gameLibrary;
		GlobalContext.Menu.TypeLibrary = menuLibrary;

		RanProtectedCommand = false;
		ConVarSystem.Members["menu_guard_protected"] = new ProtectedCommand();
	}

	[TestCleanup]
	public void Cleanup()
	{
		ConVarSystem.Members.Remove( "menu_guard_protected" );

		GlobalContext.Game.TypeLibrary = previousGameLibrary;
		GlobalContext.Menu.TypeLibrary = previousMenuLibrary;
	}

	[TestMethod]
	public void PublicReflectionAlwaysMeansTheGame()
	{
		menuLibrary.AddAssembly( typeof( MenuOnlyRoute ).Assembly, false );

		using var scope = GlobalContext.MenuScope();

		Assert.AreSame( gameLibrary, Game.TypeLibrary );
		Assert.AreSame( gameLibrary, GlobalGameNamespace.TypeLibrary );
		Assert.AreSame( GlobalContext.Game.Scripting, Game.Scripting );
		Assert.IsNull( RouteAttribute.FindValidTarget( "/menu-only-route", null ), "Public route lookup returned a menu type" );
		Assert.IsNotNull( RouteAttribute.FindValidTarget( GlobalContext.Current.TypeLibrary, "/menu-only-route", null ) );
	}

	[TestMethod]
	public void PublicConsoleNeverRunsProtectedCommands()
	{
		using ( GlobalContext.MenuScope() )
		{
			Assert.ThrowsException<Exception>( () => ConsoleSystem.Run( "menu_guard_protected" ) );
			Assert.IsFalse( RanProtectedCommand );
		}

		ConsoleSystem.RunCommand( "menu_guard_protected", null, true );
		Assert.IsTrue( RanProtectedCommand, "The menu's console lost its rights" );
	}

	[TestMethod]
	public void GameWebSurfacesAreLimited()
	{
		using var scope = GlobalContext.MenuScope();
		using var surface = Game.CreateWebSurface();
		var panel = new Sandbox.UI.WebPanel();

		try
		{
			Assert.IsTrue( surface.IsLimited );
			Assert.IsTrue( panel.Surface.IsLimited );
		}
		finally
		{
			panel.Delete( true );
		}
	}

	[Expose, Route( "/menu-only-route" )]
	public sealed class MenuOnlyRoute
	{
	}

	class ProtectedCommand : Command
	{
		public ProtectedCommand()
		{
			Name = "menu_guard_protected";
			IsConCommand = true;
			IsProtected = true;
		}

		public override void Run( string args )
		{
			RanProtectedCommand = true;
		}
	}
}
