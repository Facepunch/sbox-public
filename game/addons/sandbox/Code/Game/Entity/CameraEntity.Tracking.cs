using Sandbox.Npcs;

public enum CameraTrackingTarget
{
	[Title( "#entity.camera_tracking.owner" )] Owner,
	[Title( "#entity.camera_tracking.player" )] NearestPlayer,
	[Title( "#entity.camera_tracking.npc" )] NearestNpc,
	[Title( "#entity.camera_tracking.player_npc" )] NearestPlayerOrNpc,
	[Title( "#entity.camera_tracking.entity" )] NearestEntity
}

public sealed partial class CameraEntity
{
	[Property, ClientEditable, ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public bool Tracking { get; set; }

	[Property, ClientEditable, Title( "#entity.property.camera_tracking_target" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public CameraTrackingTarget TrackingTarget { get; set; } = CameraTrackingTarget.Owner;

	[Property, ClientEditable, Range( 0.1f, 20f ), Title( "#entity.property.camera_tracking_speed" ), Description( "#entity.description.camera_tracking_speed" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public float TrackingLerpSpeed { get; set; } = 5f;

	[Property, ClientEditable, Range( 0f, 8192f ), Title( "#entity.property.camera_tracking_range" ), Description( "#entity.description.camera_tracking_range" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public float TrackingRange { get; set; } = 2048f;

	[Property, ClientEditable, Title( "#entity.property.camera_tracking_return" ), Description( "#entity.description.camera_tracking_return" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public bool ReturnToOriginal { get; set; } = true;

	[Property, ClientEditable, Title( "#entity.property.camera_tracking_offset" ), Description( "#entity.description.camera_tracking_offset" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public Vector3 TrackingOffset { get; set; }

	[Property, ClientEditable, Title( "#entity.property.camera_tracking_zoom" ), Description( "#entity.description.camera_tracking_zoom" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" )]
	public bool ZoomToFit { get; set; }

	[Property, ClientEditable, Range( -60f, 60f ), Title( "#entity.property.camera_tracking_zoom_offset" ), Description( "#entity.description.camera_tracking_zoom_offset" ), ToggleGroup( "Tracking", Label = "#entity.group.camera_tracking" ), ShowIf( nameof( ZoomToFit ), true )]
	public float ZoomOffset { get; set; }

	Component _trackingTarget;
	Vector3 _smoothedTarget;
	float _trackingFov;
	Rotation _trackingRotation;
	Transform _trackingView;
	bool _trackingInitialized;
	RealTimeSince _sinceTargetSearch = 999f;
	CameraTrackingTarget _lastTrackingTarget;
	float _lastTrackingRange = -1;

	Transform PhysicalView => Feed.IsValid() ? Feed.PhysicalViewTransform : WorldTransform;

	/// <summary>
	/// The field of view to draw with: zoomed to fit the target while tracking with <see cref="ZoomToFit"/>, else <see cref="Fov"/>.
	/// </summary>
	public float ViewFov => Tracking && _trackingInitialized ? _trackingFov : ClampedFov;

	GameObject _head;
	GameObject Head => _head.IsValid() ? _head : _head = GameObject.Children.FirstOrDefault( x => x.Name == "head" );

	Transform HeadMount( GameObject head ) => new( head.WorldPosition, WorldRotation );

	void ResetTracking()
	{
		_trackingInitialized = false;
		_trackingTarget = null;
		_sinceTargetSearch = 999f;
		if ( Head is { } head && head.IsValid() ) head.LocalRotation = Rotation.Identity;
		if ( Feed.IsValid() ) Feed.UseLensView( ClampedFov );
	}

	void UpdateTracking()
	{
		if ( !Tracking )
		{
			if ( _trackingInitialized ) ResetTracking();
			return;
		}

		var head = Head;
		var hasHead = head.IsValid();
		var mount = hasHead ? HeadMount( head ) : PhysicalView;

		float range = float.IsFinite( TrackingRange ) ? TrackingRange.Clamp( 0f, 8192f ) : 2048f;
		float speed = float.IsFinite( TrackingLerpSpeed ) ? TrackingLerpSpeed.Clamp( 0.1f, 20f ) : 5f;
		float blend = 1f - MathF.Exp( -speed * MathF.Max( Time.Delta, 0f ) );

		bool hasTarget = TryTrackingPoint( _trackingTarget, out var targetPoint, out var targetRadius )
			&& (targetPoint - mount.Position).LengthSquared <= range * range;
		// Keep following the selected actor every frame, but avoid a full scene scan
		// every frame for every camera. Lost/out-of-range targets are replaced immediately.
		if ( _sinceTargetSearch >= 0.15f || _lastTrackingTarget != TrackingTarget || _lastTrackingRange != range
			|| _trackingTarget is not null && !hasTarget )
		{
			_trackingTarget = FindTrackingTarget( mount.Position, range );
			_sinceTargetSearch = 0;
			_lastTrackingTarget = TrackingTarget;
			_lastTrackingRange = range;
			hasTarget = TryTrackingPoint( _trackingTarget, out targetPoint, out targetRadius );
		}

		// aim at a point beside the target, in world space - above a player's head, say
		var offset = TrackingOffset.IsFinite ? TrackingOffset : Vector3.Zero;
		targetPoint += offset;

		if ( !_trackingInitialized )
		{
			_trackingFov = ClampedFov;
			_trackingRotation = mount.Rotation;
			float distance = hasTarget ? MathF.Max( (targetPoint - mount.Position).Length, 32f ) : 256f;
			_smoothedTarget = mount.Position + mount.Forward * distance;
			_trackingInitialized = true;
		}

		if ( hasTarget )
		{
			// Deliberately retain this point across target changes instead of snapping to
			// the newly chosen actor. Exponential interpolation is frame-rate independent.
			_smoothedTarget = _smoothedTarget.LerpTo( targetPoint, blend );
			var direction = _smoothedTarget - mount.Position;
			if ( direction.LengthSquared > 0.01f )
				_trackingRotation = Rotation.Slerp( _trackingRotation, Rotation.LookAt( direction.Normal ), blend );
		}
		else
		{
			if ( ReturnToOriginal )
				_trackingRotation = Rotation.Slerp( _trackingRotation, mount.Rotation, blend );
			// Reacquisition starts from the current aim, including while returning home.
			float distance = MathF.Max( (_smoothedTarget - mount.Position).Length, 32f );
			_smoothedTarget = mount.Position + _trackingRotation.Forward * distance;
		}

		// zoom eases at the same speed as the aim, back out to the set fov when there's nothing to fit.
		// Measured to the target itself, not the offset point beside it
		var fitFov = ZoomToFit && hasTarget ? FitFov( (targetPoint - offset).Distance( mount.Position ), targetRadius ) : ClampedFov;
		_trackingFov = MathX.Lerp( _trackingFov, fitFov, blend );

		if ( hasHead )
		{
			// a world rotation, so it aims right however the body is lying or tumbling
			head.WorldRotation = _trackingRotation;
			_trackingView = PhysicalView;
		}
		else _trackingView = new Transform( mount.Position, _trackingRotation );
	}

	Component FindTrackingTarget( Vector3 origin, float range )
	{
		Component nearest = null;
		float closest = range * range;
		void Consider( Component candidate )
		{
			if ( !TrySearchPoint( candidate, out var point ) ) return;
			float distance = (point - origin).LengthSquared;
			if ( distance > closest || nearest is not null && distance == closest ) return;
			closest = distance;
			nearest = candidate;
		}

		if ( TrackingTarget == CameraTrackingTarget.Owner )
		{
			if ( GetComponent<Ownable>()?.Owner is { } owner ) Consider( Player.FindForConnection( owner ) );
			return nearest;
		}

		var entities = TrackingTarget == CameraTrackingTarget.NearestEntity;
		if ( entities || TrackingTarget is CameraTrackingTarget.NearestPlayer or CameraTrackingTarget.NearestPlayerOrNpc )
			foreach ( var player in Scene.GetAllComponents<Player>() ) Consider( player );
		if ( entities || TrackingTarget is CameraTrackingTarget.NearestNpc or CameraTrackingTarget.NearestPlayerOrNpc )
			foreach ( var npc in Scene.GetAllComponents<Npc>() ) Consider( npc );

		// anything else physical: props, vehicles, contraptions, other cameras. One body per object, not this camera
		if ( entities )
		{
			var seen = new HashSet<GameObject>();
			foreach ( var body in Scene.GetAllComponents<Rigidbody>() )
			{
				var root = body.GameObject.Root;
				if ( root == GameObject.Root || !seen.Add( root ) ) continue;
				if ( root.GetComponent<Player>() is not null || root.GetComponent<Npc>() is not null ) continue;
				Consider( body );
			}
		}

		return nearest;
	}

	/// <summary>
	/// Where a candidate is, for finding the nearest - its origin for a physics object, so a scene full of props doesn't
	/// measure every bounding box a few times a second.
	/// </summary>
	bool TrySearchPoint( Component candidate, out Vector3 point )
	{
		if ( candidate is Rigidbody body && body.IsValid() && body.Active && body.Scene == Scene )
		{
			point = body.GameObject.Root.WorldPosition;
			return point.IsFinite;
		}

		return TryTrackingPoint( candidate, out point, out _ );
	}

	/// <summary>
	/// The point to aim at on a target, and how big it is (the radius of its bounds) for <see cref="ZoomToFit"/>.
	/// </summary>
	bool TryTrackingPoint( Component target, out Vector3 point, out float radius )
	{
		point = default;
		radius = 0f;
		if ( !target.IsValid() || !target.Active || target.Scene != Scene ) return false;
		switch ( target )
		{
			case Player player when player.Health > 0 && player.Controller.IsValid():
				// Track the player's physical eyes, never their remote camera view.
				point = player.EyeTransform.Position;
				radius = BoundsRadius( player.GameObject );
				break;
			case Npc npc when npc.Health > 0:
				if ( npc.Renderer.IsValid() && npc.Renderer.GetAttachment( "eyes" ) is { } eyes )
					point = eyes.Position;
				else if ( npc.Renderer.IsValid() && npc.Renderer.Model is { } model && model.Bounds.Size.IsFinite )
					point = npc.Renderer.WorldTransform.PointToWorld( model.Bounds.Center );
				else point = npc.WorldPosition;
				radius = BoundsRadius( npc.GameObject );
				break;
			case Rigidbody body:
				// the middle of the whole object, not wherever its origin happens to be
				var bounds = body.GameObject.Root.GetBounds();
				point = bounds.Size.IsFinite && bounds.Size.LengthSquared > 0f ? bounds.Center : body.GameObject.Root.WorldPosition;
				radius = bounds.Size.IsFinite ? bounds.Size.Length * 0.5f : 0f;
				break;
			default: return false;
		}
		return point.IsFinite;
	}

	static float BoundsRadius( GameObject go )
	{
		var size = go.GetBounds().Size;
		return size.IsFinite ? size.Length * 0.5f : 0f;
	}

	/// <summary>
	/// The fov that fits a target of this radius at this distance, plus <see cref="ZoomOffset"/>.
	/// </summary>
	float FitFov( float distance, float radius )
	{
		if ( !float.IsFinite( distance ) || !float.IsFinite( radius ) || radius <= 0f ) return ClampedFov;

		// half the angle the target's bounding sphere covers from here - from inside it, as wide as it goes
		var half = distance > radius ? MathF.Asin( radius / distance ) : MathF.PI * 0.5f;

		// the lens's fov runs across the width, and a widescreen frame is shorter than it is wide, so fit the height
		// and a little more so the target isn't touching the edges
		const float aspect = 16f / 9f;
		const float margin = 1.1f;
		var across = 2f * MathF.Atan( MathF.Tan( half ) * aspect ) * (180f / MathF.PI) * margin;

		var zoom = float.IsFinite( ZoomOffset ) ? ZoomOffset.Clamp( -60f, 60f ) : 0f;
		return (across + zoom).Clamp( 1f, 150f );
	}
}
