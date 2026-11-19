using Sandbox.Audio;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A sound event. It can play a set of random sounds with optionally random settings such as volume and pitch.
/// </summary>
[Expose]
[AssetType( Name = "Sound Event", Extension = "sound", Category = "Sounds" )]
public partial class SoundEvent : GameResource
{
	/// <summary>
	/// Is this sound 2D?
	/// </summary>
	[Display( Name = "UI", Description = "Is this sound 2D?" )]
	public bool UI { get; set; }

	/// <summary>
	/// How loud the sound should be.
	/// </summary>
	[Range( 0, 1 )]
	public RangedFloat Volume { get; set; } = 1.0f;

	/// <summary>
	/// The base pitch of the sound.
	/// </summary>
	[Range( 0, 2 )]
	public RangedFloat Pitch { get; set; } = 1.0f;

	/// <summary>
	/// How loud is this sound, affects how far away it can be heard
	/// </summary>
	[Hide]
	[Obsolete( "This is not used anymore" )]
	public int Decibels { get; set; } = 70;

	/// <summary>
	/// Selection strategy to use when picking from multiple sounds.
	/// </summary>
	public SoundSelectionMode SelectionMode { get; set; } = SoundSelectionMode.Random;

	/// <summary>
	/// A random sound from the list will be selected to be played.
	/// </summary> 
	public List<SoundFile> Sounds { get; set; }

	/// <summary>
	/// Allow this sound to be occluded by geometry
	/// </summary>
	[HideIf( nameof( UI ), true )]
	public bool OcclusionEnabled { get; set; } = true;

	/// <summary>Legacy alias for <see cref="OcclusionEnabled"/>.</summary>
	[Hide, Obsolete( "Use OcclusionEnabled instead." )]
	public bool Occlusion
	{
		get => OcclusionEnabled;
		set => OcclusionEnabled = value;
	}

	/// <summary>
	/// Allow this sound to trace reflections, allowing it to be heard indirectly
	/// </summary>
	[HideIf( nameof( UI ), true )]
	public bool ReverbEnabled { get; set; } = true;

	/// <summary>Legacy alias for <see cref="ReverbEnabled"/>.</summary>
	[Hide, Obsolete( "Use ReverbEnabled instead." )]
	public bool Reflections
	{
		get => ReverbEnabled;
		set => ReverbEnabled = value;
	}

	/// <summary>
	/// Allow this sound to be absorbed by air
	/// </summary>
	[HideIf( nameof( UI ), true )]
	public bool AirAbsorption { get; set; } = true;

	/// <summary>
	/// Legacy transmission toggle. Transmission is now derived from occlusion and material response.
	/// </summary>
	[JsonInclude, Hide, Obsolete( "Transmission is derived from occlusion and material response." )]
	public bool Transmission { get; set; } = true;

	/// <summary>
	/// Legacy occlusion radius. No longer used by the simulation.
	/// </summary>
	[JsonInclude, Hide, Obsolete( "OcclusionRadius is no longer used by the simulation." )]
	public float OcclusionRadius { get; set; } = 64.0f;

	/// <summary>
	/// Should the sound fade out over distance
	/// </summary>
	[ToggleGroup( "DistanceAttenuation", Label = "Distance Attenuation" ), HideIf( nameof( UI ), true )]
	public bool DistanceAttenuation { get; set; } = true;

	/// <summary>
	/// How many units the sound can be heard from. Past this it isn't considered at all.
	/// </summary>
	[ToggleGroup( "DistanceAttenuation" ), DefaultValue( 15_000f ), Description( "How many units the sound can be heard from. Past this it isn't considered at all." ), HideIf( nameof( UI ), true ), AudioDistanceFloat]
	public float Distance { get; set; } = 15_000f;

	/// <summary>
	/// How the volume falls off with distance.
	/// </summary>
	[ToggleGroup( "DistanceAttenuation" ), Description( "Physical: full volume within the min distance, halving every time the distance doubles. Curve: the falloff curve." ), HideIf( nameof( UI ), true )]
	public SoundFalloffMode FalloffMode { get; set; } = SoundFalloffMode.Physical;

	/// <summary>
	/// Radius around the source at full volume, roughly the size of the source. Bigger carries further. 0 picks one from the distance.
	/// </summary>
	[ToggleGroup( "DistanceAttenuation" ), DefaultValue( 0f ), Description( "Radius around the source at full volume, roughly the size of the source. Bigger carries further. 0 picks one from the distance." ), ShowIf( nameof( FalloffMode ), SoundFalloffMode.Physical ), HideIf( nameof( UI ), true ), AudioDistanceFloat]
	public float MinDistance { get; set; }

	/// <summary>
	/// The falloff curve for the sound, from the source (0) to the distance (1).
	/// </summary>
	[ToggleGroup( "DistanceAttenuation" ), Description( "The falloff curve for the sound, from the source (0) to the distance (1)." ), ShowIf( nameof( FalloffMode ), SoundFalloffMode.Curve ), HideIf( nameof( UI ), true )]
	public Curve Falloff { get; set; } = Audio.SoundFalloff.ClassicCurve;

	/// <summary>
	/// Default mixer to play this sound with if one isn't provided on play.
	/// </summary>
	[Description( "Default mixer to play this sound with if one isn't provided on play." )]
	public MixerHandle DefaultMixer { get; set; }

	/// <summary>
	/// Used for selection mode
	/// </summary>
	[JsonIgnore]
	internal int InputIndex { get; set; }

	public enum SoundSelectionMode
	{
		//Index = 0, // enable when we expose input_index
		Forward = 1,
		Backward = 2,
		Random = 3,
		RandomExclusive = 4,
		//RandomWeighted = 5 // enable when we expose input_index
	};

	public SoundEvent()
	{

	}

	public SoundEvent( string soundName, float volume = 0.5f )
	{
		Volume = volume;
		Sounds = new() { SoundFile.Load( soundName ) };
	}

	internal static SoundEvent Find( string eventName )
	{
		var e = ResourceLibrary.Get<SoundEvent>( eventName );

		// Slow - probably. We can do a direct lookup if this ends up sucking
		return e ?? ResourceLibrary.GetAll<SoundEvent>().FirstOrDefault( x => string.Equals( x.ResourceName, eventName, StringComparison.OrdinalIgnoreCase ) );
	}

	internal SoundFile GetNextSound()
	{
		if ( Sounds is null )
			return null;

		var count = Sounds.Count;
		if ( count <= 1 )
			return Sounds.FirstOrDefault();

		var index = InputIndex;
		var maxIndex = count - 1;
		var selectionMode = SelectionMode;

		if ( selectionMode == SoundSelectionMode.Forward )
		{
			index++;
			index = index > maxIndex ? 0 : index;
		}
		else if ( selectionMode == SoundSelectionMode.Backward )
		{
			index--;
			index = index < 0 ? maxIndex : index;
		}
		else if ( selectionMode == SoundSelectionMode.Random )
		{
			index = Random.Shared.Int( 0, maxIndex );
			InputIndex = index;
		}
		else if ( selectionMode == SoundSelectionMode.RandomExclusive )
		{
			index = Random.Shared.Int( 0, maxIndex );
			if ( index == InputIndex )
			{
				index++;
				index = index > maxIndex ? 0 : index;
			}

			InputIndex = index;
		}

		var sound = Sounds[InputIndex];
		InputIndex = index;

		return sound;
	}

	protected override Bitmap CreateAssetTypeIcon( int width, int height )
	{
		return CreateSimpleAssetTypeIcon( "graphic_eq", width, height );
	}
}
