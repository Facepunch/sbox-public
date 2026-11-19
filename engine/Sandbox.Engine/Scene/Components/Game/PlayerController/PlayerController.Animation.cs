using Sandbox.Audio;

namespace Sandbox;

public sealed partial class PlayerController : Component
{
	SkinnedModelRenderer _renderer;
	bool _jumpAnimationPending;

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

	/// <summary>
	/// Create a child renderer using the Human model for the player's body.
	/// </summary>
	[Button( icon: "add" )]
	[Property, Feature( "Animator" ), Tint( EditorTint.Green ), ShowIf( "ShowCreateBodyRenderer", true )]
	public void CreateBodyRenderer()
	{
		var body = new GameObject( true, "Body" );
		body.Parent = GameObject;

		Renderer = body.AddComponent<SkinnedModelRenderer>();
		Renderer.Model = Model.Load( "models/citizen_human/human.vmdl" );
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

		if ( renderer.Animation.Graph is not null )
		{
			if ( !renderer.Animation.IsValid )
				return;

			renderer.Animation.Parameters.TrySet( "b_jump", _jumpAnimationPending );
		}

		_jumpAnimationPending = false;
	}

	bool IsFirstPersonViewer => UseCameraControls && Scene.Camera.IsValid() && !IsProxy
		&& (!ThirdPerson || _cameraDistance < 20);

	readonly List<ModelRenderer> _bodyRenderers = new();
	readonly List<ModelRenderer> _visibilityRenderers = new();
	HashSet<ModelRenderer> _visibilityOverrides = new();
	HashSet<ModelRenderer> _visibilityOverridesNext = new();

	readonly HashSet<GameObject> _viewerTagged = new();
	readonly HashSet<GameObject> _viewerTagTargets = new();
	readonly List<GameObject> _viewerTagRemovals = new();

	/// <summary>
	/// The hidden body parts carry the "viewer" tags while hidden in first person.
	/// Every renderer, effect and light below it inherits the tag, and the viewer's camera excludes it.
	/// </summary>
	void ApplyViewerTags()
	{
		foreach ( var go in _viewerTagged )
		{
			if ( !_viewerTagTargets.Contains( go ) ) _viewerTagRemovals.Add( go );
		}
		foreach ( var go in _viewerTagRemovals )
		{
			if ( go.IsValid() ) go.Tags.Remove( "viewer" );
			_viewerTagged.Remove( go );
		}
		_viewerTagRemovals.Clear();

		foreach ( var go in _viewerTagTargets )
		{
			if ( _viewerTagged.Contains( go ) || !go.IsValid() || go.Tags.Has( "viewer", false ) ) continue;
			go.Tags.Add( "viewer" );
			_viewerTagged.Add( go );
		}
	}

	/// <summary>
	/// The owner's tags are serialized into network create/refresh messages.
	/// Clear them from a proxy so only the owner hides their own body and held items.
	/// </summary>
	void ClearRemoteViewerTags( GameObject body )
	{
		if ( !IsProxy || !UseCameraControls ) return;

		if ( body.IsValid() ) body.Tags.Remove( "viewer" );
		if ( !Renderer.IsValid() || !Renderer.Model.IsValid() ) return;

		var bones = Renderer.Model.Bones.AllBones;
		for ( int i = 0; i < bones.Count; i++ )
		{
			var go = Renderer.GetBoneObject( bones[i] );
			if ( go.IsValid() ) go.Tags.Remove( "viewer" );
		}
		foreach ( var go in Renderer.attachmentToGameObject.Values )
		{
			if ( go.IsValid() ) go.Tags.Remove( "viewer" );
		}
	}

	void RestoreBodyVisibility()
	{
		_viewerTagTargets.Clear();
		ApplyViewerTags();

		foreach ( var renderer in _visibilityOverrides )
			RestoreRendererVisibility( renderer );
		_visibilityOverrides.Clear();

		ClearFirstPersonBodyCopies();
		ClearFirstPersonShadowCopies();
	}

