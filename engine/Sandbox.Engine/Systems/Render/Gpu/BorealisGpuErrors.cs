namespace Sandbox;

/// <summary>
/// Errors from drawing code that runs every frame (a scene object's render, a command list's entries): on Borealis.Gpu a
/// call it doesn't support fails the same way each frame, and logging each one floods the log and the error toasts. There
/// they're said once per call site (the same exception type and stack); on Source 2 every one is logged, as before.
/// </summary>
internal static class BorealisGpuErrors
{
	static readonly HashSet<string> said = new();

	/// <summary>Call sites remembered at most; past it, nothing more is said.</summary>
	const int MaxSites = 4096;

	/// <summary>Whether <paramref name="e"/> should be logged: always off Borealis.Gpu, the first time from its call site on it.</summary>
	public static bool ShouldLog( Exception e )
	{
		if ( RenderBackend.Default is not BorealisGpuRenderBackend ) return true;

		var site = $"{e.GetType().FullName}\n{e.StackTrace}";
		lock ( said )
		{
			return said.Count < MaxSites && said.Add( site );
		}
	}
}
