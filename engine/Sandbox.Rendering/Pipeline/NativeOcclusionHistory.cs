using System.Threading;

namespace Sandbox.Rendering.Pipeline;

/// <summary>
/// A depth chain an occlusion cull tests against, and the view it was drawn from. Viewport is occlusion_cull_cs.slang's: the
/// view's width and height in pixels, the chain's mip count, and log2 of a mip 0 texel's width in pixels.
/// </summary>
internal readonly record struct NativeOcclusionDepth( Texture Chain, Vector4 Viewport, Vector2 DepthRange, Matrix WorldToProjection )
{
	/// <summary>
	/// A chain as big as its view, as the history keeps: a mip 0 texel is a pixel.
	/// </summary>
	public static NativeOcclusionDepth FullResolution( Texture chain, Vector2 depthRange, Matrix worldToProjection )
		=> new( chain, new Vector4( chain.Width, chain.Height, chain.Mips, 0 ), depthRange, worldToProjection );

	/// <summary>
	/// A conservative chain for the late cull to build into <paramref name="chain"/>, a mip 0 texel to every 4x4 pixels. The
	/// effects' chain isn't conservative: it only keeps MSAA sample 0, which misses alpha-to-coverage gaps.
	/// </summary>
	public static NativeOcclusionDepth Quarter( Texture chain, Rect viewport, Vector2 depthRange, Matrix worldToProjection )
		=> new( chain, new Vector4( (int)viewport.Width, (int)viewport.Height, chain.Mips, 2 ), depthRange, worldToProjection );

	/// <summary>
	/// The size of a <see cref="Quarter"/> chain's first mip.
	/// </summary>
	public static Vector2Int QuarterSize( Rect viewport ) => new( ((int)viewport.Width + 3) / 4, ((int)viewport.Height + 3) / 4 );

	/// <summary>
	/// Binds what the cull tests against. Without <paramref name="previous"/>, everything counts as drawn by the early prepass.
	/// </summary>
	public static void Bind( RenderAttributes attributes, in NativeOcclusionDepth current, in NativeOcclusionDepth previous )
	{
		attributes.Set( "OcclusionDepthChain", current.Chain );
		attributes.Set( "OcclusionViewport", current.Viewport );
		attributes.Set( "OcclusionDepthRange", current.DepthRange );
		attributes.Set( "OcclusionWorldToProjection", current.WorldToProjection );

		// A zero mip count disables history, but something has to be bound
		var hasPrevious = previous.Chain is not null;
		attributes.Set( "OcclusionPreviousChain", hasPrevious ? previous.Chain : current.Chain );
		attributes.Set( "OcclusionPreviousViewport", hasPrevious ? previous.Viewport : Vector4.Zero );
		attributes.Set( "OcclusionPreviousDepthRange", previous.DepthRange );
		attributes.Set( "OcclusionPreviousWorldToProjection", previous.WorldToProjection );
	}
}

/// <summary>
/// A camera's occlusion culling across frames in native's pipeline: a depth chain and its view kept for the next frame's early
/// cull, and the camera's occlusion debugging - the frozen chain for <c>r_occlusion_freeze</c> and the target for
/// <c>r_occlusion_debug</c>.
/// </summary>
internal sealed class NativeOcclusionHistory : IDisposable
{
	// Last frame's chain and the view it was drawn from
	Texture chain;
	Matrix keptWorldToProjection;
	Vector2 keptDepthRange;
	bool kept;
	ulong claimed;

	// The late cull's chain from the frame a freeze started, which freeze that was, and its depth once the late cull has built it
	Texture frozenChain;
	int freeze;
	NativeOcclusionDepth frozen;

	/// <summary>
	/// What the early cull tests against this frame: the frozen depth, last frame's, or nothing without history or with
	/// <c>r_occlusion_early 0</c>.
	/// </summary>
	public NativeOcclusionDepth EarlyDepth { get; private set; }

	/// <summary>
	/// <c>r_occlusion_early 2</c>: every cullable instance misses the early cull this frame, so the late prepass draws it.
	/// </summary>
	public bool RejectsEarly { get; private set; }

	/// <summary>
	/// Whether both culls test against the frozen depth this frame, so what it hid stays hidden as the camera moves.
	/// </summary>
	public bool IsFrozen { get; private set; }

