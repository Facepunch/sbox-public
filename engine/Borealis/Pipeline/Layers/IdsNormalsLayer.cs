namespace Borealis;

/// <summary>
/// The normal of each pixel's triangle, found from the depth prepass's visibility ids (<see cref="GpuDevice.ResolvesIdsNormals"/>,
/// <c>-ids-normals</c>), into a viewport-sized target ambient occlusion reads (<c>BorealisIdsNormals</c>, with
/// <c>BorealisIdsNormalsOn</c> 1) where it would rebuild normals from depth. A pixel without one (no id, a classic draw,
/// a range without a first-corner table) is zeros there, and AO rebuilds that pixel's normal from depth as before. Right
/// after the depth chain, on the graphics queue. It writes each pixel's object motion beside them (<c>BorealisObjectMotion</c>),
/// which <c>Motion::Get</c> adds to the camera's.
/// </summary>
// shortcut: object motion rides -ids-normals' resolve; give it its own switch when it should be on without the normals
internal sealed class IdsNormalsLayer : RenderLayer
{
	static readonly StringToken NormalsName = new( "BorealisIdsNormals" );
	static readonly StringToken NormalsOnName = new( "BorealisIdsNormalsOn" );
	static readonly StringToken MotionName = new( "BorealisObjectMotion" );
	static readonly StringToken MotionOnName = new( "BorealisObjectMotionOn" );

	GpuTexture target, motion;

	/// <summary>The target the last frame resolved into, for the tests (a transient: read it before the next frame).</summary>
	internal GpuTexture LastResolved { get; private set; }

	/// <summary>The object motion the last frame resolved, for the tests (a transient: read it before the next frame).</summary>
	internal GpuTexture LastMotion { get; private set; }

	public IdsNormalsLayer() : base( "Normals From Ids" ) { }

	public override bool IsNeeded( RenderFrame frame )
	{
		// Not for reflections, captures, or a child view drawn inside another's frame with no camera stages of its own: AO runs
		// for a camera's own view
		if ( (frame.Stages is not null || frame.World.Rendering <= 1) && frame.System.DepthPrepass && !frame.IsSkybox && !frame.View.Reflection && !frame.View.Capture && RenderHost.Current.Device is { ResolvesIdsNormals: true } ) return true;
		LastResolved = LastMotion = null;
		return false;
	}

	public override FrameResources Writes => FrameResources.IdsNormals;
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.Depth;

	// What reads it is a camera effect, which the frame's output culling doesn't see
	public override bool HasSideEffects => true;

	/// <summary>The targets, named for the frame's effects only when the view's ids hold this frame's prepass.</summary>
	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		target = motion = LastResolved = LastMotion = null;
		if ( !frameContext.IdsNormalsReady( frame.MainPass ) )
		{
			frameContext.Attributes.Set( NormalsOnName, 0 );
			frameContext.Attributes.Set( MotionOnName, 0 );
			return;
		}

		var viewport = frame.View.Viewport;
		var desc = TextureDesc.Target( (int)viewport.Width, (int)viewport.Height, TextureFormat.RGBA16161616F ) with { Storage = true };
		target = frame.System.Transients.Get( TransientTextureRole.IdsNormals, desc );
		motion = frame.System.Transients.Get( TransientTextureRole.IdsObjectMotion, desc );
		frameContext.Attributes.Set( NormalsName, target );
		frameContext.Attributes.Set( NormalsOnName, 1 );
		frameContext.Attributes.Set( MotionName, motion );
		frameContext.Attributes.Set( MotionOnName, 1 );
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		if ( target is null ) return;

		// Zeros, which AO reads as no normal and Motion::Get as no object motion, if the device found nothing to resolve after all.
		// Without a last frame, this frame's transforms: nothing has moved
		var transforms = frame.System.Transforms;
		if ( !rc.ResolveIdsNormals( frame.MainPass, frame.Output.Target.Depth, target, motion, transforms.Previous ?? transforms.Current, frame.View.WorldToProjection, frame.View.Viewport, frame.DepthMin, frame.DepthMax ) )
		{
			rc.ClearTexture( target, Color.Transparent );
			rc.ClearTexture( motion, Color.Transparent );
		}

		rc.BarrierComputeToPixelShaderRead( target );
		rc.BarrierComputeToPixelShaderRead( motion );
		LastResolved = target;
		LastMotion = motion;
		target = motion = null;
	}
}
