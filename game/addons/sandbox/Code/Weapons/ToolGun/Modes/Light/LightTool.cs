using Sandbox.UI;

/// <summary>Places the existing bulb light, either rope-mounted or as a loose physics object.</summary>
[Hide, Title( "#tool.name.light" ), Icon( "💡" ), ClassName( "lighttool" ), Group( "#tool.group.building" )]
public sealed class LightTool : ToolMode
{
	public const string LightPrefab = "entities/sents/lights/bulblight.prefab";
	public override bool UseSnapGrid => true;

	[Property, Title( "#tool.setting.light_input" ), Description( "#tool.setting.light_input.description" ), Group( "#tool.setting.group.inputs" ), Order( 0 )]
	public ClientInput Toggle { get; set; }

	[Property, Title( "#tool.setting.light_toggle" ), Description( "#tool.setting.light_toggle.description" ), Group( "#tool.setting.group.inputs" ), Order( 0 )]
	public bool ToggleMode { get; set; } = true;

	[Property, Range( 0, 512 ), Title( "#tool.setting.light_rope_length" ), Group( "#tool.setting.group.behavior" ), Order( 1 )]
	public float RopeLength { get; set; } = 64f;

	[Property, Title( "#tool.setting.tint" ), Description( "#entity.description.color" ), Group( "#entity.group.light" ), Order( 2 )]
	public Color Color { get; set; } = Color.White;

	[Property, Range( 0, 50 ), Title( "#entity.property.brightness" ), Description( "#entity.description.brightness" ), Group( "#entity.group.light" ), Order( 2 )]
	public float Brightness { get; set; } = 1f;

	[Property, Range( 0, 1000 ), Title( "#entity.property.radius" ), Description( "#entity.description.radius" ), Group( "#entity.group.light" ), Order( 2 )]
	public float Radius { get; set; } = 512f;

	[Property, Range( 0, 16 ), Title( "#entity.property.attenuation" ), Description( "#entity.description.attenuation" ), Group( "#entity.group.light" ), Order( 2 )]
	public float Attenuation { get; set; } = 2.4f;

	[Property, Range( 1000, 12000 ), Title( "#entity.property.temperature" ), Description( "#entity.description.temperature" ), Group( "#entity.group.light" ), Order( 2 )]
	[SliderGradient( "#ff4400", "#ff890e", "#ffb270", "#ffdabf", "#ffffff", "#cadbff", "#bfd4ff",
		Positions = new float[] { 0, 1000f / 11000, 2000f / 11000, 3500f / 11000, 0.5f, 9000f / 11000, 1 } )]
	public float Temperature { get; set; } = 6500f;

	[Property, Title( "#entity.property.shadows" ), Description( "#entity.description.shadows" ), Group( "#entity.group.light" ), Order( 2 )]
	public bool Shadows { get; set; }

	bool _updatingExisting;

	/// <summary>
	/// On the host while an action is checked: whether it places another light, rather than updating the one aimed at.
	/// </summary>
	public bool PlacingNew { get; private set; }
	public override string Description => "#tool.hint.light.description";
	public static float ClampLength( float length ) => float.IsFinite( length ) ? length.Clamp( 0f, 512f ) : 64f;

	protected override void OnStart()
	{
		base.OnStart();
		RegisterAction( ToolInput.Primary, () => _updatingExisting ? "#tool.hint.light.update" : "#tool.hint.light.place_rope", () => Place( true ) );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.light.place_free", () => Place( false ) );
	}

	void Place( bool withRope )
	{
		var point = TraceSelect();
		if ( !point.IsValid() ) return;
		PlaceOrUpdate( point, withRope, RopeLength, WriteConfiguration<PointLightEntity>(), withRope && Input.Down( "run" ) );
		ShootEffects( point );
	}

	static PointLightEntity FindLight( SelectionPoint point ) =>
		point.IsValid() ? point.GameObject.GetComponentInParent<PointLightEntity>( true ) : null;

	static Transform PlacementTransform( SelectionPoint point, bool withRope, float length )
	{
		var surface = point.WorldTransform();
		// SelectionPoint.Forward is the surface normal. The bulb extends along local Z;
		// its fitting at the origin faces the rope/attachment surface.
		var rotation = Rotation.LookAt( surface.Rotation.Up, surface.Forward );
		return new Transform( surface.Position + surface.Forward * (withRope ? MathF.Max( ClampLength( length ), 2f ) : 2f), rotation );
	}

