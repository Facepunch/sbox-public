public sealed class PhygunViewmodel : Component, Component.ExecuteInEditor
{
	[Property] public List<SpriteRenderer> TipSprites { get; set; }
	[Property] public ParticleEffect GlowEffect { get; set; }
	[Property] public ParticleEffect SparksEffect { get; set; }
	[Property] public Material TubeFxMaterial { get; set; }
	[Property] public Material BottleMaterial { get; set; }

	[Property] public bool BeamActive { get; set; }
	/// <summary>
	/// How bright the glow is, while pulling and not. The colour itself comes from the holder's physgun colours, see <see cref="Physgun.Tint(Color, Color, Color)"/>.
	/// </summary>
	[Property] public Color GravTint { get; set; }
	[Property] public Color PhysTint { get; set; }

	/// <summary>
	/// Strength of the glow particles and tip sprites in grav (yellow) mode relative to phys (blue).
	/// Yellow at full strength saturates into flat, hard-edged blooming discs.
	/// </summary>
	[Property, Range( 0, 1 )] public float GravGlowScale { get; set; } = 0.55f;

	float _tintFrac;
	Color _effectsTint;
	float _glowScale = 1;

	protected override void OnUpdate()
	{
		if ( GetComponentInParent<Physgun>() is Physgun physgun )
		{
			BeamActive = physgun.BeamActive;

			var physTint = Physgun.Tint( PhysTint, physgun.BeamColor );
			var gravTint = Physgun.Tint( GravTint, physgun.PullColor, Physgun.DefaultPullColor );

			if ( BeamActive && SparksEffect.IsValid() )
				SparksEffect.Tint = physgun.PullActive ? gravTint : physTint;

			_tintFrac = MathX.Approach( _tintFrac, physgun.PullActive ? 1 : 0, Time.Delta * 5 );
			// Steep ease-in-out so the transition rushes through the midpoint
			var t = _tintFrac < 0.5f
				? 8f * _tintFrac * _tintFrac * _tintFrac * _tintFrac
				: 1f - 8f * (1f - _tintFrac) * (1f - _tintFrac) * (1f - _tintFrac) * (1f - _tintFrac);

			_effectsTint = Color.Lerp( physTint, gravTint, t );
			_glowScale = MathX.Lerp( 1, GravGlowScale, t );
		}
		else
		{
			_tintFrac = 0.0f;
			_effectsTint = PhysTint;
			_glowScale = 1;
		}

		UpdateGlowEffect();
		UpdateTipSprites();
		UpdateTubeFx();
		UpdateSparks();
		UpdateBottleGlow();
	}

	float _scroll;
	float _scrollSpeed;
	float _scrollSpeedVel;

	void UpdateTubeFx()
	{
		if ( TubeFxMaterial is null ) return;

		// ideally we'd scroll the self illum on its own - but that's not an option.
		// we have g_vSelfIllumScrollSpeed but we can't scale that speed up and down, because it's multiplied by time internally.

		_scrollSpeed = MathX.SmoothDamp( _scrollSpeed, BeamActive ? 2.0f : 0.2f, ref _scrollSpeedVel, BeamActive ? 0.5f : 2.5f, Time.Delta );
		_scroll += _scrollSpeed * Time.Delta;

		TubeFxMaterial.Set( "g_vSelfIllumOffset", new Vector2( _scroll % 1, 0 ) );
		TubeFxMaterial.Set( "g_flSelfIllumBrightness", 2f * (_scrollSpeed + 1) );
		TubeFxMaterial.Set( "g_vSelfIllumTint", _effectsTint );
	}

	void UpdateBottleGlow()
	{
		if ( BottleMaterial is null ) return;

		float bounce = MathF.Sin( Time.Now * (BeamActive ? 45.0f : 3.0f) ) * 0.5f;

		BottleMaterial.Set( "g_vSelfIllumTint", _effectsTint );
		BottleMaterial.Set( "g_flSelfIllumBrightness", (BeamActive ? 6.0f : 1.5f) + bounce );
	}

	void UpdateTipSprites()
	{
		var mul = BeamActive ? 0.6f : 0.3f;

		foreach ( var sprite in TipSprites )
		{
			sprite.Enabled = true;
			sprite.Color = _effectsTint.WithAlpha( mul * _glowScale * Random.Shared.Float( 0.4f, 0.9f ) );
			var targetSize = Random.Shared.Float( 3, 4f );
			sprite.Size = Vector2.Lerp( sprite.Size, targetSize, 1f - MathF.Exp( -Time.Delta * 12f ) );
		}
	}

	void UpdateGlowEffect()
	{
		if ( GlowEffect is null ) return;

		GlowEffect.Tint = _effectsTint;
		GlowEffect.Alpha = (BeamActive ? 1.0f : 0.2f) * _glowScale;
	}


	bool _wasActive;

	void UpdateSparks()
	{
		if ( !SparksEffect.IsValid() ) return;

		if ( BeamActive == _wasActive ) return;

		_wasActive = BeamActive;

		int count = BeamActive ? 20 : 3;

		for ( int i = 0; i < count; i++ )
		{
			var p = SparksEffect.Emit( SparksEffect.WorldPosition, i / (float)count );
			if ( p is null ) continue;
			p.Velocity = Vector3.Random * 100.0f + SparksEffect.WorldTransform.Forward * 30.0f;
		}
	}
}
