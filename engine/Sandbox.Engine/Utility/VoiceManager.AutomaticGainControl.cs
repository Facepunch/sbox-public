namespace Sandbox;

internal static partial class VoiceManager
{
	static readonly AutomaticGainControl gainControl = new( Rate, FrameSamples );

	/// <summary>
	/// Automatic gain control, see <see cref="Preferences.VoiceAutomaticGainControl"/>. Give every frame to
	/// <see cref="Learn"/>: it follows the background noise, and how loud speech is above it, so silence and
	/// steady noise don't get boosted. <see cref="Apply"/> scales frames towards <see cref="TargetLevel"/>.
	/// </summary>
	internal sealed class AutomaticGainControl
	{
		#region Gain

		/// <summary>
		/// How loud we want speech to be sent, in decibels
		/// </summary>
		public const float TargetLevel = -18.0f;

		/// <summary>
		/// Most we'll boost a quiet microphone, in decibels
		/// </summary>
		public const float MaxGain = 24.0f;

		/// <summary>
		/// Most we'll turn down a loud microphone, in decibels
		/// </summary>
		public const float MinGain = -12.0f;

		/// <summary>
		/// Loudest a sample is allowed to get after gain, so boosted plosives don't clip
		/// </summary>
		public const float Ceiling = 0.9f;

		/// <summary>
		/// How fast gain can go up, in decibels a second. It can always come down straight away, so a
		/// frame never clips, but coming back up is gradual, so limiting a plosive doesn't pump.
		/// </summary>
		public const float GainRiseRate = 20.0f;

		#endregion

		#region Speech

		/// <summary>
		/// How fast the speech estimate follows louder speech, in decibels a second. Fast, so a shout gets
		/// turned down. Together with <see cref="SpeechFallRate"/>, it settles near the loud end of speech.
		/// </summary>
		public const float SpeechRiseRate = 100.0f;

		/// <summary>
		/// How fast the speech estimate follows quieter speech, in decibels a second. Slow, so quiet syllables
		/// don't pump the gain up.
		/// </summary>
		public const float SpeechFallRate = 10.0f;

		/// <summary>
		/// How much louder than the background noise a frame has to be to count as speech, in decibels
		/// </summary>
		public const float NoiseMargin = 10.0f;

		/// <summary>
		/// How long a frame has to stay above <see cref="NoiseMargin"/> to count as speech, in seconds.
		/// A knock or a click is shorter than a syllable.
		/// </summary>
		public const float MinSpeechTime = 0.1f;

		#endregion

		#region Noise

		/// <summary>
		/// Frames within this many decibels of the noise estimate are the room. It follows them at
		/// <see cref="NoiseNearRate"/> either way, so it settles in the middle of the room rather than its
		/// quietest moments.
		/// </summary>
		public const float NoiseBand = 6.0f;

		/// <summary>
		/// How fast the noise estimate follows frames within <see cref="NoiseBand"/> of it, up or down, in
		/// decibels a second
		/// </summary>
		public const float NoiseNearRate = 5.0f;

		/// <summary>
		/// How fast the noise estimate rises towards frames well above it, in decibels a second. Slow, so
		/// talking doesn't push it up, but a fan that's been switched on is found in the end.
		/// </summary>
		public const float NoiseRiseRate = 0.5f;

		/// <summary>
		/// How fast the noise estimate falls towards frames well below it, in decibels a second. Quick, so
		/// it soon recovers if it started on speech.
		/// </summary>
		public const float NoiseFallRate = 50.0f;

		/// <summary>
		/// We won't boost the background noise above this, in decibels, however quiet the speech is
		/// </summary>
		public const float MaxNoiseLevel = -40.0f;

		#endregion

		/// <summary>
		/// Moves a level in decibels towards another, by at most a step each frame, never past it
		/// </summary>
		readonly struct Follower
		{
			readonly float rise, fall;

			/// <param name="riseRate">Fastest it goes up, in decibels a second</param>
			/// <param name="fallRate">Fastest it goes down, in decibels a second</param>
			/// <param name="frameTime">Seconds between frames</param>
			public Follower( float riseRate, float fallRate, float frameTime )
			{
				rise = riseRate * frameTime;
				fall = fallRate * frameTime;
			}

			public float Towards( float level, float target ) => target > level
				? MathF.Min( target, level + rise )
				: MathF.Max( target, level - fall );
		}

		readonly Follower speech;
		readonly Follower noiseNear;
		readonly Follower noiseFar;
		readonly float gainRise;
		readonly int minSpeechFrames;

		/// <summary>
		/// Frames in a row above <see cref="NoiseMargin"/>
		/// </summary>
		int speechFrames;

		/// <summary>
		/// How loud speech has been lately, in decibels
		/// </summary>
		public float SpeechLevel { get; private set; } = TargetLevel;

		/// <summary>
		/// How loud the background noise is, in decibels. <see cref="MinLevel"/> until we've heard anything.
		/// </summary>
		public float NoiseLevel { get; private set; } = MinLevel;

