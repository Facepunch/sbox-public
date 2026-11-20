public sealed class PhygunWorldmodel : Component
{
	[Property] public ParticleEffect GlowEffect { get; set; }
	[Property] public PointLight GlowLight { get; set; }

	/// <summary>
	/// How bright the glow is, while pulling and not. The colour itself comes from the holder's physgun colours, see <see cref="Physgun.Tint(Color, Color, Color)"/>.
	/// </summary>
	[Property] public Color GravTint { get; set; } = new Color( 1f, 0.8f, 0f );
	[Property] public Color PhysTint { get; set; } = new Color( 0f, 0.68333f, 1f );

	float _tintFrac;

	protected override void OnUpdate()
	{
		var physgun = GameObject.Root.Components.Get<Physgun>( FindMode.EverythingInSelfAndDescendants );
		var pullActive = physgun?.PullActive ?? false;

		_tintFrac = MathX.Approach( _tintFrac, pullActive ? 1 : 0, Time.Delta * 5 );

		// Steep ease-in-out so the transition rushes through the midpoint
		var t = _tintFrac < 0.5f
			? 8f * _tintFrac * _tintFrac * _tintFrac * _tintFrac
			: 1f - 8f * (1f - _tintFrac) * (1f - _tintFrac) * (1f - _tintFrac) * (1f - _tintFrac);

		var beam = physgun?.BeamColor ?? Physgun.DefaultBeamColor;
		var pull = physgun?.PullColor ?? Physgun.DefaultPullColor;
		var tint = Color.Lerp( Physgun.Tint( PhysTint, beam ), Physgun.Tint( GravTint, pull, Physgun.DefaultPullColor ), t );

		if ( GlowEffect is not null )
			GlowEffect.Tint = tint;

		if ( GlowLight is not null )
			GlowLight.LightColor = tint;
	}
}
