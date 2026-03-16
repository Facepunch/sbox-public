using SkiaSharp;

namespace Sandbox;

public partial class Texture
{
	internal static List<Animation> Animations = new();

	internal class Animation : IDisposable
	{
		internal WeakReference<Texture> Texture;
		internal SKBitmap Bitmap;
		internal SKCodec Codec;
		internal int Duration;
		internal int FrameIndex;
		internal Task UpdateTask;

		SKCodecFrameInfo[] _frameInfo;

		internal Animation( SKCodec codec )
		{
			Codec = codec;
			_frameInfo = Codec.FrameInfo;
			Bitmap = new SKBitmap( Codec.Info.Width, Codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul );
			Duration = _frameInfo.Sum( x => x.Duration );
			FrameIndex = -1;

			Decode();
		}

		private bool Decode()
		{
			// Loop global time by the total duration.
			var time = RealTime.Now % (Duration / 1000.0f);
			var frameIndex = 0;
			var duration = 0.0f;

			// Find the frame index based off global time.
			for ( int i = 0; i < _frameInfo.Length; i++ )
			{
				duration += Duration > 0 ? (_frameInfo[i].Duration / 1000.0f) : 0.1f;
				if ( duration > time )
				{
					frameIndex = i;
					break;
				}
			}

			// Same frame from last time so don't bother decoding.
			if ( frameIndex == FrameIndex )
				return false;

			// Decode this frame and put it into the bitmap.
			Codec.GetPixels( Bitmap.Info, Bitmap.GetPixels(), new SKCodecOptions( frameIndex ) );
			FrameIndex = frameIndex;

			return true;
		}

		internal unsafe void Update( Texture texture )
		{
			// If we didn't decode, it's probably still on the same frame,
			// so no need to update the texture.
			if ( !Decode() )
				return;

			// The texture may have been disposed between when Tick() captured
			// the reference and when this background task actually runs.
			if ( !texture.IsValid )
				return;

			// Update the texture pixels from the bitmap.
			var width = Bitmap.Width;
			var height = Bitmap.Height;
			var span = new ReadOnlySpan<byte>( Bitmap.GetPixels().ToPointer(), width * height * Bitmap.BytesPerPixel );
			texture.Update( span, 0, 0, width, height );

			return;
		}

		public void Dispose()
		{
			if ( Bitmap != null )
			{
				Bitmap.Dispose();
				Bitmap = null;
			}

			if ( Codec != null )
			{
				Codec.Dispose();
				Codec = null;
			}

			Duration = 0;
			FrameIndex = -1;
		}
	}

	internal static void Tick()
	{
		for ( int i = Animations.Count - 1; i >= 0; i-- )
		{
			var animation = Animations[i];
			var task = animation.UpdateTask;

			// Texture has been disposed, remove it from this list.
			if ( !animation.Texture.TryGetTarget( out var texture ) )
			{
				task?.Wait();
				Animations.RemoveAt( i );
				animation.Dispose();
				continue;
			}

			// Update task is still running.
			if ( task != null && !task.IsCompleted )
				continue;

			// If the texture hasn't been used recently, don't bother decoding or updating texture.
			if ( texture.LastUsed > 2 )
				continue;

			// Decode and update texture in background thread.
			animation.UpdateTask = Task.Run( () => animation.Update( texture ) );
		}
	}
}
