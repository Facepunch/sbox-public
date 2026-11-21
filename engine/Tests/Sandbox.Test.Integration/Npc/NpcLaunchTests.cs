using Sandbox.Npcs;
using Sandbox.Npcs.Layers;

namespace NpcTests;

[TestClass]
public class NpcLaunchTests
{
	[TestMethod]
	public void ACloneCanBeLaunchedBeforeItsFirstFrame()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var source = scene.CreateObject();
		source.Enabled = false;
		source.Components.Create<BoxCollider>().Scale = new Vector3( 16 );
		source.Components.Create<Rigidbody>().Gravity = false;
		source.Components.Create<AnimationLayer>();
		source.Components.Create<BaseNpc>();

		var clone = source.Clone( new CloneConfig { StartEnabled = true } );
		var npc = clone.GetComponent<BaseNpc>();
		var body = clone.GetComponent<Rigidbody>();
		var velocity = new Vector3( 400, 0, 200 );

		npc.Launch( velocity );

		Assert.IsTrue( body.MotionEnabled );
		Assert.IsTrue( body.Velocity.Distance( velocity ) < 0.01f, $"Launch velocity was {body.Velocity}" );
	}
}
