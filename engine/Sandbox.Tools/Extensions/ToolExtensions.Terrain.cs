namespace Sandbox;

public static partial class SandboxToolExtensions
{
	/// <summary>
	/// Schedule a normal map update after editing terrain heights on the GPU.
	/// </summary>
	public static void InvalidateHeightMap( this Terrain terrain )
	{
		terrain.RebakeNormalMap();
	}
}
