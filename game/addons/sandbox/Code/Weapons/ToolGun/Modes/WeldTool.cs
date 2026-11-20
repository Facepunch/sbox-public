
[Icon( "🥽" )]
[Title( "#tool.name.weld" )]
[ClassName( "weld" )]
[Group( "#tool.group.constraints" )]
public sealed class WeldTool : BaseConstraintToolMode
{
	bool _easyMode;

	[Property, Sync, Title( "#tool.setting.rigid" )]
	public bool Rigid { get; set; } = false;

	float _easyModeAngle = 0f;
	float _rawAngle = 0f;
	bool _isSnapping;

	public override bool AbsorbMouseInput => _easyMode && Stage == 2;

	public override string Description => Stage switch
	{
		2 => "#tool.hint.weld.stage2",
		1 => "#tool.hint.weld.stage1",
		_ => "#tool.hint.weld.stage0"
	};

	public override string PrimaryAction => Stage switch
	{
		2 => "#tool.hint.weld.confirm",
		1 => "#tool.hint.weld.finish",
		_ => "#tool.hint.weld.source"
	};

	public override string ReloadAction => "#tool.hint.weld.remove";
	public override string SecondaryAction => Stage == 0 ? "#tool.hint.weld.easy" : "#tool.hint.weld.cancel";

	/// <summary>
	/// Overrides a SelectionPoint's local position to the nearest snap grid corner
	/// </summary>
	SelectionPoint SnapSelectionPoint( SelectionPoint select )
	{
		if ( !select.IsValid() || SnapGrid == null ) return select;

		var snapPos = SnapGrid.LastSnapWorldPos;
		var snappedLocalPos = select.GameObject.WorldTransform.ToLocal( new Transform( snapPos ) ).Position;
		var lt = select.LocalTransform;
		lt.Position = snappedLocalPos;
		select.LocalTransform = lt;

		return select;
	}

	public override void OnControl()
	{
		Toolgun.SetIsUsingJoystick( _easyMode && Stage == 2 );

		if ( _easyMode && Stage == 2 )
		{
			SnapGrid?.Hide();
			RotateStage();
			return;
		}

		if ( _easyMode && Stage == 1 && Input.Pressed( "attack1" ) && !Input.Pressed( "attack2" ) )
		{
			TryEnterRotateStage();
			return;
		}

		if ( Stage == 0 )
			_easyMode = Input.Pressed( "attack2" );

		int stageBefore = Stage;
		base.OnControl();

		// The base tool treats secondary as cancel; start Easy Weld after it updates the snap grid.
		if ( stageBefore == 0 && _easyMode )
		{
			var select = TraceSelect();
			IsValidState = select.IsValid();
			if ( !IsValidState ) return;
			Point1 = select;
			Stage = 1;
			ShootEffects( select );
		}

		if ( stageBefore == 0 && Stage == 1 && Point1.IsValid() && Input.Down( "snap" ) )
			Point1 = SnapSelectionPoint( Point1 );

		if ( _easyMode && Stage == 1 && IsValidState )
		{
			var select = TraceSelect();
			select = Input.Down( "snap" ) ? SnapSelectionPoint( select ) : select;
			if ( select.IsValid() )
				DrawEasyModePreview( GetEasyModePlacement( Point1, select ) );
		}
	}

	/// <summary>
	/// Stage 2: mouse controls rotation, shift snaps to 15° grid, attack1 confirms, attack2 cancels.
	/// </summary>
	void RotateStage()
	{
		if ( Input.Down( "attack2" ) || !Point1.IsValid() || !Point2.IsValid() )
		{
			ResetRotation();
			Stage = 0;
			return;
		}

		UpdateRotationInput();

		DrawEasyModePreview( GetRotatedPlacement( Point1, Point2, _easyModeAngle ) );

		if ( Input.Pressed( "attack1" ) )
		{
			CreateRotatedWeld( Point1, Point2, _easyModeAngle );
			ShootEffects( Point2 );
			OnConstraintSubmitted( Point1, Point2 );
			ResetRotation();
			Stage = 0;
		}
	}

	/// <summary>
	/// Intercept the Stage 1 click to enter rotation stage instead of finalizing.
	/// </summary>
	bool TryEnterRotateStage()
	{
		var select = TraceSelect();
		select = SnapSelectionPoint( select );
		if ( !select.IsValid() || !Point1.GameObject.IsValid() || select.GameObject == Point1.GameObject )
			return false;

		Point2 = select;
		ResetRotation();
		Stage = 2;
		ShootEffects( select );
		return true;
	}

