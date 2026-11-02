using System;
using System.IO;

namespace Sandbox.Mounting.Source1;

internal sealed class SourceMaterial : IDisposable
{
	public VmtFile Source { get; }
	internal object Scope { get; }
	private readonly Lazy<SourceMaterialBuilder> builder;
	private readonly object buildLock = new();
	private bool disposed;

	private SourceMaterial( BaseMount mount, string path, VmtFile source, object scope )
	{
		Source = source;
		Scope = scope;
		builder = new( () => new SourceMaterialBuilder( mount, path, source ) );
	}

	internal static SourceMaterial Read( BaseMount mount, string path, object scope, Func<string, string, Stream> open )
	{
		var contentRoot = BaseMount.GetContentRoot( path, "materials" );
		var entry = VmtFile.NormalizePath( path[contentRoot.Length..], ".vmt" );
		Stream Open( string file ) => file.Equals( entry, StringComparison.OrdinalIgnoreCase )
			? open( path, "" ) : open( file, contentRoot );
		var source = VmtFile.Load( entry, Open,
			symbol => symbol.ToLowerInvariant() switch
			{
				"srgb" => true,
				"gpu<2" or "lowfill" => false,
				_ => null
			},
			diagnostic => Log.Trace( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );
		return new SourceMaterial( mount, path, source, scope );
	}

	public Material CreateMaterial( string name, bool mapSurface = false )
	{
		lock ( buildLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return builder.Value.CreateMaterial( name, mapSurface );
		}
	}

	internal bool TryGetMappingSize( out Vector2 size )
	{
		lock ( buildLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return builder.Value.TryGetMappingSize( out size );
		}
	}

	public void Dispose()
	{
		lock ( buildLock )
		{
			disposed = true;
			if ( builder.IsValueCreated ) builder.Value.ClearDependencies();
		}
	}
}
