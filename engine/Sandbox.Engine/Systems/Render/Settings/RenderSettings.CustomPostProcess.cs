using Sandbox.Diagnostics;
using System.Reflection;

namespace Sandbox.Engine.Settings;

/// <summary>
/// A post-processing effect a game put on the ladder with <see cref="PostProcessSettingAttribute"/>.
/// </summary>
public sealed class CustomPostProcessEffect
{
	internal CustomPostProcessEffect( Command command, string title, string description )
	{
		Command = command;
		Title = title;
		Description = description;
	}

	internal Command Command { get; }

	/// <summary>The ConVar's name, which is also the setting's name in the preset tables.</summary>
	public string Name => Command.Name;

	public string Title { get; }

	public string Description { get; }

	/// <summary>
	/// Registered and dropped with their ConVars, so they come and go with the game and its
	/// hotloads. Kept here rather than on <see cref="RenderSettings"/> so the ConVar system can
	/// call in without building the settings instance for every ConVar it sees.
	/// </summary>
	internal static readonly List<CustomPostProcessEffect> All = new();

	static readonly Logger log = new( "RenderSettings" );

	/// <summary>Called by <see cref="ConVarSystem"/> for every managed ConVar it registers.</summary>
	internal static void OnConVarAdded( ManagedCommand command )
	{
		if ( command.member is not PropertyInfo property || !property.IsDefined( typeof( PostProcessSettingAttribute ) ) )
			return;

		if ( property.PropertyType != typeof( int ) )
		{
			log.Warning( $"[PostProcessSetting] {command.Name} isn't an int, so it can't take a quality level" );
			return;
		}

		// The server owns a replicated value, so a per-player quality setting can't drive it.
		if ( command.IsReplicated )
		{
			log.Warning( $"[PostProcessSetting] {command.Name} is replicated, so the settings menu can't drive it" );
			return;
		}

		var info = DisplayInfo.ForMember( property );
		var effect = new CustomPostProcessEffect( command, info.Name, info.Description ?? command.attribute.Help );
		All.Add( effect );

		// The ConVar just loaded its own saved value. The stored setting wins, the same way the
		// built-in effects get their quality profiles pushed at boot.
		effect.Push( RenderSettings.Instance.GetEffectQuality( command.Name ) );
	}

	/// <summary>Called by <see cref="ConVarSystem"/> as it drops a ConVar.</summary>
	internal static void OnConVarRemoved( Command command ) => All.RemoveAll( x => x.Command == command );

	/// <summary>Off is 0 and Low to High are 1 to 3, the scale the built-in quality ConVars use.</summary>
	internal void Push( EffectQuality level ) => Command.Value = ((int)level + 1).ToString();
}

public partial class RenderSettings
{
	/// <summary>Effects the loaded game added. Empty when no game is loaded.</summary>
	public IReadOnlyList<CustomPostProcessEffect> CustomPostProcessEffects => CustomPostProcessEffect.All;

	/// <summary>
	/// Levels picked by hand, by ConVar name. Holds every game's, not just the loaded one's, so a
	/// preset can put back the effects of games that aren't running.
	/// </summary>
	const string CustomEffectLevelsKey = "postprocess.custom";

	/// <summary>
	/// The level of the last preset the effects added up to. An effect nobody picked by hand sits
	/// here, which is how a preset chosen with no game loaded reaches that game's effects.
	/// </summary>
	const string CustomEffectBaseKey = "postprocess.custom.base";

	Dictionary<string, EffectQuality> CustomEffectLevels =>
		VideoSettings.Get<Dictionary<string, EffectQuality>>( CustomEffectLevelsKey, null ) ?? new();

	EffectQuality CustomEffectBase
	{
		get
		{
			if ( VideoSettings.TryGet<EffectQuality>( CustomEffectBaseKey, out var level ) && Enum.IsDefined( level ) )
				return level;

			// Nothing recorded yet, so follow whatever the built-in effects add up to.
			var preset = BuiltInPostProcessQuality;
			return preset == PostProcessQuality.Custom ? EffectQuality.High : EffectLevelFor( preset );
		}
	}

	EffectQuality CustomEffectLevel( Dictionary<string, EffectQuality> levels, string name ) =>
		levels.TryGetValue( name, out var level ) && Enum.IsDefined( level ) ? level : CustomEffectBase;

	/// <summary>The level of a <see cref="CustomPostProcessEffect"/>, by name.</summary>
	public EffectQuality GetEffectQuality( string name ) => CustomEffectLevel( CustomEffectLevels, name );

	/// <summary>
	/// Pick a level for one <see cref="CustomPostProcessEffect"/>, by name. Takes effect now if its
	/// game is loaded, otherwise when it is.
	/// </summary>
	public void SetEffectQuality( string name, EffectQuality value )
	{
		var levels = CustomEffectLevels;
		levels[name] = value;
		VideoSettings.Set( CustomEffectLevelsKey, levels );

		foreach ( var effect in CustomPostProcessEffect.All )
		{
			if ( string.Equals( effect.Name, name, StringComparison.OrdinalIgnoreCase ) )
				effect.Push( value );
		}
	}

	/// <summary>Every custom effect follows this level now, including those of games that aren't loaded.</summary>
	void SetCustomEffectsBase( EffectQuality level )
	{
		VideoSettings.Set( CustomEffectBaseKey, level );
		VideoSettings.Set( CustomEffectLevelsKey, new Dictionary<string, EffectQuality>() );

		foreach ( var effect in CustomPostProcessEffect.All )
			effect.Push( level );
	}

	/// <summary>
	/// The settings menu writes effects one at a time, never the preset. When they add up to one,
	/// record its level so the effects of games that aren't loaded follow it too.
	/// </summary>
	void SettleCustomEffects()
	{
		var preset = PostProcessQuality;
		if ( preset != PostProcessQuality.Custom )
			SetCustomEffectsBase( EffectLevelFor( preset ) );
	}
}
