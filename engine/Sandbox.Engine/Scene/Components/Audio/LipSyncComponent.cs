namespace Sandbox;

/// <summary>
/// Drive morphs with lipsync from sounds.
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

	private SoundHandle _soundHandle;

	static readonly string[] VisemeNames = new string[]
	{
		"viseme_sil",
		"viseme_PP",
		"viseme_FF",
		"viseme_TH",
		"viseme_DD",
		"viseme_KK",
		"viseme_CH",
		"viseme_SS",
		"viseme_NN",
		"viseme_RR",
		"viseme_AA",
		"viseme_E",
		"viseme_I",
		"viseme_O",
		"viseme_U",
	};

	protected override void OnUpdate()
	{
		base.OnUpdate();

		if ( Sound.IsValid() )
		{
			_soundHandle = Sound.SoundHandleInternal;
			if ( _soundHandle.IsValid() )
				_soundHandle.LipSync.Enabled = true;
		}
		else
		{
			_soundHandle = null;
		}

		if ( !_soundHandle.IsValid() )
		{
			ResetMorphs();
		}
		else
		{
			UpdateMorphs();
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		ResetMorphs();
	}

	private void ResetMorphs()
	{
		if ( !Renderer.IsValid() )
			return;

		var sceneModel = Renderer.SceneModel;
		if ( !sceneModel.IsValid() )
			return;

		sceneModel.Morphs.ResetAll();
	}

	private void UpdateMorphs()
	{
		if ( !_soundHandle.IsValid() )
			return;

		var visemes = _soundHandle.LipSync.Visemes;
		if ( visemes is null )
			return;

		if ( !Renderer.IsValid() )
			return;

		var model = Renderer.Model;
		if ( model is null )
			return;

		var morphCount = model.MorphCount;
		if ( morphCount == 0 )
			return;

		var sceneModel = Renderer.SceneModel;
		if ( !sceneModel.IsValid() )
			return;

		for ( var morphIndex = 0; morphIndex < morphCount; morphIndex++ )
		{
			var morph = 0.0f;
			for ( var visemeIndex = 0; visemeIndex < visemes.Count; visemeIndex++ )
			{
				var weight = model.GetVisemeMorph( VisemeNames[visemeIndex], morphIndex );
				morph = morph.LerpTo( weight, visemes[visemeIndex] );
			}

			var current = sceneModel.Morphs.Get( morphIndex );
			var target = (morph * MorphScale).Clamp( 0, 1 );
			current = MathX.ExponentialDecay( current, target, MorphSmoothTime * 0.17f, Time.Delta );
			current = Math.Max( 0, current );
			sceneModel.Morphs.Set( morphIndex, current );
		}
	}
}
