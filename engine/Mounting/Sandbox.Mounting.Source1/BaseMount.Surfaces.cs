using System;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	internal static readonly string[] SurfaceSoundKeys =
		["stepleft", "stepright", "bulletimpact", "impacthard", "impactsoft", "scraperough", "scrapesmooth", "break"];

	private readonly HashSet<string> missingSurfaces = new( StringComparer.OrdinalIgnoreCase );

	private string SurfaceResourceName( string name ) => $"{Ident}_{Uri.EscapeDataString( name.ToLowerInvariant() )}";

	private void MountSurfaces( MountContext context, FileIndex files )
	{
		missingSurfaces.Clear();
		files.Surfaces.Clear();
		files.SurfaceSounds.Clear();
		const string manifest = "scripts/surfaceproperties_manifest.txt";
		if ( !files.Files.ContainsKey( manifest ) )
			return;

		foreach ( var path in ScriptManifest.Read( Read( manifest ), manifest, "surfaceproperties_manifest", "file" ) )
		{
			if ( !files.Files.ContainsKey( path ) )
			{
				Log.Trace( $"{Title}: surface manifest references missing script '{path}'; those definitions were not imported." );
				continue;
			}
			SourceSurfaceDefinition.Parse( Read( path ), path, files.Surfaces, message => Log.Trace( message ) );
		}

		var wanted = files.Surfaces.Values.SelectMany( surface => SurfaceSoundKeys.Select( key => surface.Parameters.String( key ) ) )
			.Where( name => !string.IsNullOrWhiteSpace( name ) ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		var scripts = new Dictionary<string, (VmtNode Root, string Path)>( StringComparer.OrdinalIgnoreCase );
		const string soundsManifest = "scripts/game_sounds_manifest.txt";
		try
		{
			if ( wanted.Count > 0 && files.Files.ContainsKey( soundsManifest ) )
			{
				foreach ( var path in ScriptManifest.Read( Read( soundsManifest ), soundsManifest, "game_sounds_manifest", "precache_file", "declare_file", "preload_file" ) )
				{
					if ( !files.Files.ContainsKey( path ) )
					{
						Log.Trace( $"{Title}: sound manifest references missing script '{path}'; its surface sounds were not imported." );
						continue;
					}
					try
					{
						foreach ( var root in ScriptManifest.Parse( Read( path ), path, message => Log.Trace( message ) ) )
							if ( root.IsBlock && wanted.Contains( root.Name ) )
								scripts.TryAdd( root.Name, (root, path) );
					}
					catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
					{
						Log.Trace( $"{Title}: surface sounds from '{path}' were not imported: {e.Message}" );
					}
				}
			}
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{Title}: surface sound manifest could not be imported: {e.Message} Physical surface properties remain available." );
		}
		foreach ( var name in wanted )
		{
			try
			{
				SourceSoundDefinition definition;
				if ( scripts.TryGetValue( name, out var script ) )
					definition = SourceSoundDefinition.Parse( script.Root, script.Path );
				else if ( name.EndsWith( ".wav", StringComparison.OrdinalIgnoreCase ) || name.EndsWith( ".mp3", StringComparison.OrdinalIgnoreCase ) )
					definition = SourceSoundDefinition.Parse(
						new VmtNode( name, null, [new VmtNode( "wave", name, null )] ), manifest );
				else
				{
					Log.Trace( $"{Title}: surface sound '{name}' has no sound-script definition; that sound slot will be empty." );
					continue;
				}
				if ( ResolveSurfaceSound( definition ) is { } sound )
					files.SurfaceSounds[name] = sound;
			}
			catch ( Exception e ) when ( e is InvalidDataException or NotSupportedException )
			{
				Log.Trace( $"{Title}: surface sound '{name}' was not imported: {e.Message}" );
			}
		}

		foreach ( var sound in files.SurfaceSounds.Values )
			context.Add( ResourceType.GameResource, $"sounds/surfaces/{SurfaceResourceName( sound.Name )}.sound", new SurfaceSoundLoader( sound ) );
		foreach ( var surface in files.Surfaces.Values )
			context.Add( ResourceType.GameResource, $"surfaces/{SurfaceResourceName( surface.Name )}.surface", new SourceSurfaceLoader( surface ) );

		string Read( string path )
		{
			using var stream = files.Files[path].Open();
			using var reader = new StreamReader( stream );
			return reader.ReadToEnd();
		}
	}

	internal SourceSoundDefinition ResolveSurfaceSound( SourceSoundDefinition definition )
	{
		var missing = definition.Waves.Where( wave => GetContentPath( wave ) is null )
			.ToHashSet( StringComparer.OrdinalIgnoreCase );
		if ( missing.Count == 0 )
			return definition;
		var available = definition.Waves.Where( wave => !missing.Contains( wave ) ).ToArray();
		var outcome = available.Length > 0 ? "Only available variants will be used." : "That sound slot will be empty.";
		Log.Trace( $"{Title}: surface sound '{definition.Name}' from '{definition.SourcePath}' references missing waves: {string.Join( ", ", missing )}. {outcome} Physical surface properties remain available." );
		return available.Length > 0 ? definition with { Waves = available } : null;
	}

	internal Surface GetSurface( string name, string source )
	{
		if ( string.IsNullOrWhiteSpace( name ) || !index.Surfaces.ContainsKey( name ) )
		{
			if ( missingSurfaces.Add( name ?? "" ) )
				Log.Trace( $"{source}: Source surface '{name}' is unavailable in {Title}; using the default surface." );
			if ( !index.Surfaces.ContainsKey( "default" ) )
				return Surface.FindByName( "default" )
					?? throw new InvalidOperationException( $"{source}: the engine default physics surface is not loaded." );
			name = "default";
		}
		var path = $"mount://{Ident}/surfaces/{SurfaceResourceName( name )}.surface";
		return GameResource.Load<Surface>( path )
			?? throw new InvalidOperationException( $"{source}: mounted surface '{path}' could not be loaded." );
	}

	internal string GetSurfaceBindingName( string name, string source )
	{
		var surface = GetSurface( name, source );
		return MountUtility.IsMountPath( surface.ResourcePath ) ? surface.ResourcePath : surface.ResourceName;
	}

	internal SoundEvent GetSurfaceSound( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) || !index.SurfaceSounds.ContainsKey( name ) )
			return null;
		var path = $"mount://{Ident}/sounds/surfaces/{SurfaceResourceName( name )}.sound";
		var sound = GameResource.Load<SoundEvent>( path );
		if ( sound is null )
			Log.Trace( $"{Title}: surface sound '{name}' could not be loaded; its sound slot will be empty. Physical surface properties remain available." );
		return sound;
	}
}
