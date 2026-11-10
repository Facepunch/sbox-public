namespace Sandbox;

/// <summary>
/// Drive a renderer's viseme morphs from a playing sound component. This is the
/// same lipsync <see cref="SkinnedModelRenderer.SpeakSound(SoundEvent, GameObject)"/>
/// uses, pointed at a sound component instead.
/// </summary>
[Expose]
[Category( "Audio" )]
[Title( "Lip Syncing" )]
[Icon( "emoji_emotions" )]
[Tint( EditorTint.Green )]
public sealed class LipSync : Component
{
	[Property]
	public BaseSoundComponent Sound { get; set; }

	[Property]
	public SkinnedModelRenderer Renderer { get; set; }

	[Property, Title( "Scale" ), Group( "Morph" ), Range( 0, 5 )]
	public float MorphScale { get; set; } = 1.5f;

	[Property, Title( "Smoothing" ), Group( "Morph" ), Range( 0, 1 )]
	public float MorphSmoothTime { get; set; } = 0.1f;

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( !Renderer.IsValid() )
			return;

		Renderer.VoiceRealtimeScale = MorphScale;
		Renderer.VoiceMorphSmoothTime = MorphSmoothTime;
		Renderer.Voice = Sound.IsValid() ? Sound.SoundHandleInternal : null;
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		if ( Renderer.IsValid() )
		{
			Renderer.Voice = null;
		}
	}
}
