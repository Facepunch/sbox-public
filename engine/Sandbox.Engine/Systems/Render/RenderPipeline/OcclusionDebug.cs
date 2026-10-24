using System.Threading;

namespace Sandbox.Rendering;

/// <summary>
/// What <c>r_occlusion_debug</c> shows over the view.
/// </summary>
internal enum OcclusionDebugView
{
	Off,

	/// <summary>Boxes for what culling did: hidden, drawn by the late prepass, or partly hidden.</summary>
	Hidden,

	/// <summary>Boxes for everything the cull tested.</summary>
	All,

	/// <summary><see cref="Hidden"/> over the depth the late cull tested against.</summary>
	Depth,
}

/// <summary>
/// How the early cull behaves, for <c>r_occlusion_early</c>.
/// </summary>
internal enum OcclusionEarlyCull
{
	/// <summary>No history: the prepass draws everything, and only the forward pass culls.</summary>
	Off,

	On,

	/// <summary>Every cullable instance misses the early cull, so the late prepass has to draw everything visible.</summary>
	RejectAll,
}

/// <summary>
/// Debugging for both renderers' GPU occlusion culling.
/// </summary>
internal static class OcclusionDebug
{
	[ConVar( "r_occlusion_debug", ConVarFlags.Cheat, Min = 0, Max = 3, Help = "Draw occlusion culling's boxes over the view (0: off, 1: hidden, 2: all, 3: hidden over the depth it tested against)" )]
	internal static int DebugMode { get; set; }

	public static OcclusionDebugView View => (OcclusionDebugView)DebugMode;

	[ConVar( "r_occlusion_debug_mip", ConVarFlags.Cheat, Min = 0, Max = 15, Help = "Depth chain mip shown by r_occlusion_debug 3" )]
	public static int Mip { get; set; }

	[ConVar( "r_occlusion_freeze", ConVarFlags.Cheat, Help = "Keep occlusion culling against the depth and view from when this was turned on" )]
	public static bool Freeze
	{
		get;
		set
		{
			// Every time it's turned on is a new freeze, even for a camera that wasn't culling in between
			if ( value && !field ) Freezes++;
			field = value;
		}
	}

	/// <summary>
	/// How many times <see cref="Freeze"/> has been turned on.
	/// </summary>
	internal static int Freezes { get; private set; }

	[ConVar( "r_occlusion_early", ConVarFlags.Cheat, Min = 0, Max = 2, Help = "Early occlusion cull (0: off, 1: on, 2: reject everything, so the late prepass draws all that's visible)" )]
	internal static int EarlyMode { get; set; } = 1;

	public static OcclusionEarlyCull EarlyCull => (OcclusionEarlyCull)EarlyMode;

	static readonly Lock publishLock = new();
	static OcclusionDebugTarget shown;
	static OcclusionDebugTarget showing;
	static ulong showingFrame;

	/// <summary>
	/// Offers a view's resolved debug image to the overlay. With several views culling, the biggest in a frame is shown.
	/// </summary>
	internal static void Publish( OcclusionDebugTarget target )
	{
		lock ( publishLock )
		{
			var frame = Application.FrameCount;
			if ( frame != showingFrame )
			{
				shown = showing;
				showing = null;
				showingFrame = frame;
			}

			if ( showing is null || Area( target ) > Area( showing ) )
				showing = target;
		}

		static float Area( OcclusionDebugTarget target ) => target.Viewport.Width * target.Viewport.Height;
	}

	/// <summary>
	/// The last complete frame's shown view, or null if nothing has culled lately.
	/// </summary>
	internal static OcclusionDebugTarget Current
	{
		get
		{
			lock ( publishLock )
			{
				var frame = Application.FrameCount;
				if ( showingFrame + 1 < frame ) return null;
				return showingFrame < frame ? showing : shown;
			}
		}
	}
}
