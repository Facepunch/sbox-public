namespace Borealis.Shadows;

/// <summary>
/// Sun cascade and extra sun shadow targets several render systems draw into in turn, rather than a set each
/// (<see cref="ShadowSystem.Shared"/>): a cube capture's six faces. Their owner disposes them, not the systems.
/// </summary>
internal sealed class SharedShadowTargets : IDisposable
{
	internal readonly ShadowMap[] Cascades = new ShadowMap[ShadowSystem.MaxCascades];
	internal readonly ShadowMap[] ExtraSuns = new ShadowMap[SceneLighting.MaxExtraSuns];

	/// <summary>The system that last drew into them.</summary>
	internal ShadowSystem User;

	public void Dispose()
	{
		for ( int i = 0; i < Cascades.Length; i++ )
		{
			Cascades[i]?.Dispose();
			Cascades[i] = null;
		}

		for ( int i = 0; i < ExtraSuns.Length; i++ )
		{
			ExtraSuns[i]?.Dispose();
			ExtraSuns[i] = null;
		}

		User = null;
	}
}
