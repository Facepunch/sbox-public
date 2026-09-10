namespace SceneTests.Components;

/// <summary>
/// Pins the CharacterController contract: it is entirely trace driven (no physics
/// body, no gravity of its own), only moves when Move()/MoveTo() are called, finds
/// ground by tracing down, steps over ledges, and respects GroundAngle/IgnoreLayers.
/// </summary>
[TestClass]
public class CharacterControllerTest
{
	/// <summary>
	/// Creates a large static floor whose top surface sits at z = 0, optionally
	/// tagged so tests can exercise IgnoreLayers.
	/// </summary>
	static GameObject CreateFloor( Scene scene, params string[] tags )
	{
		var go = scene.CreateObject();
		go.Name = "Floor";
		go.WorldPosition = new Vector3( 0, 0, -50 );

		foreach ( var tag in tags )
		{
			go.Tags.Add( tag );
		}

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 2000, 2000, 100 );
		box.Static = true;

		return go;
	}

	/// <summary>
	/// Creates a GameObject with a CharacterController at the given world position.
	/// </summary>
	static CharacterController CreateController( Scene scene, Vector3 position )
	{
		var go = scene.CreateObject();
		go.Name = "Character";
		go.WorldPosition = position;

		return go.Components.Create<CharacterController>();
	}

	/// <summary>
	/// Pins the property defaults, that BoundingBox is derived from Radius/Height,
	/// and that the controller creates no collider or rigidbody - it's trace driven.
	/// </summary>
	[TestMethod]
	public void DefaultsAndBoundingBox()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cc = CreateController( scene, Vector3.Zero );
		var go = cc.GameObject;

		Assert.AreEqual( 16f, cc.Radius );
		Assert.AreEqual( 64f, cc.Height );
		Assert.AreEqual( 18f, cc.StepHeight );
		Assert.AreEqual( 45f, cc.GroundAngle );
		Assert.AreEqual( 10f, cc.Acceleration );
		Assert.AreEqual( 0.3f, cc.Bounciness );
		Assert.IsFalse( cc.UseCollisionRules );
		Assert.IsFalse( cc.IsOnGround );
		Assert.AreEqual( Vector3.Zero, cc.Velocity );

		Assert.AreEqual( new Vector3( -16, -16, 0 ), cc.BoundingBox.Mins );
		Assert.AreEqual( new Vector3( 16, 16, 64 ), cc.BoundingBox.Maxs );

		cc.Radius = 8;
		cc.Height = 32;
		Assert.AreEqual( new Vector3( -8, -8, 0 ), cc.BoundingBox.Mins );
		Assert.AreEqual( new Vector3( 8, 8, 32 ), cc.BoundingBox.Maxs );

		// Trace driven - the controller must not create physics objects
		Assert.IsNull( go.Components.Get<Collider>() );
		Assert.IsNull( go.Components.Get<Rigidbody>() );
	}

	/// <summary>
	/// Move() categorizes the position: a controller hovering just above a static
	/// floor becomes grounded and reports the ground object/collider, one high in
	/// the air does not.
	/// </summary>
	[TestMethod]
	public void GroundingOnStaticFloor()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = CreateFloor( scene );
		var floorCollider = floor.Components.Get<BoxCollider>();

		var cc = CreateController( scene, new Vector3( 0, 0, 1 ) );
		cc.Move();

		Assert.IsTrue( cc.IsOnGround );
		Assert.AreEqual( floor, cc.GroundObject );
		Assert.AreEqual( floorCollider, cc.GroundCollider );

		// The ground search only looks ~2 units down when airborne
		var air = CreateController( scene, new Vector3( 200, 0, 100 ) );
		air.Move();

		Assert.IsFalse( air.IsOnGround );
		Assert.IsNull( air.GroundObject );
		Assert.IsNull( air.GroundCollider );
	}

	/// <summary>
	/// The controller applies no gravity itself - that's the game code's job. An
	/// airborne controller with zero velocity stays exactly where it is, and one
	/// with a horizontal velocity translates without losing any height.
	/// </summary>
	[TestMethod]
	public void NoGravityWithoutGameCode()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var cc = CreateController( scene, new Vector3( 0, 0, 100 ) );
		var go = cc.GameObject;

		using var timeScope = Time.Scope( 1.0, 0.02 );

		for ( int i = 0; i < 30; i++ )
		{
			cc.Move();
		}

		Assert.AreEqual( 100f, go.WorldPosition.z, 0.01f, $"nothing should move it: {go.WorldPosition}" );
		Assert.IsFalse( cc.IsOnGround );

		// horizontal velocity moves it sideways - exactly Velocity * Time.Delta per Move
		cc.Velocity = new Vector3( 100, 0, 0 );
		for ( int i = 0; i < 30; i++ )
		{
			cc.Move();
		}

		Assert.AreEqual( 60f, go.WorldPosition.x, 0.5f, $"{go.WorldPosition}" );
		Assert.AreEqual( 100f, go.WorldPosition.z, 0.01f, $"it must not fall: {go.WorldPosition}" );
		Assert.AreEqual( 100f, cc.Velocity.x, 0.01f, "nothing bleeds velocity in the air" );
	}

	/// <summary>
	/// Accelerate adds Acceleration * Time.Delta * wishspeed per call, capped at the
	/// wish speed - and ApplyFriction bleeds speed using the stop-speed control value.
	/// </summary>
	[TestMethod]
	public void AccelerateAndFrictionMath()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var cc = CreateController( scene, Vector3.Zero );

		using var timeScope = Time.Scope( 1.0, 0.02 );

		// accelspeed = Acceleration(10) * Delta(0.02) * wishspeed(100) = 20
		cc.Accelerate( new Vector3( 100, 0, 0 ) );
		Assert.AreEqual( 20f, cc.Velocity.x, 0.01f );

		// keeps adding 20 per call until capped at the wish speed
		for ( int i = 0; i < 10; i++ )
		{
			cc.Accelerate( new Vector3( 100, 0, 0 ) );
		}

		Assert.AreEqual( 100f, cc.Velocity.x, 0.01f, "acceleration is capped at the wish speed" );

		// drop = control(140) * Delta(0.02) * friction(10) = 28
		cc.ApplyFriction( 10f );
		Assert.AreEqual( 72f, cc.Velocity.x, 0.01f );

		// enough friction stops it dead
		cc.ApplyFriction( 1000f );
		Assert.AreEqual( Vector3.Zero, cc.Velocity );
	}

	/// <summary>
	/// A grounded controller driven by Accelerate + Move translates across the floor,
	/// gets its vertical velocity zeroed and is snapped down onto the surface.
	/// </summary>
	[TestMethod]
	public void GroundedMoveTranslatesAndSnapsToFloor()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var cc = CreateController( scene, new Vector3( 0, 0, 1 ) );
		var go = cc.GameObject;

		cc.Move();
		Assert.IsTrue( cc.IsOnGround );

		using var timeScope = Time.Scope( 1.0, 0.02 );

		for ( int i = 0; i < 30; i++ )
		{
			cc.Accelerate( new Vector3( 100, 0, 0 ) );
			cc.Move();
		}

		Assert.IsTrue( go.WorldPosition.x > 30f, $"should have walked: {go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.z < 1.0f, $"should have snapped down to the floor: {go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.z > -0.5f, $"must not sink into the floor: {go.WorldPosition}" );
		Assert.IsTrue( cc.IsOnGround );
		Assert.AreEqual( 0f, cc.Velocity.z, 0.01f, "grounded movement zeroes the vertical velocity" );
	}

	/// <summary>
	/// Punch disconnects from the ground and adds to the velocity, and a strong
	/// upwards velocity prevents re-grounding on the next Move.
	/// </summary>
	[TestMethod]
	public void PunchClearsGroundAndAddsVelocity()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var cc = CreateController( scene, new Vector3( 0, 0, 1 ) );
		var go = cc.GameObject;

		cc.Move();
		Assert.IsTrue( cc.IsOnGround );

		cc.Punch( new Vector3( 50, 0, 100 ) );

		Assert.IsFalse( cc.IsOnGround );
		Assert.IsNull( cc.GroundObject );
		Assert.IsNull( cc.GroundCollider );
		Assert.AreEqual( new Vector3( 50, 0, 100 ), cc.Velocity );

		using var timeScope = Time.Scope( 1.0, 0.02 );
		cc.Move();

		Assert.AreEqual( 3f, go.WorldPosition.z, 0.1f, $"the punch should carry it upwards: {go.WorldPosition}" );
		Assert.IsFalse( cc.IsOnGround, "moving up faster than 40u/s never lands" );
	}

	/// <summary>
	/// A falling controller (gravity simulated by the test, as a game would) lands on
	/// the floor instead of passing through it, and its downward velocity is clipped.
	/// </summary>
	[TestMethod]
	public void FallingControllerLandsOnFloor()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );
		var cc = CreateController( scene, new Vector3( 0, 0, 40 ) );
		var go = cc.GameObject;

		using var timeScope = Time.Scope( 1.0, 0.02 );

		cc.Velocity = Vector3.Down * 100f;

		for ( int i = 0; i < 60 && !cc.IsOnGround; i++ )
		{
			cc.Move();
		}

		Assert.IsTrue( cc.IsOnGround, $"should have landed: {go.WorldPosition}" );

		// the first grounded move zeroes the vertical velocity and snaps to the surface
		cc.Move();

		Assert.IsTrue( go.WorldPosition.z > -0.5f, $"must not fall through the floor: {go.WorldPosition}" );
		Assert.IsTrue( go.WorldPosition.z < 2.5f, $"should be resting on the floor: {go.WorldPosition}" );
		Assert.AreEqual( 0f, cc.Velocity.z, 0.1f, "grounded movement zeroes the downward velocity" );
	}

	/// <summary>
	/// MoveTo reaches an unobstructed target exactly, stops at the bounding box
	/// distance from a wall, and slides along the wall when moving diagonally.
	/// </summary>
	[TestMethod]
	public void MoveToStopsAtWallsAndSlides()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var wall = scene.CreateObject();
		wall.Name = "Wall";
		wall.WorldPosition = new Vector3( 200, 0, 0 );
		var wallBox = wall.Components.Create<BoxCollider>();
		wallBox.Scale = new Vector3( 10, 400, 400 );
		wallBox.Static = true;

		var cc = CreateController( scene, new Vector3( 0, 0, 50 ) );
		var go = cc.GameObject;

		// unobstructed - lands exactly on the target
		cc.MoveTo( new Vector3( 100, 0, 50 ), false );
		Assert.AreEqual( 100f, go.WorldPosition.x, 0.1f, $"{go.WorldPosition}" );

		// blocked - the near wall face is at x=195, our hull is 16 wide, so we stop at 179
		cc.MoveTo( new Vector3( 400, 0, 50 ), false );
		Assert.AreEqual( 179f, go.WorldPosition.x, 1f, $"{go.WorldPosition}" );
		Assert.AreEqual( 0f, go.WorldPosition.y, 0.1f );

		// diagonal into the wall slides along it
		cc.MoveTo( new Vector3( 400, 150, 50 ), false );
		Assert.AreEqual( 179f, go.WorldPosition.x, 1f, $"{go.WorldPosition}" );
		Assert.AreEqual( 150f, go.WorldPosition.y, 1f, $"should have slid along the wall: {go.WorldPosition}" );
	}

	/// <summary>
	/// MoveTo with useStep climbs a ledge lower than StepHeight: tracing forward,
	/// up, across and back down to land on top of the obstacle.
	/// </summary>
	[TestMethod]
	public void MoveToWithStepClimbsLedge()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );

		// A 10 unit tall ledge - below the default 18 unit StepHeight
		var ledge = scene.CreateObject();
		ledge.Name = "Ledge";
		ledge.WorldPosition = new Vector3( 80, 0, 0 );
		var ledgeBox = ledge.Components.Create<BoxCollider>();
		ledgeBox.Scale = new Vector3( 40, 200, 20 );
		ledgeBox.Static = true;

		var cc = CreateController( scene, new Vector3( 0, 0, 1 ) );
		var go = cc.GameObject;

		cc.Move();
		Assert.IsTrue( cc.IsOnGround );

		cc.MoveTo( new Vector3( 80, 0, 1 ), true );

		Assert.AreEqual( 80f, go.WorldPosition.x, 1f, $"should have stepped to the target: {go.WorldPosition}" );
		Assert.AreEqual( 10f, go.WorldPosition.z, 0.5f, $"should be standing on the ledge top: {go.WorldPosition}" );
	}

	/// <summary>
	/// TraceDirection sweeps the controller's bounding box, so it reports what the
	/// hull would hit - and changing Height changes what it collides with.
	/// </summary>
	[TestMethod]
	public void TraceDirectionUsesBoundingBox()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var floor = CreateFloor( scene );

		// a ceiling whose underside is at z=70
		var ceiling = scene.CreateObject();
		ceiling.Name = "Ceiling";
		ceiling.WorldPosition = new Vector3( 0, 0, 120 );
		var ceilingBox = ceiling.Components.Create<BoxCollider>();
		ceilingBox.Scale = new Vector3( 400, 400, 100 );
		ceilingBox.Static = true;

		var cc = CreateController( scene, new Vector3( 0, 0, 1 ) );

		var down = cc.TraceDirection( Vector3.Down * 10f );
		Assert.IsTrue( down.Hit );
		Assert.AreEqual( floor, down.GameObject );
		Assert.AreEqual( 1f, down.Distance, 0.1f );

		// the default 64 tall hull tops out at z=65, so 10 up hits the ceiling after 5
		var up = cc.TraceDirection( Vector3.Up * 10f );
		Assert.IsTrue( up.Hit );
		Assert.AreEqual( ceiling, up.GameObject );
		Assert.AreEqual( 5f, up.Distance, 0.5f );

		// a shorter hull clears the ceiling entirely
		cc.Height = 30;
		up = cc.TraceDirection( Vector3.Up * 10f );
		Assert.IsFalse( up.Hit, $"a 30 tall hull shouldn't reach the ceiling: {up.Distance}" );
	}

	/// <summary>
	/// Geometry tagged with an IgnoreLayers tag is invisible to the controller's
	/// traces - it can't be ground until the tag is removed from the set.
	/// </summary>
	[TestMethod]
	public void IgnoreLayersSkipsTaggedGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene, "glass" );
		var cc = CreateController( scene, new Vector3( 0, 0, 1 ) );

		cc.IgnoreLayers.Add( "glass" );
		cc.Move();

		Assert.IsFalse( cc.IsOnGround, "the tagged floor must be ignored" );
		Assert.IsFalse( cc.TraceDirection( Vector3.Down * 10f ).Hit );

		cc.IgnoreLayers.Remove( "glass" );
		cc.Move();

		Assert.IsTrue( cc.IsOnGround, "with the tag removed the floor is solid again" );
	}

	/// <summary>
	/// A surface steeper than GroundAngle is not ground: a 50 degree slope is
	/// rejected by the default 45 degree limit but accepted when the limit is raised.
	/// </summary>
	[TestMethod]
	public void SlopesSteeperThanGroundAngleAreNotGround()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var slope = scene.CreateObject();
		slope.Name = "Slope";
		slope.WorldRotation = Rotation.FromPitch( 50 );
		var slopeBox = slope.Components.Create<BoxCollider>();
		slopeBox.Scale = new Vector3( 200 );
		slopeBox.Static = true;

		// hover above the center of the tilted top face, leaving clearance for the
		// uphill corner of the hull (16 * tan(50) is about 19)
		var surfacePoint = slope.WorldRotation.Up * 100f;
		var cc = CreateController( scene, surfacePoint + Vector3.Up * 20f );

		cc.Move();
		Assert.IsFalse( cc.IsOnGround, "a 50 degree slope is steeper than the default 45 degree limit" );

		cc.GroundAngle = 80;
		cc.Move();
		Assert.IsTrue( cc.IsOnGround, "raising GroundAngle makes the slope standable" );
	}

	/// <summary>
	/// A controller that starts slightly inside solid geometry recovers: the first
	/// unstuck attempt teleports it 2 units up, after which it grounds normally.
	/// </summary>
	[TestMethod]
	public void UnstuckTeleportsUpwards()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		CreateFloor( scene );

		// one unit inside the floor - 2 units up is free space
		var cc = CreateController( scene, new Vector3( 0, 0, -1 ) );
		var go = cc.GameObject;

		cc.Move();

		Assert.AreEqual( 1f, go.WorldPosition.z, 0.1f, $"should have unstuck upwards: {go.WorldPosition}" );
		Assert.IsTrue( cc.IsOnGround, "free again and standing on the floor" );
	}
}
