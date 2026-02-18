namespace Sandbox.Audio;

class BinauralEffect : IDisposable
{
	[ConVar] public static float snd_dir_power { get; set; } = 0.80f;
	[ConVar] public static bool snd_steamaudio { get; set; } = true;

	PerChannel<float> _gains;
	internal CBinauralEffect _native;

	internal BinauralEffect()
	{
		if ( snd_steamaudio )
		{
			_native = CBinauralEffect.Create();
		}
	}

	~BinauralEffect()
	{
		Dispose();
	}

	public void Dispose()
	{
		if ( _native.IsNull )
			return;

		GC.SuppressFinalize( this );

		MainThread.QueueDispose( _native );
		_native = default;
	}

	internal void Apply( Vector3 direction, float spatialBlend, bool useNearestInterpolation, MultiChannelBuffer input, MultiChannelBuffer output )
	{
		if ( snd_steamaudio && _native.IsValid )
		{
			output.Silence();
			_native.Apply( direction, spatialBlend, useNearestInterpolation, input._native, output._native );
		}
		else
		{
			if ( spatialBlend <= 0 )
			{
				output.CopyFrom( input );
				return;
			}

			output.Silence();

			if ( spatialBlend < 1 )
			{
				output.MixFrom( input, 1 - spatialBlend );
			}

			// Normalize direction
			direction = direction.Normal;

			float fwd = direction.Dot( Vector3.Forward );

			if ( fwd < 0 )
			{
				fwd *= -0.5f;
			}

			float left = Vector3.Left.Dot( direction ).Remap( -1, 1, -1, 1 );

			{
				float gain = left.Clamp( 0, 1 ) + fwd;
				gain = MathF.Pow( gain, snd_dir_power );
				gain = MathX.Lerp( 0.0f, gain, spatialBlend );
				gain = gain.Clamp( 0.0001f, 1.0f );

				var smoothed = _gains.Get( AudioChannel.Left ).Approach( gain, 0.1f );
				_gains.Set( AudioChannel.Left, smoothed );
				output.Get( AudioChannel.Left ).MixFrom( input, smoothed );
			}

			{
				float gain = (-left).Clamp( 0, 1 ) + fwd;
				gain = MathF.Pow( gain, snd_dir_power );
				gain = MathX.Lerp( 0.0f, gain, spatialBlend );
				gain = gain.Clamp( 0.0001f, 1.0f );

				var smoothed = _gains.Get( AudioChannel.Right ).LerpTo( gain, 0.1f );
				_gains.Set( AudioChannel.Right, smoothed );
				output.Get( AudioChannel.Right ).MixFrom( input, smoothed );
			}
		}

	}
}
