namespace Sandbox;

internal static partial class VoiceManager
{
	static readonly LevelMeter levelMeter = new( Rate, FrameSamples );

	/// <summary>
	/// How loud each frame is, for the gate, automatic gain control and the levels we report: its RMS in
	/// decibels relative to full scale (a full scale square wave is 0, a sine -3), with frequencies below
	/// <see cref="HighPassFrequency"/> taken out first. Desk thumps, fans and mic handling are mostly down
	/// there and voice mostly isn't, so they shouldn't open the gate or be mistaken for speech.
	/// </summary>
	internal sealed class LevelMeter
	{
		/// <summary>
		/// Frequencies below this don't count, in hertz
		/// </summary>
		public const float HighPassFrequency = 100.0f;

		readonly Sandbox.Audio.Biquad highPass;
		readonly float[] buffer;
		float z1, z2;

		/// <param name="sampleRate">Samples per second of what we'll be given</param>
		/// <param name="frameSamples">Most samples in a frame given to <see cref="Measure"/></param>
		public LevelMeter( int sampleRate, int frameSamples )
		{
			highPass = Sandbox.Audio.Biquad.HighPass( HighPassFrequency, 0.7071f, sampleRate );
			buffer = new float[frameSamples];
		}

		/// <summary>
		/// Forget the filter's state, for when what comes next doesn't follow on from what came before
		/// </summary>
		public void Reset()
		{
			z1 = z2 = 0;
		}

		/// <summary>
		/// How loud this frame is, in decibels, no quieter than <see cref="MinLevel"/>. The filter carries
		/// over between frames, so give it every frame in order. Doesn't change the frame.
		/// </summary>
		public float Measure( ReadOnlySpan<float> frame )
		{
			if ( frame.Length > buffer.Length )
				throw new ArgumentException( $"Frame is longer than {buffer.Length} samples", nameof( frame ) );

			var filtered = buffer.AsSpan( 0, frame.Length );
			frame.CopyTo( filtered );
			highPass.Process( filtered, ref z1, ref z2 );

			float sum = 0;
			foreach ( var s in filtered )
				sum += s * s;

			var db = 20.0f * MathF.Log10( MathF.Sqrt( sum / filtered.Length ) + 1e-9f );
			return MathF.Max( db, MinLevel );
		}
	}
}
