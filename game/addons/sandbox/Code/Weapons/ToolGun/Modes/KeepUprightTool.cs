
[Hide]
[Title( "#tool.name.keepupright" )]
[Icon( "👆🏻" )]
[ClassName( "upright" )]
[Group( "#tool.group.constraints" )]
public sealed class KeepUprightTool : ToolMode
{
	[Range( 0, 20 )]
	[Property, Sync, Title( "#tool.setting.hertz" )]
	public float Hertz { get; set; } = 2.0f;

	[Range( 0, 2 )]
	[Property, Sync, Title( "#tool.setting.damping_ratio" )]
	public float DampingRatio { get; set; } = 0.7f;

	[Property, Sync, Range( 1000, 25000 ), Step( 10 ), Title( "#tool.setting.torque_multiplier" )]
	public float TorqueMultiplier { get; set; } = 5000f;

	SelectionPoint _point1;
	int _stage = 0;
	const float torqueScale = 10f;

	public override string Description => _stage == 1
		? "#tool.hint.keepupright.stage1"
		: "#tool.hint.keepupright.description";

	public override string PrimaryAction => "#tool.hint.keepupright.apply_world";

	public override string SecondaryAction => _stage == 1
		? "#tool.hint.keepupright.finish"
		: "#tool.hint.keepupright.start_link";

	public override string ReloadAction => _stage == 1
		? "#tool.hint.keepupright.cancel"
		: "#tool.hint.keepupright.remove";

	protected override void OnDisabled()
	{
		base.OnDisabled();
		_stage = 0;
		_point1 = default;
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();
		IsValidState = select.IsValid();

		if ( !IsValidState ) return;

		if ( Input.Pressed( "reload" ) )
		{
			if ( _stage == 1 )
			{
				_stage = 0;
				_point1 = default;
			}
			else
			{
				var go = select.GameObject.Network.RootGameObject ?? select.GameObject;
				RemoveConstraints( go );
				ShootEffects( select );
			}
			return;
		}

		if ( Input.Pressed( "attack1" ) )
		{
			CreateWorldAnchor( select );
			ShootEffects( select );
			_stage = 0;
			_point1 = default;
			return;
		}

		if ( Input.Pressed( "attack2" ) )
		{
			if ( _stage == 0 )
			{
				_point1 = select;
				_stage = 1;
				ShootEffects( select );
			}
			else if ( _stage == 1 )
			{
				if ( _point1.IsValid() && _point1.GameObject != select.GameObject )
				{
					CreateLinked( _point1, select );
					ShootEffects( select );
				}

				_stage = 0;
				_point1 = default;
			}
		}
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void CreateWorldAnchor( SelectionPoint point )
	{
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		if ( !point.IsValid() ) return;

		var go = new GameObject( point.GameObject, false, "keep_upright" );
		go.LocalTransform = point.LocalTransform;
		go.Tags.Add( "constraint" );

		var mass = point.GameObject.GetComponentInParent<Rigidbody>( true )?.Mass ?? 100f;

		var joint = go.AddComponent<UprightJoint>();
		joint.Body = null;
		joint.Hertz = Hertz;
		joint.DampingRatio = DampingRatio;

		joint.MaxTorque = TorqueMultiplier * mass * torqueScale;

		go.NetworkSpawn();

		Track( go );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.keepupright";
		undo.Icon = "👆🏻";
		undo.Add( go );

		CheckContraptionStats( point.GameObject );

		FirePostToolAction( ToolInput.Primary );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void CreateLinked( SelectionPoint point1, SelectionPoint point2 )
	{
		if ( !FireToolAction( ToolInput.Secondary ) ) return;

		if ( !point1.IsValid() || !point2.IsValid() ) return;
		if ( point1.GameObject == point2.GameObject ) return;

		var go2 = new GameObject( point2.GameObject, false, "keep_upright" );
		go2.LocalTransform = point2.LocalTransform;
		go2.Tags.Add( "constraint" );

		var go1 = new GameObject( point1.GameObject, false, "keep_upright" );
		go1.WorldTransform = go2.WorldTransform;
		go1.Tags.Add( "constraint" );

		var cleanup = go1.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = go2;

		var mass = point1.GameObject.GetComponentInParent<Rigidbody>( true )?.Mass ?? 100f;

		var joint = go1.AddComponent<UprightJoint>();
		joint.Body = go2;
		joint.Hertz = Hertz;
		joint.DampingRatio = DampingRatio;
		joint.MaxTorque = TorqueMultiplier * mass * torqueScale;

		go2.NetworkSpawn();
		go1.NetworkSpawn();

		Track( go1, go2 );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.keepupright";
		undo.Icon = "👆🏻";
		undo.Add( go1, go2 );

		CheckContraptionStats( point1.GameObject );

		FirePostToolAction( ToolInput.Secondary );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void RemoveConstraints( GameObject go )
	{
		if ( !FireToolAction( ToolInput.Reload ) ) return;

		if ( !go.IsValid() ) return;

		// Remove world-anchor upright joints (Body == null means no second object)
		foreach ( var joint in go.GetComponentsInChildren<UprightJoint>( true ) )
		{
			if ( !joint.Body.IsValid() )
				joint.GameObject.Destroy();
		}

		// Remove linked upright joints via ConstraintCleanup
		var builder = new LinkedGameObjectBuilder();
		builder.AddConnected( go );

		var toRemove = new List<GameObject>();
		foreach ( var linked in builder.Objects )
		{
			foreach ( var cleanup in linked.GetComponentsInChildren<ConstraintCleanup>( true ) )
			{
				if ( linked != go && cleanup.Attachment?.Root != go ) continue;
				if ( cleanup.GameObject.GetComponentInChildren<UprightJoint>( true ) is not null )
					toRemove.Add( cleanup.GameObject );
			}
		}

		foreach ( var host in toRemove )
			host.Destroy();

		FirePostToolAction( ToolInput.Reload );
	}
}
