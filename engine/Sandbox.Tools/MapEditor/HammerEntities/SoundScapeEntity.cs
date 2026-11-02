namespace Editor.MapEditor.EntityDefinitions;

/// <summary>
/// Plays a soundscape when you enter the bounds.
/// </summary>
[Library( "snd_soundscape_box" ), HammerEntity]
[EditorSprite( "editor/env_soundscape.vmat" ), VisGroup( VisGroup.Sound )]
[Title( "Soundscape Box" ), Category( "Sound" ), Icon( "speaker_group" )]
[BoundsHelper( "extents" )]
class SoundScapeBoxEntity : HammerEntityDefinition
{
	/// <summary>
	/// Name of the soundscape to play.
	/// </summary>
	[Property( "soundscape" ), FGDType( "resource:sndscape" )]
	public Soundscape Soundscape { get; set; }

	/// <summary>
	/// Is Enabled
	/// </summary>
	[Property( "enabled" )]
	public bool Enabled { get; set; }

	[Property( "extents", Title = "Extents" )]
	[DefaultValue( "64 64 64" )]
	public Vector3 Extents { get; set; } = new Vector3( 64, 64, 64 );
}

/// <summary>
/// Plays a soundscape when you enter the radius.
/// </summary>
[Library( "snd_soundscape" ), HammerEntity]
[EditorSprite( "editor/env_soundscape.vmat" ), VisGroup( VisGroup.Sound )]
[Title( "Soundscape" ), Category( "Sound" ), Icon( "speaker" )]
[Sphere( "radius" )]
class SoundScapeEntity : HammerEntityDefinition
{
	/// <summary>
	/// Name of the soundscape to play.
	/// </summary>
	[Property( "soundscape" ), FGDType( "resource:sndscape" )]
	public Soundscape Soundscape { get; set; }

	/// <summary>
	/// Is Enabled
	/// </summary>
	[Property( "enabled" )]
	public bool Enabled { get; set; }

	[Property( "radius", Title = "Radius" )]
	[DefaultValue( "64" )]
	public float Radius { get; set; } = 64;
}
