using Sandbox.UI;

/// <summary>
/// Copies and edits hand poses on characters with articulated fingers.
/// </summary>
[Title( "#tool.name.fingerposer" ), Icon( "🖐️" ), ClassName( "fingerposer" ), Group( "#tool.group.posing" )]
public sealed class FingerPoser : ToolMode, ICustomSettingsSheet
{
	static readonly string[] FingerNames = new string[] { "thumb", "index", "middle", "ring", "pinky" };
	static readonly Vector4 Curl = new Vector4( 58, 76, 45, 0 );
	static readonly Vector4 TuckedThumb = new Vector4( 32, 30, 20, 8 );
	static readonly Vector4[] Horns = new[] { TuckedThumb, Vector4.Zero.WithW( -8 ), Curl, Curl, Vector4.Zero.WithW( 12 ) };
	static readonly Vector4[] FourFingerHorns = new[] { TuckedThumb, Horns[1], Curl, Horns[4], Vector4.Zero };

	// XYZ bend the three joints. W spreads the finger sideways at the knukle.
	Vector4[] _pose = new Vector4[5];
	Vector4[] _lastPose = new Vector4[5];
	SkinnedModelRenderer _selected;
	bool _left;

	/// <summary>
	/// Whether to preview the left hand. Preset drafts use a left hand.
	/// </summary>
	public bool PreviewLeftHand => !_selected.IsValid() || _left;

	/// <summary>
	/// The renderer whose model and hand pose are being edited.
	/// </summary>
	public SkinnedModelRenderer PreviewSource => _selected.IsValid() ? _selected : null;

	/// <summary>
	/// Builds the hand preview for this tool or its preset draft.
	/// </summary>
	public void BuildSettingsSheet( Panel body )
	{
		var sheet = body.AddChild<Panel>( "finger-pose-editor" );
		sheet.StyleSheet.Load( "/ui/finger_pose_editor.scss" );
		sheet.AddChild<FingerPoseEditor>( "hand-board" ).Bind( this );

		var shortcuts = sheet.AddChild<Panel>( "hand-shortcuts" );
		var open = Vector4.Zero;

		AddShortcut( shortcuts, "🖐️", new[] { open, open, open, open, open } );
		AddShortcut( shortcuts, "✊", new[] { TuckedThumb, Curl, Curl, Curl, Curl } );
		AddShortcut( shortcuts, "👆", new[] { TuckedThumb, open, Curl, Curl, Curl } );
		AddShortcut( shortcuts, "✌️", new[] { TuckedThumb, open.WithW( -12 ), open.WithW( 10 ), Curl, Curl } );
		AddShortcut( shortcuts, "👍", new[] { new Vector4( -18, -5, 0, -12 ), Curl, Curl, Curl, Curl } );
		AddShortcut( shortcuts, "🤏", new[] { new Vector4( -30, 39, 95, 36 ), new Vector4( 48, 43, 24, -9 ), Curl, Curl, Curl } );
		AddShortcut( shortcuts, "👌", new[] { new Vector4( -29, 50, 60, 16 ), new Vector4( 48, 31, 62, -38 ), open, open.WithW( 5 ), open.WithW( 12 ) } );
		AddShortcut( shortcuts, "🤘", Horns );
	}

	void AddShortcut( Panel row, string emoji, Vector4[] pose )
	{
		var button = row.AddChild<Panel>( "hand-shortcut" );
		button.AddChild<Label>().Text = emoji;
		button.AddEventListener( "onclick", e =>
		{
			FitPoseToHand( pose, PreviewSource?.Model, PreviewLeftHand ).CopyTo( _pose, 0 );
			e.StopPropagation();
		} );
	}

	static Vector4[] FitPoseToHand( Vector4[] pose, Model model, bool left )
	{
		// Older Citizen presets saved the raised pinky too. Picking the hand in-world reads it as zero.
		if ( pose[4] != Vector4.Zero && pose[4] != Horns[4] ) return pose;
		if ( !pose.SequenceEqual( Horns ) && !pose.Take( 4 ).SequenceEqual( FourFingerHorns.Take( 4 ) ) ) return pose;

		var hand = FindHand( model, left );
		if ( hand is null ) return pose;

		// Keep the outermost finger raised when transfering horns between rigs.
		return FindJoint( model, hand, left, 4, 0 ) is null ? FourFingerHorns : Horns;
	}

	/// <summary>
	/// Reads the three bends and knuckle spread displayed for a finger.
	/// </summary>
	public Vector4 GetFingerPose( int finger ) => _pose[finger];

	/// <summary>
	/// Changes a finger through the preview, using the same pose as presets and the toolgun.
	/// </summary>
	public void SetFingerPose( int finger, Vector4 pose ) => _pose[finger] = pose;

