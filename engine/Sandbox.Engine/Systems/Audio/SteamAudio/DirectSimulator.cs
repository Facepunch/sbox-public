using System.Threading;

namespace Sandbox.Audio;

class DirectSource : IDisposable
{
	LowPassProcessor _lowPass;
	Lock _lock = new Lock();

	public Transform Transform { get; private set; }


	internal DirectSource()
	{
	}

	~DirectSource()
	{
		Dispose();
	}

	public void Dispose()
	{
		_lowPass = null;

		GC.SuppressFinalize( this );
	}

	float? _occlusion;
	float _targetOcclusion = 1.0f;

	/// <summary>
	/// Time tracking for occlusion updates, managed by SoundOcclusionSystem
	/// </summary>
	internal RealTimeUntil TimeUntilNextOcclusionCalc { get; set; } = 0;

	public void Update( Transform position, Vector3 listenerPos, Mixer targetMixer, PhysicsWorld world )
	{
		lock ( _lock )
		{
			Transform = position;

			// Smooth damp towards target occlusion (set by SoundOcclusionSystem)
			if ( Occlusion && !ListenLocal )
			{
				if ( _occlusion.HasValue )
				{
					_occlusion = MathX.ExponentialDecay( _occlusion.Value, _targetOcclusion, 0.05f, RealTime.Delta );
				}
				else
				{
					Snap();
				}
			}
			else
			{
				_targetOcclusion = 1.0f;
				Snap();
			}
		}
	}

	/// <summary>
	/// Stop any lerping and jump straight to the target occlusion
	/// </summary>
	public void Snap()
	{
		_occlusion = _targetOcclusion;
	}

	/// <summary>
	/// Set the target occlusion value. Called by SoundOcclusionSystem after computing occlusion.
	/// </summary>
	internal void SetTargetOcclusion( float value )
	{
		lock ( _lock )
		{
			_targetOcclusion = value;
		}
	}

	[ConVar] public static float snd_lowpass_power { get; set; } = 4;
	[ConVar] public static float snd_lowpass_trans { get; set; } = 0.40f;
	[ConVar] public static float snd_lowpass_dist { get; set; } = 0.85f;
	[ConVar] public static float snd_gain_trans { get; set; } = 0.8f;

	public void Apply( in Listener listener, MultiChannelBuffer input, MultiChannelBuffer output, float occlusionMultiplier, float inputGain )
	{
		lock ( _lock )
		{
			Vector3 listenerPos = listener.MixTransform.Position;
			float distanceInUnits = Transform.Position.Distance( listenerPos );

			//
			// Calculate using the Distance float and Attenuation Curve
			//
			float curveVal = MathX.Clamp( distanceInUnits / Distance, 0f, 1f );
			float distanceAtten = Falloff.Evaluate( curveVal );

			//
			// TODO
			//
			float directivity = 1.0f;

			//
			// This is calculated in Update
			//
			float occlusion = _occlusion ?? 1.0f + (1 - occlusionMultiplier).Clamp( 0, 1 );

			//
			// Not real transmission, like Steam Audio does it, where it gets the material and tries to measure how 
			// many walls are between and the surface. But we're making video games - not science!
			//
			float transmission = 0;

			if ( !DistanceAttenuation ) distanceAtten = 1;
			if ( !Occlusion ) occlusion = 1;

			float lowPass = 0;

			// Add low pass effect on sounds coming through walls
			if ( Transmission )
			{
				transmission = (1 - occlusion).Clamp( 0, 1 );
				lowPass += transmission * snd_lowpass_trans;
			}

			// Add low pass effect on sounds coming from a distance
			if ( AirAbsorption )
			{
				lowPass += curveVal.Remap( 0, 1, 0, 1 ) * snd_lowpass_dist;
			}

			lowPass = lowPass.Remap( 0, 1, -0.5f, 1f );

			if ( lowPass > 0 )
			{
				_lowPass ??= new LowPassProcessor();
				_lowPass.Cutoff = MathF.Pow( (1 - lowPass).Clamp( 0.005f, 1.0f ), snd_lowpass_power );
				_lowPass.SetListener( listener );
				_lowPass.ProcessInPlace( input );
			}

			// reduce the volume of sounds coming through surfaces
			transmission = snd_gain_trans;

			var gain = distanceAtten * directivity * (occlusion + transmission);

			output.CopyFromUpmix( input );

			output.Scale( gain.Clamp( 0, 1 ) * inputGain );
		}
	}

	public bool ListenLocal { get; set; } = false;
	public bool AirAbsorption { get; set; } = true;
	public bool Transmission { get; set; } = true;
	public bool Occlusion { get; set; } = true;
	public bool DistanceAttenuation { get; set; } = true;
	public float OcclusionSize { get; set; } = 16.0f;

	public float Distance { get; set; } = 15_000f;
	public Curve Falloff { get; set; } = new Curve( new( 0, 1, 0, -1.8f ), new( 0.05f, 0.22f, 3.5f, -3.5f ), new( 0.2f, 0.04f, 0.16f, -0.16f ), new( 1, 0 ) );
}
