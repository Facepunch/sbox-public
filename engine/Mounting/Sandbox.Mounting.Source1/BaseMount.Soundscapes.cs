using System;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	private void MountSoundscapes( MountContext context, FileIndex files )
	{
		const string manifest = "scripts/soundscapes_manifest.txt";
		files.SoundscapeRoots.Clear();
		var paths = ReadManifest( "", context );
		var catalog = new SourceSoundscapes();
		foreach ( var path in paths )
			Add( catalog, path, "" );
		Register( catalog, "", context );
		foreach ( var map in files.Resources.Where( resource => resource.Factory.Type == ResourceType.Scene ) )
		{
			var root = GetMapPakRoot( map.Path );
			var mapScript = $"scripts/soundscapes_{System.IO.Path.GetFileNameWithoutExtension( map.Path )}.txt";
			var hasPackedAudio = files.PackedAudioRoots.Contains( root );
			var hasMapScript = FindContentFile( files, mapScript, root ) is not null;
			if ( !hasPackedAudio && !hasMapScript )
				continue;
			files.SoundscapeRoots.Add( root );
			var scoped = new SourceSoundscapes();
			var scopedPaths = FindContentFile( files, manifest, root ) is { } scopedManifest && scopedManifest != manifest ? ReadManifest( root, context ) : paths;
			foreach ( var path in scopedPaths )
				Add( scoped, path, root );
			if ( hasMapScript && !scopedPaths.Contains( mapScript, StringComparer.OrdinalIgnoreCase ) )
				Add( scoped, mapScript, root );
			Register( scoped, root, context );
		}

		IReadOnlyList<string> ReadManifest( string root, MountContext registration )
		{
			try
			{
				if ( !TryOpenContentFile( manifest, root, out var stream ) )
					return Array.Empty<string>();
				using ( stream )
				using ( var reader = new StreamReader( stream ) )
				{
					var text = reader.ReadToEnd();
					var roots = ScriptManifest.Parse( text, manifest, message => throw new NotSupportedException( message ) );
					if ( roots.Count != 1 || !roots[0].IsBlock || !(roots[0].Name.Equals( "soundscaples_manifest", StringComparison.OrdinalIgnoreCase ) || roots[0].Name.Equals( "soundscapes_manifest", StringComparison.OrdinalIgnoreCase )) )
						throw new InvalidDataException( $"{manifest}: expected a soundscape manifest block." );
					var entries = new List<string>();
					foreach ( var entry in roots[0].Children )
					{
						if ( entry.IsBlock || !entry.Name.Equals( "file", StringComparison.OrdinalIgnoreCase ) )
							throw new InvalidDataException( $"{manifest}: unsupported manifest entry '{entry.Name}'." );
						entries.Add( ScriptManifest.ContentPath( entry.Value ) );
					}

					return entries;
				}
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Warning( $"{Title}: soundscape manifest '{root}{manifest}' could not be imported: {e.Message}" );
				registration.AddError( $"Soundscape manifest '{root}{manifest}' could not be imported: {e.Message}" );
				return Array.Empty<string>();
			}
		}

		void Add( SourceSoundscapes library, string path, string root )
		{
			try
			{
				using var source = OpenContentFile( path, root );
				using var reader = new StreamReader( source );
				library.Add( reader.ReadToEnd(), path );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				Log.Warning( $"{Title}: soundscape script '{root}{path}' could not be imported: {e.Message}" );
			}
		}

		void Register( SourceSoundscapes library, string root, MountContext registration )
		{
			foreach ( var name in library.Names )
			{
				try
				{
					var definition = library.Read( name );
					var path = SoundscapePath( name, root );
					for ( var i = 0; i < definition.Entries.Count; i++ )
						if ( definition.Entries[i].Random )
							registration.Add( ResourceType.GameResource, $"{path}.{i}.sound", new SourceSoundscapeEventLoader( definition.Entries[i], root ) );
					registration.Add( ResourceType.GameResource, path, new SourceSoundscapeLoader( definition, root ) );
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException or OverflowException )
				{
					Log.Warning( $"{Title}: soundscape '{name}' was not registered: {e.Message}" );
				}
			}
		}
	}

	internal string SoundscapePath( string name, string root = "" ) => $"{root}soundscapes/{SurfaceResourceName( name )}.sndscape";
	internal Soundscape GetSoundscape( string name, string root )
	{
		var path = SoundscapePath( name, index.SoundscapeRoots.Contains( root ) ? root : "" );
		return GameResource.Load<Soundscape>( $"mount://{Ident}/{path}" ) ?? throw new InvalidDataException( $"{Title}: soundscape '{name}' could not be loaded." );
	}
}