	/// <summary>
	/// Bend at the base of the thumb finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.thumb" ), Title( "#tool.finger.base" ), Range( -30, 100 ), Step( 1 )]
	public float ThumbBase
	{
		get => _pose[0].x;
		set => _pose[0] = _pose[0].WithX( value );
	}

	/// <summary>
	/// Bend at the middle joint of the thumb finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.thumb" ), Title( "#tool.finger.middle_joint" ), Range( -30, 100 ), Step( 1 )]
	public float ThumbMiddle
	{
		get => _pose[0].y;
		set => _pose[0] = _pose[0].WithY( value );
	}

	/// <summary>
	/// Bend at the tip of the thumb finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.thumb" ), Title( "#tool.finger.tip" ), Range( -30, 100 ), Step( 1 )]
	public float ThumbTip
	{
		get => _pose[0].z;
		set => _pose[0] = _pose[0].WithZ( value );
	}

	/// <summary>
	/// Sideways rotation at the base of the thumb finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.thumb" ), Title( "#tool.finger.spread" ), Range( -45, 45 ), Step( 1 )]
	public float ThumbSpread
	{
		get => _pose[0].w;
		set => _pose[0] = _pose[0].WithW( value );
	}

	/// <summary>
	/// Bend at the base of the index finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.index" ), Title( "#tool.finger.base" ), Range( -30, 100 ), Step( 1 )]
	public float IndexBase
	{
		get => _pose[1].x;
		set => _pose[1] = _pose[1].WithX( value );
	}

	/// <summary>
	/// Bend at the middle joint of the index finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.index" ), Title( "#tool.finger.middle_joint" ), Range( -30, 100 ), Step( 1 )]
	public float IndexMiddle
	{
		get => _pose[1].y;
		set => _pose[1] = _pose[1].WithY( value );
	}

	/// <summary>
	/// Bend at the tip of the index finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.index" ), Title( "#tool.finger.tip" ), Range( -30, 100 ), Step( 1 )]
	public float IndexTip
	{
		get => _pose[1].z;
		set => _pose[1] = _pose[1].WithZ( value );
	}

	/// <summary>
	/// Sideways rotation at the base of the index finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.index" ), Title( "#tool.finger.spread" ), Range( -45, 45 ), Step( 1 )]
	public float IndexSpread
	{
		get => _pose[1].w;
		set => _pose[1] = _pose[1].WithW( value );
	}

	/// <summary>
	/// Bend at the base of the middle finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.middle" ), Title( "#tool.finger.base" ), Range( -30, 100 ), Step( 1 )]
	public float MiddleBase
	{
		get => _pose[2].x;
		set => _pose[2] = _pose[2].WithX( value );
	}

	/// <summary>
	/// Bend at the middle joint of the middle finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.middle" ), Title( "#tool.finger.middle_joint" ), Range( -30, 100 ), Step( 1 )]
	public float MiddleMiddle
	{
		get => _pose[2].y;
		set => _pose[2] = _pose[2].WithY( value );
	}

	/// <summary>
	/// Bend at the tip of the middle finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.middle" ), Title( "#tool.finger.tip" ), Range( -30, 100 ), Step( 1 )]
	public float MiddleTip
	{
		get => _pose[2].z;
		set => _pose[2] = _pose[2].WithZ( value );
	}

	/// <summary>
	/// Sideways rotation at the base of the middle finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.middle" ), Title( "#tool.finger.spread" ), Range( -45, 45 ), Step( 1 )]
	public float MiddleSpread
	{
		get => _pose[2].w;
		set => _pose[2] = _pose[2].WithW( value );
	}

	/// <summary>
	/// Bend at the base of the ring finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.ring" ), Title( "#tool.finger.base" ), Range( -30, 100 ), Step( 1 )]
	public float RingBase
	{
		get => _pose[3].x;
		set => _pose[3] = _pose[3].WithX( value );
	}

	/// <summary>
	/// Bend at the middle joint of the ring finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.ring" ), Title( "#tool.finger.middle_joint" ), Range( -30, 100 ), Step( 1 )]
	public float RingMiddle
	{
		get => _pose[3].y;
		set => _pose[3] = _pose[3].WithY( value );
	}

	/// <summary>
	/// Bend at the tip of the ring finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.ring" ), Title( "#tool.finger.tip" ), Range( -30, 100 ), Step( 1 )]
	public float RingTip
	{
		get => _pose[3].z;
		set => _pose[3] = _pose[3].WithZ( value );
	}

