using System;
using System.IO;
using System.Threading;
using Sandbox.Mounting;

namespace Sandbox.Mounting.Source1;

public abstract partial class BaseMount
{
	internal static string GetMapPakRoot( string path ) => $"{Path.ChangeExtension( path, null )}/pak/";

	private sealed class MapImportEntry( SourceFile file )
	{
		public readonly Lazy<BspFile> Data = new( () =>
		{
			using var stream = file.Open();
			return new BspFile( stream );
		} );
		public int Users;
	}

	internal sealed class MapImport( BspFile data, Action release ) : IDisposable
	{
		public BspFile Data { get; } = data;
		private Action release = release;
		public void Dispose() => Interlocked.Exchange( ref release, null )?.Invoke();
	}

	internal MapImport BeginMapImport( string path )
	{
		var files = AcquireIndex();
		MapImportEntry entry;
		string filename;
		try
		{
			lock ( indexLock )
			{
				filename = ResolveContentFile( files, path );
				if ( !files.MapImports.TryGetValue( filename, out entry ) )
					files.MapImports.Add( filename, entry = new MapImportEntry( files.Files[filename] ) );
				entry.Users++;
			}
		}
		catch
		{
			files.Dispose();
			throw;
		}

		void Release()
		{
			lock ( indexLock )
				if ( --entry.Users == 0 )
					files.MapImports.Remove( filename );
			files.Dispose();
		}
		try
		{
			return new MapImport( entry.Data.Value, Release );
		}
		catch
		{
			Release();
			throw;
		}
	}

	private void IndexMapPaks( FileIndex files )
	{
		var maps = files.Resources.Where( resource => resource.Factory.Type == ResourceType.Scene ).ToArray();
		foreach ( var map in maps )
		{
			BspPakArchive indexed;
			BspPakArchive package = null;
			Stream stream = null;
			try
			{
				stream = files.Files[map.Path].Open();
				var header = new BspHeader( stream );
				var entities = BspFile.ReadEntities( BspFile.ReadRaw( stream, header.Start, header.Lumps[0] ) );
				var mapRoot = GetMapPakRoot( map.Path );
				foreach ( var entity in entities )
					if ( entity.Get( "classname" ) is "infodecal" or "info_projecteddecal"
						&& !string.IsNullOrWhiteSpace( entity.Get( "texture" ) ) )
						files.MapDecals.Add( (VmtFile.NormalizePath( entity.Get( "texture" ), ".vmt" ), mapRoot) );
				stream.Position = header.Start;
				package = new BspPakArchive( stream, files.Files[map.Path].Open );
				stream = null;
				files.Packages.Add( package );
				indexed = package;
				package = null;
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or NotSupportedException )
			{
				var diagnostic = $"{map.Path}: BSP pak was not indexed: {e.Message}";
				files.Errors.Add( diagnostic );
				Log.Warning( diagnostic );
				continue;
			}
			finally
			{
				package?.Dispose();
				stream?.Dispose();
			}
			var root = GetMapPakRoot( map.Path );
			files.ContentParents.Add( root, GetContentRoot( map.Path, "maps" ) );
			foreach ( var path in indexed.GetFilePaths() )
			{
				files.Add( $"{root}{path}", indexed, physicalPath: path );
				if ( path.StartsWith( "sound/", StringComparison.OrdinalIgnoreCase ) )
					files.PackedAudioRoots.Add( root );
			}
		}
	}
}
