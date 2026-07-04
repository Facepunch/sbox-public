using System.Text.Json.Nodes;
using Sandbox.Internal;

namespace SceneTests.Core;

/// <summary>
/// Pins how per-scene GameObjectSystem property overrides apply: matching systems get
/// their properties set, unknown systems and malformed json warn instead of crashing,
/// and one bad property doesn't stop the others.
/// </summary>
[TestClass]
public class SceneSystemOverridesTest
{
	/// <summary>
	/// Runs the body with a TypeLibrary containing the test assembly, so the test
	/// system is instantiated for the scene. The scene is destroyed afterwards so
	/// its hooks don't leak - the same convention as GameObjectSystemTests.cs.
	/// </summary>
	static void WithTestSystems( System.Action<Scene> body )
	{
		var oldTypeLibrary = Game.TypeLibrary;
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( SceneSystemOverridesTest ).Assembly, false );
		Game.TypeLibrary = typeLibrary;

		Scene scene = null;

		try
		{
			scene = new Scene();
			using var sceneScope = scene.Push();
			body( scene );
		}
		finally
		{
			scene?.Destroy();
			Game.TypeLibrary = oldTypeLibrary;
		}
	}

	/// <summary>
	/// A valid override sets the system's property.
	/// </summary>
	[TestMethod]
	public void OverrideApplies()
	{
		WithTestSystems( scene =>
		{
			var system = scene.GetSystem<OverridableSystem>();
			Assert.IsNotNull( system );
			Assert.AreEqual( 0, system.Speed );

			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			scene.ApplyGameObjectSystemOverrides( node );

			Assert.AreEqual( 42, system.Speed );
		} );
	}

	/// <summary>
	/// Unknown system names are skipped, and a bad value for one property doesn't
	/// stop a good value for another.
	/// </summary>
	[TestMethod]
	public void BadEntriesAreSkipped()
	{
		WithTestSystems( scene =>
		{
			var system = scene.GetSystem<OverridableSystem>();

			var node = Json.ParseToJsonObject( $$"""
				{
					"Some.System.That.Does.Not.Exist": { "Whatever": 1 },
					"{{typeof( OverridableSystem ).FullName}}": { "Speed": "not a number", "Title": "applied" }
				}
				""" );

			scene.ApplyGameObjectSystemOverrides( node );

			Assert.AreEqual( 0, system.Speed, "the malformed value should be skipped" );
			Assert.AreEqual( "applied", system.Title, "the valid sibling property should still apply" );
		} );
	}

	/// <summary>
	/// Entirely malformed override json warns instead of throwing.
	/// </summary>
	[TestMethod]
	public void MalformedOverridesNodeIsIgnored()
	{
		WithTestSystems( scene =>
		{
			scene.ApplyGameObjectSystemOverrides( JsonValue.Create( "this is not an object" ) );
			scene.ApplyGameObjectSystemOverrides( null );
		} );
	}

	/// <summary>
	/// A property-holding scene system used to test overrides. Inert - it has no
	/// behavior, so its presence in other tests' scenes is harmless.
	/// </summary>
	public class OverridableSystem : GameObjectSystem
	{
		[Property] public int Speed { get; set; }
		[Property] public string Title { get; set; }

		public OverridableSystem( Scene scene ) : base( scene )
		{
		}
	}
}
