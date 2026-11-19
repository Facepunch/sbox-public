/// <summary>
/// How a placed text looks, everything apart from what it says. The text tool and <see cref="TextEntity"/> both show these
/// as properties of their own, this carries them from one to the other and turns them into what the renderer draws.
/// Lengths are world units, like the font size.
/// </summary>
public sealed record WorldTextStyle
{
	public const int MaxLength = 512;
	public const float MinSize = 8f;
	public const float MaxSize = 144f;
	public const int MinWeight = 100;
	public const int MaxWeight = 900;
	public const float MinSpacing = -8f;
	public const float MaxSpacing = 32f;
	public const float MinLineHeight = 0.5f;
	public const float MaxLineHeight = 3f;
	public const float MaxOutlineWidth = 8f;
	public const float MaxShadowBlur = 16f;
	public const float MaxShadowOffset = 16f;

	/// <summary>
	/// The text is laid out this many times bigger than it shows and scaled down, so small sizes keep their shape.
	/// </summary>
	const float RenderScale = 4f;

	// How big text roughly comes out in Inter, as a share of the font size, for a server that can't lay it out
	const float AverageCharacterWidth = 0.5f;
	const float TypicalLineHeight = 1.21f;

	static readonly WorldTextStyle Defaults = new();

	public string Font { get; init; } = GameFonts.Default;
	public float Size { get; init; } = 14f;
	public int Weight { get; init; } = 400;
	public float LetterSpacing { get; init; }
	public float WordSpacing { get; init; }
	public float LineHeight { get; init; } = 1f;
	public Color Color { get; init; } = Color.White;

	public bool Outline { get; init; }
	public float OutlineWidth { get; init; } = 1f;
	public Color OutlineColor { get; init; } = Color.Black;

	public bool Shadow { get; init; }
	public Vector2 ShadowOffset { get; init; } = new( 1f, 1f );
	public float ShadowBlur { get; init; } = 1f;
	public Color ShadowColor { get; init; } = Color.Black.WithAlpha( 0.8f );

	public BlendMode BlendMode { get; init; } = BlendMode.Normal;

	/// <summary>
	/// This style kept within the limits, for values that came from someone else. Numbers that aren't numbers go back to the defaults.
	/// </summary>
	public WorldTextStyle Clamped() => this with
	{
		Font = GameFonts.Families.Contains( Font ) ? Font : GameFonts.Default,
		Size = Finite( Size, Defaults.Size ).Clamp( MinSize, MaxSize ),
		Weight = Weight.Clamp( MinWeight, MaxWeight ),
		LetterSpacing = Finite( LetterSpacing, Defaults.LetterSpacing ).Clamp( MinSpacing, MaxSpacing ),
		WordSpacing = Finite( WordSpacing, Defaults.WordSpacing ).Clamp( MinSpacing, MaxSpacing ),
		LineHeight = Finite( LineHeight, Defaults.LineHeight ).Clamp( MinLineHeight, MaxLineHeight ),
		Color = Finite( Color, Defaults.Color ),
		OutlineWidth = Finite( OutlineWidth, Defaults.OutlineWidth ).Clamp( 0f, MaxOutlineWidth ),
		OutlineColor = Finite( OutlineColor, Defaults.OutlineColor ),
		ShadowOffset = new Vector2( Finite( ShadowOffset.x, Defaults.ShadowOffset.x ), Finite( ShadowOffset.y, Defaults.ShadowOffset.y ) ).Clamp( -MaxShadowOffset, MaxShadowOffset ),
		ShadowBlur = Finite( ShadowBlur, Defaults.ShadowBlur ).Clamp( 0f, MaxShadowBlur ),
		ShadowColor = Finite( ShadowColor, Defaults.ShadowColor ),
		BlendMode = Enum.IsDefined( BlendMode ) ? BlendMode : Defaults.BlendMode,
	};

	static float Finite( float value, float fallback ) => float.IsFinite( value ) ? value : fallback;
	static Color Finite( Color value, Color fallback ) => float.IsFinite( value.r ) && float.IsFinite( value.g ) && float.IsFinite( value.b ) && float.IsFinite( value.a ) ? value : fallback;