	/// <summary>
	/// Sideways rotation at the base of the ring finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.ring" ), Title( "#tool.finger.spread" ), Range( -45, 45 ), Step( 1 )]
	public float RingSpread
	{
		get => _pose[3].w;
		set => _pose[3] = _pose[3].WithW( value );
	}

	/// <summary>
	/// Bend at the base of the pinky finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.pinky" ), Title( "#tool.finger.base" ), Range( -30, 100 ), Step( 1 )]
	public float PinkyBase
	{
		get => _pose[4].x;
		set => _pose[4] = _pose[4].WithX( value );
	}

	/// <summary>
	/// Bend at the middle joint of the pinky finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.pinky" ), Title( "#tool.finger.middle_joint" ), Range( -30, 100 ), Step( 1 )]
	public float PinkyMiddle
	{
		get => _pose[4].y;
		set => _pose[4] = _pose[4].WithY( value );
	}

	/// <summary>
	/// Bend at the tip of the pinky finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.pinky" ), Title( "#tool.finger.tip" ), Range( -30, 100 ), Step( 1 )]
	public float PinkyTip
	{
		get => _pose[4].z;
		set => _pose[4] = _pose[4].WithZ( value );
	}

	/// <summary>
	/// Sideways rotation at the base of the pinky finger, in degrees.
	/// </summary>
	[Property, Group( "#tool.finger.pinky" ), Title( "#tool.finger.spread" ), Range( -45, 45 ), Step( 1 )]
	public float PinkySpread
	{
		get => _pose[4].w;
		set => _pose[4] = _pose[4].WithW( value );
	}

	/// <summary>
	/// Explains how to edit a hand in the tool panel.
	/// </summary>
	public override string Description => "#tool.hint.fingerposer.description";

	/// <summary>
	/// Applies the tool panel's pose to the hand under the crosshair.
	/// </summary>
	public override string PrimaryAction => "#tool.hint.fingerposer.apply";

	/// <summary>
	/// Copies the nearest hand's pose into the panel and selects it for live editing.
	/// </summary>
	public override string SecondaryAction => "#tool.hint.fingerposer.select";

	/// <summary>
	/// Restores the hand under the crosshair.
	/// </summary>
	public override string ReloadAction => "#tool.hint.fingerposer.reset";

	/// <summary>
	/// Applies, selects, or restores the hand under the crosshair.
	/// </summary>
	public override void OnControl()
	{
		if ( Input.Pressed( "attack1" ) ) PickHand( ToolInput.Primary );
		if ( Input.Pressed( "attack2" ) ) PickHand( ToolInput.Secondary );
		if ( Input.Pressed( "reload" ) ) PickHand( ToolInput.Reload );
	}

	protected override void OnDisabled()
	{
		_selected = null;
		base.OnDisabled();
	}

	static GameObject TargetRoot( GameObject target )
	{
		return target.Network.Active ? target.Network.RootGameObject : target.Root;
	}

	/// <summary>
	/// Finds the wrist on supported character skeletons.
	/// </summary>
	public static BoneCollection.Bone FindHand( Model model, bool left )
	{
		var side = left ? "L" : "R";
		var bones = model?.Bones;
		return bones?.GetBone( $"hand_{side}" ) ?? bones?.GetBone( $"bip_hand_{side}" )
			?? bones?.GetBone( $"ValveBiped.Bip01_{side}_Hand" ) ?? bones?.GetBone( $"ValveBiped_Bip01_{side}_Hand" )
			?? bones?.GetBone( $"Bip01_{side}_Hand" );
	}

	/// <summary>
	/// Finds the specified finger segment relative to the wrist's naming convention.
	/// </summary>
	public static BoneCollection.Bone FindJoint( Model model, BoneCollection.Bone hand, bool left, int finger, int joint )
	{
		var side = left ? "L" : "R";
		if ( hand.IsNamed( $"hand_{side}" ) || hand.IsNamed( $"bip_hand_{side}" ) )
		{
			var prefix = hand.IsNamed( $"hand_{side}" ) ? "finger" : "bip";
			return model.Bones.GetBone( $"{prefix}_{FingerNames[finger]}_{joint}_{side}" );
		}

		var stem = hand.Name.Substring( 0, hand.Name.Length - 4 );
		var suffix = joint == 0 ? "" : joint.ToString();
		return model.Bones.GetBone( $"{stem}Finger{finger}{suffix}" );
	}

	/// <summary>
	/// Returns the bind-space direction from the index knuckle toward the outside of the hand.
	/// </summary>
	public static Vector3 AcrossPalm( Model model, BoneCollection.Bone hand, bool left )
	{
		var index = FindJoint( model, hand, left, 1, 0 );
		if ( index is null ) return Vector3.Zero;

		for ( int finger = 4; finger >= 2; finger-- )
		{
			var outer = FindJoint( model, hand, left, finger, 0 );
			if ( outer is not null ) return (outer.LocalTransform.Position - index.LocalTransform.Position).Normal;
		}

		return Vector3.Zero;
	}

