namespace Borealis;

/// <summary>
/// The normal of each pixel's triangle, found from the depth prepass's visibility ids (<see cref="GpuDevice.ResolvesIdsNormals"/>,
/// <c>-ids-normals</c>), into a viewport-sized target ambient occlusion reads (<c>BorealisIdsNormals</c>, with
/// <c>BorealisIdsNormalsOn</c> 1) where it would rebuild normals from depth. A pixel without one (no id, a classic draw,
/// a range without a first-corner table) is zeros there, and AO rebuilds that pixel's normal from depth as before. Right
/// after the depth chain, on the graphics queue.
/// </summary>
internal sealed class IdsNormalsLayer : RenderLayer
{
	static readonly StringToken NormalsName = new( "BorealisIdsNormals" );
	static readonly StringToken NormalsOnName = new( "BorealisIdsNormalsOn" );

	GpuTexture target;

	/// <summary>The target the last frame resolved into, for the tests (a transient: read it before the next frame).</summary>
	internal GpuTexture LastResolved { get; private set; }

	public IdsNormalsLayer() : base( "Normals From Ids" ) { }

	public override bool IsNeeded( RenderFrame frame )
	{
		if ( frame.System.DepthPrepass && !frame.IsSkybox && RenderHost.Current.Device is { ResolvesIdsNormals: true } ) return true;
		LastResolved = null;
		return false;
	}

	public override FrameResources Writes => FrameResources.IdsNormals;
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.Depth;

	// What reads it is a camera effect, which the frame's output culling doesn't see
	public override bool HasSideEffects => true;

	/// <summary>The target, named for the frame's effects only when the view's ids hold this frame's prepass.</summary>
	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		target = LastResolved = null;
		if ( !frameContext.IdsNormalsReady( frame.MainPass ) )
		{
			frameContext.Attributes.Set( NormalsOnName, 0 );
			return;
		}

		var viewport = frame.View.Viewport;
		target = frame.System.Transients.Get( TextureDesc.Target( (int)viewport.Width, (int)viewport.Height, TextureFormat.RGBA16161616F ) with { Storage = true }, "Ids Normals" );
		frameContext.Attributes.Set( NormalsName, target );
		frameContext.Attributes.Set( NormalsOnName, 1 );
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		if ( target is null ) return;

		// Zeros, which AO reads as no normal, if the device found nothing to resolve after all
		if ( !rc.ResolveIdsNormals( frame.MainPass, frame.Output.Target.Depth, target, frame.View.WorldToProjection, frame.View.Viewport, frame.DepthMin, frame.DepthMax ) )
			rc.ClearTexture( target, Color.Transparent );

		rc.BarrierComputeToPixelShaderRead( target );
		LastResolved = target;
		target = null;
	}
}
