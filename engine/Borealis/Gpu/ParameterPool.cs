using System.Collections.Concurrent;

namespace Borealis.Gpu;

/// <summary>
/// Shader parameter sets for one pass at a time, from any recording thread.
/// </summary>
internal sealed class ParameterPool
{
	readonly ConcurrentBag<ShaderParameters> free = new();

	/// <summary>Borrow an empty set and return it to its original device on dispose.</summary>
	public static Lease Borrow( out ShaderParameters parameters )
	{
		var device = RenderHost.Current.Device;
		var pool = device.PassParameters;
		if ( !pool.free.TryTake( out parameters ) ) parameters = device.CreateParameters();
		return new Lease( pool, parameters );
	}

	/// <summary>Returns pass parameters to the pool they came from.</summary>
	public readonly struct Lease( ParameterPool owner, ShaderParameters parameters ) : IDisposable
	{
		/// <summary>Clear and return the borrowed set.</summary>
		public void Dispose()
		{
			parameters.Recycle();
			owner.free.Add( parameters );
		}
	}
}

/// <summary>Device-shared materials used by renderer passes and warmed before worker recording.</summary>
internal enum GpuPassMaterial
{
	DepthResolveCompute,
	DepthResolve,
	Spd,
	ContactShadows,
	ShadowTiles,
	ShadowCacheCopy,
	OutputFlip,
	ShadowDebug,
	IdsView,
	Count,
}
