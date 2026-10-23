using System.Threading;

namespace Sandbox.SceneRenderer.Culling;

/// <summary>
/// Retains a depth chain and its view for the next frame's early cull.
/// </summary>
internal sealed class OcclusionHistory : IDisposable
{
	Texture chain;
	bool kept;
	ulong claimed;

	public bool Valid { get; private set; }

	public Texture Chain => chain;
	public Matrix WorldToProjection { get; private set; }
	public Vector2 DepthRange { get; private set; }

	/// <summary>
	/// Only the first view in a frame may use history, so stereo views can't change it between early and late culls.
	/// </summary>
	public bool Claim( ulong frame ) => Interlocked.Exchange( ref claimed, frame ) != frame;

	public void BeginFrame( bool culling, Rect viewport )
	{
		Valid = culling && kept && chain is not null && chain.Width == (int)viewport.Width && chain.Height == (int)viewport.Height;
		kept = false;
	}

	public Texture Target( int width, int height )
	{
		if ( chain is not null && chain.Width == width && chain.Height == height ) return chain;

		// The device retains resources used by in-flight frames.
		chain?.Dispose();
		var mips = (int)Math.Log2( Math.Min( width, height ) ) + 1;
		chain = Texture.CreateRenderTarget().WithFormat( ImageFormat.RG3232F ).WithSize( width, height ).WithMips( mips ).WithUAVBinding().Create( "SceneRenderer occlusion history" );
		return chain;
	}

	public void Keep( Matrix worldToProjection, float minZ, float maxZ )
	{
		WorldToProjection = worldToProjection;
		DepthRange = new Vector2( minZ, maxZ );
		kept = true;
	}

	public void Dispose()
	{
		chain?.Dispose();
		chain = null;
		kept = false;
		Valid = false;
	}
}