	/// <summary>
	/// Accumulate mouse yaw into rotation, with optional shift-to-snap.
	/// </summary>
	void UpdateRotationInput()
	{
		var isSnapping = Input.Down( "run" );
		if ( !isSnapping && _isSnapping ) _rawAngle = _easyModeAngle;
		_isSnapping = isSnapping;

		var delta = Input.AnalogLook.yaw;
		_rawAngle += delta;

		_easyModeAngle = _isSnapping
			? MathF.Round( _rawAngle / 15f ) * 15f
			: _rawAngle;

		Toolgun.UpdateJoystick( new Angles( delta, 0, 0 ) );
	}

	void ResetRotation()
	{
		_easyModeAngle = 0f;
		_rawAngle = 0f;
		_isSnapping = false;
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		ResetRotation();
		_easyMode = false;
	}

	/// <summary>
	/// Draw a ghost preview of the source object and all its connected objects at the given placement.
	/// </summary>
	void DrawEasyModePreview( Transform placement )
	{
		var go = Point1.GameObject.Network.RootGameObject ?? Point1.GameObject;

		var builder = new LinkedGameObjectBuilder();
		builder.AddConnected( go );

		foreach ( var obj in builder.Objects )
		{
			var previewTransform = obj == go
				? placement
				: placement.ToWorld( go.WorldTransform.ToLocal( obj.WorldTransform ) );

			DebugOverlay.GameObject( obj, transform: previewTransform, color: Color.White.WithAlpha( 0.3f ) );
		}
	}

	Transform GetRotatedPlacement( SelectionPoint a, SelectionPoint b, float angle )
	{
		var placement = GetEasyModePlacement( a, b );

		var contactPoint = b.WorldPosition();
		// Use the inward normal (-Forward) as rotation axis so the spin direction is natural.
		var axisRotation = Rotation.FromAxis( -b.WorldTransform().Rotation.Forward, angle );

		placement.Position = contactPoint + axisRotation * (placement.Position - contactPoint);
		placement.Rotation = axisRotation * placement.Rotation;

		return placement;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void CreateRotatedWeld( SelectionPoint point1, SelectionPoint point2, float angle )
	{
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		if ( !point1.GameObject.IsValid() || !point2.GameObject.IsValid() )
		{
			Log.Warning( "Tried to create invalid constraint" );
			return;
		}

		if ( point1.GameObject == point2.GameObject )
		{
			Log.Warning( "Tried to create invalid constraint" );
			return;
		}

		// Placement is part of this RPC, independent of the client's current input state.
		var moving = point1.GameObject.Network.RootGameObject ?? point1.GameObject;
		moving.WorldTransform = GetRotatedPlacement( point1, point2, angle );
		CreateConstraint( point1, point2 );
		CheckContraptionStats( point1.GameObject );

		FirePostToolAction( ToolInput.Primary );
	}

	protected override IEnumerable<GameObject> FindConstraints( GameObject linked, GameObject target )
	{
		foreach ( var joint in linked.GetComponentsInChildren<FixedJoint>( true ) )
			if ( linked == target || joint.Body?.Root == target )
				yield return joint.GameObject;
	}


	protected override void CreateConstraint( SelectionPoint point1, SelectionPoint point2 )
	{
		var go1 = new GameObject( false, "weld" );
		go1.Parent = point1.GameObject;
		go1.LocalTransform = point1.LocalTransform;
		go1.LocalRotation = Rotation.Identity;

		var go2 = new GameObject( false, "weld" );
		go2.Parent = point2.GameObject;
		go2.LocalTransform = point2.LocalTransform;
		go2.LocalRotation = Rotation.Identity;

		var cleanup = go1.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = go2;

		var joint = go1.AddComponent<FixedJoint>();
		joint.Attachment = Joint.AttachmentMode.Auto;
		joint.Body = go2;
		joint.EnableCollision = true;
		joint.AngularFrequency = Rigid ? 0 : 10;
		joint.LinearFrequency = Rigid ? 0 : 10;

		go2.NetworkSpawn();
		go1.NetworkSpawn();

		Track( go1, go2 );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.weld";
		undo.Add( go1, go2 );

		Player.PlayerData?.AddStat( "tool.weld.create" );
	}
}
