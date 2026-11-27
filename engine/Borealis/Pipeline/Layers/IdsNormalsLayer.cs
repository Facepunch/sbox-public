namespace Borealis;

/// <summary>
/// What the depth prepass's visibility ids give each pixel (<see cref="GpuDevice.ResolvesIds"/>), right after the depth chain,
/// on the graphics queue: with <c>-ids-normals</c> (<see cref="GpuDevice.ResolvesIdsNormals"/>) the normal of its triangle,
/// into a viewport-sized target ambient occlusion reads (<c>BorealisIdsNormals</c>, with <c>BorealisIdsNormalsOn</c> 1) where it
/// would rebuild normals from depth; and for a frame that reads motion (<see cref="RenderView.ObjectMotion"/>) its object's
/// motion (<c>BorealisObjectMotion</c>, with <c>BorealisObjectMotionOn</c> 1), which <c>Motion::Get</c> adds to the camera's.
/// A frame that wants neither records nothing here and takes no targets. A pixel without one (no id, a classic draw, a range
/// without a first-corner table) is zeros there, and its reader falls back as before.
/// </summary>
internal sealed class IdsNormalsLayer : RenderLayer
{
	static readonly StringToken NormalsName = new( "BorealisIdsNormals" );
	static readonly StringToken NormalsOnName = new( "BorealisIdsNormalsOn" );
	static readonly StringToken MotionName = new( "BorealisObjectMotion" );
	static readonly StringToken MotionOnName = new( "BorealisObjectMotionOn" );

	GpuTexture target, motion;

	// Whether the system's attributes still name an earlier frame's targets
	bool named;

	/// <summary>The target the last frame resolved into, for the tests (a transient: read it before the next frame).</summary>
	internal GpuTexture LastResolved { get; private set; }

	/// <summary>The object motion the last frame resolved, for the tests (a transient: read it before the next frame).</summary>
	internal GpuTexture LastMotion { get; private set; }

	public IdsNormalsLayer() : base( "Normals From Ids" ) { }

	public override bool IsNeeded( RenderFrame frame )
	{
		// Not for reflections, captures, or a child view drawn inside another's frame with no camera stages of its own: AO and
		// the motion's readers run for a camera's own view
		if ( (frame.Stages is not null || frame.World.Rendering <= 1) && frame.System.DepthPrepass && !frame.IsSkybox && !frame.View.Reflection && !frame.View.Capture
			&& RenderHost.Current.Device is { } device && (frame.View.ObjectMotion || device.ResolvesIdsNormals) && device.ResolvesIds ) return true;
		LastResolved = LastMotion = null;

		// The system's attributes outlive the frame: an earlier frame's targets were given back
		if ( named )
		{
			frame.System.FrameContext.Attributes.Set( NormalsOnName, 0 );
			frame.System.FrameContext.Attributes.Set( MotionOnName, 0 );
			named = false;
		}

		return false;
	}

	public override FrameResources Writes => FrameResources.IdsNormals;
	public override FrameResources Reads( RenderFrame frame ) => FrameResources.Depth;

	// What reads it is a camera effect, which the frame's output culling doesn't see
	public override bool HasSideEffects => true;

	/// <summary>The targets the frame wants, named for its effects only when the view's ids hold this frame's prepass.</summary>
	public override void BeforeSegments( RenderFrame frame, GpuContext frameContext )
	{
		target = motion = LastResolved = LastMotion = null;
		var wantsMotion = frame.View.ObjectMotion;
		if ( wantsMotion ) frameContext.WantObjectMotion( frame.MainPass );
		if ( !frameContext.IdsNormalsReady( frame.MainPass ) )
		{
			frameContext.Attributes.Set( NormalsOnName, 0 );
			frameContext.Attributes.Set( MotionOnName, 0 );
			return;
		}

		var viewport = frame.View.Viewport;
		var desc = TextureDesc.Target( (int)viewport.Width, (int)viewport.Height, TextureFormat.RGBA16161616F ) with { Storage = true };
		if ( RenderHost.Current.Device.ResolvesIdsNormals )
		{
			target = frame.System.Transients.Get( TransientTextureRole.IdsNormals, desc );
			frameContext.Attributes.Set( NormalsName, target );
		}

		if ( wantsMotion )
		{
			motion = frame.System.Transients.Get( TransientTextureRole.IdsObjectMotion, desc );
			frameContext.Attributes.Set( MotionName, motion );
		}

		frameContext.Attributes.Set( NormalsOnName, target is null ? 0 : 1 );
		frameContext.Attributes.Set( MotionOnName, motion is null ? 0 : 1 );
		named = true;
		frame.System.ObjectMotion = motion;
	}

	public override void Record( RenderFrame frame, GpuContext rc, ref RenderStats stats )
	{
		if ( target is null && motion is null ) return;

		// Zeros, which AO reads as no normal and Motion::Get as no object motion, if the device found nothing to resolve after all.
		// Without a last frame, this frame's transforms: nothing has moved
		var transforms = frame.System.Transforms;
		if ( !rc.ResolveIdsNormals( frame.MainPass, frame.Output.Target.Depth, target, motion, transforms.Previous ?? transforms.Current, frame.View.WorldToProjection, frame.View.Viewport, frame.DepthMin, frame.DepthMax ) )
		{
			if ( target is not null ) rc.ClearTexture( target, Color.Transparent );
			if ( motion is not null ) rc.ClearTexture( motion, Color.Transparent );
		}

		if ( target is not null ) rc.BarrierComputeToPixelShaderRead( target );
		if ( motion is not null ) rc.BarrierComputeToPixelShaderRead( motion );
		LastResolved = target;
		LastMotion = motion;
		target = motion = null;
	}
}