	/// <summary>
	/// Calculates the joint rotation used by both the tool and the live preview.
	/// </summary>
	public static Rotation PoseOffset( BoneCollection.Bone bone, Vector3 direction, Vector3 across, bool left, int finger, float bend, float spread )
	{
		var spreadAxis = Vector3.Cross( direction, across ).Normal;
		var curlAxis = Vector3.Cross( spreadAxis, direction ).Normal * (left ? 1f : -1f);

		// Thumbs bend across the palm. Their knuckle spread moves them toward or away from it.
		if ( finger == 0 )
		{
			var thumbBend = spreadAxis;
			spreadAxis = curlAxis;
			curlAxis = thumbBend;
		}

		var toBone = bone.LocalTransform.Rotation.Inverse;
		return Rotation.FromAxis( toBone * spreadAxis, spread ) * Rotation.FromAxis( toBone * curlAxis, bend );
	}

	void PickHand( ToolInput input )
	{
		_selected = null;
		var hit = TraceSelect();
		if ( !hit.IsValid() || hit.IsWorld ) return;

		var root = TargetRoot( hit.GameObject );
		if ( root.GetComponentInParent<Player>().IsValid() ) return;

		SkinnedModelRenderer target = null;
		BoneCollection.Bone hand = null;
		var nearest = float.MaxValue;
		foreach ( var renderer in root.GetComponentsInChildren<SkinnedModelRenderer>() )
		{
			if ( renderer.BoneMergeTarget.IsValid() || !renderer.SceneModel.IsValid() ) continue;
			for ( int side = 0; side < 2; side++ )
			{
				var left = side == 0;
				var wrist = FindHand( renderer.Model, left );
				if ( wrist is null ) continue;
				var transform = renderer.SceneModel.GetBoneWorldTransform( wrist.Index );
				var distance = (transform.Position - hit.WorldPosition()).LengthSquared;
				if ( distance >= nearest || AcrossPalm( renderer.Model, wrist, left ).IsNearZeroLength ) continue;

				target = renderer;
				hand = wrist;
				_left = left;
				nearest = distance;
			}
		}

		if ( !target.IsValid() ) return;

		if ( input == ToolInput.Secondary )
		{
			_selected = target;
			for ( int finger = 0; finger < 5; finger++ )
			{
				var angles = Vector4.Zero;
				for ( int joint = 0; joint < 3; joint++ )
				{
					var bone = FindJoint( target.Model, hand, _left, finger, joint );
					if ( bone is null ) continue;
					var saved = target.GetBoneObject( bone )?.GetComponent<FingerPoseJoint>();
					if ( saved is null ) continue;
					angles[joint] = saved.Bend;
					if ( joint == 0 ) angles.w = saved.Spread;
				}

				_pose[finger] = angles;
				_lastPose[finger] = angles;
			}
		}
		else
		{
			var pose = input == ToolInput.Primary ? FitPoseToHand( _pose, target.Model, _left ) : _pose;
			ApplyHand( target, _left, input, pose, 0x1f );
		}

		ShootEffects( hit );
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !_selected.IsValid() ) return;

		int changedFingers = 0;
		for ( int finger = 0; finger < _pose.Length; finger++ )
		{
			if ( _pose[finger] == _lastPose[finger] ) continue;

			changedFingers |= 1 << finger;
			_lastPose[finger] = _pose[finger];
		}

