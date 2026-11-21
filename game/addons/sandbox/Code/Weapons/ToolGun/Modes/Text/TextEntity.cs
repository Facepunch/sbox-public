/// <summary>
/// Text placed in the world
/// </summary>
[Title( "#entity.name.textentity" )]
public sealed class TextEntity : Component, IPhysgunEvent
{
	/// <summary>
	/// How thick the box we trace and grab against is.
	/// </summary>
	const float Thickness = 2f;

	const float HighlightWidth = 0.4f;

	[Property, Sync, ClientEditable, Hide, TextArea, HideLabel, Group( "#tool.setting.group.contents" )]
	public string Text { get; set; } = "Text";

	// [Order] keeps the groups in the order they're written, a sheet sorts them by name otherwise

	[Property, Sync, ClientEditable, Hide, FontName, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.font" )]
	public string Font { get; set; } = GameFonts.Default;

	/// <summary>
	/// Height of the font in world units.
	/// </summary>
	[Range( WorldTextStyle.MinSize, WorldTextStyle.MaxSize ), Step( 1 )]
	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.size" )]
	public float Size { get; set; } = 14f;

	[Range( WorldTextStyle.MinWeight, WorldTextStyle.MaxWeight ), Step( 100 )]
	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.font_weight" ), Description( "#tool.setting.font_weight.description" )]
	public int Weight { get; set; } = 400;

	[Range( WorldTextStyle.MinSpacing, WorldTextStyle.MaxSpacing ), Step( 0.25f )]
	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.letter_spacing" )]
	public float LetterSpacing { get; set; }

	[Range( WorldTextStyle.MinSpacing, WorldTextStyle.MaxSpacing ), Step( 0.25f )]
	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.word_spacing" )]
	public float WordSpacing { get; set; }

	[Range( WorldTextStyle.MinLineHeight, WorldTextStyle.MaxLineHeight ), Step( 0.05f )]
	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.line_height" ), Description( "#tool.setting.line_height.description" )]
	public float LineHeight { get; set; } = 1f;

	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.text" ), Order( 1 ), Title( "#tool.setting.colour" )]
	public Color Color { get; set; } = Color.White;

	[Property, Sync, ClientEditable, Hide, ToggleGroup( nameof( Outline ), Label = "#tool.setting.outline" ), Order( 2 )]
	public bool Outline { get; set; }

	[Range( 0f, WorldTextStyle.MaxOutlineWidth ), Step( 0.25f )]
	[Property, Sync, ClientEditable, Hide, ToggleGroup( nameof( Outline ) ), Order( 2 ), Title( "#tool.setting.width" )]
	public float OutlineWidth { get; set; } = 1f;

	[Property, Sync, ClientEditable, Hide, ToggleGroup( nameof( Outline ) ), Order( 2 ), Title( "#tool.setting.colour" )]
	public Color OutlineColor { get; set; } = Color.Black;

	[Property, Sync, ClientEditable, Hide, ToggleGroup( nameof( Shadow ), Label = "#tool.setting.shadow" ), Order( 3 )]
	public bool Shadow { get; set; }

	[Property, Sync, ClientEditable, Hide, ToggleGroup( nameof( Shadow ) ), Order( 3 ), Title( "#tool.setting.offset" )]
	public Vector2 ShadowOffset { get; set; } = new( 1f, 1f );

	[Range( 0f, WorldTextStyle.MaxShadowBlur ), Step( 0.25f )]
	[Property, Sync, ClientEditable, Hide, ToggleGroup( nameof( Shadow ) ), Order( 3 ), Title( "#tool.setting.blur" )]
	public float ShadowBlur { get; set; } = 1f;

	[Property, Sync, ClientEditable, Hide, ToggleGroup( nameof( Shadow ) ), Order( 3 ), Title( "#tool.setting.colour" )]
	public Color ShadowColor { get; set; } = Color.Black.WithAlpha( 0.8f );

	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.rendering" ), Order( 4 ), Title( "#tool.setting.blend_mode" )]
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.physics" ), Order( 5 ), Title( "#tool.setting.collisions" ), Description( "#tool.setting.collisions.description" )]
	public bool Collisions { get; set; }

	[Property, Sync, ClientEditable, Hide, Group( "#tool.setting.group.physics" ), Order( 5 ), EnabledIf( nameof( Collisions ) ), Title( "#tool.setting.gravity" ), Description( "#tool.setting.gravity.description" )]
	public bool Gravity { get; set; }

	/// <summary>
	/// Whether the text falls. Only when it collides, so there's something to land on.
	/// </summary>
	public bool Falls => Collisions && Gravity;

	[RequireComponent] public TextRenderer Renderer { get; set; }
	[RequireComponent] public BoxCollider Collider { get; set; }
	[RequireComponent] public Rigidbody Body { get; set; }

	/// <summary>
	/// The look of the text, from its properties
	/// </summary>
	public WorldTextStyle Style
	{
		get => new()
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
		set
		{
			value = value.Clamped();
			Font = value.Font;
			Size = value.Size;
			Weight = value.Weight;
			LetterSpacing = value.LetterSpacing;
			WordSpacing = value.WordSpacing;
			LineHeight = value.LineHeight;
			Color = value.Color;
			Outline = value.Outline;
			OutlineWidth = value.OutlineWidth;
			OutlineColor = value.OutlineColor;
			Shadow = value.Shadow;
			ShadowOffset = value.ShadowOffset;
			ShadowBlur = value.ShadowBlur;
			ShadowColor = value.ShadowColor;
			BlendMode = value.BlendMode;
		}
	}

	int _applied;
	int _shown;
	int _physics;
	bool _physicsApplied;

	/// <summary>
	/// What we show, never empty. Text with nothing in it couldn't be seen or picked again, so an edit that erases it all keeps the last text.
	/// </summary>
	string _text;

	/// <summary>
	/// Builds the text as soon as we're on, the same frame as the renderer, so we avoid seeing engine placeholder text 
	/// </summary>
	protected override void OnEnabled()
	{
		Apply();
	}

	protected override void OnStart()
	{
		ApplyPhysics();
	}

	protected override void OnUpdate()
	{
		// the settings are synced, everyone builds the renderer and collider from them
		if ( SettingsHash() != _applied )
			Apply();

		if ( HashCode.Combine( Collisions, Falls ) != _physics )
			ApplyPhysics();

		var highlight = FindHighlight();
		if ( HashCode.Combine( _applied, highlight ) != _shown )
			Show( Style, highlight );
	}

	/// <summary>
	/// Changes with any of the settings, so a frame where none did costs a hash rather than a new <see cref="Style"/>.
	/// </summary>
	int SettingsHash()
	{
		var hc = new HashCode();
		hc.Add( Text );
		hc.Add( Font );
		hc.Add( Size );
		hc.Add( Weight );
		hc.Add( LetterSpacing );
		hc.Add( WordSpacing );
		hc.Add( LineHeight );
		hc.Add( Color );
		hc.Add( Outline );
		hc.Add( OutlineWidth );
		hc.Add( OutlineColor );
		hc.Add( Shadow );
		hc.Add( ShadowOffset );
		hc.Add( ShadowBlur );
		hc.Add( ShadowColor );
		hc.Add( BlendMode );
		return hc.ToHashCode();
	}

	void Apply()
	{
		if ( !string.IsNullOrWhiteSpace( Text ) )
			_text = WorldTextStyle.ClampText( Text );

		_text ??= "Text";

		// the inspector sets whatever a player sends, the host puts it back within the limits and that's what syncs.
		// An erased text gets the last one back, the inspector shows it again once its box loses focus
		if ( !IsProxy )
		{
			Text = _text;
			Style = Style;
		}

		_applied = SettingsHash();
		var style = Style;

		if ( Collider.IsValid() )
		{
			var size = style.Measure( _text );
			Collider.Center = Vector3.Zero;
			Collider.Scale = new Vector3( Thickness, MathF.Max( size.x, 1f ), MathF.Max( size.y, 1f ) );
		}

		Show( style, FindHighlight() );
	}

	void ApplyPhysics()
	{
		var starting = !_physicsApplied;
		_physicsApplied = true;
		_physics = HashCode.Combine( Collisions, Falls );

		// without collisions nothing bumps into it, traces still find it so the physgun and inspector can pick it
		GameObject.Tags.Set( "noclip", !Collisions );

		if ( IsProxy || !Body.IsValid() ) return;

		Body.Gravity = Falls;

		if ( !Falls )
			Freeze();

		else if ( !starting )
			Body.MotionEnabled = true;
	}

	void Show( WorldTextStyle style, Color? highlight )
	{
		_shown = HashCode.Combine( _applied, highlight );

		if ( !Renderer.IsValid() ) return;

		style.Apply( Renderer, style.CreateScope( _text, highlight, HighlightWidth ) );
	}

	/// <summary>
	/// The colour of an outline effect drawing around us, like the inspector's selection or the physgun's hold.
	/// Those only draw around models, so we draw the highlight into the text ourselves.
	/// </summary>
	Color? FindHighlight()
	{
		if ( !Renderer.IsValid() ) return null;

		var highlighted = Scene.GetSystem<HighlightedRenderers>();
		if ( highlighted is null || !highlighted.TryGet( Renderer, out var color ) ) return null;

		// outline colours are made bright for the outline effect, unlit text would bloom with them, keep just the hue
		var brightest = MathF.Max( 1f, MathF.Max( color.r, MathF.Max( color.g, color.b ) ) );
		return new Color( color.r / brightest, color.g / brightest, color.b / brightest, color.a );
	}

	void Freeze()
	{
		Body.Velocity = Vector3.Zero;
		Body.AngularVelocity = Vector3.Zero;
		Body.MotionEnabled = false;
	}

	void IPhysgunEvent.OnPhysgunDrop()
	{
		// falling text drops when let go, the rest stays where it was put
		if ( IsProxy || !Body.IsValid() || Falls ) return;

		Freeze();
	}
}
