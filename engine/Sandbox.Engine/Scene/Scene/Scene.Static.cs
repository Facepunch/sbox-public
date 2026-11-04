using Sandbox.Internal;

namespace Sandbox;

partial class Scene
{
	static WeakHashSet<Scene> _all { get; set; } = [];

	/// <summary>
	/// All active non-editor scenes.
	/// </summary>
	public static IEnumerable<Scene> All => AllIncludingUnlisted.Where( x => !x.IsUnlisted );

	internal static IEnumerable<Scene> AllIncludingUnlisted => _all.Where( x => !x.IsEditor && x.Active );

	/// <summary>
	/// Kept out of <see cref="All"/>, like the menu's own scene.
	/// </summary>
	internal bool IsUnlisted { get; init; }

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
