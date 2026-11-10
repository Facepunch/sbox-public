using System.Collections.Generic;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins the geometry contract of the collider components: each shape produces physics
/// geometry matching its properties, property changes rebuild the shape live, object
/// scale applies, and triggers report touch events. Geometry is verified through
/// traces - if a trace hits the face where the math says the face is, the shape is right.
/// </summary>
[TestClass]
public class ColliderShapeTest
{
	static SceneTraceResult TraceX( Scene scene, float y = 0, float z = 0 )
	{
		return scene.Trace.Ray( new Vector3( 0, y, z ), new Vector3( 400, y, z ) ).Run();
	}

	/// <summary>
	/// A box collider's faces sit at Center ± Scale/2 in object space.
	/// </summary>
	[TestMethod]
	public void BoxGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );

		// Center offsets the shape without moving the object
		box.Center = new Vector3( 20, 0, 0 );
		Assert.AreEqual( 95f, TraceX( scene ).EndPosition.x, 0.5f );
	}

	/// <summary>
	/// A sphere collider's surface sits at Radius from Center.
	/// </summary>
	[TestMethod]
	public void SphereGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var sphere = go.Components.Create<SphereCollider>();
		sphere.Radius = 25f;

		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );

		// A ray passing further from the center than the radius misses
		Assert.IsFalse( TraceX( scene, y: 30 ).Hit );
	}

	/// <summary>
	/// A capsule collider spans Start to End with the given radius: rays through the
	/// shaft hit at Radius from the axis, rays beyond the cap miss.
	/// </summary>
	[TestMethod]
	public void CapsuleGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var capsule = go.Components.Create<CapsuleCollider>();
		capsule.Start = new Vector3( 0, 0, 0 );
		capsule.End = new Vector3( 0, 0, 50 );
		capsule.Radius = 10f;

		// Through the shaft
		Assert.AreEqual( 90f, TraceX( scene, z: 25 ).EndPosition.x, 0.5f );

		// Through the top cap's sphere
		Assert.IsTrue( TraceX( scene, z: 55 ).Hit );

		// Beyond the cap radius
		Assert.IsFalse( TraceX( scene, z: 70 ).Hit );
	}

	/// <summary>
	/// Changing shape properties after creation rebuilds the physics shape live.
	/// </summary>
	[TestMethod]
	public void PropertyChangesRebuildShape()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );

		box.Scale = new Vector3( 100 );
		Assert.AreEqual( 50f, TraceX( scene ).EndPosition.x, 0.5f );

		box.Scale = new Vector3( 10 );
		Assert.AreEqual( 95f, TraceX( scene ).EndPosition.x, 0.5f );
	}

	/// <summary>
	/// The object's world scale applies to the collider geometry.
	/// </summary>
	[TestMethod]
	public void ObjectScaleAppliesToShape()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		go.WorldScale = new Vector3( 2 );

		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		// Half extent 25 scaled by 2 -> face at 100 - 50
		Assert.AreEqual( 50f, TraceX( scene ).EndPosition.x, 0.5f );
	}

	/// <summary>
	/// Moving the object moves its keyframed physics shape - but the body syncs to
	/// the transform on the next tick, not immediately.
	/// </summary>
	[TestMethod]
	public void ShapeFollowsObjectAfterTick()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );

		go.WorldPosition = new Vector3( 200, 0, 0 );

		// Until a tick runs, traces still see the old keyframed position
		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );

		scene.GameTick();

		Assert.AreEqual( 175f, TraceX( scene ).EndPosition.x, 0.5f );
	}

	/// <summary>
	/// LocalBounds and GetWorldBounds reflect the shape's extents in the right space.
	/// </summary>
	[TestMethod]
	public void Bounds()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.IsTrue( box.LocalBounds.Mins.AlmostEqual( new Vector3( -25 ), 0.5f ), $"{box.LocalBounds}" );
		Assert.IsTrue( box.LocalBounds.Maxs.AlmostEqual( new Vector3( 25 ), 0.5f ) );

		var world = box.GetWorldBounds();
		Assert.IsTrue( world.Mins.AlmostEqual( new Vector3( 75, -25, -25 ), 0.5f ), $"{world}" );
		Assert.IsTrue( world.Maxs.AlmostEqual( new Vector3( 125, 25, 25 ), 0.5f ) );
	}

	/// <summary>
	/// FindClosestPoint clamps an outside point onto the collider surface and
	/// returns inside points unchanged-ish.
	/// </summary>
	[TestMethod]
	public void FindClosestPoint()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		var closest = box.FindClosestPoint( Vector3.Zero );
		Assert.AreEqual( 75f, closest.x, 1f );
		Assert.AreEqual( 0f, closest.y, 1f );
	}

	/// <summary>
	/// Toggling IsTrigger on a live collider switches it between solid and
	/// trigger for traces.
	/// </summary>
	[TestMethod]
	public void IsTriggerTogglesLive()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.IsTrue( TraceX( scene ).Hit );

		box.IsTrigger = true;
		Assert.IsFalse( TraceX( scene ).Hit );

		box.IsTrigger = false;
		Assert.IsTrue( TraceX( scene ).Hit );
	}

	/// <summary>
	/// A dynamic body overlapping a trigger raises enter and exit events and shows
	/// up in Touching while overlapped.
	/// </summary>
	[TestMethod]
	public void TriggerTouchEvents()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var triggerGo = scene.CreateObject();
		triggerGo.Name = "Trigger";
		var trigger = triggerGo.Components.Create<BoxCollider>();
		trigger.Scale = new Vector3( 100 );
		trigger.IsTrigger = true;

		var enters = 0;
		var exits = 0;
		trigger.OnTriggerEnter = _ => enters++;
		trigger.OnTriggerExit = _ => exits++;

		// A dynamic body inside the trigger volume
		var bodyGo = scene.CreateObject();
		bodyGo.Name = "Body";
		var rb = bodyGo.Components.Create<Rigidbody>();
		rb.Gravity = false;
		var bodyCollider = bodyGo.Components.Create<BoxCollider>();
		bodyCollider.Scale = new Vector3( 10 );

		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( 1, enters, "trigger enter should have fired" );
		Assert.IsTrue( trigger.Touching.Contains( bodyCollider ), "trigger should be touching the body" );

		// Move the body far away and tick - the trigger should report the exit
		bodyGo.WorldPosition = new Vector3( 1000, 0, 0 );
		for ( int i = 0; i < 5; i++ ) scene.GameTick();

		Assert.AreEqual( 1, exits, "trigger exit should have fired" );
		Assert.IsFalse( trigger.Touching.Any() );
	}

	/// <summary>
	/// Surface material overrides round-trip through the collider properties.
	/// </summary>
	[TestMethod]
	public void SurfacePropertiesRoundTrip()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		box.Friction = 0.5f;
		box.Elasticity = 0.25f;
		box.RollingResistance = 0.1f;
		box.SurfaceVelocity = new Vector3( 10, 0, 0 );

		Assert.AreEqual( 0.5f, box.Friction.Value, 0.001f );
		Assert.AreEqual( 0.25f, box.Elasticity.Value, 0.001f );
		Assert.AreEqual( 0.1f, box.RollingResistance.Value, 0.001f );
		Assert.IsTrue( box.SurfaceVelocity.AlmostEqual( new Vector3( 10, 0, 0 ) ) );

		box.Friction = null;
		Assert.IsFalse( box.Friction.HasValue );
	}

	static List<Vector3> CubeCorners( float half ) => new()
	{
		new( -half, -half, -half ), new( half, -half, -half ),
		new( -half, half, -half ), new( half, half, -half ),
		new( -half, -half, half ), new( half, -half, half ),
		new( -half, half, half ), new( half, half, half ),
	};

	/// <summary>
	/// A hull collider in its default Box mode behaves like a box: faces at
	/// Center ± BoxSize/2, and resizing the box updates the live shape.
	/// </summary>
	[TestMethod]
	public void HullBoxGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var hull = go.Components.Create<HullCollider>();

		Assert.AreEqual( HullCollider.PrimitiveType.Box, hull.Type );
		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );

		hull.BoxSize = new Vector3( 100 );
		Assert.AreEqual( 50f, TraceX( scene ).EndPosition.x, 0.5f );

		hull.Center = new Vector3( 20, 0, 0 );
		Assert.AreEqual( 70f, TraceX( scene ).EndPosition.x, 0.5f );
	}

	/// <summary>
	/// A hull collider in Cylinder mode spans Height around Center with the given
	/// Radius - rays beyond the half height miss, and resizing updates the live shape.
	/// </summary>
	[TestMethod]
	public void HullCylinderGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var hull = go.Components.Create<HullCollider>( false );
		hull.Type = HullCollider.PrimitiveType.Cylinder;
		hull.Height = 50f;
		hull.Radius = 25f;
		hull.Enabled = true;

		// The slice vertex at 180 degrees sits exactly at -Radius
		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 1f );

		// Inside the half height there is hull, above it nothing
		Assert.IsTrue( TraceX( scene, z: 20 ).Hit );
		Assert.IsFalse( TraceX( scene, z: 30 ).Hit );

		// Shrinking the radius updates the live shape
		hull.Radius = 10f;
		Assert.AreEqual( 90f, TraceX( scene ).EndPosition.x, 1f );
	}

	/// <summary>
	/// A hull collider in Cone mode tapers from Radius at the base to Radius2 at
	/// the tip, so rays near the tip hit deeper than rays near the base.
	/// </summary>
	[TestMethod]
	public void HullConeGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var hull = go.Components.Create<HullCollider>( false );
		hull.Type = HullCollider.PrimitiveType.Cone;
		hull.Height = 50f;
		hull.Radius = 25f;
		hull.Radius2 = 0f;
		hull.Enabled = true;

		var nearBase = TraceX( scene, z: -20 );
		var nearTip = TraceX( scene, z: 20 );

		Assert.IsTrue( nearBase.Hit );
		Assert.IsTrue( nearTip.Hit );
		Assert.IsTrue( nearBase.EndPosition.x < nearTip.EndPosition.x - 10f,
			$"the cone should taper: {nearBase.EndPosition.x} vs {nearTip.EndPosition.x}" );
		Assert.IsFalse( TraceX( scene, z: 30 ).Hit );

		// Widening the tip pulls the surface near the tip outwards, live
		hull.Radius2 = 20f;
		var widened = TraceX( scene, z: 20 );
		Assert.IsTrue( widened.EndPosition.x < nearTip.EndPosition.x - 5f,
			$"the wider tip should be hit sooner: {widened.EndPosition.x} vs {nearTip.EndPosition.x}" );
	}

	/// <summary>
	/// A hull collider in Points mode builds a convex hull from the point cloud,
	/// and assigning a new point list rebuilds the live shape.
	/// </summary>
	[TestMethod]
	public void HullPointsGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var hull = go.Components.Create<HullCollider>( false );
		hull.Type = HullCollider.PrimitiveType.Points;
		hull.Points = CubeCorners( 25f );
		hull.Enabled = true;

		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );

		hull.Points = CubeCorners( 10f );
		Assert.AreEqual( 90f, TraceX( scene ).EndPosition.x, 0.5f );
	}

	/// <summary>
	/// An unknown hull primitive type produces no shapes at all.
	/// </summary>
	[TestMethod]
	public void HullInvalidTypeHasNoShapes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var hull = go.Components.Create<HullCollider>( false );
		hull.Type = (HullCollider.PrimitiveType)123;
		hull.Enabled = true;

		Assert.AreEqual( 0, hull.Shapes.Count );
		Assert.IsFalse( TraceX( scene ).Hit );
	}

	/// <summary>
	/// A plane collider is a quad mesh of Scale extents around Center: rays within
	/// the extents hit it, rays beyond miss, resizing updates the live shape - and
	/// being concave it is always static.
	/// </summary>
	[TestMethod]
	public void PlaneGeometry()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var plane = go.Components.Create<PlaneCollider>();

		SceneTraceResult TraceDown( float x ) =>
			scene.Trace.Ray( new Vector3( x, 0, 50 ), new Vector3( x, 0, -50 ) ).Run();

		var hit = TraceDown( 0 );
		Assert.IsTrue( hit.Hit );
		Assert.AreEqual( 0f, hit.EndPosition.z, 0.5f );
		Assert.IsTrue( hit.Normal.AlmostEqual( Vector3.Up, 0.01f ), $"{hit.Normal}" );

		// Default size is 50 corner to corner - x 40 is off the edge
		Assert.IsFalse( TraceDown( 40 ).Hit );

		// Growing the plane updates the live shape
		plane.Scale = new Vector2( 200, 200 );
		Assert.IsTrue( TraceDown( 40 ).Hit );

		// Concave colliders are always static and can't be made dynamic
		Assert.IsTrue( plane.Static );
		plane.Static = false;
		Assert.IsTrue( plane.Static );
		Assert.IsFalse( plane.IsDynamic );
	}

	/// <summary>
	/// The plane's Normal property orients the quad.
	/// </summary>
	[TestMethod]
	public void PlaneNormalOrientsThePlane()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );

		var plane = go.Components.Create<PlaneCollider>( false );
		plane.Normal = Vector3.Backward;
		plane.Scale = new Vector2( 100, 100 );
		plane.Enabled = true;

		var hit = TraceX( scene );
		Assert.IsTrue( hit.Hit, "the plane should face the ray" );
		Assert.AreEqual( 100f, hit.EndPosition.x, 0.5f );
	}

	/// <summary>
	/// Sphere colliders use a true sphere shape under uniform scale and swap to a
	/// hull approximation under non-uniform scale, converting back and forth as
	/// the object's scale changes.
	/// </summary>
	[TestMethod]
	public void SphereScaleSwapsBetweenSphereAndHull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		go.WorldScale = new Vector3( 1, 1, 2 );

		var sphere = go.Components.Create<SphereCollider>();
		sphere.Radius = 25f;

		// Ellipsoid hull: x extent 25, z extent 50. The hull samples 7 longitudes
		// so the -x face sits a couple of units short of the true radius.
		Assert.AreEqual( 77.5f, TraceX( scene ).EndPosition.x, 3f );
		Assert.IsTrue( TraceX( scene, z: 40 ).Hit );

		// Back to uniform: a true sphere again, nothing at z 40
		go.WorldScale = new Vector3( 1, 1, 1 );
		Assert.AreEqual( 75f, TraceX( scene ).EndPosition.x, 0.5f );
		Assert.IsFalse( TraceX( scene, z: 40 ).Hit );

		// Uniform scale scales the sphere radius
		go.WorldScale = new Vector3( 2, 2, 2 );
		Assert.AreEqual( 50f, TraceX( scene ).EndPosition.x, 0.5f );

		// Non-uniform again: back to a hull, taller than the sphere was
		go.WorldScale = new Vector3( 2, 2, 4 );
		Assert.IsTrue( TraceX( scene, z: 80 ).Hit );
		Assert.IsFalse( TraceX( scene, z: 110 ).Hit );

		// Stretching further while already a hull updates the hull in place
		go.WorldScale = new Vector3( 2, 2, 6 );
		Assert.IsTrue( TraceX( scene, z: 110 ).Hit );
	}

	/// <summary>
	/// The IgnoreTraces collider flag hides the collider from traces - applied to
	/// the live shapes, surviving rebuilds, until the flag is removed.
	/// </summary>
	[TestMethod]
	public void IgnoreTracesFlagHidesFromTraces()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.IsTrue( TraceX( scene ).Hit );

		box.ColliderFlags = ColliderFlags.IgnoreTraces;
		Assert.IsFalse( TraceX( scene ).Hit );

		// The flag survives a full shape rebuild
		box.IsTrigger = true;
		box.IsTrigger = false;
		Assert.IsFalse( TraceX( scene ).Hit );

		box.ColliderFlags = default;
		Assert.IsTrue( TraceX( scene ).Hit );
	}

	/// <summary>
	/// A surface assigned to the collider is applied to its live shapes and
	/// reapplied when the shapes rebuild - traces report it as the hit surface.
	/// </summary>
	[TestMethod]
	public void SurfaceAppliesToShapes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		var surface = new Surface();
		box.Surface = surface;
		Assert.AreSame( surface, box.Shapes[0].Surface );

		// Surface and the per-collider overrides survive a rebuild
		box.Friction = 0.5f;
		box.Elasticity = 0.25f;
		box.RollingResistance = 0.1f;
		box.IsTrigger = true;
		box.IsTrigger = false;

		Assert.AreSame( surface, box.Shapes[0].Surface );
		Assert.AreEqual( 0.5f, box.Friction.Value, 0.001f );
		Assert.IsTrue( TraceX( scene ).Hit );
	}

	/// <summary>
	/// IsDynamic is true only while the collider is attached to a rigidbody -
	/// plain colliders are keyframed and static colliders are static.
	/// </summary>
	[TestMethod]
	public void IsDynamicReflectsBodyOwnership()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var box = go.Components.Create<BoxCollider>();
		Assert.IsFalse( box.IsDynamic );

		box.Static = true;
		Assert.IsFalse( box.IsDynamic );
		box.Static = false;

		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;
		Assert.IsTrue( box.IsDynamic );

		rb.Enabled = false;
		Assert.IsFalse( box.IsDynamic );
	}

	/// <summary>
	/// Static switches the keyframe body between Keyframed and Static body types,
	/// keeping the shape solid and surviving rebuilds.
	/// </summary>
	[TestMethod]
	public void StaticTogglesBodyType()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		Assert.AreEqual( PhysicsBodyType.Keyframed, box.KeyBody.BodyType );

		box.Static = true;
		Assert.AreEqual( PhysicsBodyType.Static, box.KeyBody.BodyType );
		Assert.IsTrue( TraceX( scene ).Hit );

		// A rebuild while static keeps the static body type
		box.IsTrigger = true;
		box.IsTrigger = false;
		Assert.AreEqual( PhysicsBodyType.Static, box.KeyBody.BodyType );

		box.Static = false;
		Assert.AreEqual( PhysicsBodyType.Keyframed, box.KeyBody.BodyType );
	}

	/// <summary>
	/// Point queries route to whichever physics body the collider currently has:
	/// the keyframe body normally, the rigidbody when attached to one, and inert
	/// fallbacks when the collider is disabled.
	/// </summary>
	[TestMethod]
	public void QueriesFollowTheBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 50 );

		// Keyframed: velocity is zero, joints are empty
		Assert.IsTrue( box.GetVelocityAtPoint( new Vector3( 100, 0, 0 ) ).AlmostEqual( Vector3.Zero ) );
		Assert.AreEqual( 0, box.Joints.Count );
