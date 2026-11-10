namespace Sandbox.Physics;

[Expose]
public class PhysicsSettings : ConfigData
{
	/// <summary>
	/// If false, then instead of operating physics, and UpdateFixed in a fixed update frequency
	/// they will be called the same as Update - every frame, with a variable time delta.
	/// </summary>
	public bool UseFixedUpdate { get; set; } = true;

	/// <summary>
	/// If you're seeing objects go through other objects or you have a low tickrate, you might want to increase the number of physics substeps.
	/// This breaks physics steps down into this many substeps. The default is 1 and works pretty good.
	/// Be aware that the number of physics ticks per second is going to be tickrate * substeps.
	/// So if you're ticking at 90 and you have SubSteps set to 1000 then you're going to do 90,000 steps per second. So be careful here.
	/// </summary>
	public int SubSteps { get; set; } = 1;

	/// <summary>
	/// How many times a second FixedUpdate runs
	/// </summary>
	public float FixedUpdateFrequency { get; set; } = 50.0f;

	/// <summary>
	/// If the frame took longer than a FixedUpdate step, we need to run multiple
	/// steps for that frame, to catch up. How many are allowed? Too few, and the 
	/// simluation will run slower than the game. If you allow an unlimited amount
	/// then the frame time could snowball to infinity and never catch up.
	/// </summary>
	public int MaxFixedUpdates { get; set; } = 5;
}