		if ( changedFingers != 0 )
		{
			ApplyHand( _selected, _left, ToolInput.Secondary, _pose, changedFingers );
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	void ApplyHand( SkinnedModelRenderer target, bool left, ToolInput input, Vector4[] pose, int fingers )
	{
		if ( !target.IsValid() || target.IsProxy || target.BoneMergeTarget.IsValid() ) return;
		var root = TargetRoot( target.GameObject );
		if ( root.Tags.Has( "world" ) || root.GetComponentInParent<Player>().IsValid() ) return;

		var hand = FindHand( target.Model, left );
		if ( hand is null || pose is null || pose.Length != 5 || pose.Any( x => !x.IsFinite ) ) return;
		var across = AcrossPalm( target.Model, hand, left );
		if ( across.IsNearZeroLength || !FireToolAction( input ) ) return;

		var hadBoneObjects = target.CreateBoneObjects;
		if ( input != ToolInput.Reload ) target.CreateBoneObjects = true;

		GameObject changedBone = null;
		int structureChanges = 0;
		for ( int finger = 0; finger < 5; finger++ )
		{
			if ( (fingers & (1 << finger)) == 0 ) continue;

			var first = FindJoint( target.Model, hand, left, finger, 0 );
			var second = FindJoint( target.Model, hand, left, finger, 1 );
			if ( first is null || second is null ) continue;

			var direction = (second.LocalTransform.Position - first.LocalTransform.Position).Normal;
			if ( Vector3.Cross( direction, across ).IsNearZeroLength ) continue;

			for ( int joint = 0; joint < 3; joint++ )
			{
				var bone = FindJoint( target.Model, hand, left, finger, joint );
				var go = target.GetBoneObject( bone );
				if ( !go.IsValid() || go.Flags.Contains( GameObjectFlags.Absolute ) ) continue;

				var bend = pose[finger][joint].Clamp( -30f, 100f );
				var spread = joint == 0 ? pose[finger].w.Clamp( -45f, 45f ) : 0f;
				var saved = go.GetComponent<FingerPoseJoint>();
				if ( input == ToolInput.Reload || (bend == 0f && spread == 0f) )
				{
					if ( saved is null ) continue;

					saved.Restore();
					changedBone = go;
					structureChanges++;
					continue;
				}

				if ( saved is null )
				{
					saved = go.AddComponent<FingerPoseJoint>();
					saved.PreviousRotation = go.LocalRotation;
					saved.WasProcedural = go.Flags.Contains( GameObjectFlags.ProceduralBone );
					changedBone = go;
					structureChanges++;
				}

				saved.Bend = bend;
				saved.Spread = spread;
				var rest = saved.WasProcedural ? saved.PreviousRotation : bone.Parent.LocalTransform.Rotation.Inverse * bone.LocalTransform.Rotation;
				go.Flags |= GameObjectFlags.ProceduralBone;
				saved.PosedRotation = rest * PoseOffset( bone, direction, across, left, finger, bend, spread );
			}
		}

		// Only changes to the bone hierarchy or its components need a snapshot.
		// During a drag the joint's synced properties carry the changing pose.
		if ( (!hadBoneObjects && target.CreateBoneObjects) || structureChanges > 1 )
		{
			root.Network.Refresh();
		}
		else if ( structureChanges == 1 )
		{
			root.Network.Refresh( changedBone );
		}

		FirePostToolAction( input );
	}
}

/// <summary>
/// Projects editing handles onto the fingers of a skinned model in a private preview scene.
/// </summary>
public sealed class FingerPoseEditor : ScenePanel
{
	const float HandleFraction = 0.45f;

	FingerPoser _tool;
	CameraComponent _camera;
	DirectionalLight _key;
	SkinnedModelRenderer _model;
	SkinnedModelRenderer _source;
	Model _draftModel;
	Panel[] _handles = new Panel[15];
	// Each non-knuckle joint owns the line from the preceding joint.
	Panel[] _boneLines = new Panel[15];
	BoneCollection.Bone[] _bones;
	GameObject[] _joints;
	Vector3[] _segments = new Vector3[15];
	Vector3[] _directions = new Vector3[5];
	Vector2[] _points = new Vector2[15];
	float _lineScale;

	bool _left;
	Vector3 _across;
	Vector3 _up;
	Vector3 _front;
	Vector3 _center;
	float _orbit;
	float _startOrbit;
	bool _orbiting;
	int _focus = 3;
	int _drag = -1;
	Vector2 _start;
	Vector4 _original;

	/// <summary>
	/// Creates the preview scene for the live tool or preset draft.
	/// </summary>
	public void Bind( FingerPoser tool )
	{
		_tool = tool;
		_bones = new BoneCollection.Bone[15];
		_joints = new Sandbox.GameObject[15];
		RenderOnce = true;
		var scene = RenderScene;
		_camera = new GameObject( scene, true, "Hand camera" ).AddComponent<CameraComponent>();
		_camera.Orthographic = true;
		_camera.BackgroundColor = new Color( 0.14f, 0.19f, 0.23f );
		new GameObject( scene, true, "Fill" ).AddComponent<AmbientLight>().Color = Color.White * 0.6f;
		_key = new GameObject( scene, true, "Key" ).AddComponent<DirectionalLight>();
		_key.LightColor = Color.White * 1.4f;
		_key.Shadows = false;

		for ( int i = 0; i < _boneLines.Length; i++ )
		{
			if ( i % 3 == 0 ) continue;
			_boneLines[i] = AddChild<Panel>( "bone-line hidden" );
		}

		for ( int i = 0; i < _handles.Length; i++ )
		{
			_handles[i] = AddChild<Panel>( "joint unavailable" );
		}
	}

