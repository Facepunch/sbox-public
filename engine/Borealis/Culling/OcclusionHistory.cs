namespace Borealis.Culling;

/// <summary>
/// Retains a depth chain and its view for the next frame's early cull, and the camera's occlusion debugging: the frozen chain
/// for <c>r_occlusion_freeze</c> and the target for <c>r_occlusion_debug</c>.
/// </summary>
internal sealed class OcclusionHistory : IDisposable
{
	// Last frame's chain and the view it was drawn from
	GpuTexture chain;
	Matrix keptWorldToProjection;
	Vector2 keptDepthRange;
	bool kept;

	// The late cull's chain from the frame a freeze started, which freeze that was (<see cref="RenderHost.OcclusionFreezes"/>), and its depth
	// once the late cull has built it
	GpuTexture frozenChain;
	int freeze;
	OcclusionDepth frozen;

	/// <summary>
	/// What the early cull tests against this frame: the frozen depth, last frame's, or nothing without history or with
	/// <c>r_occlusion_early 0</c>.
	/// </summary>
	public OcclusionDepth EarlyDepth { get; private set; }

	/// <summary>
	/// <c>r_occlusion_early 2</c>: every cullable instance misses the early cull this frame, so the late prepass draws it.
	/// </summary>
	public bool RejectsEarly { get; private set; }

	/// <summary>
	/// Whether both culls test against the frozen depth this frame, so what it hid stays hidden as the camera moves.
	/// </summary>
	public bool IsFrozen { get; private set; }

	/// <summary>
	/// This frame's debug output, or null when not debugging.
	/// </summary>
	public OcclusionDebugOutput Debug { get; private set; }

	/// <summary>
	/// Settles what this frame's culls test against, from what was kept and the debug convars, so both culls agree on it.
	/// <paramref name="debug"/> turns the debug view on while the host's is off, for the cull reasons view.
	/// </summary>
	public void BeginFrame( bool culling, Rect viewport, bool debug = false )
	{
		var valid = culling && kept && chain is not null && chain.Width == (int)viewport.Width && chain.Height == (int)viewport.Height;
		kept = false;

		var host = RenderHost.Current;
		BeginFreeze( culling && host.OcclusionFrozen, viewport );

		var earlyCull = host.OcclusionEarlyCull;
		EarlyDepth = IsFrozen ? frozen
			: valid && earlyCull != OcclusionEarlyCull.Off ? OcclusionDepth.FullResolution( chain, keptDepthRange, keptWorldToProjection )
			: default;
		RejectsEarly = earlyCull == OcclusionEarlyCull.RejectAll;

		if ( !culling || (host.OcclusionDebugView == OcclusionDebugView.Off && !debug) )
		{
			Debug?.Dispose();
			Debug = null;
			return;
		}

		Debug ??= host.CreateOcclusionDebug();
		Debug?.Resize( viewport );
	}

	/// <summary>
	/// What the late cull tests against: the frozen depth, a new freeze's chain, or scratch borrowed for this frame.
	/// </summary>
	public OcclusionDepth LateDepth( Rect viewport, Vector2 depthRange, Matrix worldToProjection, TransientTextures transients )
	{
		if ( IsFrozen ) return frozen;
		var target = frozenChain ?? transients.Get( TransientTextureRole.OcclusionChain, OcclusionDepth.QuarterChain( viewport ) );
		return OcclusionDepth.Quarter( target, viewport, depthRange, worldToProjection );
	}

	void BeginFreeze( bool freezing, Rect viewport )
	{
		// A frozen chain belongs to one freeze, at one size
		var size = OcclusionDepth.QuarterSize( viewport );
		var stale = frozenChain is not null && (freeze != RenderHost.Current.OcclusionFreezes || frozenChain.Width != size.x || frozenChain.Height != size.y);
		if ( !freezing || stale ) Thaw();

		IsFrozen = freezing && frozen.Chain is not null;
		if ( !freezing || IsFrozen ) return;

		frozenChain ??= CreateChain( size.x, size.y, "Borealis occlusion freeze" );
		freeze = RenderHost.Current.OcclusionFreezes;
	}

	/// <summary>
	/// Call once the late cull has built <paramref name="depth"/>'s chain. If that's a freeze's, the freeze holds it from the
	/// next frame.
	/// </summary>
	public void KeepFrozen( in OcclusionDepth depth )
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

	public GpuTexture Target( int width, int height )
	{
		if ( chain is not null && chain.Width == width && chain.Height == height ) return chain;

		// The device retains resources used by in-flight frames.
		chain?.Dispose();
		chain = CreateChain( width, height, "Borealis occlusion history" );
		return chain;
	}

	static GpuTexture CreateChain( int width, int height, string name )
		=> RenderHost.Current.Device.CreateTexture( SharedPasses.DepthChainDesc( width, height ), name );

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
