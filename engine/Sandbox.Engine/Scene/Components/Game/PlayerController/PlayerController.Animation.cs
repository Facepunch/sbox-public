using Sandbox.Audio;
using Sandbox.Citizen;

namespace Sandbox;

public sealed partial class PlayerController : Component, BaseCombatWeapon.IEvents, BaseCombatWeapon.IHolder
{
	SkinnedModelRenderer _renderer;
	bool _jumpAnimationPending;

	// The gestures waiting for a weapon's next animation update, and the weapon they belong to.
	BaseCombatWeapon _gestureWeapon;
	[Flags]
	private enum PendingGestures
	{
		None = 0,
		Attack = 1,
		Reload = 2,
		Deploy = 4,
		ReloadCancel = 8
	}

	PendingGestures _pendingGestures;

	// The weapon whose last update left a gesture parameter on.
	BaseCombatWeapon _playingGestureWeapon;

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

	/// <summary>
	/// Pose the body for a weapon this player holds and play its pending deploy, attack and reload gestures.
	/// The weapon calls this every frame while it's deployed, whether or not <see cref="UseAnimatorControls"/> is on.
	/// </summary>
	internal void UpdateWeaponAnimation( BaseCombatWeapon weapon, SkinnedModelRenderer renderer )
	{
		var pending = _gestureWeapon == weapon;

		if ( renderer.Animation.Graph is not null )
		{
			// Not running yet: the gestures wait for it.
			if ( !renderer.Animation.IsValid )
				return;

			var parameters = renderer.Animation.Parameters;
			parameters.TrySet( "b_attack", pending && (_pendingGestures & PendingGestures.Attack) != 0 );
			parameters.TrySet( "b_reload", pending && (_pendingGestures & PendingGestures.Reload) != 0 );
			parameters.TrySet( "b_deploy", pending && (_pendingGestures & PendingGestures.Deploy) != 0 );
			parameters.TrySet( "b_reload_cancel", pending && (_pendingGestures & PendingGestures.ReloadCancel) != 0 );
			_playingGestureWeapon = pending && _pendingGestures != PendingGestures.None ? weapon : null;
		}

		if ( pending )
			ClearWeaponGestures();

		UpdateWeaponHold( weapon, renderer );
	}

	/// <summary>
	/// Pose the body for a held weapon: its <see cref="BaseCombatWeapon.HoldType"/> when the graph has that option,
	/// else the closest option to it or to its <see cref="BaseCombatWeapon.HoldFallback"/>, else the name as it is.
	/// </summary>
	void UpdateWeaponHold( BaseCombatWeapon weapon, SkinnedModelRenderer renderer )
	{
		var holdType = weapon.HoldType;
		var fallback = weapon.HoldFallback;

		// A weapon without a hold leaves the parameter to the game.
		if ( string.IsNullOrEmpty( holdType ) && string.IsNullOrEmpty( fallback ) )
			return;

		if ( renderer.Animation.Graph is { } graph )
		{
			var parameters = renderer.Animation.Parameters;
			parameters.TrySet( "holdtype", CitizenHoldTypes.Resolve( holdType, fallback, graph, HasHoldOption ) );
			parameters.TrySet( "holdtype_handedness", (int)weapon.Handedness );
			return;
		}

#pragma warning disable CS0618
		renderer.Set( "holdtype", CitizenHoldTypes.Resolve( holdType, fallback, renderer, HasLegacyHoldOption ) );
		renderer.Set( "holdtype_handedness", (int)weapon.Handedness );
#pragma warning restore CS0618
	}