#pragma warning disable CS0618
		Assert.AreSame( box.KeyBody, box.KeyframeBody );
		box.OnPhysicsChanged();
#pragma warning restore CS0618

		// On a rigidbody the queries answer from the rigidbody
		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;
		rb.AngularVelocity = new Vector3( 0, 0, 10 );

		Assert.IsTrue( box.GetVelocityAtPoint( new Vector3( 110, 0, 0 ) ).Length > 0.1f );
		Assert.AreEqual( 125f, box.FindClosestPoint( new Vector3( 300, 0, 0 ) ).x, 1f );
		Assert.AreEqual( 50f, box.GetWorldBounds().Size.x, 3f, $"{box.GetWorldBounds()}" );
		Assert.IsFalse( box.Touching.Any() );

		// Disabled: inert fallbacks
		rb.Enabled = false;
		box.Enabled = false;
		Assert.IsTrue( box.GetVelocityAtPoint( Vector3.Zero ).AlmostEqual( Vector3.Zero ) );
		Assert.IsTrue( box.FindClosestPoint( new Vector3( 1, 2, 3 ) ).AlmostEqual( new Vector3( 1, 2, 3 ) ) );
		Assert.AreEqual( 0.1f, box.GetWorldBounds().Size.x, 0.01f );
		Assert.IsFalse( box.Touching.Any() );
	}

	/// <summary>
	/// Rebuilding shapes on a dynamic body snaps the GameObject to wherever the
	/// physics body actually is, so the new shapes aren't built against a stale
	/// transform.
	/// </summary>
	[TestMethod]
	public void RebuildSnapsObjectToMovedBody()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;
		var box = go.Components.Create<BoxCollider>();
		box.Scale = new Vector3( 10 );

		// Move the physics body directly, behind the transform's back
		rb.PhysicsBody.Transform = new Transform( new Vector3( 50, 0, 0 ) );

		// Any rebuild snaps the object onto the body
		box.IsTrigger = true;
		box.IsTrigger = false;

		Assert.AreEqual( 50f, go.WorldPosition.x, 0.5f, $"{go.WorldPosition}" );
	}
}
