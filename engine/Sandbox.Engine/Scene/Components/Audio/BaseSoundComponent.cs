using Sandbox.Audio;
using System.Text.Json.Serialization;

namespace Sandbox;

[Expose]
[Tint( EditorTint.Green )]
public abstract class BaseSoundComponent : Component
{
	/// <summary>
	/// The mixer we want this sound to play through
	/// </summary>
	[Property]
	public MixerHandle TargetMixer { get; set; }

	[Property, Group( "Sound" )] public SoundEvent SoundEvent { get; set; }
	[Property, Group( "Sound" )] public bool PlayOnStart { get; set; } = true;
	[Property, Group( "Sound" )] public bool StopOnNew { get; set; } = false;

	[Property, ToggleGroup( "SoundOverride" )] public bool SoundOverride { get; set; } = false;
	[Range( 0, 1 ), Property, Group( "SoundOverride" )] public float Volume { get; set; } = 1.0f;
	[Range( 0, 2 ), Property, Group( "SoundOverride" )] public float Pitch { get; set; } = 1.0f;
	[Property, Group( "SoundOverride" )] public bool Force2d { get; set; } = false;

	[Property, ToggleGroup( "Repeat" )] public bool Repeat { get; set; } = false;
	[Property, Group( "Repeat" )] public float MinRepeatTime { get; set; } = 1.0f;
	[Property, Group( "Repeat" )] public float MaxRepeatTime { get; set; } = 1.0f;

	[Property, ToggleGroup( "DistanceAttenuationOverride", Label = "Override Distance Attenuation" )] public bool DistanceAttenuationOverride { get; set; } = false;
	[Property, Group( "DistanceAttenuationOverride" )] public bool DistanceAttenuation { get; set; } = false;
	[Property, Group( "DistanceAttenuationOverride" ), AudioDistanceFloat] public float Distance { get; set; } = 512f;
	[Property, Group( "DistanceAttenuationOverride" )] public Curve Falloff { get; set; } = new Curve( new( 0, 1, MathF.PI, -MathF.PI ), new( 1, 0 ) );

	[Property, ToggleGroup( "OcclusionOverride", Label = "Override Occlusion" )] public bool OcclusionOverride { get; set; } = false;
	[Property, Group( "OcclusionOverride" )] public bool OcclusionEnabled { get; set; } = false;

	/// <summary>Legacy alias for <see cref="OcclusionEnabled"/>.</summary>
	[Hide, Obsolete( "Use OcclusionEnabled instead." )]
	public bool Occlusion
	{
		get => OcclusionEnabled;
		set => OcclusionEnabled = value;
	}

	/// <summary>Legacy occlusion radius. No longer used by the simulation.</summary>
	[JsonInclude, Hide, Obsolete( "OcclusionRadius is no longer used by the simulation." )]
	public float OcclusionRadius { get; set; } = 32.0f;

	[Property, ToggleGroup( "ReverbOverride", Label = "Override Reverb" )] public bool ReverbOverride { get; set; } = false;
	[Property, Group( "ReverbOverride" )] public bool ReverbEnabled { get; set; } = false;

	/// <summary>Legacy alias for <see cref="ReverbOverride"/>.</summary>
	[Hide, Obsolete( "Use ReverbOverride instead." )]
	public bool ReflectionOverride
	{
		get => ReverbOverride;
		set => ReverbOverride = value;
	}

	/// <summary>Legacy alias for <see cref="ReverbEnabled"/>.</summary>
	[Hide, Obsolete( "Use ReverbEnabled instead." )]
	public bool Reflections
	{
		get => ReverbEnabled;
		set => ReverbEnabled = value;
	}

	protected SoundHandle SoundHandle;

	internal SoundHandle SoundHandleInternal => SoundHandle;

	public virtual void StartSound() { }
	public virtual void StopSound() { }

	protected void ApplyOverrides( SoundHandle h )
	{
		if ( !h.IsValid() )
			return;

		h.TargetMixer = TargetMixer.Get( h.TargetMixer );

		if ( SoundOverride )
		{
			h.Volume = Volume;
			h.Pitch = Pitch;
			h.ListenLocal = Force2d;
		}

		if ( OcclusionOverride )
		{
			h.OcclusionEnabled = OcclusionEnabled;
		}

		if ( ReverbOverride )
		{
			h.ReverbEnabled = ReverbEnabled;
		}

		if ( DistanceAttenuationOverride )
		{
			h.DistanceAttenuation = DistanceAttenuation;
			h.Distance = Distance;
			h.Falloff = Falloff;
		}

		if ( Force2d )
		{
			h.Position = Vector3.Forward * 10.0f;
			h.OcclusionEnabled = false;
			h.AirAbsorption = false;
			h.DistanceAttenuation = false;
			h.Transmission = false;
		}
	}

	[Group( "Sound" ), Button( "Test Sound", "play_arrow" ), HideIf( nameof( SoundEvent ), null ), WideMode]
	protected void TestSound()
	{
		StopSound();
		StartSound();
	}

}

