using Sandbox.Audio;

namespace Sandbox;

public sealed partial class PlayerController : Component
{
	SkinnedModelRenderer _renderer;

	[Property, FeatureEnabled( "Animator", Icon = "sports_martial_arts", Description = "Automatically derive animations from Velocity/Inputs" )]
	public bool UseAnimatorControls { get; set; } = true;

	/// <summary>
	/// The body will usually be a child object with SkinnedModelRenderer
	/// </summary>
	[Property, Feature( "Animator" )]
	public SkinnedModelRenderer Renderer
	{
		get => _renderer;
		set
		{
			if ( _renderer == value ) return;

			DisableAnimationEvents();

			_renderer = value;

			EnableAnimationEvents();
		}
	}

	/// <summary>
	/// If true we'll show the "create body" button
	/// </summary>
	public bool ShowCreateBodyRenderer => UseAnimatorControls && Renderer is null;

	[Button( icon: "add" )]
	[Property, Feature( "Animator" ), Tint( EditorTint.Green ), ShowIf( "ShowCreateBodyRenderer", true )]
	public void CreateBodyRenderer()
	{
		var body = new GameObject( true, "Body" );
		body.Parent = GameObject;

		Renderer = body.AddComponent<SkinnedModelRenderer>();
		Renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );
	}

	[Property, Feature( "Animator" )] public float RotationAngleLimit { get; set; } = 45.0f;
	[Property, Feature( "Animator" )] public float RotationSpeed { get; set; } = 1.0f;

	[Property, Feature( "Animator" ), Group( "Footsteps" )] public bool EnableFootstepSounds { get; set; } = true;
	[Property, Feature( "Animator" ), Group( "Footsteps" )] public float FootstepVolume { get; set; } = 1;


	[Property, Feature( "Animator" ), Group( "Footsteps" )] public MixerHandle FootstepMixer { get; set; }

	/// <summary>
	/// How strongly to look in the eye direction with our eyes
	/// </summary>
	[Property, Feature( "Animator" ), Group( "Aim" ), Order( 1001 ), Range( 0, 1 )] public float AimStrengthEyes { get; set; } = 1;

	/// <summary>
	/// How strongly to turn in the eye direction with our head
	/// </summary>
	[Property, Feature( "Animator" ), Group( "Aim" ), Order( 1002 ), Range( 0, 1 )] public float AimStrengthHead { get; set; } = 1;


	/// <summary>
	/// How strongly to turn in the eye direction with our body
	/// </summary>
	[Property, Feature( "Animator" ), Group( "Aim" ), Order( 1003 ), Range( 0, 1 )] public float AimStrengthBody { get; set; } = 1;


	void EnableAnimationEvents()
	{
		if ( Renderer is null ) return;
		Renderer.OnFootstepEvent -= OnFootstepEvent;
		Renderer.OnFootstepEvent += OnFootstepEvent;
	}

	void DisableAnimationEvents()
	{
		if ( Renderer is null ) return;
		Renderer.OnFootstepEvent -= OnFootstepEvent;
	}

	/// <summary>
	/// Update the animation for this renderer. This will update the body rotation etc too.
	/// </summary>
	public void UpdateAnimation( SkinnedModelRenderer renderer )
	{
		if ( !renderer.IsValid() ) return;

		// TODO: move to MoveMode?
		// TODO: frame rate dependent

		renderer.LocalPosition = bodyDuckOffset;
		bodyDuckOffset = bodyDuckOffset.LerpTo( 0, Time.Delta * 5.0f );

		Mode?.UpdateAnimator( renderer );
	}

	void UpdateBodyVisibility()
	{
		if ( !UseCameraControls ) return;
		if ( Scene.Camera is not CameraComponent cam ) return;

		// are we looking through this GameObject?
		bool viewer = !ThirdPerson;
		viewer = viewer && HideBodyInFirstPerson;
		viewer = viewer && !IsProxy;

		if ( !IsProxy && _cameraDistance < 20 )
		{
			viewer = true;
		}

		if ( IsProxy )
		{
			viewer = false;
		}

		var go = Renderer?.GameObject ?? GameObject;

		if ( go.IsValid() )
		{
			go.Tags.Set( "viewer", viewer );
		}
	}
}
