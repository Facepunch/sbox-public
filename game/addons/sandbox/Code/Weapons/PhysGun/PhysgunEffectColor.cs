/// <summary>
/// Colours a physgun effect (the beam end and the grab ball) in exactly the holder's colour. The prefab's own colours only
/// say how bright each part is: particle gradients are turned grey and glowing materials neutral, so nothing else tints them.
/// Remembers what the prefab set up, so it can be applied every frame.
/// </summary>
public sealed class PhysgunEffectColor : Component
{
	readonly Dictionary<Component, Color> _original = new();
	readonly HashSet<Component> _neutral = new();

	// a copy of each glowing material with its self-illum tint taken out, shared by every effect
	static readonly Dictionary<Material, Material> _neutralMaterials = new();

	/// <summary>
	/// Colour <paramref name="effect"/> and everything under it in <paramref name="color"/>
	/// </summary>
	public static void Apply( GameObject effect, Color color )
	{
		effect.GetOrAddComponent<PhysgunEffectColor>().Recolor( color );
	}

	void Recolor( Color color )
	{
		foreach ( var particles in GetComponentsInChildren<ParticleEffect>( true ) )
		{
			if ( _neutral.Add( particles ) )
				particles.Gradient = Grey( particles.Gradient );

			particles.Tint = Physgun.Tint( Original( particles, particles.Tint ), color );
		}

		foreach ( var light in GetComponentsInChildren<PointLight>( true ) )
			light.LightColor = Physgun.Tint( Original( light, light.LightColor ), color );

		foreach ( var model in GetComponentsInChildren<ModelRenderer>( true ) )
		{
			if ( _neutral.Add( model ) )
				Neutralize( model );

			model.Tint = Physgun.Tint( Original( model, model.Tint ), color );
		}
	}

	Color Original( Component component, Color current )
	{
		if ( _original.TryGetValue( component, out var original ) ) return original;

		_original[component] = current;
		return current;
	}

	/// <summary>
	/// Swaps a glowing material for a copy without its self-illum tint, which goes into the model's original tint instead so it stays as bright
	/// </summary>
	void Neutralize( ModelRenderer model )
	{
		if ( model.MaterialOverride is not { } material ) return;

		var selfIllum = material.GetColor( "g_vSelfIllumTint" ).WithAlpha( 1 );
		if ( selfIllum == Color.White ) return;

		if ( !_neutralMaterials.TryGetValue( material, out var neutral ) )
		{
			neutral = material.CreateCopy();
			neutral.Set( "g_vSelfIllumTint", Color.White );
			_neutralMaterials[material] = neutral;
		}

		_original[model] = model.Tint * selfIllum;
		model.MaterialOverride = neutral;
	}

	static ParticleGradient Grey( ParticleGradient gradient )
	{
		gradient.ConstantA = Grey( gradient.ConstantA );
		gradient.ConstantB = Grey( gradient.ConstantB );
		gradient.GradientA = Grey( gradient.GradientA );
		gradient.GradientB = Grey( gradient.GradientB );
		return gradient;
	}

	static Gradient Grey( Gradient gradient ) => gradient.WithFrames( [.. gradient.Colors.Select( x => new Gradient.ColorFrame( x.Time, Grey( x.Value ) ) )] );

	static Color Grey( Color color )
	{
		var luminance = Physgun.Luminance( color );
		return new Color( luminance, luminance, luminance, color.a );
	}
}
