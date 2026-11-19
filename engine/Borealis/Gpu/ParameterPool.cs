using System.Collections.Concurrent;

namespace Borealis.Gpu;

/// <summary>
/// Shader parameter sets for one pass at a time, from any recording thread.
/// </summary>
internal static class ParameterPool
{
	static readonly ConcurrentBag<ShaderParameters> free = new();

	/// <summary>
	/// An empty set.
	/// </summary>
	public static ShaderParameters Get() => free.TryTake( out var parameters ) ? parameters : RenderHost.Current.Device.CreateParameters();

	/// <summary>
	/// Clear a set and keep it for the next pass.
	/// </summary>
	public static void Return( ShaderParameters parameters )
	{
		parameters.Recycle();
		free.Add( parameters );
	}
}
