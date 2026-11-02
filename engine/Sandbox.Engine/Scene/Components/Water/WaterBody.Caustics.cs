namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// How strongly caustics modulate light at an intensity of one.
	/// </summary>
	const float CausticsStrength = 0.156f;

	LightModulator _caustics;
	Transform? _causticsSourceTransform;

	/// <summary>
	/// Project the material's caustics (its Caustics feature, texture, intensity, brightness and contrast) onto
	/// surfaces lit through the mean water plane, with one generated modulator aligned with the water's physical
	/// footprint. Zero intensity removes them. Scale, speed and wobble follow the material's detail normals; the
	/// two-layer animation recipe is fixed.
	/// The shader animates the texture using the detail clock at ten times its rate.
	/// </summary>
	void UpdateCaustics()
	{
		var material = ActiveMaterial;
		var texture = material.GetFeature( "F_CAUSTICS" ) != 0 ? material.GetTexture( "g_tCaustics" ) : null;
		float intensity = Math.Clamp( WaterFloat( material, "CausticsIntensity" ), 0, 8 );
		if ( !IsTexture2D( texture ) || !(intensity > 0) || Application.IsDedicatedServer
			|| WorldScale.x == 0 || WorldScale.y == 0 || WorldScale.z == 0 )
		{
			DestroyCaustics();
			return;
		}

		if ( !_caustics.IsValid() )
		{
			var helper = new GameObject( GameObject, false, "Water caustics" );
			helper.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.NotNetworked;
			_caustics = helper.Components.Create<LightModulator>();
			_causticsSourceTransform = null;
		}

		// Modulators use their local Y/Z plane; positive X points down through the water.
		var source = new Transform( SurfaceOrigin, WorldRotation, WorldScale );
		if ( _causticsSourceTransform != source )
		{
			_caustics.GameObject.WorldTransform = new Transform( source.Position,
				source.Rotation * Rotation.FromToRotation( Vector3.Forward, Vector3.Down ), 1 );
			_causticsSourceTransform = source;
		}
		_caustics.Size = new Vector2( MathF.Abs( Size.y * WorldScale.y ), MathF.Abs( Size.x * WorldScale.x ) );
		_caustics.Infinite = Ocean;
		_caustics.TwoSided = true;
		_caustics.WorldSizedUVs = true;
		_caustics.EdgeFeather = 0.015f;
		_caustics.Effect = LightModulator.ModulationEffect.Caustics;
		_caustics.Texture = texture;
		_caustics.Strength = Math.Clamp( CausticsStrength * intensity, 0, 1 );
		_caustics.BlendMode = LightModulator.ModulationBlendMode.Multiply;
		_caustics.Brightness = Math.Clamp( WaterFloat( material, "CausticsBrightness" ), 0, 16 );
		_caustics.Contrast = Math.Clamp( WaterFloat( material, "CausticsContrast" ), 0, 8 );
		_caustics.Tiling = Vector2.One * (4 * 128 / DetailScale);
		_caustics.AnimationTime = DetailTime * 10;
		_caustics.CausticsWobble = Math.Clamp( DetailStrength, 0, 4 ) * 0.025f;
		_caustics.CausticsLayerScale = 1.13f;
		_caustics.DistanceFeather = 10;
		_caustics.GameObject.Enabled = true;
	}

	/// <summary>
	/// Remove the owned modulator when the water or its caustics are disabled.
	/// </summary>
	void DestroyCaustics()
	{
		if ( _caustics.IsValid() )
		{
			_caustics.GameObject.Enabled = false;
			_caustics.GameObject.Destroy();
		}

		_caustics = null;
		_causticsSourceTransform = null;
	}
}
