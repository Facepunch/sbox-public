using Sandbox.Internal;

namespace Sandbox;

partial class Scene
{
	static WeakHashSet<Scene> _all { get; set; } = [];

	/// <summary>
	/// All active non-editor scenes.
	/// </summary>
	public static IEnumerable<Scene> All => _all.Where( x => !x.IsEditor && x.Active );

	/// <summary>
	/// <see cref="RenderEnvmaps"/> on each scene in <see cref="All"/>, without allocating.
	/// </summary>
	internal static void RenderAllEnvmaps()
	{
		foreach ( var scene in _all.GetLiveItems() )
		{
			if ( scene.IsEditor || !scene.Active ) continue;
			scene.RenderEnvmaps();
		}
	}
}
