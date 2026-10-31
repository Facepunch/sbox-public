using System;
using System.IO;
using System.Threading.Tasks;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount : BaseGameMount
{
	public abstract long AppId { get; }
	public override long? SteamAppId => AppId;
	protected abstract string ContentDirectory { get; }

	private volatile FileIndex index = new();
	private readonly object indexLock = new();

	private readonly record struct ResourceFactory( ResourceType Type, Func<string, Sandbox.Mounting.ResourceLoader> Create );

	private static readonly Dictionary<string, ResourceFactory> FileTypes = new( StringComparer.OrdinalIgnoreCase )
	{
		{ ".bsp", new( ResourceType.Scene, static path => new MapLoader( path ) ) },
		{ ".mdl", new( ResourceType.Model, static path => new ModelLoader( path ) ) },
		{ ".vmt", new( ResourceType.Material, static path => new MaterialLoader( path ) ) },
		{ ".vtf", new( ResourceType.Texture, static path => new TextureLoader( path ) ) },
		{ ".raw", new( ResourceType.Texture, static path => new SourceColorLookupLoader( path ) ) },
		{ ".wav", new( ResourceType.Sound, static path => new WavSoundLoader( path ) ) },
		{ ".mp3", new( ResourceType.Sound, static path => new Mp3SoundLoader( path ) ) }
	};

	protected override void Initialize( InitializeContext context )
	{
		ReplaceIndex( new FileIndex() );
		IsInstalled = false;
		IsMounted = false;
		if ( !context.IsAppInstalled( AppId ) )
			return;

		var directory = context.GetAppDirectory( AppId );
		if ( !System.IO.Directory.Exists( directory ) )
			return;

		IndexContent( directory );
	}

	internal void IndexContent( string directory ) => IndexContent( ScanContent( directory ) );

	internal void IndexContent( IEnumerable<ContentSource> sources )
	{
		var next = new FileIndex();
		try
		{
			IndexSources( next, sources );
			IndexMapPaks( next );
			var unavailable = next.RemoveUnavailableModels();
			if ( unavailable.Count > 0 )
			{
				foreach ( var diagnostic in unavailable )
					Log.Trace( $"{Title}: model was not registered: {diagnostic}" );
				Log.Trace( $"{Title}: skipped {unavailable.Count} models with unavailable geometry. {string.Join( "; ", unavailable.Take( 3 ) )}"
					+ (unavailable.Count > 3 ? "; remaining details are logged at trace verbosity." : "") );
			}
			ReplaceIndex( next );
			IsInstalled = next.Resources.Count > 0;
			next = null;
		}
		finally
		{
			next?.Dispose();
		}
	}

	private void ReplaceIndex( FileIndex next )
	{
		FileIndex previous;
		lock ( indexLock )
		{
			previous = index;
			index = next;
		}
		previous.Dispose();
	}

	private FileIndex AcquireIndex()
	{
		lock ( indexLock )
		{
			index.Retain();
			return index;
		}
	}

	protected override void Shutdown()
	{
		ReplaceIndex( new FileIndex() );
	}

	protected override Task Mount( MountContext context )
	{
		IsMounted = false;
		using var current = AcquireIndex();
		foreach ( var error in current.Errors )
			context.AddError( error );
		if ( current.Resources.Count == 0 )
		{
			context.AddError( $"No supported {Title} resources were found." );
			return Task.CompletedTask;
		}

		foreach ( var resource in current.Resources )
		{
			context.Add( resource.Factory.Type, resource.Path, resource.Factory.Create( resource.Path ) );
			if ( resource.Factory.Type == ResourceType.Texture && resource.Path.EndsWith( ".vtf", StringComparison.OrdinalIgnoreCase ) )
				MountTextureFrames( context, current, resource.Path );
			if ( resource.Factory.Type == ResourceType.Scene )
			{
				context.Add( ResourceType.Scene, $"{resource.Path}_skybox", new MapLoader( resource.Path, skybox: true ) );
				context.Add( ResourceType.Texture, $"{resource.Path}_grading.vtex", new MapColorLookupLoader( resource.Path ) );
			}
		}

		MountDecals( context, current );
		MountSoundscapes( context, current );
		try
		{
			MountSurfaces( context, current );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			current.Surfaces.Clear();
			Log.Trace( $"{Title}: Source surfaces were not mounted: {e.Message}" );
			context.AddError( $"Source surfaces were not mounted: {e.Message}" );
		}

		IsMounted = true;
		return Task.CompletedTask;
	}

	private void MountTextureFrames( MountContext context, FileIndex files, string path )
	{
		try
		{
			using var stream = files.Files[path].OpenHeader( VtfFile.HeaderLength );
			var header = VtfFile.ReadHeader( stream );
			if ( header.Frames == 0 )
				throw new InvalidDataException( "VTF frame count is zero." );
			for ( var frame = 1; frame < header.Frames; frame++ )
				context.Add( ResourceType.Texture, TextureLoader.FramePath( path, frame ), new TextureLoader( path, frame ) );
			if ( header.Frames > 1 && (header.Flags & VtfFlags.ENVMAP) == 0 )
				context.Add( ResourceType.Texture, TextureLoader.FramePath( path, null ), new TextureLoader( path, null ) );
		}
		catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
		{
			Log.Trace( $"{path}: texture frame loaders were not registered: {e.Message}" );
			context.AddError( $"{path}: texture frame loaders were not registered: {e.Message}" );
		}
	}

	internal string GetContentPath( string path, string contentRoot = "" ) =>
		FindContentFile( index, path, contentRoot ) is { } filename ? $"mount://{Ident}/{filename}" : null;

}
