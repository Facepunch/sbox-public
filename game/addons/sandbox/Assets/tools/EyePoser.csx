/// <summary>
/// Poses a character's eyes using a world target, the player, or the tool control panel.
/// </summary>
[Title( "#tool.name.eyeposer" )]
[Icon( "👀" )]
[ClassName( "eyeposer" )]
[Group( "#tool.group.posing" )]
public sealed class EyePoser : ToolMode
{
	SkinnedModelRenderer _selected;
	SkinnedModelRenderer _panelTarget;
	Vector3 _lastPanelSettings;
	TimeSince _lastPanelUpdate;

	/// <summary>
	/// Horizontal gaze angle relative to the selected character's head, in degrees.
	/// </summary>
	[Property, Range( -45f, 45f ), Title( "#tool.setting.eyeposer.horizontal" )]
	public float Horizontal { get; set; }

	/// <summary>
	/// Vertical gaze angle relative to the selected character's head, in degrees.
	/// </summary>
	[Property, Range( -35f, 35f ), Title( "#tool.setting.eyeposer.vertical" )]
	public float Vertical { get; set; }

	/// <summary>
	/// Negative values turn the eyes inward. Positive values turn them outward.
	/// </summary>
	[Property, Range( -1f, 1f ), Step( 0.01f ), Title( "#tool.setting.eyeposer.strabismus" )]
	public float Strabismus { get; set; }

	Vector3 PanelSettings => new Vector3( Horizontal, Vertical, Strabismus );

	protected override string ToolgunScreenText => _selected.IsValid() ? "#tool.screen.eyeposer.aim" : Name;

	protected override float ToolgunScreenScrollSpeed => base.ToolgunScreenScrollSpeed * (_selected.IsValid() ? 2f : 1f);

	/// <summary>
	/// Describes which characters the tool can pose.
	/// </summary>
	public override string Description => "#tool.hint.eyeposer.description";

	/// <summary>
	/// Shows the next step for selecting a character and aiming its eyes.
	/// </summary>
	public override string PrimaryAction => _selected.IsValid() ? "#tool.hint.eyeposer.target" : "#tool.hint.eyeposer.select";

	/// <summary>
	/// Aims the character under the crosshair at the player.
	/// </summary>
	public override string UsePrimaryAction => "#tool.hint.eyeposer.look_at_me";

	/// <summary>
	/// Selects the character to pose from the tool settings panel.
	/// </summary>
	public override string SecondaryAction => "#tool.hint.eyeposer.panel";

	/// <summary>
	/// Restores the eyes of the character under the crosshair.
	/// </summary>
	public override string ReloadAction => "#tool.hint.eyeposer.reset";

	/// <summary>
	/// Handles character selection, aiming and reset on the player's tool gun.
	/// </summary>
	public override void OnControl()
	{
		base.OnControl();

		var primary = Input.Pressed( "attack1" );
		var secondary = Input.Pressed( "attack2" );
		var reload = Input.Pressed( "reload" );
		if ( !primary && !secondary && !reload && !_selected.IsValid() ) return;

		var hit = TraceSelect();
		if ( primary )
		{
			if ( Input.Down( "use" ) )
			{
				PoseUnderCrosshair( hit, ToolInput.Primary );
			}
			else
			{
				SelectOrAim( hit );
			}
		}

		if ( secondary ) SelectForPanel( hit );
		if ( reload ) PoseUnderCrosshair( hit, ToolInput.Reload );

		if ( _selected.IsValid() && _selected.SceneModel.IsValid() )
		{
			// Keep the guides on the crosshair even when it passes over open sky.
			var aim = Player.EyeTransform;
			var point = hit.IsValid() ? hit.WorldPosition() : aim.Position + aim.Forward * ToolTraceDistance;
			var model = _selected.SceneModel;
			var left = FindEyeBone( _selected.Model, true );
			var right = FindEyeBone( _selected.Model, false );
			if ( left is not null && right is not null )
			{
				DebugOverlay.Line( model.GetBoneWorldTransform( left.Index ).Position, point, Color.Green, overlay: true );
				DebugOverlay.Line( model.GetBoneWorldTransform( right.Index ).Position, point, Color.Green, overlay: true );
			}
		}
	}