	/// <summary>
	/// The text cut down to <see cref="MaxLength"/>.
	/// </summary>
	public static string ClampText( string text )
	{
		text ??= "";
		return text.Length > MaxLength ? text[..MaxLength] : text;
	}

	/// <summary>
	/// This style see-through by <paramref name="opacity"/>, its outline and shadow too.
	/// </summary>
	public WorldTextStyle Faded( float opacity ) => this with
	{
		Color = Color.WithAlpha( Color.a * opacity ),
		OutlineColor = OutlineColor.WithAlpha( OutlineColor.a * opacity ),
		ShadowColor = ShadowColor.WithAlpha( ShadowColor.a * opacity ),
	};

	/// <summary>
	/// What the renderer draws for <paramref name="text"/> in this style. A highlight is drawn as an outline around
	/// everything else, for when an outline effect wants to draw around the text but can't since it isn't a model.
	/// </summary>
	public TextRendering.Scope CreateScope( string text, Color? highlight = null, float highlightWidth = 0f )
	{
		var style = Clamped();
		var scope = new TextRendering.Scope( ClampText( text ),style.Color, style.Size * RenderScale, style.Font, style.Weight )
		{
			LetterSpacing = style.LetterSpacing * RenderScale,
			WordSpacing = style.WordSpacing * RenderScale,
			LineHeight = style.LineHeight,
		};

		// effects draw in the order shadow, highlight, outline, under the letters
		if ( style.Shadow )
		{
			scope.ShadowUnder = new TextRendering.Shadow
			{
				Enabled = true,
				Color = style.ShadowColor,
				Offset = style.ShadowOffset * RenderScale,
				Size = style.ShadowBlur * RenderScale,
			};
		}

		var outlineWidth = style.Outline ? style.OutlineWidth : 0f;

		if ( outlineWidth > 0f )
			scope.Outline = new TextRendering.Outline { Enabled = true, Color = style.OutlineColor, Size = outlineWidth * RenderScale };

		// the width is the whole stroke, half of it outside the letters, so the highlight shows past the outline
		if ( highlight is { } color )
			scope.OutlineUnder = new TextRendering.Outline { Enabled = true, Color = color, Size = (outlineWidth + highlightWidth * 2f) * RenderScale };

		return scope;
	}

	/// <summary>
	/// The size in world units of the text drawn in this style, effects included.
	/// </summary>
	public Vector2 Measure( string text )
	{
		// a server without rendering lays nothing out, the measure would be empty
		if ( Application.IsHeadless )
			return Estimate( text );

		return CreateScope( text ).Measure() / RenderScale;
	}

	/// <summary>
	/// We can't get exact size by <see cref="Measure"/> when we're from dedicated server, so fuck it and just try using approximate colliders instead.
	/// </summary>
	public Vector2 Estimate( string text )
	{
		var style = Clamped();
		var lines = ClampText( text ).Split( '\n' );

		var width = lines.Max( line => line.Length * (style.Size * AverageCharacterWidth + style.LetterSpacing) + line.Count( c => c == ' ' ) * style.WordSpacing );
		var height = lines.Length * style.Size * style.LineHeight * TypicalLineHeight;

		// effects reach past the letters, the outline by its width on each side, the shadow by its offset and blur
		var outline = style.Outline ? style.OutlineWidth * 2f : 0f;
		var shadowBlur = style.Shadow ? style.ShadowBlur * 3f : 0f;
		var shadowOffset = style.Shadow ? style.ShadowOffset.Abs() : Vector2.Zero;

		return new Vector2( MathF.Max( width, 0f ) + outline + shadowBlur + shadowOffset.x, height + outline + shadowBlur + shadowOffset.y );
	}

	/// <summary>
	/// Puts the text on a renderer in this style, at its size in the world.
	/// </summary>
	public void Apply( TextRenderer renderer, TextRendering.Scope scope )
	{
		renderer.TextScope = scope;
		renderer.Scale = 1f / RenderScale;
		renderer.BlendMode = BlendMode;
	}
}