		/// <summary>
		/// Gain the next frame will be scaled towards, in decibels, before the ceiling is applied. Boosts no
		/// further than would lift the noise above <see cref="MaxNoiseLevel"/>.
		/// </summary>
		public float Gain
		{
			get
			{
				var maxGain = Math.Clamp( MaxNoiseLevel - NoiseLevel, 0.0f, MaxGain );
				return Math.Clamp( TargetLevel - SpeechLevel, MinGain, maxGain );
			}
		}

		/// <summary>
		/// Linear gain the last frame finished on, the next one ramps from here so it doesn't click
		/// </summary>
		float appliedGain = 1.0f;

		/// <summary>
		/// Device we're learning from, we'll <see cref="Reset"/> if it changes.
		/// </summary>
		string currentDevice;

		/// <param name="sampleRate">Samples per second of what we'll be given</param>
		/// <param name="frameSamples">Samples in each frame given to <see cref="Learn"/> and <see cref="Apply"/></param>
		public AutomaticGainControl( int sampleRate, int frameSamples )
		{
			var frameTime = (float)frameSamples / sampleRate;

			speech = new Follower( SpeechRiseRate, SpeechFallRate, frameTime );
			noiseNear = new Follower( NoiseNearRate, NoiseNearRate, frameTime );
			noiseFar = new Follower( NoiseRiseRate, NoiseFallRate, frameTime );
			gainRise = DbToGain( GainRiseRate * frameTime );
			minSpeechFrames = Math.Max( 1, (int)MathF.Round( MinSpeechTime / frameTime ) );
		}

		/// <summary>
		/// Start over, the next mic might be nothing like the last one
		/// </summary>
		private void Reset()
		{
			SpeechLevel = TargetLevel;
			NoiseLevel = MinLevel;
			speechFrames = 0;
			appliedGain = 1.0f;
		}

		/// <summary>
		/// Set which device we're learning from, this will trigger a reset if it's different from the current value
		/// </summary>
		public void SetDevice( string device )
		{
			if ( currentDevice == device ) return;

			currentDevice = device;
			Reset();
		}

		/// <summary>
		/// Learn from a frame, given its level in decibels from a <see cref="LevelMeter"/>. Give it every
		/// frame, it works out which are speech.
		/// </summary>
		public void Learn( float level )
		{
			// Digital silence is a muted or idle mic, not the room, so it says nothing about the noise
			if ( level <= MinLevel )
				return;

			LearnNoise( level );
			LearnSpeech( level );
		}

		void LearnNoise( float level )
		{
			// Haven't heard anything yet, start from here. If it's speech, the room will soon pull it down.
			if ( NoiseLevel <= MinLevel )
			{
				NoiseLevel = level;
				return;
			}

			var follower = MathF.Abs( level - NoiseLevel ) <= NoiseBand ? noiseNear : noiseFar;

			NoiseLevel = follower.Towards( NoiseLevel, level );
		}

		void LearnSpeech( float level )
		{
			if ( level < NoiseLevel + NoiseMargin )
			{
				speechFrames = 0;
				return;
			}

			// Long enough to be someone talking, not a knock
			if ( ++speechFrames < minSpeechFrames )
				return;

			SpeechLevel = speech.Towards( SpeechLevel, level );
		}

		/// <summary>
		/// Apply gain to <paramref name="frame"/> in place. While not <paramref name="enabled"/>, eases back to
		/// unity and then leaves frames alone.
		/// </summary>
		public void Apply( Span<float> frame, bool enabled = true )
		{
			if ( !enabled && appliedGain == 1.0f )
				return;

			// Off, we're only easing back to unity, so don't limit what the mic gives us
			var gain = enabled ? DbToGain( Gain ) : 1.0f;
			var ceiling = enabled ? Ceiling : 1.0f;

			// Come up gradually, but go down as far as this frame needs. We have the whole frame, so look
			// ahead and keep its peak under the ceiling.
			gain = MathF.Min( gain, appliedGain * gainRise );

			var peak = Peak( frame );
			if ( peak * gain > ceiling )
				gain = ceiling / peak;

			// Ramp from last frame's gain, unless that would clip this one. Both ends are under the
			// ceiling, so everything in between is too.
			var start = peak * appliedGain > ceiling ? gain : appliedGain;

			var increment = 1f / frame.Length;

			for ( int i = 0; i < frame.Length; i++ )
			{
				frame[i] *= MathX.Lerp( start, gain, (i + 1) * increment );
			}

			appliedGain = gain;
		}

		static float Peak( ReadOnlySpan<float> frame )
		{
			float peak = 0;
			foreach ( var s in frame )
				peak = MathF.Max( peak, MathF.Abs( s ) );

			return peak;
		}

		static float DbToGain( float db ) => MathF.Pow( 10.0f, db / 20.0f );
	}
}
