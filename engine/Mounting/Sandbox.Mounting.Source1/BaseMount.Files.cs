using System;
using System.Globalization;
using System.IO;
using System.Threading;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	internal readonly record struct SourceFile( string Filename, IContentArchive Package )
	{
		public Stream Open() => Package is null ? File.OpenRead( Filename ) : Package.OpenFile( Filename );
		public Stream OpenHeader( int length ) => Package is null ? File.OpenRead( Filename ) : Package.OpenFileHeader( Filename, length );
	}

	internal readonly record struct ContentSource( string Path, bool IsArchive, bool Recursive = true,
		string Prefix = "", IReadOnlyList<string> Files = null );

	private static bool IsArchive( string path ) => path.EndsWith( ".vpk", StringComparison.OrdinalIgnoreCase )
		|| path.EndsWith( ".gma", StringComparison.OrdinalIgnoreCase );

	private static readonly EnumerationOptions ContentFiles = new()
	{
		IgnoreInaccessible = false,
		AttributesToSkip = FileAttributes.ReparsePoint,
		MatchCasing = MatchCasing.CaseInsensitive
	};

	private FileSystemInfo[] ReadContentDirectory( string directory )
	{
		try
		{
			return new DirectoryInfo( directory ).GetFileSystemInfos( "*", ContentFiles );
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"{directory}: content directory was not indexed: {e.Message}" );
			return [];
		}
	}

	private IEnumerable<string> EnumerateContentFiles( string directory, bool recursive )
	{
		var pending = new Stack<string>();
		pending.Push( directory );
		while ( pending.TryPop( out var current ) )
		{
			foreach ( var entry in ReadContentDirectory( current ) )
			{
				if ( entry is DirectoryInfo )
				{
					if ( recursive ) pending.Push( entry.FullName );
					continue;
				}
				yield return entry.FullName;
			}
		}
	}

	internal IEnumerable<ContentSource> ScanContent( string directory )
	{
		var root = Path.GetFullPath( directory );
		var primary = Path.Combine( root, ContentDirectory );
		var sources = new List<ContentSource>();
		var entries = ReadContentDirectory( root );
		foreach ( var folder in entries.OfType<DirectoryInfo>().Select( entry => entry.FullName )
			.OrderBy( path => !path.Equals( primary, StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase ) )
		{
			var name = Path.GetFileName( folder );
			// Asset folders at the installation root are not separate game/content roots.
			var prefix = name.ToLowerInvariant() is "materials" or "models" or "sound" or "scripts" or "maps" ? $"{name}/" : "";
			sources.Add( new( folder, false, Prefix: prefix ) );
		}
		sources.Add( new( root, false, false, Files: entries.OfType<FileInfo>().Select( entry => entry.FullName ).ToArray() ) );

		// Walk each directory once; keep loose paths while collecting archive precedence.
		var archives = new List<string>();
		for ( var i = 0; i < sources.Count; i++ )
		{
			var source = sources[i];
			var loose = new List<string>();
			foreach ( var path in source.Files ?? EnumerateContentFiles( source.Path, source.Recursive ) )
			{
				if ( IsArchive( path ) ) archives.Add( path );
				else loose.Add( path );
			}
			sources[i] = source with { Files = loose };
		}

		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in archives
			.OrderBy( path => !path.StartsWith( primary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => !path.EndsWith( ".gma", StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase ) )
		{
			var archive = ResolveArchive( path );
			if ( archive is not null && seen.Add( archive ) )
				yield return new( archive, true );
		}
		foreach ( var source in sources )
			yield return source;
	}

	private static string ResolveArchive( string path )
	{
		if ( path.EndsWith( ".gma", StringComparison.OrdinalIgnoreCase ) )
			return path;
		var stem = Path.GetFileNameWithoutExtension( path );
		var separator = stem.LastIndexOf( '_' );
		if ( separator >= 0 && stem.Length - separator == 4
			&& int.TryParse( stem.AsSpan( separator + 1 ), NumberStyles.None, CultureInfo.InvariantCulture, out _ )
			&& File.Exists( Path.Combine( Path.GetDirectoryName( path ), stem[..separator] + "_dir.vpk" ) ) )
			return null;
		var directoryArchive = path[..^4] + "_dir.vpk";
		return !path.EndsWith( "_dir.vpk", StringComparison.OrdinalIgnoreCase ) && File.Exists( directoryArchive )
			? directoryArchive : path;
	}

	private sealed class FileIndex : IDisposable
	{
		private int references = 1;
		public readonly object SourceScope = new();
		public readonly List<IContentArchive> Packages = [];
		public readonly Dictionary<string, SourceFile> Files = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, string> ContentParents = new( StringComparer.OrdinalIgnoreCase );
		public readonly HashSet<string> SoundscapeRoots = new( StringComparer.OrdinalIgnoreCase );
		public readonly HashSet<string> PackedAudioRoots = new( StringComparer.OrdinalIgnoreCase );
		public readonly List<(string Path, string Root)> MapDecals = [];
		public readonly List<(string Path, ResourceFactory Factory)> Resources = [];
		public readonly List<string> Errors = [];
		public readonly Dictionary<string, SourceSurfaceDefinition> Surfaces = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, SourceSoundDefinition> SurfaceSounds = new( StringComparer.OrdinalIgnoreCase );
		public SourceDecalLibrary Decals = new();
		public readonly Dictionary<string, SourceDecalMaterial> DecalMaterials = new( StringComparer.OrdinalIgnoreCase );
		public SourcePropData PropData;
		public readonly List<string> PropDataScripts = [];
		public readonly Dictionary<string, Lazy<SourceMaterial>> MaterialSources = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, Lazy<SourceTexture>> TextureSources = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<Material, SourceMaterialVariant> MaterialVariants = new( ReferenceEqualityComparer.Instance );
		public readonly Dictionary<string, MapImportEntry> MapImports = new( StringComparer.OrdinalIgnoreCase );
		public Texture CameraRenderTarget;

		public bool Add( string path, IContentArchive package = null, string physicalPath = null )
		{
			if ( !Files.TryAdd( path, new SourceFile( physicalPath ?? path, package ) ) )
				return false;

			if ( path.StartsWith( "scripts/propdata/", StringComparison.OrdinalIgnoreCase )
				&& path.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) )
				PropDataScripts.Add( path );

			var extension = Path.GetExtension( path );
			if ( FileTypes.TryGetValue( extension, out var resource ) )
			{
				if ( extension.Equals( ".raw", StringComparison.OrdinalIgnoreCase ) )
				{
					try
					{
						using var stream = Files[path].OpenHeader( SourceColorLookup.DataLength + 1 );
						if ( stream.Length != SourceColorLookup.DataLength )
							return true;
					}
					catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException )
					{
						Errors.Add( $"{path}: color-correction texture was not registered: {e.Message}" );
						return true;
					}
				}
				Resources.Add( (path, resource) );
			}
			return true;
		}

		public IReadOnlyList<string> RemoveUnavailableModels()
		{
			var diagnostics = new List<string>();
			Resources.RemoveAll( resource =>
			{
				if ( resource.Factory.Type != ResourceType.Model )
					return false;
				var vvdPath = Path.ChangeExtension( resource.Path, ".vvd" );
				var vtxPath = Path.ChangeExtension( resource.Path, ".dx90.vtx" );
				var hasVertices = Files.ContainsKey( vvdPath );
				var hasIndices = Files.ContainsKey( vtxPath );
				if ( (hasVertices && hasIndices) || Files.ContainsKey( Path.ChangeExtension( resource.Path, ".phy" ) ) )
					return false;
				var missing = !hasVertices && !hasIndices ? $"{vvdPath}, {vtxPath}" : !hasVertices ? vvdPath : vtxPath;
				diagnostics.Add( $"{resource.Path}: missing {missing}" );
				return true;
			} );
			return diagnostics;
		}

		public void Retain() => Interlocked.Increment( ref references );

		public void Dispose()
		{
			if ( Interlocked.Decrement( ref references ) != 0 )
				return;
			foreach ( var source in TextureSources.Values )
				if ( source.IsValueCreated ) source.Value.Dispose();
			foreach ( var source in MaterialSources.Values )
				if ( source.IsValueCreated ) source.Value.Dispose();
			TextureSources.Clear();
			MaterialSources.Clear();
			MaterialVariants.Clear();
			CameraRenderTarget?.Dispose();
			foreach ( var package in Packages )
				package.Dispose();
			Packages.Clear();
		}
	}

	private void IndexSources( FileIndex files, IEnumerable<ContentSource> sources )
	{
		foreach ( var source in sources )
		{
			if ( source.IsArchive )
			{
				IContentArchive indexed;
				IContentArchive package = null;
				try
				{
					package = source.Path.EndsWith( ".gma", StringComparison.OrdinalIgnoreCase )
						? new GmaArchive( source.Path ) : new VpkArchive( source.Path );
					files.Packages.Add( package );
					indexed = package;
					package = null;
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException or NotSupportedException )
				{
					var error = $"{source.Path}: archive was not indexed: {e.Message}";
					files.Errors.Add( error );
					Log.Warning( error );
					continue;
				}
				finally
				{
					package?.Dispose();
				}
				foreach ( var path in indexed.GetFilePaths() )
					files.Add( path, indexed );
				continue;
			}

			foreach ( var path in source.Files ?? EnumerateContentFiles( source.Path, source.Recursive ) )
			{
				if ( IsArchive( path ) )
					continue;
				var virtualPath = source.Prefix + Path.GetRelativePath( source.Path, path ).Replace( '\\', '/' );
				if ( !files.Add( virtualPath, physicalPath: path ) )
					Log.Trace( $"{path}: '{virtualPath}' was not indexed because an earlier content source takes precedence." );
			}
		}
	}

	internal Stream OpenContentFile( string filename )
		=> OpenContentFile( filename, "" );

	internal Stream OpenContentFile( string filename, string contentRoot )
	{
		if ( TryOpenContentFile( filename, contentRoot, out var stream ) )
			return stream;
		throw new FileNotFoundException( $"File '{filename}' not found in {Title} content.", filename );
	}

	internal Texture GetCameraRenderTarget()
	{
		lock ( indexLock )
		{
			if ( index.CameraRenderTarget is { } target )
				return target;
			var texture = Texture.CreateRenderTarget().WithSize( 256, 256 )
				.WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( Color.Black )
				.Create( $"source1/{Ident}/_rt_camera" );
			if ( texture is null || !texture.IsValid || texture.IsError )
				throw new InvalidOperationException( $"Mount '{Ident}' could not create its camera render target." );
			return index.CameraRenderTarget = texture;
		}
	}
	internal byte[] GetFileBytes( string filename )
	{
		using var stream = OpenContentFile( filename );
		var bytes = new byte[checked((int)stream.Length)];
		stream.ReadExactly( bytes );
		return bytes;
	}

	internal bool TryOpenContentFile( string filename, out Stream stream )
		=> TryOpenContentFile( filename, "", out stream );

	internal bool TryOpenContentFile( string filename, string contentRoot, out Stream stream )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( filename );
		var files = AcquireIndex();
		try
		{
			var path = FindContentFile( files, filename, contentRoot );
			if ( path is not null )
			{
				stream = new ContentStream( files.Files[path], files );
				files = null;
				return true;
			}
			stream = null;
			return false;
		}
		finally
		{
			files?.Dispose();
		}
	}

	private static Stream OpenContentFile( FileIndex files, string filename, string contentRoot = "" ) =>
		files.Files[ResolveContentFile( files, filename, contentRoot )].Open();

	private sealed class ContentStream( SourceFile source, FileIndex files ) : Stream
	{
		private readonly Stream stream = source.Open();
		private FileIndex snapshot = files;
		public override bool CanRead => stream.CanRead;
		public override bool CanSeek => stream.CanSeek;
		public override bool CanWrite => false;
		public override long Length => stream.Length;
		public override long Position { get => stream.Position; set => stream.Position = value; }
		public override int Read( byte[] buffer, int offset, int count ) => stream.Read( buffer, offset, count );
		public override int Read( Span<byte> buffer ) => stream.Read( buffer );
		public override int ReadByte() => stream.ReadByte();
		public override long Seek( long offset, SeekOrigin origin ) => stream.Seek( offset, origin );
		public override void Flush() => stream.Flush();
		public override void SetLength( long value ) => throw new NotSupportedException();
		public override void Write( byte[] buffer, int offset, int count ) => throw new NotSupportedException();

		protected override void Dispose( bool disposing )
		{
			if ( disposing && Interlocked.Exchange( ref snapshot, null ) is { } owner )
			{
				try
				{
					stream.Dispose();
				}
				finally
				{
					owner.Dispose();
				}
			}
			base.Dispose( disposing );
		}
	}
}
