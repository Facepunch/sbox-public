using Sandbox.Engine;
using Sandbox.Internal;

namespace MountingTests;

[Library]
public sealed class TestMountComponent : Component
{
}

/// <summary>
/// Mount assemblies loaded outside the package system have their types enrolled in the menu and game
/// type libraries, and keep them when a library is rebuilt.
/// </summary>
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
	}

	[TestCleanup]
	public void Cleanup()
	{
		Sandbox.Mounting.Directory.RemoveTypeAssembly( typeof( TestMountComponent ).Assembly );

		GlobalContext.Game = _previousGame;
		GlobalContext.Menu = _previousMenu;
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

		// What Game.InitTypeLibrary does for a rebuilt library
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
}
