namespace GameObjects;

[TestClass]
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

	/// <summary>
	/// Parent objects should update their descendants enabled state
	/// when they toggle <see cref="GameObject.Enabled"/>.
	/// </summary>
	[TestMethod]
	public void Child_StartParentDisabled()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent", enabled: false );
		var child = new GameObject( parent, name: "Child" );

		var o = child.Components.Create<OrderTestComponent>();

		Assert.AreEqual( 0, o.AwakeCalls ); // awake shouldn't call until the gameobject is active
		Assert.AreEqual( 0, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		parent.Enabled = true;
		scene.GameTick();

		Assert.AreEqual( 1, o.AwakeCalls );
		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		parent.Enabled = false;
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 0, o.DestroyCalls );

		parent.Destroy();
		scene.GameTick();

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
		Assert.AreEqual( 1, o.DestroyCalls );
	}

	/// <summary>
	/// Objects should update their enabled state when changing to an inactive parent.
	/// </summary>
	[TestMethod]
	public void Child_MoveToDisabledParent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent", enabled: false );
		var child = new GameObject( name: "Child" );

		var o = child.Components.Create<OrderTestComponent>();

		Assert.AreEqual( 1, o.AwakeCalls );
		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		scene.GameTick();

		// Nest under an inactive parent, should become inactive itself
		child.Parent = parent;

		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 1, o.DisabledCalls );
	}

	/// <summary>
	/// Objects should update their enabled state when changing from an inactive parent.
	/// </summary>
	[TestMethod]
	public void Child_MoveFromDisabledParent()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = new GameObject( name: "Parent", enabled: false );
		var child = new GameObject( parent, name: "Child" );

		var o = child.Components.Create<OrderTestComponent>();

		Assert.AreEqual( 0, o.AwakeCalls ); // awake shouldn't call until the gameobject is active
		Assert.AreEqual( 0, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );

		scene.GameTick();

		// Move to scene root, should become active
		child.Parent = null;

		Assert.AreEqual( 1, o.AwakeCalls );
		Assert.AreEqual( 1, o.EnabledCalls );
		Assert.AreEqual( 0, o.DisabledCalls );
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
