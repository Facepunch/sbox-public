using System;
using Sandbox.Internal;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins how deserialization behaves with bad input: a prefab instance referencing
/// a missing prefab warns and degrades instead of crashing.
/// </summary>
[TestClass]
public class SerializeRobustnessTest
{

	/// <summary>
	/// A prefab instance referencing a prefab that doesn't exist warns and degrades
	/// instead of throwing.
	/// </summary>
	[TestMethod]
	public void MissingPrefabWarnsInsteadOfThrowing()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var json = Json.ParseToJsonObject( $$"""
			{
				"__guid": "{{Guid.NewGuid()}}",
				"Name": "Ghost",
				"__Prefab": "prefabs/does_not_exist.prefab"
			}
			""" );

		var go = new GameObject();
		go.Deserialize( json );

		Assert.IsTrue( go.IsValid() );
	}
}
