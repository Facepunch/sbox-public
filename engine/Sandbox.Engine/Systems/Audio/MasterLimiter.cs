namespace Sandbox.Audio;

/// <summary>
/// Lookahead peak limiter for the master output. The gain is already down when a peak arrives and recovers over
/// <see cref="ReleaseSeconds"/>, so loud moments get quieter instead of clipping.
/// </summary>
sealed class MasterLimiter
{
	public const int Lookahead = 64;
	public const float Ceiling = 0.98f;
	const float ReleaseSeconds = 0.1f;

	readonly float _release = 1f - MathF.Exp( -1f / (ReleaseSeconds * AudioEngine.SamplingRate) );
	readonly float[] _targets = new float[Lookahead];
	readonly float[] _gains = new float[Lookahead];
	float[][] _delay = [];
	float[][] _scratch = [];
	int _pos, _limiting;
	float _env = 1f;
	double _gainSum = Lookahead;

	public MasterLimiter()
	{
		Array.Fill( _targets, 1f );
		Array.Fill( _gains, 1f );
	}

	public void Process( MultiChannelBuffer buffer )
	{
		int channels = buffer.ChannelCount;
		int frames = AudioEngine.MixBufferSize;
		if ( _scratch.Length != channels )
		{
			_scratch = new float[channels][];
			for ( int ch = 0; ch < channels; ch++ ) _scratch[ch] = new float[frames];
		}

		for ( int ch = 0; ch < channels; ch++ ) buffer.Get( ch ).Buffer.CopyTo( _scratch[ch] );
		Process( _scratch, frames );
		for ( int ch = 0; ch < channels; ch++ ) _scratch[ch].AsSpan( 0, frames ).CopyTo( buffer.Get( ch ).Buffer );
	}

	/// <summary>
	/// Limit <paramref name="frames"/> samples of every channel in place. Output lags input by <see cref="Lookahead"/> - 1 samples.
	/// </summary>
	public void Process( float[][] channels, int frames )
	{
		if ( _delay.Length != channels.Length )
		{
			_delay = new float[channels.Length][];
			for ( int ch = 0; ch < channels.Length; ch++ ) _delay[ch] = new float[Lookahead];
		}

		for ( int i = 0; i < frames; i++ )
		{
			float peak = 0f;
			for ( int ch = 0; ch < channels.Length; ch++ ) peak = MathF.Max( peak, MathF.Abs( channels[ch][i] ) );

			// The gain each sample needs, and the lowest of those still ahead in the delay.
			float target = peak > Ceiling ? Ceiling / peak : 1f;
			if ( _targets[_pos] < 1f ) _limiting--;
			if ( target < 1f ) _limiting++;
			_targets[_pos] = target;

			float needed = 1f;
			if ( _limiting > 0 )
			{
				foreach ( var t in _targets ) needed = MathF.Min( needed, t );
			}

			// Recover slowly, drop at once; averaging over the lookahead turns the drop into a ramp that lands on the peak.
			_env = MathF.Min( needed, _env + (1f - _env) * _release );
			_gainSum += _env - _gains[_pos];
			_gains[_pos] = _env;
			float gain = (float)(_gainSum / Lookahead);

			int oldest = _pos + 1 == Lookahead ? 0 : _pos + 1;
			for ( int ch = 0; ch < channels.Length; ch++ )
			{
				var delay = _delay[ch];
				delay[_pos] = channels[ch][i];
				channels[ch][i] = delay[oldest] * gain;
			}

			_pos = oldest;
		}
	}
}
