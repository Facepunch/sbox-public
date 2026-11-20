/// <summary>
/// How an object highlight looks: its outline colour, the colour where something closer hides it, and its width.
/// </summary>
public readonly record struct HighlightStyle( Color Color, Color ObscuredColor, float Width = 0.2f )
{
	/// <summary>
	/// Something we're acting on - held by the physgun, frozen, or selected in the inspector. In the local player's physgun colour.
	/// </summary>
	public static HighlightStyle Selected => WithPreferences( new( Physgun.Tint( new Color( 4.7f, 10.1f, 30.6f, 1 ), GamePreferences.PhysgunColorValue ), new Color( 2.2f, 2.3f, 2.9f ) ) );

	/// <summary>
	/// Something under the inspector's cursor
	/// </summary>
	public static HighlightStyle Hovered => WithPreferences( new( new Color( 2.6f, 2.0f, 0.2f, 1 ), new Color( 2.6f, 2.0f, 0.2f ) ) );

	/// <summary>
	/// The local player's outline width and how much of an outline shows through what's in front of it
	/// </summary>
	static HighlightStyle WithPreferences( HighlightStyle style ) => style with
	{
		Width = GamePreferences.OutlineWidth,
		ObscuredColor = style.ObscuredColor.WithAlpha( GamePreferences.OutlineObscuredOpacity )
	};
}

public static class ObjectHighlight
{
	static Material _material;

	/// <summary>
	/// The opacity-aware highlight material every sandbox outline draws with.
	/// </summary>
	public static Material Material => _material ??= Material.FromShader( "shaders/postprocess/sandbox_highlight.shader" );

	/// <summary>
	/// Add a hidden outline to <paramref name="host"/>, to point at things later with <see cref="Show(HighlightOutline, GameObject, HighlightStyle)"/>.
	/// </summary>
	public static HighlightOutline Create( GameObject host )
	{
		var outline = host.AddComponent<HighlightOutline>( false );
		outline.OverrideTargets = true;
		outline.Targets = new();
		outline.Material = Material;
		return outline;
	}

	/// <summary>
	/// Outline <paramref name="target"/>, or hide the outline if it's not valid.
	/// </summary>
	public static void Show( this HighlightOutline outline, GameObject target, HighlightStyle style )
	{
		Show( outline, [target], style );
	}

	/// <summary>
	/// Outline <paramref name="targets"/>, or hide the outline if there are none.
	/// </summary>
	public static void Show( this HighlightOutline outline, IEnumerable<GameObject> targets, HighlightStyle style )
	{
		if ( !outline.IsValid() ) return;

		outline.Material = Material;
		outline.OverrideTargets = true;
		outline.Color = style.Color;
		outline.ObscuredColor = style.ObscuredColor;
		outline.Width = style.Width;

		outline.Targets ??= new();
		outline.Targets.Clear();

		foreach ( var target in targets )
		{
			AddRenderers( target, outline.Targets );
		}

		outline.Enabled = outline.Targets.Count > 0;
	}

	/// <summary>
	/// Stop outlining anything.
	/// </summary>
	public static void Hide( this HighlightOutline outline )
	{
		if ( !outline.IsValid() ) return;

		outline.Targets?.Clear();
		outline.Enabled = false;
	}

	/// <summary>
	/// Briefly flash an outline around <paramref name="target"/>, fading in to <paramref name="brightness"/> times
	/// the style's colour and back out over <paramref name="duration"/> seconds.
	/// </summary>
	public static void Flash( GameObject target, HighlightStyle style, float duration = 0.25f, float brightness = 3.0f )
	{
		if ( !target.IsValid() ) return;

		var go = new GameObject( true, "Outline Flash" );
		go.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;

		var outline = go.AddComponent<HighlightOutline>();
		outline.Show( target, style );

		var flash = go.AddComponent<OutlineFlash>();
		flash.Style = style;
		flash.Duration = duration;
		flash.Brightness = brightness;
		flash.Play( outline );
	}

	/// <summary>
	/// What to outline for <paramref name="go"/>: the whole ragdoll for one of its bones, otherwise itself.
	/// </summary>
	public static GameObject GetTarget( GameObject go )
	{
		var modelPhysics = go.Components.GetInAncestorsOrSelf<ModelPhysics>();
		if ( !modelPhysics.IsValid() ) return go;

		return modelPhysics.Renderer.IsValid() ? modelPhysics.Renderer.GameObject : modelPhysics.GameObject;
	}

	static void AddRenderers( GameObject o, List<Renderer> renderers )
	{
		if ( !o.IsValid() ) return;

		renderers.AddRange( o.GetComponents<Renderer>() );

		foreach ( var c in o.Children )
		{
			if ( c.NetworkMode == NetworkMode.Object ) continue;

			AddRenderers( c, renderers );
		}
	}
}
