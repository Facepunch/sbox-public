namespace Sandbox;

public sealed partial class PlayerController : Component
{
	const float _movementSkin = 0.05f;
	const float _traceSeparation = 0.03125f; // Native DIST_EPSILON.

	/// <summary>
	/// Return an aabb representing the body
	/// </summary>
	public BBox BodyBox( float scale = 1.0f, float heightScale = 1.0f )
	{
		var radius = BodyRadius * 0.5f * scale;
		var height = CurrentHeight * heightScale;
		var up = UpDirection;
		var center = up * height * 0.5f;
		var extents = new Vector3( radius ) + up.Abs() * (height * 0.5f - radius);

		return new BBox( center - extents, center + extents );
	}

	/// <summary>
	/// Trace the aabb body from one position to another and return the result
	/// </summary>
	public SceneTraceResult TraceBody( Vector3 from, Vector3 to, float scale = 1.0f, float heightScale = 1.0f )
	{
		return Scene.Trace.Box( BodyBox( scale, heightScale ), from, to )
								.IgnoreGameObjectHierarchy( GameObject )
								.WithCollisionRules( Tags )
								.Run();
	}

	internal SceneTraceResult TraceMovement( Vector3 from, Vector3 to, float skin = _movementSkin, bool includeGroundClearance = false )
	{
		var result = TraceFeetMovement( from, to, skin, includeGroundClearance, out var offset ).Run();
		result.StartPosition -= offset;
		result.EndPosition -= offset;

		if ( !BodyCollider.IsValid() || !BodyCollider.Enabled || result.StartedSolid )
			return result;

		var upper = TraceUpperMovement( from, to, skin ).Run();
		return upper.StartedSolid || upper.Fraction < result.Fraction ? upper : result;
	}

	internal SceneTraceResult TraceStaticMovement( Vector3 from, Vector3 to, PhysicsShape previousContact, Vector3 previousNormal, bool includeGroundClearance = false )
	{
		var result = TraceMovement( from, to, 0, includeGroundClearance );
		// Perpendicular probes distinguish grazing contacts from actual penetration.
		if ( result.Hit )
			RefineStaticContact( ref result, from, to, result.Shape, UpDirection, includeGroundClearance, matchNormal: false );
		RefineStaticContact( ref result, from, to, previousContact, previousNormal, includeGroundClearance, matchNormal: true );
		return result;
	}

	bool TryProbeSlideContact( Vector3 position, PhysicsShape shape, Vector3 direction, bool includeGroundClearance, bool matchNormal, out SceneTraceResult surface )
	{
		var probeOffset = direction * 0.125f;
		foreach ( var hit in TraceFeetMovement( position + probeOffset, position - probeOffset, 0, includeGroundClearance, out var offset ).UseHitPosition().RunAll() )
		{
			if ( hit.Shape != shape || hit.StartedSolid || (matchNormal && hit.Normal.Dot( direction ) < 0.9999f) ) continue;
			surface = hit;
			// Reconstruct the contact from its hull face to avoid accumulating sweep error.
			var face = Scene.Trace.Ray( hit.HitPosition + hit.Normal * 0.125f, hit.HitPosition - hit.Normal * 0.125f )
				.IgnoreGameObjectHierarchy( GameObject ).WithCollisionRules( Tags ).UseHitPosition().Run();
			surface.StartPosition -= offset;
			surface.EndPosition -= offset;
			var normalAlongProbe = face.Normal.Dot( direction );
			if ( face.Hit && !face.StartedSolid && face.Shape == shape && face.Normal.Dot( hit.Normal ) > 0.9999f && normalAlongProbe > 0.001f )
			{
				var feet = FeetCollider;
				var extents = feet.Scale * feet.WorldScale.Abs() * 0.5f;
				var clearance = (feet.WorldTransform.PointToWorld( feet.Center ) - WorldPosition - offset).Dot( UpDirection );
				extents += (feet.WorldRotation.Inverse * UpDirection).Abs() * clearance;
				var support = (feet.WorldRotation.Inverse * face.Normal).Abs().Dot( extents );
				var separation = (position + offset - face.HitPosition).Dot( face.Normal ) - support;
				surface.Normal = face.Normal;
				surface.EndPosition = position - direction * ((separation - _traceSeparation) / normalAlongProbe);
			}
			return true;
		}
		surface = default;
		return false;
	}

