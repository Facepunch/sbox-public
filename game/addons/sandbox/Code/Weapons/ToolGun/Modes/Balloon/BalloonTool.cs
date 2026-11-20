using Sandbox.UI;

[Icon( "🎈" )]
[Title( "#tool.name.balloon" )]
[ClassName( "balloon" )]
[Group( "#tool.group.building" )]
public sealed class BalloonTool : ToolMode
{
	public override bool UseSnapGrid => true;
	[Property, ResourceSelect( Extension = "bdef", AllowPackages = true ), Title( "#tool.name.balloon" )]
	public string Definition { get; set; } = "entities/balloon/basic.bdef";

	[Range( 0, 500 )]
	[Property, Sync, Title( "#tool.setting.length" )]
	public float Length { get; set; } = 50.0f;

	[Range( -10, 10 )]
	[Property, Sync, Title( "#tool.setting.force" )]
	public float Force { get; set; } = 1.0f;

	[Property, Sync, Title( "#tool.setting.rigid" )]
	public bool Rigid { get; set; } = false;

	[Property, Sync, Title( "#tool.setting.tint" )]
	public Color Tint { get; set; } = Color.White;

	public override string Description => "#tool.hint.balloon.description";

	Color _previewTint = Color.Random;

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.balloon.place_rope", OnPlaceWithRope );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.balloon.place", OnPlaceWithoutRope );
	}

	protected override void OnEnabled()
	{
		base.OnEnabled();
		_previewTint = Color.Random;
	}

	/// <summary>
	/// At the aimed point, turned about the vertical so the balloon's front faces the player.
	/// </summary>
	Transform PlacementTransform( SelectionPoint select )
	{
		var position = select.WorldTransform().Position;
		var toPlayer = (Player.EyeTransform.Position - position).WithZ( 0f );

		// looking straight down on it there's no direction to face, keep the old placement
		if ( toPlayer.LengthSquared < 0.01f ) return new Transform( position );

		// the balloon's front is its forward axis
		return new Transform( position, Rotation.FromYaw( Rotation.LookAt( toPlayer.Normal ).Yaw() ) );
	}

	void OnPlaceWithRope()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var thrusterDef = ResourceLibrary.Get<BalloonDefinition>( Definition );
		if ( thrusterDef == null ) return;

		var placementTx = PlacementTransform( select );

		Spawn( select, thrusterDef.Prefab, placementTx, true, _previewTint );
		ShootEffects( select );
		_previewTint = Color.Random;
	}

	void OnPlaceWithoutRope()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var thrusterDef = ResourceLibrary.Get<BalloonDefinition>( Definition );
		if ( thrusterDef == null ) return;

		var placementTx = PlacementTransform( select );

		Spawn( select, thrusterDef.Prefab, placementTx, false, _previewTint );
		ShootEffects( select );
		_previewTint = Color.Random;
	}

	public override void OnControl()
	{
		base.OnControl();

		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var thrusterDef = ResourceLibrary.Get<BalloonDefinition>( Definition );
		if ( thrusterDef == null ) return;

		var placementTx = PlacementTransform( select );

		var previewTint = Tint == Color.White ? _previewTint : Tint;
		DebugOverlay.GameObject( thrusterDef.Prefab.GetScene(), transform: placementTx, castShadows: true, color: previewTint.WithAlpha( 0.9f ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void Spawn( SelectionPoint point, PrefabFile thrusterPrefab, Transform tx, bool withRope, Color spawnTint )
	{
		if ( !FireToolAction( withRope ? ToolInput.Primary : ToolInput.Secondary ) ) return;

		var go = thrusterPrefab.GetScene().Clone( global::Transform.Zero, startEnabled: false );
		go.Tags.Add( "removable" );
		go.WorldTransform = Rigid && withRope ? tx.WithPosition( tx.Position + Vector3.Up * Length ) : tx;

		var tint = Tint == Color.White ? spawnTint : Tint;

		foreach ( var c in go.GetComponentsInChildren<Prop>( true ) )
		{
			c.Tint = tint;
		}

		if ( withRope )
		{
			var anchor = new GameObject( false, "anchor" );
			anchor.Parent = point.GameObject;
			anchor.LocalTransform = point.LocalTransform;

			var joint = go.AddComponent<SpringJoint>();
			joint.Body = anchor;
			joint.MinLength = Rigid ? Length : 0;
			joint.MaxLength = Length;
			joint.RestLength = Length;
			joint.Frequency = 0;
			joint.Damping = 0;
			joint.EnableCollision = true;

			var cleanup = go.AddComponent<ConstraintCleanup>();
			cleanup.Attachment = anchor;
			cleanup.DestroyOnDetach = false; // popping the balloon we're tied to should only cut the string

			const float ropeWidth = 0.4f;
			var splineInterpolation = 0;
			if ( !Rigid )
			{
				var vertletRope = go.AddComponent<VerletRope>();
				vertletRope.Attachment = anchor;
				vertletRope.LengthOverride = MathF.Max( 1f, Length );

				const int minSegmentCount = 4;
				const int maxSegmentCount = 48;
				int segmentCount = Math.Clamp( MathX.CeilToInt( Length / 16.0f ), minSegmentCount, maxSegmentCount );

				vertletRope.SegmentCount = segmentCount;
				vertletRope.Radius = ropeWidth;
				splineInterpolation = segmentCount > maxSegmentCount ? 8 : 4;
			}

			var lineRenderer = go.AddComponent<LineRenderer>();
			lineRenderer.Points = [go, anchor];
			lineRenderer.Width = ropeWidth;
			lineRenderer.Color = Color.White;
			lineRenderer.Lighting = true;
			lineRenderer.CastShadows = true;
			lineRenderer.SplineInterpolation = splineInterpolation;
			lineRenderer.Texturing = lineRenderer.Texturing with { Material = Material.Load( "materials/default/rope01.vmat" ), WorldSpace = true, UnitsPerTexture = 32 };
			lineRenderer.Face = SceneLineObject.FaceMode.Cylinder;

			anchor.NetworkSpawn( true, null );
		}

		ApplyPhysicsProperties( go );

		go.NetworkSpawn( true, null );

		foreach ( var c in go.GetComponentsInChildren<Rigidbody>( true ) )
		{
			c.GravityScale = Force;
		}

		var props = go.GetOrAddComponent<PhysicalProperties>();
		props.GravityScale = Force;

		Track( go );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.balloon";
		undo.Add( go );

		Player.PlayerData?.AddStat( "tool.balloon.place" );

		FirePostToolAction( withRope ? ToolInput.Primary : ToolInput.Secondary );
	}
}
