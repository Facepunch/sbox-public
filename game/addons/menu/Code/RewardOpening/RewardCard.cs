using Sandbox;

/// <summary>
/// A 3D reward card that flies out of the crate and hovers in front of the camera.
/// Uses spring physics for idle rotation, hover interaction, and click selection.
/// Rendered as a WorldPanel on a plane.
/// </summary>
public sealed class RewardCard : Component
{
	/// <summary>
	/// The reward item this card represents.
	/// </summary>
	public RewardItem Item { get; set; }

	/// <summary>
	/// Whether this card has been selected by the player.
	/// </summary>
	public bool IsSelected { get; set; }

	/// <summary>
	/// The target position this card should settle at in world space.
	/// </summary>
	public Vector3 TargetPosition { get; set; }

	/// <summary>
	/// The target rotation this card should face (generally toward camera).
	/// </summary>
	public Rotation TargetRotation { get; set; }

	/// <summary>
	/// Callback when this card is clicked (selected/deselected).
	/// </summary>
	public Action<RewardCard> OnClicked { get; set; }

	// Spring physics for position
	[Property] public float PositionStiffness { get; set; } = 120f;
	[Property] public float PositionDamping { get; set; } = 14f;

	// Spring physics for rotation
	[Property] public float RotationStiffness { get; set; } = 80f;
	[Property] public float RotationDamping { get; set; } = 10f;

	// Idle rotation wobble
	[Property] public float IdleRotationSpeed { get; set; } = 0.8f;
	[Property] public float IdleRotationAmount { get; set; } = 3f;

	// Click push spring
	[Property] public float ClickPushDistance { get; set; } = 8f;
	[Property] public float ClickSpringStiffness { get; set; } = 400f;
	[Property] public float ClickSpringDamping { get; set; } = 18f;

	// Internal state
	private Vector3 _velocity;
	private float _pushOffset;
	private float _pushVelocity;
	private float _idleTime;
	private bool _isHovered;
	private bool _wasHovered;
	private float _hoverScale;
	private Vector3 _startPosition;
	private Rotation _startRotation;
	private bool _launched;
	private float _launchTime;

	/// <summary>
	/// How long the card takes to fly from the crate to its target position.
	/// </summary>
	private const float LaunchDuration = 0.6f;

	protected override void OnStart()
	{
		_startPosition = WorldPosition;
		_startRotation = WorldRotation;
		_launchTime = Time.Now;
		_launched = true;
	}

	protected override void OnUpdate()
	{
		float dt = Time.Delta;
		_idleTime += dt;

		UpdateLaunchOrSpring( dt );
		UpdatePushSpring( dt );
		UpdateHover();
		UpdateClickDetection();
		ApplyTransform();
	}

	/// <summary>
	/// During launch, interpolate from start to target. After launch, use spring physics.
	/// </summary>
	private void UpdateLaunchOrSpring( float dt )
	{
		if ( _launched )
		{
			float elapsed = Time.Now - _launchTime;
			float t = MathF.Min( elapsed / LaunchDuration, 1f );

			// Ease out cubic
			float ease = 1f - MathF.Pow( 1f - t, 3f );

			WorldPosition = Vector3.Lerp( _startPosition, TargetPosition, ease );

			// Smoothly rotate toward facing the camera during launch
			var camera = Scene.Camera;
			if ( camera != null )
			{
				var toCamera = (camera.WorldPosition - WorldPosition).Normal;
				var targetRot = Rotation.LookAt( toCamera, Vector3.Up );
				WorldRotation = Rotation.Slerp( _startRotation, targetRot, ease );
			}

			if ( t >= 1f )
			{
				_launched = false;
				WorldPosition = TargetPosition;
			}
			return;
		}

		// Spring toward target position (with idle wobble offset)
		var idleOffset = Vector3.Up * MathF.Sin( _idleTime * IdleRotationSpeed * 2f ) * 0.5f;
		var goalPos = TargetPosition + idleOffset;

		var posError = goalPos - WorldPosition;
		var springForce = posError * PositionStiffness - _velocity * PositionDamping;
		_velocity += springForce * dt;
		WorldPosition += _velocity * dt;
	}

	/// <summary>
	/// Push the card forward (toward camera) on click, spring it back.
	/// </summary>
	private void UpdatePushSpring( float dt )
	{
		float target = 0f;
		float springForce = (target - _pushOffset) * ClickSpringStiffness - _pushVelocity * ClickSpringDamping;
		_pushVelocity += springForce * dt;
		_pushOffset += _pushVelocity * dt;
	}

	private void UpdateHover()
	{
		var camera = Scene.Camera;
		if ( camera == null ) return;

		var ray = camera.ScreenPixelToRay( Mouse.Position );

		// Simple sphere check around the card
		var toCard = WorldPosition - ray.Position;
		var proj = Vector3.Dot( toCard, ray.Forward );
		if ( proj > 0 )
		{
			var closestPoint = ray.Position + ray.Forward * proj;
			var dist = (closestPoint - WorldPosition).Length;
			_isHovered = dist < 12f;
		}
		else
		{
			_isHovered = false;
		}

		// Smooth hover scale
		float targetScale = _isHovered ? 1.08f : 1f;
		_hoverScale = MathX.Lerp( _hoverScale, targetScale, Time.Delta * 12f );

		// Play hover sound on enter
		if ( _isHovered && !_wasHovered )
		{
			Sound.Play( "ui.button.over" );
		}
		_wasHovered = _isHovered;
	}

	private void UpdateClickDetection()
	{
		if ( !_isHovered ) return;
		if ( _launched ) return;

		if ( Input.Pressed( "attack1" ) )
		{
			// Play click sound
			Sound.Play( "ui.button.press" );

			// Push the card in toward the camera and spring it back
			_pushVelocity = -ClickPushDistance * 40f;

			OnClicked?.Invoke( this );

			// Sync selection state to the panel component
			SyncPanelSelection();
		}
	}

	/// <summary>
	/// Updates the RewardCardPanel's IsSelected to match this card's state.
	/// </summary>
	public void SyncPanelSelection()
	{
		var panel = GameObject.Components.Get<RewardCardPanel>();
		if ( panel != null )
		{
			panel.IsSelected = IsSelected;
		}
	}

	private void ApplyTransform()
	{
		if ( _launched ) return;

		var camera = Scene.Camera;
		if ( camera == null ) return;

		// Face the camera with idle wobble
		var toCamera = (camera.WorldPosition - WorldPosition).Normal;
		var baseRot = Rotation.LookAt( toCamera, Vector3.Up );

		// Add idle rotation wobble
		float yawWobble = MathF.Sin( _idleTime * IdleRotationSpeed ) * IdleRotationAmount;
		float pitchWobble = MathF.Cos( _idleTime * IdleRotationSpeed * 0.7f ) * IdleRotationAmount * 0.5f;

		var wobbleRot = baseRot * Rotation.FromYaw( yawWobble ) * Rotation.FromPitch( pitchWobble );

		WorldRotation = wobbleRot;

		// Apply push offset along forward direction (toward camera)
		WorldPosition += toCamera * _pushOffset * Time.Delta;

		// Apply hover scale
		WorldScale = _hoverScale * (IsSelected ? 1.05f : 1f);
	}

	protected override void DrawGizmos()
	{
		Gizmo.Draw.LineSphere( Vector3.Zero, 10f );
	}
}