	static void RestoreRendererVisibility( ModelRenderer renderer )
	{
		if ( !renderer.IsValid() ) return;

		// Rebuild what we may have changed - the scene object's tags and shadow flag - as ModelRenderer sets them.
		var so = renderer.SceneObject;
		if ( !so.IsValid() ) return;

		so.Tags.SetFrom( renderer.GameObject.Tags );
		var renderType = renderer.RenderType;
		so.Flags.CastShadows = renderType is ModelRenderer.ShadowRenderType.On or ModelRenderer.ShadowRenderType.ShadowsOnly;
	}

	/// <summary>
	/// Hide the local player's body from their own first person view.
	/// Runs in pre-render so it lands after any game code that changed the renderers this frame.
	/// </summary>
	bool UpdateBodyVisibility()
	{
		var body = Renderer.IsValid() ? Renderer.GameObject : GameObject;
		if ( Scene.IsEditor ) return false;

		ClearRemoteViewerTags( body );

		if ( !IsFirstPersonViewer || !body.IsValid() )
		{
			if ( _viewerTagged.Count > 0 || _visibilityOverrides.Count > 0 || _firstPersonBodyCopies.Count > 0 || _firstPersonShadowCopies.Count > 0 )
				RestoreBodyVisibility();
			return false;
		}

		bool firstPersonBody = PrepareFirstPersonBody();
		if ( firstPersonBody ) CollectFirstPersonHiddenAttachments();
		else ClearFirstPersonBodyCopies();

		bool hideModels = HideBodyInFirstPerson || firstPersonBody;
		_viewerTagTargets.Clear();
		if ( HideBodyInFirstPerson ) _viewerTagTargets.Add( body );
		else if ( firstPersonBody ) CollectFirstPersonViewerTagTargets( body );
		ApplyViewerTags();

		if ( hideModels && CastShadowsInFirstPerson ) UpdateFirstPersonShadowCopies( body );
		else ClearFirstPersonShadowCopies();

		_visibilityOverridesNext.Clear();
		_visibilityRenderers.Clear();
		body.Components.GetAll( _visibilityRenderers, FindMode.InSelf | FindMode.InDescendants | FindMode.Enabled );
		foreach ( var renderer in _visibilityRenderers )
		{
			if ( ApplyRendererVisibility( renderer, hideModels ) )
				_visibilityOverridesNext.Add( renderer );
		}
		_visibilityRenderers.Clear();

		// Anything we overrode last frame but not this frame (removed, reparented, disabled, setting changed).
		foreach ( var renderer in _visibilityOverrides )
		{
			if ( !_visibilityOverridesNext.Contains( renderer ) )
				RestoreRendererVisibility( renderer );
		}

		(_visibilityOverrides, _visibilityOverridesNext) = (_visibilityOverridesNext, _visibilityOverrides);
		return firstPersonBody;
	}

	bool ApplyRendererVisibility( ModelRenderer renderer, bool hideModels )
	{
		var so = renderer.SceneObject;
		if ( !so.IsValid() ) return false;

		if ( hideModels )
		{
			if ( !so.Tags.Has( "viewer" ) ) so.Tags.Add( "viewer" );
			return true;
		}

		// A body that stays visible (no first person body available) with first person shadows disabled.
		if ( CastShadowsInFirstPerson ) return false;
		if ( so.Flags.CastShadows ) so.Flags.CastShadows = false;
		return true;
	}

	/// <summary>
	/// True when something between this object and the body has a viewer tag the game set, not us.
	/// </summary>
	bool HasGameViewerTag( GameObject go, GameObject body )
	{
		for ( ; go.IsValid() && go != body; go = go.Parent )
		{
			if ( go.Tags.Has( "viewer", false ) && !_viewerTagged.Contains( go ) ) return true;
		}
		return false;
	}
}