	SceneTraceResult TraceMovementIgnoringShape( Vector3 from, Vector3 to, PhysicsShape ignoredShape, bool includeGroundClearance )
	{
		var result = new SceneTraceResult { StartPosition = from, EndPosition = to, Fraction = 1 };
		void Consider( SceneTraceResult hit, Vector3 offset )
		{
			if ( hit.Shape == ignoredShape || (!hit.StartedSolid && hit.Fraction >= result.Fraction) ) return;
			result = hit;
			result.StartPosition -= offset;
			result.EndPosition -= offset;
		}
		foreach ( var hit in TraceFeetMovement( from, to, 0, includeGroundClearance, out var offset ).RunAll() ) Consider( hit, offset );
		if ( BodyCollider.IsValid() && BodyCollider.Enabled )
			foreach ( var hit in TraceUpperMovement( from, to, 0 ).RunAll() ) Consider( hit, Vector3.Zero );
		return result;
	}

	void RefineStaticContact( ref SceneTraceResult result, Vector3 from, Vector3 to, PhysicsShape shape, Vector3 direction, bool includeGroundClearance, bool matchNormal )
	{
		if ( !shape.IsValid() || !shape.IsHullShape || shape.Body?.BodyType != PhysicsBodyType.Static
			|| !shape.SurfaceVelocity.IsNearZeroLength || direction.Dot( UpDirection ) <= 0 ) return;
		if ( !TryProbeSlideContact( from, shape, direction, includeGroundClearance, matchNormal, out var contact )
			|| contact.Normal.Dot( UpDirection ) <= 0 || Mode.IsStandableSurface( contact ) ) return;

		var movement = to - from;
		var offsetToPlane = (contact.EndPosition - from).Dot( contact.Normal );
		var separation = _traceSeparation - offsetToPlane;
		if ( separation <= 0.00001f ) return;
		var approach = movement.Dot( contact.Normal );
		if ( result.Shape == shape && (result.StartedSolid || approach > -separation) )
			result = TraceMovementIgnoringShape( from, to, shape, includeGroundClearance );

		if ( approach > -separation || result.StartedSolid ) return;
		var fraction = MathF.Max( 0, offsetToPlane / approach );
		if ( fraction >= 1 || fraction > result.Fraction ) return;
		if ( result.Hit && fraction == result.Fraction && (result.Normal.Dot( UpDirection ) <= 0 || Mode.IsStandableSurface( result )) ) return;
		if ( fraction > 0 && !TryProbeSlideContact( from + movement * fraction, shape, contact.Normal, includeGroundClearance, true, out _ ) ) return;

		result = contact;
		result.StartPosition = from;
		result.EndPosition = from + movement * fraction;
		result.Fraction = fraction;
	}

	SceneTrace TraceFeetMovement( Vector3 from, Vector3 to, float skin, bool includeGroundClearance, out Vector3 offset )
	{
		var feet = FeetCollider;
		offset = feet.WorldTransform.PointToWorld( feet.Center ) - WorldPosition;
		var extents = feet.Scale * feet.WorldScale.Abs() * 0.5f;
		if ( includeGroundClearance )
		{
			// Detect low risers before they enter the raised feet's speculative contacts.
			var localUp = feet.WorldRotation.Inverse * UpDirection;
			// Box2D's cast slop still needs a gap above the supporting floor.
			var floorGap = Scene.Is2D ? _stepClearance * 0.5f : 0;
			var clearance = MathF.Max( 0, offset.Dot( UpDirection ) - extents.Dot( localUp.Abs() ) - floorGap );
			offset -= UpDirection * (clearance * 0.5f);
			extents += localUp.Abs() * (clearance * 0.5f);
		}
		var inset = MathF.Min( skin, MathF.Min( extents.x, MathF.Min( extents.y, extents.z ) ) * 0.25f );
		extents -= new Vector3( inset );
		return Scene.Trace.Box( new BBox( -extents, extents ), from + offset, to + offset )
			.Rotated( feet.WorldRotation )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithCollisionRules( Tags );
	}

	SceneTrace TraceUpperMovement( Vector3 from, Vector3 to, float skin )
	{
		var body = BodyCollider;
		var radius = body.Radius * MathF.Abs( body.WorldTransform.UniformScale );
		var capsule = new Capsule(
			body.WorldTransform.PointToWorld( body.Start ) - WorldPosition,
			body.WorldTransform.PointToWorld( body.End ) - WorldPosition,
			radius - MathF.Min( skin, radius * 0.25f ) );
		return Scene.Trace.Capsule( capsule, from, to )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithCollisionRules( Tags );
	}

	SceneTraceResult TraceStepMovement( Vector3 from, Vector3 to )
	{
		var result = TraceMovement( from, to, includeGroundClearance: true );
		// Uneven ground can overlap the nominal feet while the raised movement shape is clear.
		return result.StartedSolid || (result.Hit && Mode.IsStandableSurface( result )) ? TraceMovement( from, to ) : result;
	}

