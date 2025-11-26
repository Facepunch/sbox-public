using NativeEngine;
using System.Collections.Concurrent;
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

	private static ConcurrentDictionary<int, TextureReadDelegate> _activeReadTextureCallbacks = new();
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

		_activeReadTextureCallbacks.TryAdd( callbackId, callback );

		var nativeRect = new NativeRect( srcRect.X, srcRect.Y, srcRect.Width, srcRect.Height );
		context.ReadTexturePixels( texture, nativeCallbackObject, nativeRect, slice, mipLevel, true );
	}

	/// <summary>
	/// Called by native to dispatch the managed callback for texture read operations.
	/// </summary>
	internal static void DispatchManagedReadTextureCallback( CReadTexturePixelsManagedCallback caller, IntPtr pData, ImageFormat format, int nMipLevel, int nWidth, int nHeight, int nPitchInBytes )
	{
		var callerId = caller.GetManagedId();
		if ( _activeReadTextureCallbacks.TryRemove( callerId, out var callback ) )
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

	private static ConcurrentDictionary<int, BufferReadDelegate> _activeReadBufferCallbacks = new();
	private static int _nextReadBufferCallbackId = 1;

	internal static void ReadBufferAsync( this IRenderContext context, GpuBuffer buffer, BufferReadDelegate callback, int offset, int bytesToRead )
	{
		// Generate a unique ID for this callback
		int callbackId = Interlocked.Increment( ref _nextReadBufferCallbackId );

		// Destroyed after Done is called
		var nativeCallbackObject = CReadBufferManagedCallback.Create();
		nativeCallbackObject.SetManagedId( callbackId );

		_activeReadBufferCallbacks.TryAdd( callbackId, callback );

		context.ReadBuffer( buffer.native, nativeCallbackObject, offset, bytesToRead, true );
	}

	/// <summary>
	/// Called by native to dispatch the managed callback for buffer read operations.
	/// </summary>
	internal static void DispatchManagedReadBufferCallback( CReadBufferManagedCallback caller, IntPtr pData, int nBytes )
	{
		var callerId = caller.GetManagedId();
		if ( _activeReadBufferCallbacks.TryRemove( callerId, out var callback ) )
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
}
