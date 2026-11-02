namespace Sandbox;

public sealed partial class WaterBody
{
	const string WaterShader = "shaders/water.shader";
	const string DefaultMaterial = "materials/water/water.vmat";

	/// <summary>
	/// How the water looks: colour and opacity from above and below, roughness, detail normals, refraction and its
	/// fog, and the caustics it projects below. A material using the Water shader.
	/// </summary>
	[Property, Group( "Surface" ), Order( -296 )]
	public Material Material { get; set; } = Material.Load( DefaultMaterial );

	/// <summary>
	/// The material drawn: the assigned one when it uses the Water shader, else the default.
	/// </summary>
	internal Material ActiveMaterial => IsWaterMaterial( Material ) ? Material : Material.Load( DefaultMaterial );

	static bool IsWaterMaterial( Material material ) => material is { IsValid: true }
		&& material.ShaderName.Replace( '\\', '/' ).StartsWith( WaterShader, StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// A float parameter of a water material, or the fallback when it is NaN. Readers clamp infinities to the same
	/// limits the shader does, so both agree on every value.
	/// </summary>
	static float WaterFloat( Material material, string name, float fallback = 0 )
	{
		float value = material.GetVector4( name ).x;
		return float.IsNaN( value ) ? fallback : value;
	}

	/// <summary>
	/// Coverage of the material's colour over the captured scene behind the side seen: zero transmits it, one is opaque.
	/// </summary>
	internal static float MaterialOpacity( Material material, bool underside )
		=> Math.Clamp( WaterFloat( material, underside ? "UndersideOpacity" : "Opacity" ), 0, 1 );

	// The shader applies the same limits to scale and strength; a NaN reads as their lower limit there too.
	float DetailSpeed => Math.Clamp( WaterFloat( ActiveMaterial, "DetailSpeed" ), 0, 10 );
	float DetailScale => Math.Clamp( WaterFloat( ActiveMaterial, "DetailScale", 1 ), 1, 4096 );
	float DetailStrength => Math.Clamp( WaterFloat( ActiveMaterial, "DetailStrength" ), 0, 4 );

	/// <summary>
	/// Detail-clock offset, preserving phase across material speed edits, swaps, cloning and serialization.
	/// </summary>
	[Property, Hide]
	public double DetailTimeOffset
	{
		get;
		set => field = double.IsFinite( value ) ? value : 0;
	}

	/// <summary>
	/// The detail speed <see cref="DetailTimeOffset"/> was last corrected for; negative before the first.
	/// </summary>
	[Property, Hide]
	public float DetailTimeSpeed
	{
		get;
		set => field = float.IsFinite( value ) ? value : -1;
	} = -1;

	/// <summary>
	/// The detail animation phase shared by the surface normals and caustics. A speed change, by editing or
	/// swapping the material, only changes future motion: the phase carries on from where it was.
	/// </summary>
	internal float DetailTime
	{
		get
		{
			float speed = DetailSpeed;
			double now = Time.NowDouble;
			if ( speed != DetailTimeSpeed )
			{
				if ( DetailTimeSpeed >= 0 )
					DetailTimeOffset += now * ((double)DetailTimeSpeed - speed);
				DetailTimeSpeed = speed;
			}
			return (float)(now * speed + DetailTimeOffset);
		}
	}
}