	bool TryFindContactStepLift( Vector3 from, Vector3 direction, float maxLift, SceneTraceResult obstruction, out float lift, out float groundOffset )
	{
		lift = 0;
		groundOffset = 0;
		var physicsContact = !Scene.Is2D && GroundUsesContactFriction && _groundShape.IsValid();
		if ( (!Scene.Is2D && !physicsContact) || !obstruction.StartedSolid )
			return false;

		// Only recover shallow contact penetration while moving into the riser.
		var retreat = from - direction * (_groundedFeetLift + _skin);
		var approach = default( SceneTraceResult );
		if ( physicsContact )
		{
			bool CheckApproach( SceneTrace trace )
			{
				foreach ( var hit in trace.RunAll() )
				{
					if ( hit.StartedSolid )
					{
						if ( hit.Body != _groundShape.Body )
							return false;
						continue;
					}
					if ( Mode.IsStandableSurface( hit ) || hit.Normal.Dot( direction ) >= 0 )
						continue;
					if ( !approach.Hit || hit.Fraction < approach.Fraction )
						approach = hit;
				}
				return true;
			}

			// Floor penetration must not hide a separate riser in the same compound body.
			if ( !CheckApproach( TraceFeetMovement( retreat, from, _movementSkin, false, out _ ) )
				|| (BodyCollider.IsValid() && BodyCollider.Enabled && !CheckApproach( TraceUpperMovement( retreat, from, _movementSkin ) )) )
				return false;
		}
		else
		{
			approach = TraceStepMovement( retreat, from );
		}
		if ( !approach.Hit || approach.StartedSolid || approach.Normal.Dot( direction ) >= 0
			|| (physicsContact ? Mode.IsStandableSurface( approach ) : approach.Shape != obstruction.Shape) )
			return false;
		if ( physicsContact )
			obstruction = approach;

		// Measure height from the supporting pose, not the solver's floor penetration.
		if ( !TryFindGround( retreat, 4, 2, out var ground, out var support ) )
			return false;
		var supportOffset = (support - from).Dot( UpDirection );
		if ( physicsContact )
			supportOffset -= (from - retreat).Dot( ground.Normal ) / ground.Normal.Dot( UpDirection );
		groundOffset = MathF.Max( 0, supportOffset );
		if ( groundOffset > _groundedFeetLift + _skin )
			return false;

		bool IsContactShape( SceneTraceResult hit ) => physicsContact
			? hit.Body == _groundShape.Body || hit.Body == obstruction.Body
			: hit.Shape == obstruction.Shape;

		maxLift += groundOffset;
		var available = maxLift;
		bool CheckUpward( SceneTrace trace )
		{
			foreach ( var hit in trace.RunAll() )
			{
				if ( hit.StartedSolid )
				{
					if ( !IsContactShape( hit ) )
						return false;
					continue;
				}
				available = MathF.Min( available, MathF.Max( 0, hit.Distance - _movementSkin ) );
			}
			return true;
		}

		var top = from + UpDirection * maxLift;
		var hasUpper = BodyCollider.IsValid() && BodyCollider.Enabled;
		if ( !CheckUpward( TraceFeetMovement( from, top, _movementSkin, false, out _ ) )
			|| (hasUpper && !CheckUpward( TraceUpperMovement( from, top, _movementSkin ) ))
			|| available <= _skin )
			return false;

		var foundContact = false;
		bool CheckExit( SceneTrace trace )
		{
			foreach ( var hit in trace.RunAll() )
			{
				if ( hit.StartedSolid || !IsContactShape( hit ) )
					return false;
				// A tilted riser's side may permit upward escape before its top is reached.
				if ( !Mode.IsStandableSurface( hit )
					&& !(physicsContact && hit.Shape == obstruction.Shape
						&& hit.Normal.Dot( UpDirection ) > 0 && hit.Normal.Dot( direction ) < 0) )
					return false;
				foundContact = true;
			}
			return true;
		}

		// The reverse sweep must enter only verified contacts through faces permitting upward escape.
		top = from + UpDirection * available;
		if ( !CheckExit( TraceFeetMovement( top, from, _movementSkin, false, out _ ) )
			|| (hasUpper && !CheckExit( TraceUpperMovement( top, from, _movementSkin ) ))
			|| !foundContact )
			return false;

		lift = available;
		return true;
	}

