
[Title( "#tool.name.text" )]
[Icon( "🔤" )]
[ClassName( "texttool" )]
[Group( "#tool.group.render" )]
public sealed class TextTool : ToolMode
{
	/// <summary>
	/// Offset from the surface to avoid collision clashing
	/// </summary>
	const float SurfaceOffset = 1.25f;

	[Property, TextArea, HideLabel, Group( "#tool.setting.group.contents" )]
	public string Text { get; set; } = "Text";

	// the same settings as TextEntity, which carries them on the placed text, [Order] keeps the groups in this order
	[Property, FontName, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.font" )]
	public string Font { get; set; } = GameFonts.Default;

	[Range( WorldTextStyle.MinSize, WorldTextStyle.MaxSize ), Step( 1 )]
	[Property, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.size" )]
	public float Size { get; set; } = 14f;

	[Range( WorldTextStyle.MinWeight, WorldTextStyle.MaxWeight ), Step( 100 )]
	[Property, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.font_weight" ), Description( "#tool.setting.font_weight.description" )]
	public int Weight { get; set; } = 400;

	[Range( WorldTextStyle.MinSpacing, WorldTextStyle.MaxSpacing ), Step( 0.25f )]
	[Property, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.letter_spacing" )]
	public float LetterSpacing { get; set; }

	[Range( WorldTextStyle.MinSpacing, WorldTextStyle.MaxSpacing ), Step( 0.25f )]
	[Property, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.word_spacing" )]
	public float WordSpacing { get; set; }

	[Range( WorldTextStyle.MinLineHeight, WorldTextStyle.MaxLineHeight ), Step( 0.05f )]
	[Property, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.line_height" ), Description( "#tool.setting.line_height.description" )]
	public float LineHeight { get; set; } = 1f;

	[Property, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.colour" )]
	public Color Color { get; set; } = Color.White;

	[Property, ToggleGroup( nameof( Outline ), Label = "#tool.setting.outline" ), Order( 2 )]
	public bool Outline { get; set; }

	[Range( 0f, WorldTextStyle.MaxOutlineWidth ), Step( 0.25f )]
	[Property, ToggleGroup( nameof( Outline ) ), Order( 2 ), Title( "#tool.setting.width" )]
	public float OutlineWidth { get; set; } = 1f;

	[Property, ToggleGroup( nameof( Outline ) ), Order( 2 ), Title( "#tool.setting.colour" )]
	public Color OutlineColor { get; set; } = Color.Black;

	[Property, ToggleGroup( nameof( Shadow ), Label = "#tool.setting.shadow" ), Order( 3 )]
	public bool Shadow { get; set; }

	[Property, ToggleGroup( nameof( Shadow ) ), Order( 3 ), Title( "#tool.setting.offset" )]
	public Vector2 ShadowOffset { get; set; } = new( 1f, 1f );

	[Range( 0f, WorldTextStyle.MaxShadowBlur ), Step( 0.25f )]
	[Property, ToggleGroup( nameof( Shadow ) ), Order( 3 ), Title( "#tool.setting.blur" )]
	public float ShadowBlur { get; set; } = 1f;

	[Property, ToggleGroup( nameof( Shadow ) ), Order( 3 ), Title( "#tool.setting.colour" )]
	public Color ShadowColor { get; set; } = Color.Black.WithAlpha( 0.8f );

	[Property, Group( "#tool.setting.group.rendering" ), Order( 4 ), Title( "#tool.setting.blend_mode" )]
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	[Property, Group( "#tool.setting.group.physics" ), Order( 5 ), Title( "#tool.setting.collisions" ), Description( "#tool.setting.collisions.description" )]
	public bool Collisions { get; set; }

	[Property, Group( "#tool.setting.group.physics" ), Order( 5 ), EnabledIf( nameof( Collisions ) ), Title( "#tool.setting.gravity" ), Description( "#tool.setting.gravity.description" )]
	public bool Gravity { get; set; }

	WorldTextStyle Style => new()
	{
		Font = Font,
		Size = Size,
		Weight = Weight,
		LetterSpacing = LetterSpacing,
		WordSpacing = WordSpacing,
		LineHeight = LineHeight,
		Color = Color,
		Outline = Outline,
		OutlineWidth = OutlineWidth,
		OutlineColor = OutlineColor,
		Shadow = Shadow,
		ShadowOffset = ShadowOffset,
		ShadowBlur = ShadowBlur,
		ShadowColor = ShadowColor,
		BlendMode = BlendMode,
	};

	public override string Description => "#tool.hint.texttool.description";

	public override string ReloadAction => "#tool.hint.texttool.rotate";

	float _rotation;
	bool _absorbLook;

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.texttool.place", OnPlace );
	}

	public override void OnCameraMove( Player player, ref Angles angles )
	{
		base.OnCameraMove( player, ref angles );

		if ( _absorbLook )
			angles = default;
	}

	public override void OnControl()
	{
		var rotating = Input.Down( "reload" );

		_absorbLook = rotating;
		Toolgun.SetIsUsingJoystick( rotating );

		if ( rotating )
		{
			var look = Input.AnalogLook;
			_rotation = (_rotation + look.yaw).UnsignedMod( 360f );
			Toolgun.UpdateJoystick( new Angles( look.yaw, 0, 0 ) );
			Input.Clear( "reload" );
		}

		IsValidState = !string.IsNullOrWhiteSpace( Text );

		base.OnControl();
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		var toolgun = Toolgun;
		var player = toolgun?.Owner;
		var active = toolgun.IsValid() && player.IsValid()
			&& player.GetComponent<PlayerInventory>()?.ActiveWeapon == toolgun;

		if ( !active || IsProxy )
		{
			HidePreview();
			return;
		}

		UpdatePreview();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		DestroyPreview();
	}

	protected override void OnDestroy()
	{
		DestroyPreview();
		base.OnDestroy();
	}

	/// <summary>
	/// Where the text goes for a point on a surface. It faces out of the surface and stands upright on walls,
	/// on floors and ceilings its top points the way we're looking so it reads the right way round.
	/// </summary>
	Transform GetPlacement( SelectionPoint select )
	{
		var hit = select.WorldTransform();
		var normal = hit.Rotation.Forward;
		var up = Vector3.Up;

		if ( MathF.Abs( normal.z ) > 0.99f )
		{
			var eye = Toolgun.Owner.EyeTransform.Rotation;
			up = eye.Forward.WithZ( 0 );
			if ( up.IsNearZeroLength ) up = eye.Up.WithZ( 0 );
		}

		var rotation = Rotation.LookAt( -normal, up ) * Rotation.FromRoll( _rotation );
		return new Transform( hit.Position + normal * SurfaceOffset, rotation );
	}

	// Text preview visuals settings, the decal tool's pulse a little stronger so the text stays readable
	const float PreviewOpacityMax = 0.6f;
	const float PreviewOpacityMin = 0.3f;
	const float PreviewPulsePeriod = 1f;

	/// <summary>
	/// Every text style is a cached text block, so the pulse moves in steps rather than making a new one each frame
	/// </summary>
	const float PreviewOpacityStep = 0.05f;

	/// <summary>
	/// Local client preview of the text where the player is looking
	/// </summary>
	GameObject _preview;
	TextRenderer _previewText;
	int _previewShown;

	void UpdatePreview()
	{
		var select = TraceSelect();

		if ( !select.IsValid() || string.IsNullOrWhiteSpace( Text ) )
		{
			HidePreview();
			return;
		}

		// recreated if something removed it, like a map cleanup
		if ( !_preview.IsValid() || !_previewText.IsValid() )
		{
			_preview?.Destroy();
			_preview = new GameObject( false, "text preview" );
			_preview.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked | GameObjectFlags.Hidden;
			_preview.NetworkMode = NetworkMode.Never;

			_previewText = _preview.AddComponent<TextRenderer>();
			_previewShown = 0;
		}

		_preview.WorldTransform = GetPlacement( select );

		var pulse = 0.5f + 0.5f * MathF.Cos( Time.Now * MathF.Tau / PreviewPulsePeriod );
		var opacity = MathX.Lerp( PreviewOpacityMin, PreviewOpacityMax, pulse ).SnapToGrid( PreviewOpacityStep );
		var style = Style.Faded( opacity );

		// setting the scope measures the text again, only when it changed
		var shown = HashCode.Combine( Text, style );
		if ( shown != _previewShown )
		{
			_previewShown = shown;
			style.Apply( _previewText, style.CreateScope( Text ) );
		}

		_preview.Enabled = true;
	}

	void HidePreview()
	{
		if ( _preview.IsValid() ) _preview.Enabled = false;
	}

	void DestroyPreview()
	{
		if ( _preview.IsValid() ) _preview.Destroy();
		_preview = null;
		_previewText = null;
	}

	void OnPlace()
	{
		if ( string.IsNullOrWhiteSpace( Text ) ) return;

		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		SpawnText( GetPlacement( select ), Text, Json.Serialize( Style ), Collisions, Gravity );
		ShootEffects( select );
	}

	/// <summary>
	/// Spawn the text on the host. The settings come with the call, the look as a <see cref="WorldTextStyle"/>, since only the owner has their current values.
	/// </summary>
	[Rpc.Host( NetFlags.OwnerOnly )]
	public void SpawnText( Transform tx, string text, string styleJson, bool collisions, bool gravity )
	{
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		if ( string.IsNullOrWhiteSpace( text ) ) return;

		var style = Json.Deserialize<WorldTextStyle>( styleJson );
		if ( style is null ) return;

		var go = new GameObject( false, "text" );
		go.Tags.Add( "removable" );
		go.WorldTransform = tx;

		// placed frozen, like stacked props, even with gravity. The text sets the rest of its physics up when it starts
		var body = go.AddComponent<Rigidbody>();
		body.Gravity = false;
		body.MotionEnabled = false;

		var entity = go.AddComponent<TextEntity>();
		entity.Text = WorldTextStyle.ClampText( text );
		entity.Style = style;
		entity.Collisions = collisions;
		entity.Gravity = gravity;

		go.NetworkSpawn( true, null );

		Track( go );

		var undo = Player.Undo.Create();
		undo.Name = "#tool.name.text";
		undo.Icon = "🔤";
		undo.Add( go );

		FirePostToolAction( ToolInput.Primary );
	}
}
