namespace ResourceTests;

/// <summary>
/// Pins the sound lipsync and subtitle surface: SoundFile.Visemes/.Subtitles read
/// the "LIPS"/"SUBT" blocks that the managed sound compiler injects at compile time
/// (that path needs an editor compile, so the positive case is covered by unit
/// tests on VisemeTrack/SubtitleTrack and in editor use), and SpeakSound wires a
/// voice up to a SkinnedModelRenderer.
/// </summary>
[TestClass]
public class SoundVisemeTests
{
	[TestMethod]
	public async System.Threading.Tasks.Task SoundWithoutVisemesHasNoTrack()
	{
		// Load a copy of a shipped sound from a scratch directory - game/core is
		// mounted at the VFS root so anything written there is loadable
		var engineDir = System.Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE" );
		Assert.IsNotNull( engineDir );

		var coreDir = System.IO.Path.GetFullPath( System.IO.Path.Combine( engineDir, "core" ) );
		var sourceSound = System.IO.Path.Combine( coreDir, "sounds", "kenney", "ui", "error_001.vsnd_c" );

		if ( !System.IO.File.Exists( sourceSound ) )
			Assert.Inconclusive( $"Shipped test sound not found: {sourceSound}" );

		var testName = $"rc_test_{System.Guid.NewGuid():N}";
		var testDir = System.IO.Path.Combine( coreDir, testName );
		System.IO.Directory.CreateDirectory( testDir );

		try
		{
			System.IO.File.Copy( sourceSound, System.IO.Path.Combine( testDir, "plain.vsnd_c" ) );

			var soundFile = SoundFile.Load( $"{testName}/plain.vsnd" );
			if ( soundFile is null )
				Assert.Inconclusive( "Native sound system can't precache sounds on this machine (no audio device?)" );

			// An unloaded sound has no tracks yet
			Assert.IsNull( soundFile.Visemes );
			Assert.IsNull( soundFile.Subtitles );

			// Async vsnd resource loads only complete while the engine is pumping
			// frames, which this harness doesn't do - so the loaded path can only
			// be pinned when the load manages to finish
			if ( !await soundFile.LoadAsync() )
				Assert.Inconclusive( "Sound resource load didn't complete in the test harness - can't verify the loaded path" );

			// Loaded and still no tracks - this sound genuinely has no visemes or subtitles
			Assert.IsTrue( soundFile.IsValidForPlayback, "A loaded sound should be valid for playback" );
			Assert.IsNull( soundFile.Visemes );
			Assert.IsNull( soundFile.Subtitles );
		}
		finally
		{
			System.IO.Directory.Delete( testDir, true );
		}
	}

	/// <summary>
	/// SpeakSound plays the sound on the GameObject and stores the handle as the
	/// renderer's Voice, ready for the animation system to lipsync with.
	/// </summary>
	[TestMethod]
	public void SpeakSoundSetsVoice()
	{
		var soundFile = SoundFile.Load( "sounds/kenney/ui/error_001.vsnd" );
		if ( soundFile is null )
			Assert.Inconclusive( "Native sound system can't precache sounds on this machine (no audio device?)" );

		var soundEvent = new SoundEvent
		{
			Sounds = new System.Collections.Generic.List<SoundFile> { soundFile }
		};

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var renderer = go.Components.Create<SkinnedModelRenderer>();

		Assert.IsNull( renderer.Voice );

		var handle = renderer.SpeakSound( soundEvent );

		Assert.IsNotNull( handle );
		Assert.AreSame( handle, renderer.Voice );
		Assert.AreSame( go, handle.Parent );

		handle.Stop();
	}
}