	void SetModel( Model model, SkinnedModelRenderer source, bool left )
	{
		if ( _model.IsValid() ) _model.GameObject.Destroy();
		_source = source;
		_left = left;
		_lineScale = 0f;
		_orbit = 12f;
		_orbiting = false;
		_drag = -1;

		_model = new GameObject( RenderScene, true, "Hand model" ).AddComponent<SkinnedModelRenderer>();
		_model.UseAnimGraph = false;
		_model.Model = model;
		_model.CreateBoneObjects = true;
		if ( source.IsValid() )
		{
			CopyAppearance( source, _model );
			foreach ( var item in source.GameObject.GetComponentsInChildren<SkinnedModelRenderer>() )
			{
				if ( item.BoneMergeTarget != source || !item.Active || FingerPoser.FindHand( item.Model, left ) is null ) continue;

				var clothing = new GameObject( _model.GameObject, true, item.GameObject.Name ).AddComponent<SkinnedModelRenderer>();
				clothing.Model = item.Model;
				clothing.BoneMergeTarget = _model;
				CopyAppearance( item, clothing );
			}
		}

		var wrist = FingerPoser.FindHand( model, left );
		_across = wrist is null ? Vector3.Zero : FingerPoser.AcrossPalm( model, wrist, left );
		for ( int i = 0; i < _bones.Length; i++ )
		{
			var bone = wrist is null ? null : FingerPoser.FindJoint( model, wrist, left, i / 3, i % 3 );
			_bones[i] = bone;
			_joints[i] = _model.GetBoneObject( bone );
			_handles[i].SetClass( "unavailable", bone is null );
			if ( _boneLines[i] is not null )
			{
				_boneLines[i].SetClass( "hidden", bone is null || _bones[i - 1] is null );
			}
		}

		if ( wrist is null || _across.IsNearZeroLength || _bones[3] is null ) return;

		// Anchor the wrist and arm to bind pose so ragdoll motion can't move the hand out of frame.
		for ( var parent = wrist; parent is not null; parent = parent.Parent )
		{
			var go = _model.GetBoneObject( parent );
			go.Flags |= GameObjectFlags.ProceduralBone;
			go.LocalTransform = parent.Parent is null ? parent.LocalTransform : parent.Parent.LocalTransform.ToLocal( parent.LocalTransform );
		}

		var knuckle = _bones[6] ?? _bones[3];
		var origin = wrist.LocalTransform.Position;
		_up = (knuckle.LocalTransform.Position - origin).Normal;
		_front = Vector3.Cross( _up, _across ).Normal * (left ? 1f : -1f);
		var right = Vector3.Cross( _front, _up ).Normal;
		var min = Vector2.Zero;
		var max = Vector2.Zero;

		for ( int i = 0; i < _bones.Length; i++ )
		{
			var bone = _bones[i];
			if ( bone is null ) continue;

			var frame = bone.LocalTransform;
			var next = i % 3 < 2 ? _bones[i + 1] : null;
			var segment = next is null ? (frame.Position - bone.Parent.LocalTransform.Position) * 0.65f : next.LocalTransform.Position - frame.Position;
			if ( i % 3 == 0 ) _directions[i / 3] = segment.Normal;
			_segments[i] = frame.Rotation.Inverse * segment;
			_joints[i].Flags |= GameObjectFlags.ProceduralBone;
			_joints[i].LocalTransform = bone.Parent.LocalTransform.ToLocal( frame );

			var end = frame.Position + segment - origin;
			var projected = new Vector2( Vector3.Dot( end, right ), Vector3.Dot( end, _up ) );
			min = Vector2.Min( min, projected );
			max = Vector2.Max( max, projected );
		}

		var center = (min + max) * 0.5f;
		_center = origin + right * center.x + _up * center.y;
		_camera.OrthographicHeight = MathF.Max( max.x - min.x, max.y - min.y ) * 1.3f;
		_camera.ZNear = _camera.OrthographicHeight * 0.1f;
		_camera.ZFar = _camera.OrthographicHeight * 2f;
		UpdateView();
	}

	static void CopyAppearance( SkinnedModelRenderer source, SkinnedModelRenderer preview )
	{
		preview.BodyGroups = source.BodyGroups;
		preview.MaterialGroup = source.MaterialGroup;
		preview.MaterialOverride = source.MaterialOverride;
		preview.Tint = source.Tint;
	}

	void UpdateView()
	{
		var forward = Rotation.FromAxis( _up, _orbit ) * _front;
		_camera.WorldRotation = Rotation.LookAt( forward, _up );
		_camera.WorldPosition = _center - forward * _camera.OrthographicHeight;
		_key.WorldRotation = Rotation.LookAt( forward - _up * 0.6f + _camera.WorldRotation.Right * 0.35f );
		RenderNextFrame();
	}

