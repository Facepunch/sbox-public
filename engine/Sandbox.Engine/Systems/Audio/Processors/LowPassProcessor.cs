using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox.Audio;

/// <summary>
/// Cuts frequencies above <see cref="Frequency"/>, making sounds muffled.
/// </summary>
[Expose]
public sealed class LowPassProcessor : AudioProcessor<LowPassProcessor.State>
{
	/// <summary>
	/// Frequencies above this are cut, in Hz.
	/// </summary>
	[Range( 20, 20000 )]
	public float Frequency { get; set; } = 4865.015f; // Legacy Cutoff = 0.5 at 44100 Hz

	/// <summary>
	/// Boosts frequencies around the cutoff. 0.707 is a flat response.
	/// </summary>
	[Range( 0.1f, 10 )]
	public float Resonance { get; set; } = 0.707f;

	/// <summary>
	/// Legacy smoothing factor (0 to 1), mapped to <see cref="Frequency"/>.
	/// </summary>
	[Obsolete( "Use Frequency" ), Hide, JsonIgnore]
	public float Cutoff
	{
		get => 1 - MathF.Exp( -2 * MathF.PI * Frequency / AudioEngine.SamplingRate );
		set => Frequency = Math.Clamp( value >= 1 ? 20000 : -MathF.Log( 1 - Math.Max( value, 0 ) ) * AudioEngine.SamplingRate / (2 * MathF.PI), 20, 20000 );
	}

	public class State : ListenerState
	{
		internal PerChannel<float> Z1;
		internal PerChannel<float> Z2;
	}

	protected override void ProcessSingleChannel( AudioChannel channel, Span<float> input )
	{
		var filter = Biquad.LowPass( Frequency, Resonance, AudioEngine.SamplingRate );
		var z1 = CurrentState.Z1.Get( channel );
		var z2 = CurrentState.Z2.Get( channel );

		filter.Process( input, ref z1, ref z2 );

		CurrentState.Z1.Set( channel, z1 );
		CurrentState.Z2.Set( channel, z2 );
	}

	internal override void OnDeserialized( JsonObject node )
	{
#pragma warning disable CS0618
		if ( !node.ContainsKey( nameof( Frequency ) ) && node[nameof( Cutoff )] is JsonValue cutoff )
			Cutoff = cutoff.GetValue<float>();
#pragma warning restore CS0618
	}
}
