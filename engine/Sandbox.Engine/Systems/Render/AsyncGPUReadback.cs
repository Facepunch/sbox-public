using NativeEngine;
using System.Threading;

namespace Sandbox;

/// <summary>
/// Provides methods for reading GPU data asynchronously without blocking the render thread.
/// </summary>
/// <remarks>
/// Handles the management of callbacks and memory for reading textures and buffers from GPU memory.
/// Data retrieved through these methods is only valid during the callback execution.
/// </remarks>
internal static class AsyncGPUReadback
{
	internal delegate void TextureReadDelegate( Span<byte> readData, ImageFormat readFormat, int readMipLevel, int readWidth, int readHeight, Action doneWithData );

	private static readonly Dictionary<int, TextureReadDelegate> _activeReadTextureCallbacks = new();
	private static int _nextReadTextureCallbackId = 1;

	/// <summary>
	/// Reads texture data from GPU memory, data is kept valid until after the callback task is finished.
	/// If srcRect is not specified, the entire texture will be read.
	/// </summary>
	internal static void ReadTextureAsync( this IRenderContext context, Texture texture, TextureReadDelegate callback, int slice = 0, int mipLevel = 0, (int X, int Y, int Width, int Height) srcRect = default )
	{
		if ( texture is null )
			return;

		context.ReadTextureAsync( texture.native, callback, slice, mipLevel, srcRect );
	}

	internal static void ReadTextureAsync( this IRenderContext context, ITexture texture, TextureReadDelegate callback, int slice = 0, int mipLevel = 0, (int X, int Y, int Width, int Height) srcRect = default )
	{
		if ( texture.IsNull )
			return;

		// Generate a unique ID for this callback
		int callbackId = Interlocked.Increment( ref _nextReadTextureCallbackId );

		// Destroyed after Done is called
		var nativeCallbackObject = CReadTexturePixelsManagedCallback.Create();
		nativeCallbackObject.SetManagedId( callbackId );

		lock ( _activeReadTextureCallbacks ) _activeReadTextureCallbacks.TryAdd( callbackId, callback );

		var nativeRect = new NativeRect( srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height );
		context.ReadTexturePixels( texture, nativeCallbackObject, nativeRect, slice, mipLevel, true );
	}

	/// <summary>
	/// Called by native to dispatch the managed callback for texture read operations.
	/// </summary>
	internal static void DispatchManagedReadTextureCallback( CReadTexturePixelsManagedCallback caller, IntPtr pData, ImageFormat format, int nMipLevel, int nWidth, int nHeight, int nPitchInBytes )
	{
		var callerId = caller.GetManagedId();
		TextureReadDelegate callback;
		lock ( _activeReadTextureCallbacks ) _activeReadTextureCallbacks.Remove( callerId, out callback );

		if ( callback is not null )
		{
			// We move this to another thread, to unblock the render thread as soon as possible
			Task.Run( () =>
			{
				var doneEarly = false;
				try
				{
					unsafe
					{
						var doneWithData = () =>
						{
							doneEarly = true;
							caller.Done();
						};
						var bytes = new Span<byte>( pData.ToPointer(), nHeight * nPitchInBytes );
						callback( bytes, format, nMipLevel, nWidth, nHeight, doneWithData );
					}
				}
				finally
				{
					if ( !doneEarly )
					{
						caller.Done();
					}
				}
			} );
		}
	}

	internal delegate void BufferReadDelegate( Span<byte> pData );

	private static readonly Dictionary<int, (BufferReadDelegate Callback, bool Inline)> _activeReadBufferCallbacks = new();
	private static int _nextReadBufferCallbackId = 1;

	/// <summary>
	/// Reads buffer data from GPU memory. The callback runs on a worker thread, or with <paramref name="inline"/> on the
	/// render thread, which is only for tiny callbacks.
	/// </summary>
	internal static void ReadBufferAsync( this IRenderContext context, GpuBuffer buffer, BufferReadDelegate callback, int offset, int bytesToRead, bool inline = false )
	{
		// Generate a unique ID for this callback
		int callbackId = Interlocked.Increment( ref _nextReadBufferCallbackId );

		// Destroyed after Done is called
		var nativeCallbackObject = CReadBufferManagedCallback.Create();
		nativeCallbackObject.SetManagedId( callbackId );

		lock ( _activeReadBufferCallbacks ) _activeReadBufferCallbacks.TryAdd( callbackId, (callback, inline) );

		context.ReadBuffer( buffer.native, nativeCallbackObject, offset, bytesToRead, true );
	}

	/// <summary>
	/// Called by native to dispatch the managed callback for buffer read operations.
	/// </summary>
	internal static void DispatchManagedReadBufferCallback( CReadBufferManagedCallback caller, IntPtr pData, int nBytes )
	{
		var callerId = caller.GetManagedId();
		(BufferReadDelegate Callback, bool Inline) request;
		lock ( _activeReadBufferCallbacks ) _activeReadBufferCallbacks.Remove( callerId, out request );

		if ( request.Callback is null )
			return;

		if ( !request.Inline )
		{
			QueueReadBufferCallback( caller, pData, nBytes, request.Callback );
			return;
		}

		try
		{
			unsafe
			{
				request.Callback( new Span<byte>( pData.ToPointer(), nBytes ) );
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, "GPU readback callback failed" );
		}
		finally
		{
			caller.Done();
		}
	}

	// Separate so the closure isn't allocated for inline callbacks
	static void QueueReadBufferCallback( CReadBufferManagedCallback caller, IntPtr pData, int nBytes, BufferReadDelegate callback )
	{
		Task.Run( () =>
		{
			try
			{
				unsafe
				{
					var bytes = new Span<byte>( pData.ToPointer(), nBytes );
					callback( bytes );
				}
			}
			finally
			{
				caller.Done();
			}
		} );
	}
}
