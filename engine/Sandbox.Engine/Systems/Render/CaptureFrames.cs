namespace Sandbox.Rendering;

/// <summary>
/// Which frames are captures: frames other rendering samples, rather than a camera's own frame for its window, game view or
/// texture. Effects meant for what people look at (CMAA2) skip them, in either renderer.
/// </summary>
internal static class CaptureFrames
{
	/// <summary>
	/// A cube face (an envmap probe's or a DDGI probe's), a child view (a refraction, <c>DrawView</c>, <c>DrawReflection</c>,
	/// any view asked for while another draws) or a reflection (a planar reflection).
	/// </summary>
	public static bool IsCapture( int cubeFace, bool childView, bool reflection ) => cubeFace >= 0 || childView || reflection;
}
