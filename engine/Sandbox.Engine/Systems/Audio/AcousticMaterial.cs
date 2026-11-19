namespace Sandbox.Audio;

internal static class AcousticMaterial
{
	// Indexed by AudioSurface, measured per band at 250 Hz, 1 kHz and 4 kHz: Steam Audio's presets where it has one,
	// standard tables and the mass law otherwise. Transmission is the gain through one wall, absorption the energy a
	// bounce loses.
	static readonly (FrequencyBands Transmission, FrequencyBands Absorption)[] Materials =
	[
		(new( 0.1f, 0.05f, 0.03f ), new( 0.08f, 0.1f, 0.12f )), // Generic
		(new( 0.015f, 0.015f, 0.015f ), new( 0.03f, 0.04f, 0.07f )), // Brick
		(new( 0.015f, 0.002f, 0.001f ), new( 0.05f, 0.07f, 0.08f )), // Concrete
		(new( 0.06f, 0.044f, 0.011f ), new( 0.01f, 0.01f, 0.02f )), // Ceramic
		(new( 0.031f, 0.012f, 0.008f ), new( 0.6f, 0.7f, 0.8f )), // Gravel
		(new( 0.02f, 0.005f, 0.003f ), new( 0.24f, 0.69f, 0.73f )), // Carpet
		(new( 0.06f, 0.044f, 0.011f ), new( 0.06f, 0.03f, 0.02f )), // Glass
		(new( 0.056f, 0.056f, 0.004f ), new( 0.12f, 0.06f, 0.04f )), // Plaster
		(new( 0.07f, 0.014f, 0.005f ), new( 0.11f, 0.07f, 0.06f )), // Wood
		(new( 0.2f, 0.025f, 0.01f ), new( 0.2f, 0.07f, 0.06f )), // Metal
		(new( 0.015f, 0.002f, 0.001f ), new( 0.03f, 0.04f, 0.05f )), // Rock
		(new( 0.5f, 0.3f, 0.15f ), new( 0.15f, 0.45f, 0.6f )), // Fabric
		(new( 0.5f, 0.3f, 0.2f ), new( 0.3f, 0.9f, 0.95f )), // Foam
		(new( 0.015f, 0.002f, 0.001f ), new( 0.2f, 0.45f, 0.6f )), // Sand
		(new( 0.03f, 0.01f, 0.005f ), new( 0.45f, 0.85f, 0.9f )), // Snow
		(new( 0.015f, 0.002f, 0.001f ), new( 0.15f, 0.35f, 0.55f )), // Soil
		(new( 0.5f, 0.3f, 0.15f ), new( 0.3f, 0.65f, 0.7f )), // Curtain
		(new( 0.018f, 0.0045f, 0.002f ), new( 0.1f, 0.05f, 0.05f )), // Steel
		(new( 0.2f, 0.08f, 0.04f ), new( 0.45f, 0.75f, 0.85f )), // AcousticTile
		(new( 0.3f, 0.15f, 0.08f ), new( 0.2f, 0.45f, 0.55f )), // Leather
		(new( 0.015f, 0.002f, 0.001f ), new( 0.02f, 0.03f, 0.04f )), // Linoleum
		(new( 0.015f, 0.002f, 0.001f ), new( 0.03f, 0.05f, 0.08f )), // Asphalt
		(new( 0.015f, 0.002f, 0.001f ), new( 0.01f, 0.015f, 0.02f )), // Water
		(new( 0.015f, 0.002f, 0.001f ), new( 0.01f, 0.01f, 0.02f )), // Marble
		(new( 0.4f, 0.25f, 0.15f ), new( 0.1f, 0.25f, 0.35f )), // Paper
	];

	// Real rooms are full of furniture, clutter, openings and uneven surfaces that maps rarely model, all of which shorten
	// the reverb. Every bounce loses this much energy on top of the surface's own absorption.
	const float Clutter = 0.1f;

	// Games need to hear through walls more than real walls allow: we keep three quarters of the measured loss in dB.
	const float TransmissionLoss = 0.75f;

	static readonly FrequencyBands[] Transmission = Materials
		.Select( m => new FrequencyBands( MathF.Pow( m.Transmission.Low, TransmissionLoss ), MathF.Pow( m.Transmission.Mid, TransmissionLoss ), MathF.Pow( m.Transmission.High, TransmissionLoss ) ) )
		.ToArray();

	static readonly FrequencyBands[] Reflectivity = Materials
		.Select( m => new FrequencyBands( 1f - m.Absorption.Low, 1f - m.Absorption.Mid, 1f - m.Absorption.High ) * (1f - Clutter) )
		.ToArray();

	public static FrequencyBands GetTransmission( AudioSurface surface )
	{
		var idx = (int)surface;
		return (uint)idx < (uint)Transmission.Length ? Transmission[idx] : Transmission[0];
	}

	/// <summary>
	/// Energy kept per bounce, after the surface's absorption and the clutter loss.
	/// </summary>
	public static FrequencyBands GetReflectivity( AudioSurface surface )
	{
		var idx = (int)surface;
		return (uint)idx < (uint)Reflectivity.Length ? Reflectivity[idx] : Reflectivity[0];
	}
}
