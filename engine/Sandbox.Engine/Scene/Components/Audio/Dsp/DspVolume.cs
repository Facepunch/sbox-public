using Sandbox.Audio;

namespace Sandbox;

[Title( "Dsp Volume" )]
[Group( "Audio" )]
[Icon( "graphic_eq" )]
[Tint( EditorTint.Green )]
public class DspVolume : Sandbox.Volumes.VolumeComponent
{
	[Property]
	public DspPresetHandle Dsp { get; set; }

	[Property]
	public MixerHandle TargetMixer { get; set; } = new MixerHandle { Name = "Game" };

	[Property]
	public int Priority { get; set; }

	/// <summary>
	/// Optionally apply an additional effect only to this listener, testing the volume at its position.
	/// Null retains the existing global volume selection and mixer-wide routing.
	/// </summary>
	internal Listener TargetListener { get; set; }
}