	// Setting an id the graph doesn't list still succeeds, so check its described options. A graph that describes none could have any.
	static bool HasHoldOption( Sandbox.Animation.Graph graph, string name )
	{
		// Indexed: this runs every frame for every held weapon, and foreach over the interfaces boxes
		var parameters = graph.Parameters;
		for ( int i = 0; i < parameters.Count; i++ )
		{
			var parameter = parameters[i];
			if ( !string.Equals( parameter.Name, "holdtype", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var options = parameter.Options;
			if ( options.Count == 0 )
				return true;

			for ( int j = 0; j < options.Count; j++ )
			{
				if ( string.Equals( options[j], name, StringComparison.OrdinalIgnoreCase ) )
					return true;
			}

			return false;
		}

		return true;
	}

	// Until the model has spawned its graph could have any option.
	static bool HasLegacyHoldOption( SkinnedModelRenderer renderer, string name )
	{
#pragma warning disable CS0618
		if ( renderer.SceneModel?.AnimationGraph is not { IsValid: true } graph )
			return true;

		return graph.TryGetEnumOptionIndex( "holdtype", name, out _ );
#pragma warning restore CS0618
	}

	void ClearWeaponGestures()
	{
		_gestureWeapon = null;
		_pendingGestures = PendingGestures.None;
	}

	void BaseCombatWeapon.IEvents.OnWeaponEvent( BaseCombatWeapon weapon, string name )
	{
		// Events reach every ancestor, so a weapon held by something we carry isn't ours.
		if ( !weapon.IsValid() || !ReferenceEquals( weapon.Holder, this ) )
			return;

		// An earlier listener may have switched weapons while this event was on its way, so go by what the weapon
		// is now: one that was put away only holsters, one in hand does everything else.
		var inHand = weapon.GameObject.Active;

		if ( IsEvent( name, WeaponEvents.Holster ) )
		{
			if ( !inHand )
				ReleaseWeapon( weapon );

			return;
		}

		if ( !inHand )
			return;

		if ( IsEvent( name, WeaponEvents.Deploy ) )
		{
			ClearWeaponGestures();
			_gestureWeapon = weapon;
			_pendingGestures = PendingGestures.Deploy;
			return;
		}

		var attack = IsEvent( name, WeaponEvents.Attack );
		var reload = IsEvent( name, WeaponEvents.Reload );
		var reloadCancel = IsEvent( name, WeaponEvents.ReloadCancel );
		if ( !attack && !reload && !reloadCancel )
			return;

		if ( attack && Application.IsDedicatedServer )
			return;

		if ( _gestureWeapon != weapon )
		{
			ClearWeaponGestures();
			_gestureWeapon = weapon;
		}

		if ( attack )
		{
			_pendingGestures |= PendingGestures.Attack;
			SetLegacyTrigger( "b_attack" );
		}
		else if ( reload )
		{
			_pendingGestures |= PendingGestures.Reload;
			SetLegacyTrigger( "b_reload" );
		}
		else
		{
			_pendingGestures = (_pendingGestures & ~PendingGestures.Reload) | PendingGestures.ReloadCancel;
		}
	}

	/// <summary>
	/// A weapon was put away: drop its pending gestures, switch off any still playing and let go of its hold.
	/// </summary>
	void ReleaseWeapon( BaseCombatWeapon weapon )
	{
		if ( _gestureWeapon == weapon )
			ClearWeaponGestures();

		// Only this weapon's own gestures are switched off, another weapon's are its to finish.
		var gesturesOn = _playingGestureWeapon == weapon;
		if ( gesturesOn )
			_playingGestureWeapon = null;

		if ( !Renderer.IsValid() )
			return;

		var hasHold = !string.IsNullOrEmpty( weapon.HoldType ) || !string.IsNullOrEmpty( weapon.HoldFallback );

		if ( Renderer.Animation.Graph is not null )
		{
			var parameters = Renderer.Animation.Parameters;

			if ( hasHold )
				parameters.TrySet( "holdtype", "none" );

			if ( gesturesOn )
			{
				parameters.TrySet( "b_attack", false );
				parameters.TrySet( "b_reload", false );
				parameters.TrySet( "b_deploy", false );
				parameters.TrySet( "b_reload_cancel", false );
			}

			return;
		}

#pragma warning disable CS0618
		if ( hasHold )
			Renderer.Set( "holdtype", "none" );
#pragma warning restore CS0618
	}

	Ray BaseCombatWeapon.IHolder.CalculateAimRay( BaseCombatWeapon weapon )
	{
		// In third person aim where the camera looks, not where the head points.
		if ( ThirdPerson && Scene.Camera.IsValid() )
			return Scene.Camera.WorldTransform.ForwardRay;

		return EyeTransform.ForwardRay;
	}

	static bool IsEvent( string name, string expected ) => string.Equals( name, expected, StringComparison.OrdinalIgnoreCase );

	// AnimGraph1 bodies take the one-shots as they happen instead of through the pending flags.
	void SetLegacyTrigger( string parameter )
	{
#pragma warning disable CS0618
		if ( Renderer is { Animation.Graph: null } renderer )
			renderer.Set( parameter, true );
#pragma warning restore CS0618
	}

	bool IsFirstPersonViewer => UseCameraControls && Scene.Camera.IsValid() && !IsProxy
		&& (!ThirdPerson || _cameraDistance < 20);

	// This frame's renderers on the body, found once and shared by the visibility pass and the first person copies
	readonly List<ModelRenderer> _bodyRenderers = new();
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
		so.Flags.CastShadows = renderType != ModelRenderer.ShadowRenderType.Off;
		so.Flags.CastShadowsTwoSided = renderType == ModelRenderer.ShadowRenderType.TwoSided;
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
			_bodyRenderers.Clear();
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

		_bodyRenderers.Clear();
		body.Components.GetAll( _bodyRenderers, FindMode.InSelf | FindMode.InDescendants | FindMode.Enabled );

		if ( hideModels && CastShadowsInFirstPerson ) UpdateFirstPersonShadowCopies( body );
		else ClearFirstPersonShadowCopies();

		_visibilityOverridesNext.Clear();
		foreach ( var renderer in _bodyRenderers )
		{
			if ( ApplyRendererVisibility( renderer, hideModels ) )
				_visibilityOverridesNext.Add( renderer );
		}

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