	/// <summary>
	/// This frame's debug target, or null when not debugging.
	/// </summary>
	public OcclusionDebugTarget Debug { get; private set; }

	/// <summary>
	/// Only the first view in a frame may use history, so stereo views can't change it between early and late culls.
	/// </summary>
	public bool Claim( ulong frame ) => Interlocked.Exchange( ref claimed, frame ) != frame;

	/// <summary>
	/// Settles what this frame's culls test against, from what was kept and the debug convars, so both culls agree on it.
	/// </summary>
	public void BeginFrame( Rect viewport )
	{
		var valid = kept && chain is not null && chain.Width == (int)viewport.Width && chain.Height == (int)viewport.Height;
		kept = false;

		BeginFreeze( OcclusionDebug.Freeze, viewport );

		var earlyCull = OcclusionDebug.EarlyCull;
		EarlyDepth = IsFrozen ? frozen
			: valid && earlyCull != OcclusionEarlyCull.Off ? NativeOcclusionDepth.FullResolution( chain, keptDepthRange, keptWorldToProjection )
			: default;
		RejectsEarly = earlyCull == OcclusionEarlyCull.RejectAll;

		if ( OcclusionDebug.View == OcclusionDebugView.Off )
		{
			Debug?.Dispose();
			Debug = null;
			return;
		}

		Debug ??= new OcclusionDebugTarget();
		Debug.Resize( viewport );
	}

	/// <summary>
	/// What the late cull tests against this frame: the frozen depth, or this frame's for it to build, into the freeze's
	/// chain when one is starting, otherwise into <paramref name="scratch"/>.
	/// </summary>
	public NativeOcclusionDepth LateDepth( Rect viewport, Vector2 depthRange, Matrix worldToProjection, Texture scratch )
		=> IsFrozen ? frozen : NativeOcclusionDepth.Quarter( frozenChain ?? scratch, viewport, depthRange, worldToProjection );

	void BeginFreeze( bool freezing, Rect viewport )
	{
		// A frozen chain belongs to one freeze, at one size
		var size = NativeOcclusionDepth.QuarterSize( viewport );
		var stale = frozenChain is not null && (freeze != OcclusionDebug.Freezes || frozenChain.Width != size.x || frozenChain.Height != size.y);
		if ( !freezing || stale ) Thaw();

		IsFrozen = freezing && frozen.Chain is not null;
		if ( !freezing || IsFrozen ) return;

		frozenChain ??= CreateChain( size.x, size.y, "Occlusion freeze" );
		freeze = OcclusionDebug.Freezes;
	}

	/// <summary>
	/// Call once the late cull has built <paramref name="depth"/>'s chain. If that's a freeze's, the freeze holds it from the
	/// next frame.
	/// </summary>
	public void KeepFrozen( in NativeOcclusionDepth depth )
	{
		// Also stops a cull still in flight from before a thaw bringing the freeze back
		if ( !ReferenceEquals( depth.Chain, frozenChain ) ) return;

		frozen = depth;
	}

	void Thaw()
	{
		frozenChain?.Dispose();
		frozenChain = null;
		frozen = default;
		IsFrozen = false;
	}

	/// <summary>
	/// The chain the view's depth downsample writes for the next frame's early cull.
	/// </summary>
	public Texture Target( int width, int height )
	{
		if ( chain is not null && chain.Width == width && chain.Height == height ) return chain;

		// The render device keeps resources in-flight frames use
		chain?.Dispose();
		chain = CreateChain( width, height, "Occlusion history" );
		return chain;
	}

	static Texture CreateChain( int width, int height, string name )
	{
		var mips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		var builder = Texture.CreateRenderTarget().WithSize( width, height ).WithFormat( ImageFormat.RG3232F ).WithUAVBinding();
		if ( mips > 1 ) builder = builder.WithMips( mips );
		return builder.Create( name );
	}

	public void Keep( Matrix worldToProjection, float minZ, float maxZ )
	{
		keptWorldToProjection = worldToProjection;
		keptDepthRange = new Vector2( minZ, maxZ );
		kept = true;
	}

	public void Dispose()
	{
		chain?.Dispose();
		chain = null;
		kept = false;
		EarlyDepth = default;
		Thaw();
		Debug?.Dispose();
		Debug = null;
	}
}
