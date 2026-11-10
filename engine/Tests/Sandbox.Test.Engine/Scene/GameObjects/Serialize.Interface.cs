using Sandbox.Internal;
using SceneTests;

namespace SceneTests.GameObjects;

public class Damage : Component
{
	[Property] public IDamageable Damageable { get; set; }
}

[TestClass]
[DoNotParallelize]
public sealed class InterfaceTest : SceneTest
{
	[TestMethod]
	public void DeserializeInterfaces()
	{
		// note: the test assembly is already registered (as dynamic) in the global
		// TypeLibrary by the assembly init - re-adding it as non-dynamic here would
		// downgrade member discovery for every test that runs after this one

		var scene = Helpers.LoadSceneFromJson( "example.scene",
			"""
			{
				"__guid": "86b89011-9646-4ee7-ad30-c0e11d258674",
				"Name": "My Object",
				"Enabled": true,
				"NetworkMode": 1,
				"Components": [
				  {
					"__type": "Prop",
					"__guid": "3a83b805-bb2c-47ac-9bfd-d5f7a4af7853",
					"__enabled": true
				  },
				  {
					"__type": "Damage",
					"__guid": "9f2a61a8-21b0-487a-8c33-96c8df753cac",
					"__enabled": true,
					"Damageable": {
					  "Type": "Component",
					  "Value": {
						"_type": "component",
						"component_id": "3a83b805-bb2c-47ac-9bfd-d5f7a4af7853",
						"go": "5499ad65-e791-49ec-9f33-fd30cb70e758",
						"component_type": "Prop"
					  }
					}
				  }
				]
			}
			""" );

		using var scope = scene.Push();

		var damage = scene.Get<Damage>();
		var prop = scene.Get<Prop>();

		Assert.IsTrue( damage.Damageable is not null, "Damage damageable is invalid" );
		Assert.AreSame( prop, damage.Damageable, "Damageable should have resolved to the Prop component instance" );
	}
}
