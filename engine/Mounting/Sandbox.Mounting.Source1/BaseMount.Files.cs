using System;
using System.Globalization;
using System.IO;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	internal readonly record struct SourceFile( string Filename, VpkArchive Package )
	{
		public Stream Open() => Package is null ? File.OpenRead( Filename ) : Package.OpenFile( Filename );
	}

	internal readonly record struct ContentSource( string Path, bool IsArchive, bool Recursive = true );

	private static EnumerationOptions ContentFiles( bool recursive = true ) => new()
	{
		RecurseSubdirectories = recursive,
		IgnoreInaccessible = false,
		AttributesToSkip = FileAttributes.ReparsePoint,
		MatchCasing = MatchCasing.CaseInsensitive
	};

	internal IEnumerable<ContentSource> ScanContent( string directory )
	{
		var root = Path.GetFullPath( directory );
		var primary = Path.Combine( root, ContentDirectory );
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in System.IO.Directory.EnumerateFiles( root, "*.vpk", ContentFiles() )
			.OrderBy( path => !path.StartsWith( primary + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase ) )
		{
			var archive = ResolveArchive( path );
			if ( archive is not null && seen.Add( archive ) )
				yield return new( archive, true );
		}
		foreach ( var folder in System.IO.Directory.EnumerateDirectories( root, "*", ContentFiles( false ) )
			.OrderBy( path => !path.Equals( primary, StringComparison.OrdinalIgnoreCase ) )
			.ThenBy( path => path, StringComparer.OrdinalIgnoreCase ) )
			yield return new( folder, false );
		yield return new( root, false, false );
	}

	private static string ResolveArchive( string path )
	{
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
		public readonly object SourceScope = new();
		public readonly List<VpkArchive> Packages = [];
		public readonly Dictionary<string, SourceFile> Files = new( StringComparer.OrdinalIgnoreCase );
		public readonly List<(string Path, ResourceFactory Factory)> Resources = [];
		public readonly Dictionary<string, SourceSurfaceDefinition> Surfaces = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, SourceSoundDefinition> SurfaceSounds = new( StringComparer.OrdinalIgnoreCase );
		public SourceDecalLibrary Decals = new();
		public readonly Dictionary<string, SourceDecalMaterial> DecalMaterials = new( StringComparer.OrdinalIgnoreCase );
		public SourcePropData PropData;
		public readonly List<string> PropDataScripts = [];
		public readonly Dictionary<string, Lazy<SourceMaterial>> MaterialSources = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<string, Lazy<SourceTexture>> TextureSources = new( StringComparer.OrdinalIgnoreCase );
		public readonly Dictionary<Material, SourceMaterialVariant> MaterialVariants = new( ReferenceEqualityComparer.Instance );
		public Texture CameraRenderTarget;

		public void Add( string path, VpkArchive package = null, string physicalPath = null )
		{
			if ( !Files.TryAdd( path, new SourceFile( physicalPath ?? path, package ) ) )
				return;

			if ( path.StartsWith( "scripts/propdata/", StringComparison.OrdinalIgnoreCase )
				&& path.EndsWith( ".txt", StringComparison.OrdinalIgnoreCase ) )
				PropDataScripts.Add( path );

			if ( FileTypes.TryGetValue( Path.GetExtension( path ), out var resource ) )
				Resources.Add( (path, resource) );
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

		public void Dispose()
		{
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

	private static void IndexSources( FileIndex files, IEnumerable<ContentSource> sources )
	{
		foreach ( var source in sources )
		{
			if ( source.IsArchive )
			{
				var package = new VpkArchive( source.Path );
				files.Packages.Add( package );
				foreach ( var path in package.GetFilePaths() )
					files.Add( path, package );
				continue;
			}

			foreach ( var path in System.IO.Directory.EnumerateFiles( source.Path, "*", ContentFiles( source.Recursive ) ) )
			{
				if ( path.EndsWith( ".vpk", StringComparison.OrdinalIgnoreCase ) )
					continue;
				var virtualPath = Path.GetRelativePath( source.Path, path ).Replace( '\\', '/' );
				files.Add( virtualPath, physicalPath: path );
			}
		}
	}

	internal Stream OpenContentFile( string filename )
	{
		if ( TryOpenContentFile( filename, out var stream ) )
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
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( filename );
		lock ( indexLock )
		{
			if ( index.Files.TryGetValue( filename.Replace( '\\', '/' ), out var entry ) )
			{
				stream = entry.Open();
				return true;
			}
			stream = null;
			return false;
		}
	}

}