	protected override void OnDisabled()
	{
		_selected = null;
		_panelTarget = null;
		base.OnDisabled();
	}

	/// <summary>
	/// Keeps a poseable ragdoll in place while the player holds Use to aim its eyes.
	/// </summary>
	public override bool ReservesUseFor( GameObject target ) => FindRenderer( target ).IsValid();

	static SkinnedModelRenderer FindRenderer( GameObject target )
	{
		if ( !target.IsValid() || target.Tags.Has( "world" ) ) return null;

		var root = target.Network.Active ? target.Network.RootGameObject : target.Root;
		if ( root.GetComponentInParent<Player>().IsValid() ) return null;

		// TODO: Support HL2 gaze when the Source mount exposes the eyes_updown and eyes_rightleft flex controllers.
		// The mount already imports eyelid morphs for blinking.
		return root.GetComponentsInChildren<SkinnedModelRenderer>().FirstOrDefault( x =>
			!x.BoneMergeTarget.IsValid() && x.Model is not null &&
			FindEyeBone( x.Model, true )?.Parent is not null && FindEyeBone( x.Model, false )?.Parent is not null );
	}

	static BoneCollection.Bone FindEyeBone( Model model, bool left )
	{
		var bones = model?.Bones;
		return bones?.GetBone( left ? "eye_L" : "eye_R" )
			?? bones?.GetBone( left ? "l_eye" : "r_eye" )
			?? bones?.GetBone( left ? "EyeL" : "EyeR" )
			?? bones?.GetBone( left ? "Eye_L_001" : "Eye_R_001" );
	}

	void SelectOrAim( SelectionPoint hit )
	{
		_panelTarget = null;
		if ( !hit.IsValid() ) return;

		if ( !_selected.IsValid() )
		{
			_selected = FindRenderer( hit.GameObject );
			if ( _selected.IsValid() ) ShootEffects( hit );
			return;
		}

		ApplyPose( _selected, hit.WorldPosition(), ToolInput.Primary, Vector3.Zero );
		_selected = null;
		ShootEffects( hit );
	}

	void PoseUnderCrosshair( SelectionPoint hit, ToolInput input )
	{
		var target = FindRenderer( hit.GameObject );
		_panelTarget = null;
		_selected = null;
		if ( !target.IsValid() ) return;

		ApplyPose( target, Player.EyeTransform.Position, input, Vector3.Zero );
		ShootEffects( hit );
	}

	void SelectForPanel( SelectionPoint hit )
	{
		_selected = null;
		_panelTarget = FindRenderer( hit.GameObject );
		if ( !_panelTarget.IsValid() ) return;

		UpdatePanelPose();
		ShootEffects( hit );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		// The panel can have focus while the gun isn't receiving attack input.
		if ( _panelTarget.IsValid() && PanelSettings != _lastPanelSettings && _lastPanelUpdate > 0.06f )
			UpdatePanelPose();
	}

