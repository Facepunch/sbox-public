using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private const int MaxUnsimplifiedHullVertices = 44;

	private sealed record PhysicsBody( PhyFile.Solid Solid, string BoneName, Transform BindPose );

	private sealed record PhysicsJoint( int Child, int Parent, Transform ChildFrame, Transform ParentFrame, Vector2? Limits, bool Collision,
		float? Swing = null, float Friction = 0 );

	private sealed record PhysicsPlan( PhysicsBody[] Bodies, PhysicsJoint[] Joints, string[] Blockers, string[] Warnings );

	private static PhysicsPlan PreparePhysics( PhyFile phy, MdlFile mdl, Transform[] boneTransforms, string[] boneNames )
	{
		if ( phy.Checksum != mdl.Checksum )
			throw new InvalidDataException( $"{phy.SourceName}: PHY/MDL checksum mismatch." );
		if ( boneTransforms.Length != mdl.Bones.Length || boneNames.Length != mdl.Bones.Length )
			throw new ArgumentException( "Physics bind arrays must match the MDL skeleton." );
		var articulated = phy.Solids.Count > 1 || phy.Constraints.Count > 0;
		var blockers = new HashSet<string>( StringComparer.Ordinal );
		var warnings = new HashSet<string>( StringComparer.Ordinal );
		var bodies = new PhysicsBody[phy.Solids.Count];
		var joints = new List<PhysicsJoint>();
		if ( !float.IsFinite( phy.Solids.Sum( solid => solid.Mass ) ) )
			throw new InvalidDataException( $"{phy.SourceName}: total physics mass overflows." );
		if ( articulated && mdl.IsStaticProp )
			throw new InvalidDataException( $"{phy.SourceName}: static-prop MDL has articulated physics." );
		if ( articulated && phy.Constraints.Count != phy.Solids.Count - 1 )
			blockers.Add( "multiple bodies without one connected constraint tree" );

		var lookup = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		if ( articulated )
		{
			for ( var i = 0; i < boneNames.Length; i++ )
				if ( !lookup.TryAdd( boneNames[i], i ) )
					throw new InvalidDataException( $"{phy.SourceName}: ambiguous MDL bone name '{boneNames[i]}'." );
		}
		var usedBones = new HashSet<int>();
		foreach ( var solid in phy.Solids )
		{
			if ( !articulated )
			{
				// A normal collisionmodel is entity-local, even when the render MDL has bones.
				bodies[solid.Index] = new PhysicsBody( solid, null, Transform.Zero );
				continue;
			}
			// Source resolves these names, not the MDL PhysicsBone field, which can be stale.
			if ( !lookup.TryGetValue( solid.Name, out var bone ) || !usedBones.Add( bone ) )
				throw new InvalidDataException( $"{phy.SourceName}: solid {solid.Index} has missing or duplicate bone '{solid.Name}'." );
			var bind = GetPhysicsBindPose( mdl.Bones[bone].PoseToBone, phy.SourceName );
			var authored = boneTransforms[bone];
			if ( !authored.IsValid || bind.Position.Distance( authored.Position ) > 0.02f
				|| bind.Rotation.Forward.Distance( authored.Rotation.Forward ) > 0.002f
				|| bind.Rotation.Up.Distance( authored.Rotation.Up ) > 0.002f )
				throw new InvalidDataException( $"{phy.SourceName}: bone '{solid.Name}' inverse bind differs from the imported skeleton." );
			bodies[solid.Index] = new PhysicsBody( solid, boneNames[bone], bind );
		}

		var collisionRules = phy.Text.Any( block => block.Name.Equals( "collisionrules", StringComparison.OrdinalIgnoreCase ) );
		var collisionPairs = phy.SelfCollisions == false ? new HashSet<PhyFile.CollisionPair>() : phy.CollisionPairs.ToHashSet();
		var constrainedPairs = phy.Constraints.Select( c => GetPhysicsCollisionPair( c.Parent, c.Child ) ).ToHashSet();
		if ( articulated && collisionRules )
		{
			// An explicit collisionrules block is an allow-list, including an empty block.
			// Joint flags can filter constrained pairs only; all other pairs must remain enabled.
			var unconstrainedCount = (long)bodies.Length * (bodies.Length - 1) / 2 - constrainedPairs.Count;
			if ( collisionPairs.Count( pair => !constrainedPairs.Contains( pair ) ) != unconstrainedCount )
				warnings.Add( "collision-pair exclusions between bodies without a joint are unavailable; native non-adjacent collisions remain enabled" );
		}
		Span<PhyFile.AxisLimit> axes = stackalloc PhyFile.AxisLimit[3];
		foreach ( var constraint in phy.Constraints )
		{
			axes[0] = constraint.X;
			axes[1] = constraint.Y;
			axes[2] = constraint.Z;
			var moving = -1;
			var movingCount = 0;
			var outsideNativeLimits = false;
			for ( var axis = 0; axis < axes.Length; axis++ )
			{
				outsideNativeLimits |= axes[axis].Min < -180 || axes[axis].Max > 180;
				if ( axes[axis].Min == axes[axis].Max ) continue;
				movingCount++;
				moving = axis;
			}
			if ( outsideNativeLimits )
				warnings.Add( "angular ranges outside -180..180 degrees are clamped to native limits" );

			var basis = (movingCount == 1 ? moving : movingCount > 1 ? 0 : -1) switch
			{
				0 => Rotation.LookAt( Vector3.Up, Vector3.Forward ),
				1 => Rotation.LookAt( Vector3.Forward, new Vector3( 0, 1, 0 ) ),
				_ => Rotation.Identity
			};
			var childFrame = new Transform( Vector3.Zero, basis );
			var parentFrame = bodies[constraint.Parent].BindPose.ToLocal( bodies[constraint.Child].BindPose.ToWorld( childFrame ) );
			var twist = movingCount > 1 ? 0 : moving;
			Vector2? limits = twist < 0 ? null : new Vector2( Math.Clamp( -axes[twist].Max, -180, 180 ), Math.Clamp( -axes[twist].Min, -180, 180 ) );
			var collision = collisionRules && collisionPairs.Contains( GetPhysicsCollisionPair( constraint.Parent, constraint.Child ) );
			var torqueScale = bodies[constraint.Child].Solid.Mass / (0.0254f * 0.0254f);
			var friction = Math.Max( constraint.X.Friction, Math.Max( constraint.Y.Friction, constraint.Z.Friction ) ) * torqueScale;
			var swingY = axes[1].Max - axes[1].Min;
			var swingZ = axes[2].Max - axes[2].Min;
			float? swing = movingCount > 1 ? Math.Max( swingY >= 359 ? 180 : swingY * 0.5f, swingZ >= 359 ? 180 : swingZ * 0.5f ) : null;
			if ( swing.HasValue )
				warnings.Add( "ball joints approximate the two authored swing ranges with one circular cone and combine angular friction into one torque" );
			if ( swing is > 89.1f and < 180 )
				warnings.Add( "ball-joint swing cones are capped at 89.1 degrees" );
			joints.Add( new PhysicsJoint( constraint.Child, constraint.Parent, childFrame, parentFrame, limits, collision, swing, friction ) );
		}
		if ( articulated && phy.Text.Any( block => block.Name.Equals( "animatedfriction", StringComparison.OrdinalIgnoreCase ) ) )
			warnings.Add( "animated ragdoll friction is not imported" );
		if ( bodies.Any( body => body.Solid.Convexes.Any( convex => convex.Length > MaxUnsimplifiedHullVertices ) ) )
			warnings.Add( $"convexes with more than {MaxUnsimplifiedHullVertices} vertices allow native iterative vertex removal to fit the 256-half-edge hull budget; every authored convex is retained, but its boundary may shrink" );
		warnings.Add( "native center of mass/inertia are recalculated; Source mass-center overrides, rotational-inertia limits and drag are not stored" );
		return new PhysicsPlan( bodies, joints.ToArray(), blockers.ToArray(), warnings.ToArray() );
	}

	private void AddPhysics( ModelBuilder builder, PhyFile phy, PhysicsPlan plan )
	{
		if ( plan.Blockers.Length > 0 )
		{
			Log.Trace( $"{phy.SourceName}: authored ragdoll physics was NOT added: {string.Join( "; ", plan.Blockers )}. Rendering remains available; no substitute collision or loose bodies were created." );
			return;
		}
		if ( plan.Bodies.Length == 0 ) return;

		foreach ( var body in plan.Bodies )
		{
			var solid = body.Solid;
			var surface = Host.GetSurface( solid.SurfaceProperty, phy.SourceName );
			var target = builder.AddBody( solid.Mass, surface, body.BoneName ).SetBindPose( body.BindPose )
				.SetDamping( solid.Damping, solid.RotationalDamping )
				.SetInertiaScale( solid.Inertia > 0 ? Math.Min( solid.Inertia, 1e18f ) : 1 );
			foreach ( var convex in solid.Convexes )
			{
				// A triangulated convex has at most 6V-12 half-edges; native hulls allow 256.
				// IVR retains authored points instead of expanding the hull or dropping it.
				PhysicsBodyBuilder.HullSimplify? simplify = convex.Length > MaxUnsimplifiedHullVertices
					? new PhysicsBodyBuilder.HullSimplify { Method = PhysicsBodyBuilder.SimplifyMethod.IVR }
					: null;
				target.AddHull( convex, simplify: simplify );
			}
		}
		foreach ( var joint in plan.Joints )
		{
			if ( joint.Swing is { } swing )
			{
				var ball = builder.AddBallJoint( joint.Child, joint.Parent, joint.ChildFrame, joint.ParentFrame, joint.Collision )
					.WithTargetVelocity( Vector3.Zero ).WithMaxTorque( joint.Friction );
				if ( swing < 180 )
					ball.WithSwingLimit( Math.Clamp( swing, 0, 89.1f ) );
				if ( joint.Limits is { } twist && twist.y - twist.x < 359 )
					ball.WithTwistLimit( twist.x, twist.y );
			}
			else if ( joint.Limits is { } limits )
			{
				var hinge = builder.AddHingeJoint( joint.Child, joint.Parent, joint.ChildFrame, joint.ParentFrame, joint.Collision );
				if ( limits.y - limits.x < 359 )
					hinge.WithTwistLimit( limits.x, limits.y );
				if ( joint.Friction > 0 )
					hinge.WithTargetVelocity( Vector3.Zero ).WithMaxTorque( joint.Friction );
			}
			else
				builder.AddFixedJoint( joint.Child, joint.Parent, joint.ChildFrame, joint.ParentFrame, joint.Collision );
		}
		builder.WithMass( plan.Bodies.Sum( body => body.Solid.Mass ) );
		if ( plan.Warnings.Length > 0 )
			Log.Trace( $"{phy.SourceName}: authored physics imported with native fidelity differences: {string.Join( "; ", plan.Warnings )}." );
	}

	private static Transform GetPhysicsBindPose( MdlFile.Matrix3x4 matrix, string path )
	{
		var x = new Vector3( matrix.M11, matrix.M21, matrix.M31 );
		var y = new Vector3( matrix.M12, matrix.M22, matrix.M32 );
		var z = new Vector3( matrix.M13, matrix.M23, matrix.M33 );
		var position = new Vector3( matrix.M14, matrix.M24, matrix.M34 );
		if ( !IsFinite( x ) || !IsFinite( y ) || !IsFinite( z ) || !IsFinite( position )
			|| Math.Abs( x.LengthSquared - 1 ) > 0.002f || Math.Abs( y.LengthSquared - 1 ) > 0.002f
			|| Math.Abs( z.LengthSquared - 1 ) > 0.002f || Vector3.Cross( x, y ).Distance( z ) > 0.002f
			|| Math.Abs( Vector3.Dot( x, y ) ) > 0.002f )
			throw new InvalidDataException( $"{path}: PHY-linked MDL inverse bind is not a finite rigid transform." );
		var inverse = new Transform( position, Rotation.LookAt( x, z ) );
		return inverse.ToLocal( Transform.Zero );
	}

	private static void ValidatePhysics( Model model, PhyFile phy, PhysicsPlan plan )
	{
		if ( plan.Blockers.Length > 0 || plan.Bodies.Length == 0 ) return;
		var physics = model.Physics;
		if ( physics is null || physics.Parts.Count != plan.Bodies.Length || physics.Joints.Count != plan.Joints.Length )
			throw new InvalidDataException( $"{phy.SourceName}: the native builder did not create all authored physics bodies/joints." );
		for ( var i = 0; i < plan.Bodies.Length; i++ )
		{
			var hulls = physics.Parts[i].Hulls.ToArray();
			if ( hulls.Length != plan.Bodies[i].Solid.Convexes.Count || hulls.Any( hull => hull.GetPoints().Count() < 4 ) )
				throw new InvalidDataException( $"{phy.SourceName}: the native builder could not cook all convexes in solid {i} ('{plan.Bodies[i].Solid.Name}'): expected {plan.Bodies[i].Solid.Convexes.Count}, got {hulls.Length}, point counts [{string.Join( ", ", hulls.Select( hull => hull.GetPoints().Count() ) )}]." );
		}
	}

	private static PhyFile.CollisionPair GetPhysicsCollisionPair( int first, int second ) => new( Math.Min( first, second ), Math.Max( first, second ) );
}
