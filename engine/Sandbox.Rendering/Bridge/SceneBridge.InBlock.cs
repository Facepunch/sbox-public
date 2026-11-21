using Borealis.Backend;
using Vk = Borealis.Gpu;

namespace Sandbox.Rendering.Bridge;

/// <summary>
/// Texture and bitmap renders on Borealis.Gpu asked for inside an engine render block that isn't one of Borealis's frames (a
/// camera's UI stages, a panel's drawing, a command list run on its own), or with a depth target of the caller's outside a
/// frame. They render as a child view renders: in a frame of their own, prepared, recorded and submitted now, without the
/// camera's stages, at the camera's exposure. What the block draws after reads them through its engine textures, so the
/// result is copied into those in the block.
/// </summary>
internal sealed partial class SceneBridge
{
	// An engine texture that lends a frame's texture to the engine's drawing for a copy (CopyIntoEngine)
	Texture lent;

	bool warnedDepthCopy;

	/// <summary>
	/// Render <paramref name="camera"/> into <paramref name="target"/> (and <paramref name="depthTarget"/>) from inside a render
	/// block on <paramref name="gpu"/>, as a child view, then copy what it drew into the engine textures in the block.
	/// </summary>
	bool RenderInBlock( BorealisGpuRenderBackend gpu, SceneCamera camera, in ViewSetup setup, Texture target, Texture depthTarget, out ViewData viewData )
	{
		viewData = default;
		if ( ChildDepth >= MaxChildDepth ) return false;
		if ( views.TryGetValue( new ViewKey( camera._cameraId, setup.ViewHash ), out var drawing ) && drawing.Rendering ) return false;

		var exposure = camera.Tonemap?.GpuExposure( gpu )?.Scale ?? 1.0f;

		ChildDepth++;
		CameraState state;
		try
		{
			if ( !RenderOnGpu( camera, setup, target, depthTarget, exposure, out state ) ) return false;
		}
		finally
		{
			ChildDepth--;
		}

		// RenderOnGpu noted what it drew into as the engine textures' captures
		if ( MirrorContent.Texture( target ) is { } color ) CopyIntoEngine( gpu, color, target );
		if ( depthTarget is not null && MirrorContent.Texture( depthTarget ) is { } depth )
		{
			if ( SameShape( gpu, depth, depthTarget ) ) CopyIntoEngine( gpu, depth, depthTarget );
			else if ( !warnedDepthCopy )
			{
				warnedDepthCopy = true;
				Log.Warning( $"Borealis.Gpu: a texture render's depth in a render block is {depth.Format} x{depth.Samples}; the caller's {depthTarget.ImageFormat} isn't written" );
			}
		}

		viewData = ViewDataFor( state, target );
		return true;
	}

	/// <summary>Whether a frame's texture and an engine texture's copy on the device can be copied between.</summary>
	static bool SameShape( BorealisGpuRenderBackend gpu, GpuTexture drawn, Texture engine )
	{
		var (handle, _) = BackendDevice.Unwrap( drawn );
		if ( !handle.IsValid || MirrorContent.Gpu is not { } device ) return false;

		var from = device.Gpu.InfoOf( handle );
		var to = device.Gpu.InfoOf( gpu.TextureOf( engine ) );
		return from.Format == to.Format && from.Samples == to.Samples && from.Width == to.Width && from.Height == to.Height;
	}

	/// <summary>
	/// Copy mip 0 of <paramref name="drawn"/>, a frame's texture, into <paramref name="destination"/>'s copy on the device, in the
	/// block being recorded: lent to the engine's drawing for the copy, its layout handed back after.
	/// </summary>
	void CopyIntoEngine( BorealisGpuRenderBackend gpu, GpuTexture drawn, Texture destination )
	{
		var (handle, layout) = BackendDevice.Unwrap( drawn );
		if ( !handle.IsValid ) return;

		lent ??= Texture.CreateRenderTarget().WithSize( 4, 4 ).WithFormat( ImageFormat.RGBA8888 ).Create( "Borealis.Gpu lent" );
		gpu.Borrow( lent, handle, layout );
		try
		{
			gpu.CopyTexture( lent, destination, 0, 0, 0, 0 );
		}
		finally
		{
			BackendDevice.SetLayout( drawn, gpu.LayoutOf( lent ) );
			gpu.Release( lent );
		}
	}

	/// <summary>
	/// A bitmap render inside a render block: the camera as a child view into an 8-bit texture, read back once the GPU has
	/// drawn it.
	/// </summary>
	void RenderBitmapInBlock( BorealisGpuRenderBackend gpu, SceneCamera camera, Bitmap bitmap, in ViewSetup setup )
	{
		if ( ChildDepth >= MaxChildDepth ) return;

		var exposure = camera.Tonemap?.GpuExposure( gpu )?.Scale ?? 1.0f;

		ChildDepth++;
		try
		{
			if ( !RenderOnGpu( camera, setup, bitmapTarget, null, exposure, out _ ) ) return;
		}
		finally
		{
			ChildDepth--;
		}

		if ( MirrorContent.Texture( bitmapTarget ) is { } drawn && ReadBack( drawn ) is { } pixels ) Write( bitmap, pixels );
	}

	/// <summary>Mip 0 of an RGBA8 texture of Borealis's, read back, waiting for the GPU.</summary>
	static Color32[] ReadBack( GpuTexture texture )
	{
		var (handle, layout) = BackendDevice.Unwrap( texture );
		if ( !handle.IsValid || MirrorContent.Gpu is not { } device ) return null;

		var gpu = device.Gpu;
		var info = gpu.InfoOf( handle );
		if ( info.Format is not (Vk.Format.RGBA8Unorm or Vk.Format.RGBA8Srgb) ) return null;

		var size = (long)info.Width * info.Height * 4;
		var readback = gpu.CreateBuffer( new Vk.BufferInfo { Size = size, Memory = Vk.MemoryLocation.Readback, Name = "Borealis.Gpu bitmap readback" } );
		try
		{
			var list = gpu.BeginCommandList( Vk.QueueType.Graphics, "Borealis.Gpu bitmap readback" );
			list.Barrier( Vk.TextureBarrier.Transition( handle, layout, Vk.ImageLayout.CopySource, Vk.PipelineStage.AllCommands, Vk.MemoryAccess.MemoryWrite, Vk.PipelineStage.Transfer, Vk.MemoryAccess.TransferRead ) );
			list.CopyTextureToBuffer( handle, 0, 0, readback );
			list.Barrier( Vk.TextureBarrier.Transition( handle, Vk.ImageLayout.CopySource, Vk.ImageLayout.ShaderRead, Vk.PipelineStage.Transfer, Vk.MemoryAccess.TransferRead, Vk.PipelineStage.AllCommands, Vk.MemoryAccess.MemoryRead ) );
			list.Barrier( new Vk.BufferBarrier { Buffer = readback, SourceStage = Vk.PipelineStage.Transfer, SourceAccess = Vk.MemoryAccess.TransferWrite, DestinationStage = Vk.PipelineStage.Host, DestinationAccess = Vk.MemoryAccess.HostRead } );
			gpu.Wait( gpu.Submit( list ) );
			BackendDevice.SetLayout( texture, Vk.ImageLayout.ShaderRead );

			var pixels = new Color32[info.Width * info.Height];
			gpu.Map( readback )[..(int)size].CopyTo( System.Runtime.InteropServices.MemoryMarshal.AsBytes( pixels.AsSpan() ) );
			return pixels;
		}
		finally
		{
			gpu.Destroy( readback );
		}
	}
}
