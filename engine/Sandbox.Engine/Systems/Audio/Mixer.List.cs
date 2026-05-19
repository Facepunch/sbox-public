using Sandbox.Internal;
using System.Text.Json.Nodes;
using System.Threading;

namespace Sandbox.Audio;

public partial class Mixer
{
	public static Mixer Master { get; private set; }
	public static Mixer Default { get; set; }
	public static Mixer Voice { get; internal set; }

	internal Lock Lock { get; } = new Lock();

	public static void ResetToDefault()
	{
		// It's important that we destroy all processors or they will hang around forever and quickly run out of DSP slots
		Master?.Clear();

		var newMaster = new Mixer( null );
		newMaster.Name = "Master";

		// Sol: I think these defaults all get overriden by the Clear() on Deserialize? leaving incase there's a path I'm not considering

		var music = newMaster.AddChild();
		music.Name = "Music";
		music.Spacializing = 0;
		music.DistanceAttenuation = 0;
		music.Occlusion = 0;
		music.AirAbsorption = 0;

		var game = newMaster.AddChild();
		game.Name = "Game";

		var ui = newMaster.AddChild();
		ui.Name = "UI";
		ui.Spacializing = 0;
		ui.DistanceAttenuation = 0;
		ui.Occlusion = 0;
		ui.AirAbsorption = 0;

		var voice = newMaster.AddChild();
		voice.Name = "Voice";

		Master = newMaster;
		Default = game;
		Voice = voice;
	}

	internal static void LoadFromSettings( MixerSettings settings, TypeLibrary typelibrary )
	{
		ResetToDefault();

		if ( settings is null || settings.Mixers is null )
			return;

		Default = null;

		var newMaster = new Mixer( null );
		newMaster.Name = "Master";
		newMaster.SetMasterOcclusionDefaults();
		newMaster.Deserialize( settings.Mixers, typelibrary );

		Master = newMaster;
		Default ??= Master;

		// Create Voice mixer if absent
		Voice = FindMixerByName( newMaster, "Voice" );
		if ( Voice is null )
		{
			Voice = newMaster.AddChild();
			Voice.Name = "Voice";
		}
	}

	public static Mixer FindMixerByName( string name )
	{
		return FindMixerByName( Master, name );
	}

	/// <summary>
	/// We might want to do a fast lookup at some point
	/// </summary>
	internal static Mixer FindMixerByName( Mixer target, string name )
	{
		if ( target is null ) return default;
		if ( string.Equals( target.Name, name, StringComparison.OrdinalIgnoreCase ) ) return target;
		if ( target.Children is null ) return default;

		foreach ( var child in target.Children )
		{
			if ( FindMixerByName( child, name ) is Mixer found )
				return found;
		}

		return default;
	}

	public static Mixer FindMixerByGuid( Guid guid )
	{
		return FindMixerByGuid( Master, guid );
	}

	/// <summary>
	/// We might want to do a fast lookup at some point
	/// </summary>
	internal static Mixer FindMixerByGuid( Mixer target, Guid guid )
	{
		if ( target is null ) return default;
		if ( target.Id == guid ) return target;
		if ( target.Children is null ) return default;

		foreach ( var child in target.Children )
		{
			if ( FindMixerByGuid( child, guid ) is Mixer found )
				return found;
		}

		return default;
	}
}


public class MixerSettings : ConfigData
{
	public override int Version => 2;
	public JsonObject Mixers { get; set; }
}
