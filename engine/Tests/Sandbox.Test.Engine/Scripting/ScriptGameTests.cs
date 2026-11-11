using Sandbox.Engine;
using System.Collections.Generic;
using System.Reflection;

namespace ScriptingTests;

/// <summary>
/// Game uses the same exposed catalog for script execution and discovery.
/// </summary>
[TestClass, DoNotParallelize]
public class ScriptGameTests : SceneTests.SceneTest
{
	private GlobalContext previous;
	private GlobalContext previousGame;
	private Scene scene;

	/// <summary>
	/// Creates an isolated game context and scene without booting the engine loop.
	/// </summary>
	[TestInitialize]
	public void Initialize()
	{
		previous = GlobalContext.Current;
		previousGame = GlobalContext.Game;
		GlobalContext.Current = GlobalContext.Game = new GlobalContext { TypeLibrary = SharedTypeLibrary.ScriptingWithSystem };
		scene = new Scene();
		Game.ActiveScene = scene;
	}

	/// <summary>
	/// Releases the test scene and restores the previous context.
	/// </summary>
	[TestCleanup]
	public void Cleanup()
	{
		scene.Destroy();
		GlobalContext.Current.Shutdown();
		GlobalContext.Current = previous;
		GlobalContext.Game = previousGame;
	}

	/// <summary>
	/// Simple and qualified names resolve to the same registered public API.
	/// </summary>
	[TestMethod]
	public void GameIsRegisteredAndDiscoverable()
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		Assert.AreSame( typeof( Game ), resolver.ResolveType( "Game" ) );
		Assert.AreSame( typeof( Game ), resolver.ResolveType( "Sandbox.Game" ) );
		Assert.IsTrue( resolver.GetTypes().Any( type => type.Type == typeof( Game ) ) );

		var members = new List<MemberInfo>();
		resolver.GetMembers( typeof( Game ), members );
		Assert.IsTrue( members.OfType<PropertyInfo>().Any( property => property.Name == nameof( Game.ActiveScene ) ) );
		Assert.IsTrue( members.OfType<MethodInfo>().Any( method => method.Name == nameof( Game.Load ) ) );
		Assert.IsTrue( members.OfType<MethodInfo>().Any( method => method.Name == nameof( Game.Close ) ) );
		Assert.IsTrue( members.OfType<PropertyInfo>().Any( property => property.Name == nameof( Game.CheatsEnabled ) && property.SetMethod.IsPublic ) );
		Assert.IsFalse( members.Any( member => member.Name == "InputContext" || member.Name == "InitHost" ) );

		var types = Game.Scripting.Analyze( "Gam" ).GetCompletions( 3 );
		Assert.IsTrue( types.Items.Any( item => item.Label == "Game" && item.Kind == Script.SymbolKind.Type ) );

		foreach ( var source in new[] { "Game.", "Sandbox.Game." } )
		{
			var completions = Game.Scripting.Analyze( source ).GetCompletions( source.Length );
			Assert.IsTrue( completions.Items.Any( item => item.Label == "ActiveScene" ), source );
			Assert.IsFalse( completions.Items.Any( item => item.Label == "InputContext" || item.Label == "InitHost" ), source );
		}
	}

	/// <summary>
	/// Exposing Game must not make private implementation types constructible or discoverable.
	/// </summary>
	[TestMethod]
	public void PrivateGameImplementationTypesRemainUnavailable()
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		Assert.IsNull( resolver.ResolveType( "DuckHandle" ) );
		Assert.IsNull( resolver.ResolveType( "Sandbox.Game.Music.DuckHandle" ) );
		Assert.IsFalse( resolver.GetTypes().Any( type => type.Type.FullName == "Sandbox.Game+Music+DuckHandle" ) );
	}

	/// <summary>
	/// Public nested Game APIs inherit registration and resolve with their C# names.
	/// </summary>
	[TestMethod]
	public void PublicNestedGameApisRemainAvailable()
	{
		var resolver = new TypeLibraryScriptResolver( Game.TypeLibrary );
		Assert.AreSame( typeof( Game.Music ), resolver.ResolveType( "Game.Music" ) );
		Assert.AreSame( typeof( Game.Overlay ), resolver.ResolveType( "Sandbox.Game.Overlay" ) );
	}

	/// <summary>
	/// Both spellings can read the active scene and call its instance API.
	/// </summary>
	[TestMethod]
	[DataRow( "Game" )]
	[DataRow( "Sandbox.Game" )]
	public void ScriptsCanAccessTheActiveScene( string gameName )
	{
		var activeScene = Game.Scripting.CreateScript( $"return {gameName}.ActiveScene;" );
		Assert.AreSame( scene, activeScene.RunAndReturn<Scene>() );
		Assert.IsNull( activeScene.LastError );

		var camera = Game.Scripting.CreateScript( $"var cam = {gameName}.ActiveScene.Camera; return cam;" );
		Assert.IsTrue( camera.Run(), camera.LastError?.Message );
		Assert.IsNull( camera.LastError );

		var components = Game.Scripting.CreateScript( $"return {gameName}.ActiveScene.GetAllComponents<CameraComponent>();" );
		CollectionAssert.AreEqual( scene.GetAllComponents<CameraComponent>().ToArray(), components.RunAndReturn<IEnumerable<CameraComponent>>().ToArray() );
		Assert.IsNull( components.LastError );
	}

	/// <summary>
	/// A game script cannot accidentally read a different current context's scene.
	/// </summary>
	[TestMethod]
	public void ActiveSceneAccessRequiresTheOwningContext()
	{
		var script = Game.Scripting.CreateScript( "return Game.ActiveScene;" );
		var owner = GlobalContext.Current;
		var other = new GlobalContext { TypeLibrary = Game.TypeLibrary };
		try
		{
			GlobalContext.Current = other;
			Assert.IsFalse( script.Run() );
			StringAssert.Contains( script.LastError.Message, "owning game context" );
		}
		finally
		{
			other.Shutdown();
			GlobalContext.Current = owner;
		}

		Assert.AreSame( scene, script.RunAndReturn<Scene>() );
		Assert.IsNull( script.LastError );
	}

	/// <summary>
	/// Registration does not grant access to internal members, nonpublic setters or instance APIs as statics.
	/// </summary>
	[TestMethod]
	[DataRow( "Game.ActiveScene = null;" )]
	[DataRow( "Sandbox.Game.ActiveScene = null;" )]
	[DataRow( "Game.IsClosing = true;" )]
	[DataRow( "return Game.InputContext;" )]
	[DataRow( "Game.InitHost();" )]
	[DataRow( "return new Sandbox.Game.Music.DuckHandle(0.5f, 1.0f);" )]
	[DataRow( "return Scene.Camera;" )]
	[DataRow( "return Scene.GetAllComponents<CameraComponent>();" )]
	public void ScriptsCannotBypassMemberAccess( string source )
	{
		var script = Game.Scripting.CreateScript( source );
		Assert.IsFalse( script.Run(), source );
		Assert.IsNotNull( script.LastError );
		Assert.AreSame( scene, Game.ActiveScene );
	}
}
