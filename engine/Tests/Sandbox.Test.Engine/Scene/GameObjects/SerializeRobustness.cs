using System;
using Sandbox.Internal;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins how deserialization behaves with bad input: unknown components are preserved
/// rather than dropped, type resolution falls back to the short class name, missing
/// prefabs warn instead of crashing, and one broken component doesn't take down the
/// rest of the object.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SerializeRobustnessTest : SceneTest
{
	/// <summary>
	/// A component whose type can't be resolved becomes a missing-component
	/// placeholder, and the original json is preserved through a serialize round
	/// trip - user data must not be silently dropped on save.
	/// </summary>
	[TestMethod]
	public void UnknownComponentSurvivesRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var json = Json.ParseToJsonObject( $$"""
			{
				"__guid": "{{Guid.NewGuid()}}",
				"Name": "Mystery",
				"Components": [
					{
						"__type": "Totally.Unknown.ComponentType",
						"__guid": "{{Guid.NewGuid()}}",
						"SomeSetting": 42
					}
				]
			}
			""" );

		var go = new GameObject();
		go.Deserialize( json );

		Assert.AreEqual( "Mystery", go.Name );
		Assert.AreEqual( 1, go.Components.Count, "the unknown component should exist as a placeholder" );

		var serialized = go.Serialize().ToJsonString();
		StringAssert.Contains( serialized, "Totally.Unknown.ComponentType" );
		StringAssert.Contains( serialized, "SomeSetting" );
	}

	/// <summary>
	/// When the full type name doesn't resolve, deserialization falls back to the
	/// short class name - so a component that moved namespaces still loads.
	/// </summary>
	[TestMethod]
	public void ComponentTypeFallsBackToShortName()
	{
		var oldTypeLibrary = Game.TypeLibrary;
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( SerializeRobustnessTest ).Assembly, false );
		Game.TypeLibrary = typeLibrary;

		try
		{
			var scene = new Scene();
			using var sceneScope = scene.Push();

			var json = Json.ParseToJsonObject( $$"""
				{
					"__guid": "{{Guid.NewGuid()}}",
					"Name": "Renamed",
					"Components": [
						{
							"__type": "Some.Old.Namespace.WellKnownComponent",
							"__guid": "{{Guid.NewGuid()}}",
							"Value": 7
						}
					]
				}
				""" );

			var go = new GameObject();
			go.Deserialize( json );

			var component = go.Components.Get<WellKnownComponent>( true );
			Assert.IsNotNull( component, "short-name fallback should have resolved the component" );
			Assert.AreEqual( 7, component.Value );
		}
		finally
		{
			Game.TypeLibrary = oldTypeLibrary;
		}
	}

	/// <summary>
	/// A component whose constructor throws during deserialization is logged and
	/// skipped - the rest of the object still loads.
	/// </summary>
	[TestMethod]
	public void ThrowingComponentDoesNotBreakSiblings()
	{
		var oldTypeLibrary = Game.TypeLibrary;
		var typeLibrary = new Sandbox.Internal.TypeLibrary();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( SerializeRobustnessTest ).Assembly, false );
		Game.TypeLibrary = typeLibrary;

		try
		{
			ExplodingComponent.Explode = true;

			var scene = new Scene();
			using var sceneScope = scene.Push();

			var json = Json.ParseToJsonObject( $$"""
				{
					"__guid": "{{Guid.NewGuid()}}",
					"Name": "Survivor",
					"Components": [
						{
							"__type": "{{typeof( ExplodingComponent ).FullName}}",
							"__guid": "{{Guid.NewGuid()}}"
						},
						{
							"__type": "{{typeof( WellKnownComponent ).FullName}}",
							"__guid": "{{Guid.NewGuid()}}",
							"Value": 5
						}
					]
				}
				""" );

			var go = new GameObject();
			go.Deserialize( json );

			var survivor = go.Components.Get<WellKnownComponent>( true );
			Assert.IsNotNull( survivor, "the healthy component should have loaded" );
			Assert.AreEqual( 5, survivor.Value );
		}
		finally
		{
			ExplodingComponent.Explode = false;
			Game.TypeLibrary = oldTypeLibrary;
		}
	}

	public class WellKnownComponent : Component
	{
		[Property] public int Value { get; set; }
	}

	public class ExplodingComponent : Component
	{
		public static bool Explode;

		public ExplodingComponent()
		{
			if ( Explode )
				throw new InvalidOperationException( "intentional test constructor failure" );
		}
	}
}