	void OrbitTo( Vector2 cursor )
	{
		_orbit = _startOrbit + (cursor.x - _start.x) * 200f / Box.Rect.Width;
		UpdateView();
	}

	Vector2 Project( Vector3 point )
	{
		var offset = point - _center;
		var size = Box.Rect.Size;
		var scale = size.y / _camera.OrthographicHeight;
		return size * 0.5f + new Vector2( Vector3.Dot( offset, _camera.WorldRotation.Right ), -Vector3.Dot( offset, _camera.WorldRotation.Up ) ) * scale;
	}

	void UpdateJoints()
	{
		var scale = ScaleFromScreen;
		bool resized = _lineScale != scale;
		_lineScale = scale;
		bool previousMoved = false;
		for ( int i = 0; i < _bones.Length; i++ )
		{
			var bone = _bones[i];
			if ( bone is null ) continue;

			var finger = i / 3;
			var pose = _tool.GetFingerPose( finger );
			var bend = pose[i % 3].Clamp( -30f, 100f );
			var spread = i % 3 == 0 ? pose.w.Clamp( -45f, 45f ) : 0f;
			bool posed = bend != 0f || spread != 0f;
			var rotation = bone.Parent.LocalTransform.Rotation.Inverse * bone.LocalTransform.Rotation;
			if ( _source.IsValid() && _source.SceneModel.IsValid() )
			{
				var sourceBone = _source.GetBoneObject( bone );
				var saved = sourceBone?.GetComponent<FingerPoseJoint>();
				if ( saved is not null && saved.WasProcedural )
				{
					rotation = saved.PreviousRotation;
				}
				else if ( saved is null && (!posed || (sourceBone.IsValid() && sourceBone.Flags.Contains( GameObjectFlags.ProceduralBone ))) )
				{
					var sceneModel = _source.SceneModel;
					rotation = sceneModel.GetBoneWorldTransform( bone.Parent.Index ).Rotation.Inverse * sceneModel.GetBoneWorldTransform( bone.Index ).Rotation;
				}
			}

			if ( posed )
			{
				rotation *= FingerPoser.PoseOffset( bone, _directions[finger], _across, _left, finger, bend, spread );
			}

			var go = _joints[i];
			if ( go.LocalRotation != rotation )
			{
				go.LocalRotation = rotation;
				RenderNextFrame();
			}

			var point = Project( go.WorldTransform.PointToWorld( _segments[i] * HandleFraction ) );
			bool moved = _points[i] != point;
			_points[i] = point;
			var css = point * scale;
			_handles[i].Style.Left = css.x;
			_handles[i].Style.Top = css.y;
			_handles[i].SetClass( "posed", posed );

			if ( _boneLines[i] is not null && _bones[i - 1] is not null && (moved || previousMoved || resized) )
			{
				PositionLine( _boneLines[i], _points[i - 1] * scale, css );
			}
			previousMoved = moved;
		}
	}

	int NearestJoint( Vector2 cursor )
	{
		int nearest = -1;
		float radius = 11f * ScaleToScreen;
		float distance = radius * radius;
		for ( int i = 0; i < _points.Length; i++ )
		{
			if ( _bones[i] is null ) continue;
			float d = (cursor - _points[i]).LengthSquared;
			if ( d > distance ) continue;

			nearest = i;
			distance = d;
		}
		return nearest;
	}

	/// <summary>
	/// Updates the hand and projects each handle into the camera view used to draw it.
	/// </summary>
	public override void Tick()
	{
		UpdatePreview();
		base.Tick();
	}

	void UpdatePreview()
	{
		if ( _tool is null || !IsVisible || Box.Rect.Width <= 0f ) return;

		var source = _tool.PreviewSource;
		var model = source.IsValid() ? source.Model : (_draftModel ??= Model.Load( "models/citizen_human/human.vmdl" ));
		var left = _tool.PreviewLeftHand;
		if ( !_model.IsValid() || _model.Model != model || _source != source || _left != left )
		{
			SetModel( model, source, left );
		}

		if ( _across.IsNearZeroLength ) return;
		_camera.CustomSize = Box.Rect.Size;
		var cursor = MousePosition;
		if ( !HasActive )
		{
			_drag = -1;
			_orbiting = false;
		}

		if ( _drag >= 0 ) DragJoint( cursor );
		if ( _orbiting ) OrbitTo( cursor );

		UpdateJoints();
		int hovered = _drag >= 0 ? _drag : (HasHovered && !_orbiting ? NearestJoint( cursor ) : -1);
		SetClass( "over-joint", hovered >= 0 );
		if ( hovered >= 0 ) _focus = hovered;
		for ( int i = 0; i < _handles.Length; i++ )
		{
			_handles[i].SetClass( "in-finger", i / 3 == _focus / 3 );
			_handles[i].SetClass( "highlight", i == _focus );
		}

		if ( hovered >= 0 ) HighlightJoint( hovered );
	}

