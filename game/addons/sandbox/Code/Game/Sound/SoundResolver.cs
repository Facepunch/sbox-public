/// <summary>
/// Turns a sound setting picked with <c>[ResourceSelect( Extension = "sound", AllowPackages = true, Popup = true )]</c>
/// into something playable. The setting can be:
/// <list type="bullet">
/// <item>a local .sound path, e.g. <c>sounds/vehicles/buggy/horn.sound</c></item>
/// <item>a workshop package ident, e.g. <c>someone.cool_siren</c>, which is downloaded the first time it's asked for</item>
/// <item>a .sndef path, for anything saved back when these were <see cref="SoundDefinition"/>s</item>
/// </list>
/// Workshop sounds load in the background: <see cref="Get"/> returns null until they're ready, so call it again later
/// (or every frame - it's a dictionary lookup once loaded).
/// </summary>
public static class SoundResolver
{
	private static readonly Dictionary<string, SoundEvent> _loaded = new( StringComparer.OrdinalIgnoreCase );
	private static readonly HashSet<string> _requested = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The sound for this setting, or null if it's empty, unknown, or still downloading.
	/// </summary>
	public static SoundEvent Get( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;
		if ( _loaded.TryGetValue( path, out var sound ) && sound is not null ) return sound;

		sound = path.EndsWith( ".sndef", StringComparison.OrdinalIgnoreCase )
			? ResourceLibrary.Get<SoundDefinition>( path )?.Sound
			: ResourceLibrary.Get<SoundEvent>( path );

		if ( sound is not null )
		{
			_loaded[path] = sound;
			return sound;
		}

		// not in the game's files, so it's a workshop pick - fetch it once
		if ( IsPackageIdent( path ) && _requested.Add( path ) )
			_ = Download( path );

		return null;
	}

	/// <summary>
	/// Start fetching a workshop sound ahead of time, so it's ready when it's first played.
	/// </summary>
	public static void Preload( string path ) => Get( path );

	/// <summary>
	/// A short name for the setting to show players: a workshop sound's title, otherwise the file name
	/// (<c>sounds/vehicles/buggy/horn.sound</c> is "horn"). Null when empty.
	/// </summary>
	public static string GetDisplayName( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return null;

		if ( IsPackageIdent( path ) )
			return Package.TryGetCached( path, out var package, false ) && !string.IsNullOrWhiteSpace( package.Title ) ? package.Title : path;

		return System.IO.Path.GetFileNameWithoutExtension( path );
	}

	public static bool IsPackageIdent( string path ) => !string.IsNullOrWhiteSpace( path ) && !path.Contains( '/' ) && Package.TryParseIdentParts( path, out _ );

	private static async Task Download( string ident )
	{
		try
		{
			var sound = await Cloud.Load<SoundEvent>( ident );
			if ( sound is not null )
				_loaded[ident] = sound;
			else
				Log.Warning( $"Couldn't load workshop sound '{ident}'." );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't load workshop sound '{ident}': {e.Message}" );
		}
	}
}
