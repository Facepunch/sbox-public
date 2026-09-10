namespace Sandbox.Audio;

internal static class AcousticMaterial
{
	static readonly FrequencyBands[] Transmission =
	[
		new( 0.46f, 0.29f, 0.17f ),  // Generic
		new( 0.40f, 0.22f, 0.12f ),  // Brick
		new( 0.27f, 0.16f, 0.09f ),  // Concrete
		new( 0.58f, 0.26f, 0.14f ),  // Dirt
		new( 0.82f, 0.65f, 0.44f ),  // Foliage
		new( 0.76f, 0.59f, 0.38f ),  // Gravel
		new( 0.55f, 0.34f, 0.22f ),  // Grate
		new( 0.65f, 0.32f, 0.14f ),  // Metal
		new( 0.65f, 0.32f, 0.18f ),  // Metal (panel)
		new( 0.49f, 0.29f, 0.14f ),  // Mud
		new( 0.27f, 0.12f, 0.07f ),  // Rock
		new( 0.76f, 0.59f, 0.38f ),  // Sand
		new( 0.72f, 0.54f, 0.34f ),  // Slime
		new( 0.81f, 0.66f, 0.45f ),  // Snow
		new( 0.83f, 0.69f, 0.49f ),  // Tile
		new( 0.79f, 0.56f, 0.36f ),  // Water
		new( 0.76f, 0.59f, 0.38f ),  // Wood
		new( 0.49f, 0.29f, 0.14f ),  // Wood (panel)
		new( 0.70f, 0.51f, 0.29f ),  // Plastic
		new( 0.72f, 0.48f, 0.25f ),  // Rubber
		new( 0.72f, 0.45f, 0.21f ),  // Glass (thick)
		new( 0.44f, 0.22f, 0.12f ),  // Plaster
		new( 0.60f, 0.42f, 0.25f ),  // Carpet
		new( 0.27f, 0.12f, 0.07f ),  // Stone
		new( 0.83f, 0.69f, 0.53f ),  // Ceiling tile
	];

	static readonly FrequencyBands[] Reflectivity =
	[
		new( 0.85f, 0.79f, 0.63f ),
		new( 0.85f, 0.80f, 0.65f ),
		new( 0.90f, 0.85f, 0.72f ),
		new( 0.85f, 0.80f, 0.67f ),
		new( 0.65f, 0.45f, 0.25f ),
		new( 0.55f, 0.30f, 0.08f ),
		new( 0.88f, 0.85f, 0.78f ),
		new( 0.80f, 0.72f, 0.57f ),
		new( 0.78f, 0.65f, 0.48f ),
		new( 0.88f, 0.85f, 0.82f ),
		new( 0.90f, 0.85f, 0.72f ),
		new( 0.60f, 0.40f, 0.20f ),
		new( 0.35f, 0.18f, 0.05f ),
		new( 0.65f, 0.48f, 0.28f ),
		new( 0.70f, 0.52f, 0.30f ),
		new( 0.65f, 0.50f, 0.30f ),
		new( 0.65f, 0.45f, 0.28f ),
		new( 0.88f, 0.85f, 0.82f ),
		new( 0.50f, 0.35f, 0.20f ),
		new( 0.75f, 0.62f, 0.48f ),
		new( 0.80f, 0.72f, 0.52f ),
		new( 0.83f, 0.76f, 0.60f ),
		new( 0.80f, 0.70f, 0.55f ),
		new( 0.88f, 0.83f, 0.70f ),
		new( 0.40f, 0.28f, 0.15f ),
	];

	public static FrequencyBands GetTransmission( AudioSurface surface )
	{
		var idx = (int)surface;
		return (uint)idx < (uint)Transmission.Length ? Transmission[idx] : Transmission[0];
	}

	public static FrequencyBands GetReflectivity( AudioSurface surface )
	{
		var idx = (int)surface;
		return (uint)idx < (uint)Reflectivity.Length ? Reflectivity[idx] : Reflectivity[0];
	}
}
