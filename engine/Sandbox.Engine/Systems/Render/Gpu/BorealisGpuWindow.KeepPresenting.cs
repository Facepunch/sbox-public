using Borealis.Gpu;

namespace Sandbox;

internal sealed partial class BorealisGpuWindow
{
	/// <summary>
	/// On the frame thread, while the main thread is busy past a frame it handed over (<see cref="BorealisGpuRenderBackend"/>'s
	/// keep presenting): present the last frame's output again, copied from <see cref="Target"/> into the image acquired ahead,
	/// then acquire the next for the main thread's frame. Nothing while the window is minimised, waits to resize or has no
	/// target, or nothing to present to. True when it presented.
	/// </summary>
	internal bool PresentAgain( Device gpu, TextureHandle output )
	{
		if ( target is null || swapChain is null || resizeWanted || Size.x < 1 || Size.y < 1 || !output.IsValid ) return false;

		try
		{
			if ( !acquiredAhead )
			{
				if ( swapChain.Current.IsValid ) return false;
				acquiredAhead = swapChain.Acquire( out _ );
				if ( !acquiredAhead ) return false;
			}

			var image = swapChain.Current;
			var from = gpu.InfoOf( output );
			var to = gpu.InfoOf( image );
			var width = Math.Min( from.Width, to.Width );
			var height = Math.Min( from.Height, to.Height );

			// The output is readable between blocks (GraphicsContext.FinishReadable), and left so; the image's old contents
			// don't matter
			var list = gpu.BeginCommandList( QueueType.Graphics, "Present the last frame again" );
			list.Barrier( new TextureBarrier { Texture = output, OldLayout = ImageLayout.ShaderRead, NewLayout = ImageLayout.CopySource, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.MemoryWrite, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferRead, MipCount = 1, LayerCount = 1 } );
			list.Barrier( new TextureBarrier { Texture = image, OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.CopyDestination, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.None, DestinationStage = PipelineStage.Transfer, DestinationAccess = MemoryAccess.TransferWrite, MipCount = 1, LayerCount = 1 } );
			list.Blit( output, (0, 0, width, height), image, (0, 0, width, height), Filter.Nearest );
			list.Barrier( new TextureBarrier { Texture = output, OldLayout = ImageLayout.CopySource, NewLayout = ImageLayout.ShaderRead, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferRead, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.MemoryRead, MipCount = 1, LayerCount = 1 } );
			list.Barrier( new TextureBarrier { Texture = image, OldLayout = ImageLayout.CopyDestination, NewLayout = ImageLayout.Present, SourceStage = PipelineStage.Transfer, SourceAccess = MemoryAccess.TransferWrite, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.None, MipCount = 1, LayerCount = 1 } );
			gpu.Submit( list, default, swapChain );
			acquiredAhead = false;
			swapChain.Present();
			PresentedAgain++;

			// The main thread's next frame draws into an image acquired here, as it would into one acquired ahead
			acquiredAhead = swapChain.Acquire( out _ );
			return true;
		}
		catch ( GpuException e ) when ( e.IsDeviceLost )
		{
			// Left for the main thread's next frame, which says it (BorealisGpuDeviceLost); an image still held is its to submit for
			acquiredAhead = swapChain.Current.IsValid;
			return false;
		}
	}

	/// <summary>Frames presented again while the main thread was busy (<see cref="PresentAgain"/>), for the tests and stats.</summary>
	internal long PresentedAgain { get; private set; }
}
