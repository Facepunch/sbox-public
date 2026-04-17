using Sandbox;
using System.Linq;

namespace GameObjects;

[TestClass]
[DoNotParallelize]
public class ComponentEvents
{
	[TestMethod]
	public void Single()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var o = go.Components.Create<OrderTestComponent>();

		Assert.AreEqual( 1, o.AwakeCalls, "Awake wasn't called" );
		Assert.AreEqual( 1, o.EnabledCalls, "Enabled wasn't called" );
		Assert.AreEqual( 0, o.DisabledCalls, "Enabled wasn't called" );

		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		go.Enabled = false;
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 0, o.DestroyCalls );

		go.Destroy();
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 1, o.DestroyCalls );
	}

	[TestMethod]
	public void Single_StartComponentDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var o = go.Components.Create<OrderTestComponent>( false );

		Assert.AreEqual( 1, o.AwakeCalls );
		Assert.AreEqual( 0, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		o.Enabled = true;

		Assert.AreEqual( 1, o.AwakeCalls );
		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		go.Enabled = false;
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 0, o.DestroyCalls );

		go.Destroy();
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 1, o.DestroyCalls );
	}

	[TestMethod]
	public void Single_StartObjectDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject( false );
		var o = go.Components.Create<OrderTestComponent>();

		Assert.AreEqual( 0, o.AwakeCalls ); // awake shouldn't call until the gameobject is active
		Assert.AreEqual( 0, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		go.Enabled = true;

		Assert.AreEqual( 1, o.AwakeCalls );
		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		go.Enabled = false;
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 0, o.DestroyCalls );

		go.Destroy();
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 1, o.DestroyCalls );
	}

	[TestMethod]
	public void Single_Destroy()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var o = go.Components.Create<OrderTestComponent>();

		Assert.AreEqual( 1, o.AwakeCalls );
		Assert.AreEqual( 0, o.StartCalls );
		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		scene.GameTick();

		Assert.AreEqual( 1, o.StartCalls );
		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );
		Assert.AreEqual( 0, o.DestroyCalls );

		go.Destroy();
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 1, o.DestroyCalls );
	}
}

public class OrderTestComponent : Component
{
	public int AwakeCalls;
	public int EnabledCalls;
	public int StartCalls;
	public int DisabledCalls;
	public int DestroyCalls;

	protected override void OnAwake()
	{
		Assert.AreEqual( AwakeCalls, 0 );
		Assert.AreEqual( EnabledCalls, 0 );
		Assert.AreEqual( StartCalls, 0 );
		Assert.AreEqual( DisabledCalls, 0 );
		AwakeCalls++;
	}

	protected override void OnStart()
	{
		Assert.AreEqual( AwakeCalls, 1 );
		Assert.AreEqual( EnabledCalls, 1 );
		StartCalls++;
	}
	protected override void OnEnabled()
	{
		Assert.AreEqual( AwakeCalls, 1 );
		Assert.AreEqual( StartCalls, 0 );

		EnabledCalls++;
	}

	protected override void OnDisabled()
	{
		Assert.AreEqual( AwakeCalls, 1 );
		Assert.AreNotEqual( StartCalls, 0 );
		Assert.AreNotEqual( EnabledCalls, 0 );
		DisabledCalls++;
	}

	protected override void OnDestroy()
	{
		Assert.AreEqual( AwakeCalls, 1 );
		DestroyCalls++;
	}
}
