namespace Sandbox;

/// <summary>
/// Paints the interaction hint (use key and action name) below the crosshair
/// </summary>
public sealed class PressableSection : HudSection
{
	public override int Order => 5;

	/// <summary>
	/// The hint to show, null to fade out whatever is showing
	/// </summary>
	public Component.IPressable.Tooltip? Hint { get; set; }

	/// <summary>
	/// The input action that presses things, shown as the key to press
	/// </summary>
	public string Action { get; set; }

	const string TextFont = "Inter";
	const int TextWeight = 800;

	const float CrosshairGap = 48;
	const float KeySize = 26;
	const float KeyFontSize = 15;
	const float KeyPadding = 7;
	const float KeyGap = 10; // between the key and the name
	const float NameFontSize = 17;
	const float DescriptionFontSize = 14;
	const float DescriptionGap = 4;

	const float FadeDuration = 0.05f;
	const float IntroScale = 0.85f;

	const float GlowAmount = 0.75f;

	static readonly Color KeyTextColor = new( 0.07f, 0.07f, 0.07f );
	static readonly Color DimContent = Color.White.WithAlpha( 0.51f ); // a hint that can't be pressed right now

	Component.IPressable.Tooltip _shown;
	float _fade;

	string _keyLabel;
	Texture _keyGlyph;

	string _nameSource, _name;
	string _descriptionSource, _description;

	Rect _key, _nameRect, _descriptionRect, _bounds;

	public override void Tick()
	{
		if ( Hint is { } hint ) _shown = hint;

		var step = RealTime.Delta / FadeDuration;
		_fade = Hint.HasValue ? MathF.Min( _fade + step, 1 ) : MathF.Max( _fade - step, 0 );

		_bounds = default;
		if ( _fade <= 0 ) return;

		UpdateKey();
		UpdateLabels();
		UpdateLayout();
	}

	void UpdateKey()
	{
		_keyGlyph = null;
		_keyLabel = null;
		if ( string.IsNullOrEmpty( Action ) ) return;

		// controllers get the button's glyph, keyboards a key cap with the key's name
		if ( Input.UsingController )
		{
			var glyph = Input.GetGlyph( Action, InputGlyphSize.Medium, false );
			if ( glyph.IsValid() && glyph.Width > 0 && glyph.Height > 0 )
			{
				_keyGlyph = glyph;
				return;
			}
		}

		_keyLabel = (Input.GetButtonOrigin( Action ) ?? Action).ToUpperInvariant();
	}

	void UpdateLabels()
	{
		if ( _shown.Title != _nameSource )
		{
			_nameSource = _shown.Title;
			_name = Localize( _nameSource ).ToUpperInvariant();
		}

		if ( _shown.Description != _descriptionSource )
		{
			_descriptionSource = _shown.Description;
			_description = Localize( _descriptionSource );
		}
	}

	static string Localize( string text ) => !string.IsNullOrEmpty( text ) && text.StartsWith( '#' ) ? Game.Language.GetPhrase( text[1..] ) : text ?? "";

	/// <summary>
	/// The key and name sit on one centred row below the crosshair
	/// </summary>
	void UpdateLayout()
	{
		var scale = ScaleToScreen;
		var center = Canvas.PixelSize * 0.5f;

		var rowTop = MathF.Round( center.y + CrosshairGap * scale );
		var keyHeight = MathF.Round( KeySize * scale );

		var keyWidth = 0f;
		if ( _keyGlyph is { } glyph )
			keyWidth = MathF.Round( keyHeight * glyph.Width / glyph.Height );
		else if ( !string.IsNullOrEmpty( _keyLabel ) )
			keyWidth = MathF.Max( keyHeight, Measure( _keyLabel, KeyFontSize ).x + MathF.Round( KeyPadding * scale ) * 2 );

		var nameWidth = string.IsNullOrEmpty( _name ) ? 0 : Measure( _name, NameFontSize ).x;

		var keyGap = keyWidth > 0 && nameWidth > 0 ? MathF.Round( KeyGap * scale ) : 0;
		var left = MathF.Round( center.x - (keyWidth + keyGap + nameWidth) * 0.5f );

		_key = new Rect( left, rowTop, keyWidth, keyHeight );
		_nameRect = new Rect( _key.Right + keyGap, rowTop, nameWidth, keyHeight );
		_bounds = _key;
		_bounds.Add( _nameRect );

		_descriptionRect = default;
		if ( !string.IsNullOrWhiteSpace( _description ) )
		{
			var size = Measure( _description, DescriptionFontSize );
			_descriptionRect = new Rect( MathF.Round( center.x - size.x * 0.5f ), _key.Bottom + MathF.Round( DescriptionGap * scale ), size.x, size.y );
			_bounds.Add( _descriptionRect );
		}
	}

	Vector2 Measure( string text, float fontSize )
	{
		var size = new TextRendering.Scope( text, Color.White, fontSize * ScaleToScreen, TextFont, TextWeight ).Measure();
		return new Vector2( MathF.Ceiling( size.x ), MathF.Ceiling( size.y ) );
	}

	public override void GetCompositeRegions( List<Rect> regions )
	{
		if ( _bounds.Width <= 0 ) return;
		regions.Add( _bounds.Grow( MathF.Ceiling( GlowRadius * ScaleToScreen * 2 ) ) );
	}

	public override void Paint( Painter painter )
	{
		if ( _bounds.Width <= 0 ) return;

		var scale = ScaleToScreen;
		var center = Canvas.PixelSize * 0.5f;

		painter.Opacity = _fade;
		painter.Translate( center );
		painter.Scale( MathX.Lerp( IntroScale, 1, _fade ) );
		painter.Translate( -center );

		var enabled = _shown.Enabled;
		var color = enabled ? HudStyle.Brightened( Tinted( Color.White ), DigitGain * GlowAmount ) : Tinted( DimContent );

		if ( _keyGlyph is { } glyph )
		{
			painter.Texture( glyph, _key, enabled ? Color.White : DimContent );
		}
		else if ( _key.Width > 0 )
		{
			HudStyle.DrawPanel( painter, _key, color, scale );

			painter.TextStyle = new TextStyle( TextFont, KeyFontSize * scale, KeyTextColor ) { FontWeight = TextWeight, Alignment = TextFlag.Center };
			painter.Text( _keyLabel, Roomy( _key ) );
		}

		if ( _nameRect.Width > 0 )
		{
			painter.TextStyle = new TextStyle( TextFont, NameFontSize * scale, color ) { FontWeight = TextWeight, Alignment = TextFlag.Center };
			painter.Text( _name, Roomy( _nameRect ) );
		}

		if ( _descriptionRect.Width > 0 )
		{
			painter.TextStyle = new TextStyle( TextFont, DescriptionFontSize * scale, color ) { FontWeight = TextWeight, Alignment = TextFlag.Center };
			painter.Text( _description, Roomy( _descriptionRect ) );
		}
	}

	static Rect Roomy( Rect rect ) => rect.Grow( rect.Height );
}
