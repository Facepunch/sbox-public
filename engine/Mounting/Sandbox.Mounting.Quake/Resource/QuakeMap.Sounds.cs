partial class QuakeMap
{
	private static readonly Dictionary<string, (string Path, float Volume)> AmbientSounds = new()
	{
		["ambient_drone"] = ("sound/ambience/drone6.wav", 0.5f),
		["ambient_drip"] = ("sound/ambience/drip1.wav", 0.5f),
		["ambient_comp_hum"] = ("sound/ambience/comp1.wav", 1.0f),
		["ambient_flouro_buzz"] = ("sound/ambience/buzz1.wav", 1.0f),
		["ambient_light_buzz"] = ("sound/ambience/fl_hum1.wav", 0.5f),
		["ambient_suck_wind"] = ("sound/ambience/suck1.wav", 1.0f),
		["ambient_swamp1"] = ("sound/ambience/swamp1.wav", 0.5f),
		["ambient_swamp2"] = ("sound/ambience/swamp2.wav", 0.5f),
		["ambient_thunder"] = ("sound/ambience/thunder1.wav", 0.5f),
	};

	private void SpawnAmbientSound( Quake.BSP.File.ObjectEntry entity, string wavPath, float volume )
	{
		var soundEvent = BuildSoundEvent( wavPath, volume );
		if ( soundEvent is null )
			return;

		var go = new GameObject( true, entity.TypeName );
		go.WorldPosition = entity.Position;

		var component = go.AddComponent<SoundPointComponent>();
		component.SoundEvent = soundEvent;
		component.PlayOnStart = true;
		component.Repeat = false;
	}

	private SoundEvent BuildSoundEvent( string wavPath, float volume )
	{
		var sound = SoundFile.Load( $"mount://{Host.Ident}/{PakDir}/{wavPath}.vsnd".Replace( '\\', '/' ) );
		if ( sound is null )
			return null;

		var soundEvent = new SoundEvent
		{
			Sounds = [sound],
			Volume = volume,
			DistanceAttenuation = true,
			Distance = 1500f,
			OcclusionEnabled = false,
			ReverbEnabled = false,
		};

		soundEvent.EmbeddedResource = new Sandbox.Resources.EmbeddedResource { ResourceCompiler = "embed" };
		return soundEvent;
	}
}
