using System.Threading;

namespace Borealis;

/// <summary>
/// Whether the draws being recorded on this thread are drawn again every frame they're seen (a scene's custom objects): a
/// pipeline such a draw asks for that isn't made yet can be made on the device's workers while the draw is skipped, as a
/// scene draw's is, rather than on the recording thread. A one-off draw (into a texture made once) still waits for its own.
/// </summary>
internal static class RedrawnEveryFrame
{
	[ThreadStatic] static int depth;

	/// <summary>Whether this thread is recording draws that are redrawn every frame.</summary>
	public static bool Active => depth > 0;

	/// <summary>Mark this thread's draws as redrawn every frame until the scope is disposed.</summary>
	public static Scope Begin()
	{
		depth++;
		return default;
	}

	public readonly struct Scope : IDisposable
	{
		public void Dispose() => depth--;
	}
}
