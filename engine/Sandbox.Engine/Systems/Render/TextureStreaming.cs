namespace Sandbox.Rendering;

/// <summary>
/// Gives global access to the texture streaming system.
/// </summary>
public static class TextureStreaming
{
	/// <summary>
	/// Run a block of code with texture streaming disabled. The drawing device loads every texture whole, so this just runs
	/// it (Source 2's empty device streams nothing).
	/// </summary>
	public static void ExecuteWithDisabled( Action action ) => action();
}
