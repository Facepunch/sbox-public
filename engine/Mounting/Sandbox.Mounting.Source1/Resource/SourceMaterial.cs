using System;

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

	internal static SourceMaterial Read( BaseMount mount, string path, object scope )
	{
		var source = VmtFile.Load( path, mount.OpenContentFile,
			symbol => symbol.ToLowerInvariant() switch
			{
				"srgb" => true,
				"gpu<2" or "lowfill" => false,
				_ => null
			},
			diagnostic => Log.Trace( $"{diagnostic.Path}:{diagnostic.Line}: {diagnostic.Message}" ) );
		return new SourceMaterial( mount, path, source, scope );
	}

	public Material CreateMaterial( string name )
	{
		lock ( buildLock )
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return builder.Value.CreateMaterial( name );
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