	void HighlightJoint( int id )
	{
		if ( !_source.IsValid() || !_source.SceneModel.IsValid() ) return;

		var sceneModel = _source.SceneModel;
		var endOfFinger = (id / 3 + 1) * 3;
		for ( int i = id; i < endOfFinger; i++ )
		{
			var bone = _bones[i];
			if ( bone is null ) break;

			var frame = sceneModel.GetBoneWorldTransform( bone.Index );
			var next = i % 3 < 2 ? _bones[i + 1] : null;
			var end = next is null ? frame.PointToWorld( _segments[i] ) : sceneModel.GetBoneWorldTransform( next.Index ).Position;
			_source.DebugOverlay.Line( frame.Position, end, Color.Cyan, overlay: true );
			if ( i == id )
			{
				var radius = ((end - frame.Position).Length * 0.32f).Clamp( 0.3f, 1f );
				_source.DebugOverlay.Sphere( new Sphere( frame.Position, radius ), Color.White, overlay: true );
			}
		}
	}

	static void PositionLine( Panel line, Vector2 from, Vector2 to )
	{
		var center = (from + to) * 0.5f;
		var delta = to - from;
		line.Style.Left = center.x;
		line.Style.Top = center.y;
		line.Style.Width = delta.Length;
		line.Style.MarginLeft = -delta.Length * 0.5f;
		line.Style.Set( "transform", $"rotate({MathF.Atan2( delta.y, delta.x ).RadianToDegree()}deg)" );
	}

	void DragJoint( Vector2 cursor )
	{
		var delta = (cursor - _start) * (256f / Box.Rect.Width);
		var pose = _original;
		var joint = _drag % 3;
		pose[joint] = MathF.Round( (_original[joint] + delta.y * 1.5f).Clamp( -30f, 100f ) );
		if ( joint == 0 )
		{
			var direction = _left ? 1f : -1f;
			pose.w = MathF.Round( (_original.w + direction * delta.x).Clamp( -45f, 45f ) );
		}
		_tool.SetFingerPose( _drag / 3, pose );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		if ( e.Target != this ) return;

		var cursor = e.LocalPosition;
		var joint = NearestJoint( cursor );
		if ( joint < 0 )
		{
			if ( e.MouseButton != MouseButtons.Left ) return;
			_start = cursor;
			_startOrbit = _orbit;
			_orbiting = true;
			e.StopPropagation();
			return;
		}

		var pose = _tool.GetFingerPose( joint / 3 );
		if ( e.MouseButton == MouseButtons.Left )
		{
			_start = cursor;
			_original = pose;
			_drag = joint;
		}
		else if ( e.MouseButton == MouseButtons.Right )
		{
			pose[joint % 3] = 0f;
			if ( joint % 3 == 0 ) pose.w = 0f;
			_tool.SetFingerPose( joint / 3, pose );
		}
		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );
		if ( e.MouseButton != MouseButtons.Left ) return;
		var cursor = e.Target == this ? e.LocalPosition : MousePosition;
		if ( _orbiting ) OrbitTo( cursor );
		_orbiting = false;
		if ( _drag < 0 ) return;
		DragJoint( cursor );
		_drag = -1;
		e.StopPropagation();
	}
}

/// <summary>
/// Stores a joint's authored angles for copying and its previous pose for restoring after duplication.
/// </summary>
[Hide]
public sealed class FingerPoseJoint : Component
{
	/// <summary>
	/// Bend applied to this joint, in degrees.
	/// </summary>
	[Property, Sync] public float Bend { get; set; }

	/// <summary>
	/// Sideways rotation applied to the knuckle, in degrees.
	/// </summary>
	[Property, Sync] public float Spread { get; set; }

	/// <summary>
	/// Applies the authored local rotation on the host and on peers receiving the joint update.
	/// </summary>
	[Sync]
	public Rotation PosedRotation
	{
		get => GameObject.LocalRotation;
		set => GameObject.LocalRotation = value;
	}

	/// <summary>
	/// Local rotation before the poser took control.
	/// </summary>
	[Property] public Rotation PreviousRotation { get; set; }

	/// <summary>
	/// Whether the original rotation was controlled by another procedural pose.
	/// </summary>
	[Property] public bool WasProcedural { get; set; }

	/// <summary>
	/// Restores control of this joint to its previous pose or animation.
	/// </summary>
	public void Restore()
	{
		GameObject.LocalRotation = PreviousRotation;
		GameObject.Flags = GameObject.Flags.WithFlag( GameObjectFlags.ProceduralBone, WasProcedural );
		Destroy();
	}
}
