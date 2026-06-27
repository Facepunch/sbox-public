using System.Text.Json.Nodes;
using Sandbox.Internal;

namespace SceneTests.Core;

/// <summary>
/// Pins how per-scene GameObjectSystem property overrides apply: matching systems get
/// their properties set, unknown systems and malformed json warn instead of crashing,
/// and one bad property doesn't stop the others. Also covers transient overrides (e.g.
/// applied by a MapInstance): they revert on dispose and are excluded from the host
/// scene's serialization so another source's data isn't baked in.
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
	/// A transient override (e.g. applied by a MapInstance) reverts to the scene's own value
	/// when its scope is disposed.
	/// </summary>
	[TestMethod]
	public void TransientOverrideRevertsOnDispose()
	{
		WithTestSystems( scene =>
		{
			var system = scene.GetSystem<OverridableSystem>();
			Assert.AreEqual( 0, system.Speed );

			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			using ( scene.ApplyGameObjectSystemOverrides( node, transient: true ) )
			{
				Assert.AreEqual( 42, system.Speed, "the transient override should apply while in scope" );
			}

			Assert.AreEqual( 0, system.Speed, "the transient override should revert once disposed" );
		} );
	}

	/// <summary>
	/// A transient override is excluded from the scene's own serialization, so loading a map's
	/// systems through a MapInstance doesn't bake that data into the host scene.
	/// </summary>
	[TestMethod]
	public void TransientOverrideIsNotSerialized()
	{
		WithTestSystems( scene =>
		{
			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			using var scope = scene.ApplyGameObjectSystemOverrides( node, transient: true );

			Assert.AreEqual( 42, scene.GetSystem<OverridableSystem>().Speed, "the override is live in the scene" );
			Assert.IsNull( SerializedSystemSpeed( scene ), "but a transient override must not be written into GameObjectSystems" );
		} );
	}

	/// <summary>
	/// A non-transient override (the scene's own systems) is serialized normally.
	/// </summary>
	[TestMethod]
	public void NonTransientOverrideIsSerialized()
	{
		WithTestSystems( scene =>
		{
			var node = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			scene.ApplyGameObjectSystemOverrides( node );

			Assert.AreEqual( 42, (int)SerializedSystemSpeed( scene ), "a non-transient override should be serialized" );
		} );
	}

	/// <summary>
	/// When a transient override masks a value the scene already owns, serialization keeps the
	/// scene's own (pre-transient) value rather than the transient overlay - and disposing the
	/// overlay leaves the scene's own value intact.
	/// </summary>
	[TestMethod]
	public void TransientOverridePreservesSceneOwnValue()
	{
		WithTestSystems( scene =>
		{
			var ownNode = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 7 } }
				""" );
			scene.ApplyGameObjectSystemOverrides( ownNode );

			var mapNode = Json.ParseToJsonObject( $$"""
				{ "{{typeof( OverridableSystem ).FullName}}": { "Speed": 42 } }
				""" );

			using ( scene.ApplyGameObjectSystemOverrides( mapNode, transient: true ) )
			{
				Assert.AreEqual( 42, scene.GetSystem<OverridableSystem>().Speed, "the transient overlay is live while in scope" );
				Assert.AreEqual( 7, (int)SerializedSystemSpeed( scene ), "serialization keeps the scene's own value, not the overlay" );
			}

			Assert.AreEqual( 7, scene.GetSystem<OverridableSystem>().Speed, "after dispose the scene's own value remains" );
			Assert.AreEqual( 7, (int)SerializedSystemSpeed( scene ) );
		} );
	}

	/// <summary>
	/// Reads the serialized value of <see cref="OverridableSystem.Speed"/> out of the scene's
	/// GameObjectSystems block, or null if it wasn't serialized.
	/// </summary>
	static JsonNode SerializedSystemSpeed( Scene scene )
	{
		if ( scene.SerializeProperties()["GameObjectSystems"] is not JsonObject systems )
			return null;

		if ( systems[typeof( OverridableSystem ).FullName] is not JsonObject props )
			return null;

		return props["Speed"];
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