	void UpdatePanelPose()
	{
		_lastPanelSettings = PanelSettings;
		_lastPanelUpdate = 0;
		ApplyPose( _panelTarget, Vector3.Zero, ToolInput.Secondary, _lastPanelSettings );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ApplyPose( SkinnedModelRenderer target, Vector3 point, ToolInput input, Vector3 panelSettings )
	{
		if ( !target.IsValid() || target.IsProxy || !point.IsFinite || !panelSettings.IsFinite ) return;

		var root = target.GameObject.Network.Active ? target.GameObject.Network.RootGameObject : target.GameObject.Root;
		if ( root.GetComponentInParent<Player>().IsValid() || root.Tags.Has( "world" ) ) return;

		var left = FindEyeBone( target.Model, true );
		var right = FindEyeBone( target.Model, false );
		if ( left?.Parent is null || right?.Parent is null || !FireToolAction( input ) ) return;

		var hadBoneObjects = target.CreateBoneObjects;
		if ( input == ToolInput.Reload )
		{
			target.GetBoneObject( left )?.GetComponent<EyePoseOverride>()?.Restore();
			target.GetBoneObject( right )?.GetComponent<EyePoseOverride>()?.Restore();
		}
		else
		{
			if ( !target.SceneModel.IsValid() ) return;
			var leftParent = target.SceneModel.GetBoneWorldTransform( left.Parent.Index );
			var rightParent = target.SceneModel.GetBoneWorldTransform( right.Parent.Index );
			target.CreateBoneObjects = true;
			var leftEye = target.GetBoneObject( left );
			var rightEye = target.GetBoneObject( right );
			if ( !leftEye.IsValid() || !rightEye.IsValid() ) return;

			var center = (leftParent.PointToWorld( leftEye.LocalPosition ) + rightParent.PointToWorld( rightEye.LocalPosition )) * 0.5f;

			// Rust rigs like the sheep and shark point each eye bone out of the side of the head.
			var sideFacing = target.Model.ResourcePath?.StartsWith( "models/rust/animals/", StringComparison.OrdinalIgnoreCase ) == true
				&& left.LocalTransform.Forward.y > 0.5f && right.LocalTransform.Forward.y < -0.5f;
			AimEye( target, leftEye, left, leftParent, point, sideFacing, input, center, panelSettings );
			AimEye( target, rightEye, right, rightParent, point, sideFacing, input, center, panelSettings );
		}

		if ( hadBoneObjects )
		{
			var leftEye = target.GetBoneObject( left );
			var rightEye = target.GetBoneObject( right );
			if ( leftEye.IsValid() ) root.Network.Refresh( leftEye );
			if ( rightEye.IsValid() ) root.Network.Refresh( rightEye );
		}
		else if ( target.CreateBoneObjects )
		{
			root.Network.Refresh();
		}
		FirePostToolAction( input );
	}

	static void AimEye( SkinnedModelRenderer renderer, GameObject eye, BoneCollection.Bone bone, Transform parent, Vector3 point, bool sideFacing, ToolInput input, Vector3 center, Vector3 panelSettings )
	{
		if ( !eye.IsValid() ) return;

		// The bind pose tells us which way the character faces even when the eye bone has rotated axes.
		var parentBind = bone.Parent.LocalTransform;
		var eyePosition = parent.PointToWorld( eye.LocalPosition );
		var headRotation = parent.Rotation * parentBind.Rotation.Inverse;

		// Some rigs model the pupil off-center. Aim its optical axis at the target.
		var opticalAxis = renderer.GetAttachment( bone.Name + "_optical_axis" );
		var restGaze = Rotation.Identity;
		var eyeFromGaze = bone.LocalTransform.Rotation;
		if ( opticalAxis.HasValue )
		{
			var eyeWorld = renderer.SceneModel.GetBoneWorldTransform( bone.Index );
			eyeFromGaze = opticalAxis.Value.Rotation.Inverse * eyeWorld.Rotation;
		}
		else if ( sideFacing )
		{
			restGaze = Rotation.LookAt( bone.LocalTransform.Forward, Vector3.Up );
			eyeFromGaze = restGaze.Inverse * eyeFromGaze;
		}

		var frame = headRotation * restGaze;
		var direction = point - eyePosition;
		if ( input == ToolInput.Secondary )
		{
			var panel = Rotation.From( -panelSettings.y.Clamp( -35f, 35f ), -panelSettings.x.Clamp( -45f, 45f ), 0f );
			var spread = panelSettings.z.Clamp( -1f, 1f );
			var distance = 1000f * MathF.Pow( 10000f, -MathF.Abs( spread ) );

			// Move the target closer to cross the eyes, or mirror it behind the head to spread them apart.
			direction = frame * panel.Forward * distance + (center - eyePosition) * (spread > 0f ? -1f : 1f);
		}

		if ( direction.LengthSquared < 0.0001f ) return;
		var angles = Rotation.LookAt( frame.Inverse * direction ).Angles();
		var gaze = restGaze * Rotation.From( angles.pitch.Clamp( -35f, 35f ), angles.yaw.Clamp( -45f, 45f ), 0f );

		var pose = eye.GetComponent<EyePoseOverride>();
		if ( !pose.IsValid() )
		{
			pose = eye.AddComponent<EyePoseOverride>();
			pose.PreviousRotation = eye.LocalRotation;
			pose.WasProcedural = eye.Flags.Contains( GameObjectFlags.ProceduralBone );
		}

		eye.Flags |= GameObjectFlags.ProceduralBone;
		eye.LocalRotation = parentBind.Rotation.Inverse * gaze * eyeFromGaze;
	}
}