	public override void OnControl()
	{
		base.OnControl();
		var point = TraceSelect();
		var existing = FindLight( point );
		_updatingExisting = existing.IsValid() && !Input.Down( "run" );
		if ( !point.IsValid() ) return;

		if ( _updatingExisting )
		{
			DebugOverlay.Sphere( new Sphere( existing.WorldPosition, 9f ), Color.Cyan.WithAlpha( 0.7f ) );
			return;
		}

		var prefab = ResourceLibrary.Get<PrefabFile>( LightPrefab );
		if ( prefab?.GetScene() is not Scene scene ) return;
		var tx = PlacementTransform( point, true, RopeLength );
		DebugOverlay.GameObject( scene, transform: tx, castShadows: false, color: Color.White.WithAlpha( 0.75f ) );
		DebugOverlay.Line( point.WorldPosition(), tx.Position, Color.White.WithAlpha( 0.65f ) );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void PlaceOrUpdate( SelectionPoint point, bool withRope, float length, string configuration, bool forceNew = false )
	{
		if ( !point.IsValid() || !Player.IsValid() ) return;
		var input = withRope ? ToolInput.Primary : ToolInput.Secondary;

		// known before the action is checked, so the light limit doesn't block updating one you already have
		var existing = withRope && !forceNew ? FindLight( point ) : null;
		PlacingNew = !existing.IsValid();
		if ( !FireToolAction( input ) ) return;
		if ( existing.IsValid() )
		{
			if ( existing.IsProxy ) return;
			ApplyConfiguration( existing, configuration );
			existing.GetComponent<LightToolRope>()?.SetLength( length );
			existing.GameObject.Network.Refresh();
			FirePostToolAction( input );
			return;
		}

		var prefab = ResourceLibrary.Get<PrefabFile>( LightPrefab );
		if ( prefab?.GetScene() is not Scene scene ) return;
		var go = scene.Clone( new CloneConfig { StartEnabled = false } );
		go.Tags.Add( "removable" );
		go.WorldTransform = PlacementTransform( point, withRope, length );
		var light = go.GetComponent<PointLightEntity>( true );
		if ( !light.IsValid() ) { go.Destroy(); return; }
		ApplyConfiguration( light, configuration );

		ApplyPhysicsProperties( go );
		var body = go.GetComponent<Rigidbody>( true );
		body.Enabled = true;
		body.MotionEnabled = true;
		body.Gravity = true;
		Ownable.Set( go, Player.Network.Owner );
		if ( withRope ) AttachRope( go, point, length );

		go.NetworkSpawn( true, null );
		Track( go );
		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.light";
		undo.Icon = "💡";
		undo.Add( go );
		if ( withRope && !point.IsWorld ) CheckContraptionStats( point.GameObject );
		FirePostToolAction( input );
	}

	static void AttachRope( GameObject light, SelectionPoint point, float length )
	{
		var anchor = new GameObject( false, "light rope anchor" );
		anchor.Parent = point.GameObject;
		anchor.LocalTransform = point.LocalTransform;

		var joint = light.AddComponent<SpringJoint>();
		joint.Body = anchor;
		joint.Frequency = 0;
		joint.Damping = 0;
		joint.EnableCollision = true;

		var cleanup = light.AddComponent<ConstraintCleanup>();
		cleanup.Attachment = anchor;
		cleanup.DestroyOnDetach = false;

		const float width = 0.3f;
		var line = light.AddComponent<LineRenderer>();
		line.Points = [light, anchor];
		line.Width = width;
		line.Color = Color.White;
		line.Lighting = true;
		line.CastShadows = true;
		line.SplineInterpolation = 4;
		line.Texturing = line.Texturing with { Material = Material.Load( "materials/default/rope01.vmat" ), WorldSpace = true, UnitsPerTexture = 32 };
		line.Face = SceneLineObject.FaceMode.Cylinder;

		var rope = light.AddComponent<VerletRope>();
		rope.Attachment = anchor;
		rope.LinkedRenderer = line;
		rope.Radius = width;
		var settings = light.AddComponent<LightToolRope>();
		settings.Joint = joint;
		settings.Rope = rope;
		settings.SetLength( length );
		anchor.NetworkSpawn( true, null );
	}
}