	bool TryFindGround( Vector3 position, float lift, float drop, out SceneTraceResult ground, out Vector3 landing, bool requireContact = false, bool preciseDrop = false )
	{
		var feet = FeetCollider;
		var up = UpDirection;
		var localUp = feet.WorldRotation.Inverse * up;
		var extents = feet.Scale * feet.WorldScale.Abs() * 0.5f;
		// Probe the footprint, not the whole standing hull.
		var verticalExtent = extents.Dot( localUp.Abs() );
		extents -= localUp.Abs() * MathF.Max( verticalExtent - 2, 0 );
		var center = WithoutVertical( feet.WorldTransform.PointToWorld( feet.Center ) - WorldPosition )
			+ up * extents.Dot( localUp.Abs() );
		// Physical landings need actual feet overlap, not the static step planner's lookahead margin.
		var contactInset = MathF.Min( _skin * 2, MathF.Min( extents.x, MathF.Min( extents.y, extents.z ) ) * 0.25f );
		var supportMargin = Scene.Is2D ? 0 : requireContact ? -contactInset : _stepClearance;
		var supportExtents = extents + (Vector3.One - localUp.Abs()) * supportMargin;
		// Pass below the supporting plane rather than ending a maximum-height drop exactly on it.
		var queryDrop = drop + _skin * 2;
		SceneTrace GroundTrace( float inset )
		{
			var probeExtents = supportExtents - (Vector3.One - localUp.Abs()) * inset;
			return Scene.Trace.Box( new BBox( -probeExtents, probeExtents ), position + center + up * lift, position + center - up * queryDrop )
				.Rotated( feet.WorldRotation )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithCollisionRules( Tags )
				.UseHitPosition();
		}

		bool TryLanding( ref SceneTraceResult hit, bool confirmSurface, out Vector3 target )
		{
			target = position;
			if ( !hit.Hit || hit.StartedSolid )
				return false;

			var sweptPosition = hit.EndPosition - center;
			// A shape cast's edge normal is a separating axis, not necessarily the supporting surface normal.
			if ( confirmSurface && hit.Normal.Dot( up ) < 0.9999f )
			{
				var surface = Scene.Trace.Ray( hit.HitPosition + up * _stepClearance, hit.HitPosition - up * _stepClearance )
					.IgnoreGameObjectHierarchy( GameObject )
					.WithCollisionRules( Tags )
					.UseHitPosition()
					.Run();
				if ( !surface.Hit || surface.StartedSolid )
					return false;

				hit = surface;
			}

			var normalUp = hit.Normal.Dot( up );
			if ( !hit.Hit || hit.StartedSolid || normalUp <= 0 || !Mode.IsStandableSurface( hit ) )
				return false;

			var localNormal = feet.WorldRotation.Inverse * hit.Normal;
			var support = localNormal.Abs().Dot( extents ) - hit.Normal.Dot( center );
			var height = ((hit.HitPosition - position).Dot( hit.Normal ) + support + _skin) / normalUp;
			// A tilted triangle's plane may not support the entire footprint. Retain the swept support height.
			if ( confirmSurface && normalUp < 0.9999f )
				height = (sweptPosition - position).Dot( up ) + _skin / normalUp;

			// Physical supports need contact tolerance; static landings use strict reach.
			if ( preciseDrop && hit.Body?.BodyType == PhysicsBodyType.Static
				&& height - _skin / normalUp <= -drop )
				return false;

			if ( height > lift + _skin / normalUp || height < -drop - _skin )
				return false;

			target += up * height;
			return true;
		}

		ground = GroundTrace( 0 ).Run();
		if ( TryLanding( ref ground, true, out landing ) )
		{
			ground.StartPosition = position + up * lift;
			ground.EndPosition = landing;
			return true;
		}

		if ( !ground.Hit && !IsOnGround )
			return false;

		var inset = MathF.Min( _stepClearance, BodyRadius * MathF.Abs( WorldTransform.UniformScale ) * 0.1f );
		var trace = GroundTrace( supportMargin + inset );
		var found = false;
		landing = position;
		foreach ( var candidate in trace.RunAll() )
		{
			var hit = candidate;
			if ( !TryLanding( ref hit, true, out var target ) || (found && target.Dot( up ) <= landing.Dot( up )) )
				continue;

			found = true;
			ground = hit;
			landing = target;
		}

		// A shape cast can lose a very small overlap at a ledge. Keep the contact footprint supported.
		var a = WithoutVertical( feet.WorldRotation.Forward * supportExtents.x );
		var b = WithoutVertical( feet.WorldRotation.Right * supportExtents.y );
		for ( int i = 0; i < 4; ++i )
		{
			var offset = WithoutVertical( center ) + a * ((i & 1) == 0 ? -1 : 1) + b * ((i & 2) == 0 ? -1 : 1);
			var hit = Scene.Trace.Ray( position + offset + up * lift, position + offset - up * queryDrop )
				.IgnoreGameObjectHierarchy( GameObject )
				.WithCollisionRules( Tags )
				.UseHitPosition()
				.Run();
			if ( !TryLanding( ref hit, false, out var candidate ) || (found && candidate.Dot( up ) <= landing.Dot( up )) )
				continue;

			found = true;
			ground = hit;
			landing = candidate;
		}
		if ( found )
		{
			ground.StartPosition = position + up * lift;
			ground.EndPosition = landing;
		}
		return found;
	}
}
