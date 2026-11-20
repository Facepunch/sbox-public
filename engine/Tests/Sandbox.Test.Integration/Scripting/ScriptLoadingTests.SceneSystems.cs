using System.Collections.Generic;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// A script class can derive from GameObjectSystem by passing the scene to the base constructor.
	/// Scenes create it like any other system, and its hooks run with the scene.
	/// </summary>
	[TestMethod]
	public void ScriptSceneSystemsAreCreatedWithTheirScene()
	{
		const string source = """
			public class CountingSystem : GameObjectSystem
			{
				public int Ticks;

				public CountingSystem( Scene scene ) : base( scene )
				{
					Listen( GameObjectSystem.Stage.StartUpdate, 0, Tick, "Counting" );
				}

				void Tick() { Ticks++; }
			}
			""";
		var loaded = Game.Scripting.LoadFiles( new Dictionary<string, string> { ["counting.csx"] = source } );
		Assert.IsTrue( loaded.Success, string.Join( "\n", loaded.Diagnostics ) );

		scene = new Scene();
		using var scope = scene.Push();
		var system = scene.GetSystemByType( Game.TypeLibrary.GetType( "CountingSystem" ) );
		Assert.IsNotNull( system, "The scene must create script systems along with compiled ones." );
		Assert.AreSame( scene, system.Scene );

		scene.GameTick();
		scene.GameTick();
		Assert.AreEqual( 2, system.GetType().GetField( "Ticks" ).GetValue( system ) );
	}
}
