using NativeEngine;
using System.Threading;

namespace Sandbox;

public sealed partial class RenderTarget
{
	static Lock _lock = new();
	static List<RenderTarget> All = new();

	/// <summary>
	/// Get a temporary render target. You should dispose the returned handle when you're done to return the textures to the pool.
	/// </summary>
	/// <param name="width">Width of the render target you want.</param>
	/// <param name="height">Height of the render target you want.</param>
	/// <param name="colorFormat">The format for the color buffer. If set to default we'll use whatever the current pipeline is using.</param>
	/// <param name="depthFormat">The format for the depth buffer.</param>
	/// <param name="msaa">The number of msaa samples you'd like. Msaa render textures are a pain in the ass so you're probably gonna regret trying to use this.</param>
	/// <param name="numMips">Number of mips you want in this texture. You probably don't want this unless you want to generate mips in a second pass.</param>
	/// <param name="targetName">The optional name of the render target</param>
	/// <returns>A RenderTarget that is ready to render to.</returns>
	public static RenderTarget GetTemporary( int width, int height, ImageFormat colorFormat = ImageFormat.Default, ImageFormat depthFormat = ImageFormat.Default, MultisampleAmount msaa = MultisampleAmount.MultisampleNone, int numMips = 1, string targetName = "" )
	{
		const int maxSize = 1024 * 16;
		int maxMips = (int)Math.Log2( Math.Max( width, height ) );

		if ( width <= 0 ) throw new ArgumentException( $"width should be higher than 0 (was {width}x{height})" );
		if ( width > maxSize ) throw new ArgumentException( $"width should be lower than ({maxSize})" );
		if ( height <= 0 ) throw new ArgumentException( $"height should be higher than 0 (was {width}x{height})" );
		if ( height > maxSize ) throw new ArgumentException( $"height should be higher than {maxSize}" );
		if ( numMips <= 0 ) throw new ArgumentException( $"numMips should be higher than 0 (was {width}x{height}x{numMips})" );
		if ( numMips > 1 && msaa != MultisampleAmount.MultisampleNone ) throw new ArgumentException( $"Texture cannot have both msaa and mips at same time" );

		numMips = Math.Min( numMips, maxMips );

		if ( colorFormat == ImageFormat.Default )
			colorFormat = Graphics.IdealColorFormat;

		if ( depthFormat == ImageFormat.Default )
			depthFormat = g_pRenderDevice.IsUsing32BitDepthBuffer() ? ImageFormat.D32FS8 : ImageFormat.D24S8;

		int hash = HashCode.Combine( width, height, colorFormat, depthFormat, numMips, msaa == MultisampleAmount.MultisampleScreen ? (MultisampleAmount)RenderService.GetMultisampleType() : msaa, targetName );

		RenderTarget rt = null;

		lock ( _lock )
		{
			rt = All.FirstOrDefault( x => !x.Loaned && x.CreationHash == hash );

			if ( rt == null )
			{
				// Depth formats can't legally be UAV and they generally hate having mips.
				// An R32 or similar is usually bound as a depth target if you want those. (e.g depth pyramid)
				bool realDepthFormat = depthFormat.IsDepthFormat();

				var size = new Vector2( width, height );
				var rtColor = colorFormat == ImageFormat.None ? null : Texture.CreateRenderTarget().WithFormat( colorFormat ).WithMSAA( msaa ).WithSize( size ).WithMips( numMips ).WithUAVBinding().Create( $"__cache_color_{targetName}_{hash}" );
				var rtDepth = depthFormat == ImageFormat.None ? null : Texture.CreateRenderTarget().WithFormat( depthFormat ).WithMSAA( msaa ).WithSize( size ).WithMips( realDepthFormat ? 1 : numMips ).WithUAVBinding( !realDepthFormat ).Create( $"__cache_depth_{targetName}_{hash}" );

				rt = new RenderTarget()
				{
					Loaned = true,
					CreationHash = hash,
					ColorTarget = rtColor,
					DepthTarget = rtDepth,
					Width = width,
					Height = height
				};

				All.Add( rt );
			}

			rt.Loaned = true;
			rt.FramesSinceUsed = 0;
		}

		return rt;
	}

	/// <summary>
	/// Get a temporary render target. You should dispose the returned handle when you're done to return the textures to the pool.
	/// </summary>
	/// <param name="sizeFactor">Divide the screen size by this factor. 2 would be half screen sized. 1 for full screen sized.</param>
	/// <param name="colorFormat">The format for the color buffer. If null we'll choose the most appropriate for where you are in the pipeline.</param>
	/// <param name="depthFormat">The format for the depth buffer.</param>
	/// <param name="msaa">The number of msaa samples you'd like. Msaa render textures are a pain in the ass so you're probably gonna regret trying to use this.</param>
	/// <param name="numMips">Number of mips you want in this texture. You probably don't want this unless you want to generate mips in a second pass.</param>
	/// <param name="targetName">The optional name of the render target</param>
	/// <returns>A RenderTarget that is ready to render to.</returns>
	public static RenderTarget GetTemporary( int sizeFactor, ImageFormat colorFormat = ImageFormat.Default, ImageFormat depthFormat = ImageFormat.Default, MultisampleAmount msaa = MultisampleAmount.MultisampleNone, int numMips = 1, string targetName = "" )
	{
		if ( sizeFactor <= 0 )
			throw new ArgumentException( "cannot be lower than 1", nameof( sizeFactor ) );

		var ss = Graphics.Viewport.Size;

		if ( ss.x < 8 || ss.y < 8 )
		{
			Log.Warning( $"Really small viewport {ss}, forcing to 8x8" );
			ss = 8;
		}

		return GetTemporary( (int)(ss.x / sizeFactor), (int)(ss.y / sizeFactor), colorFormat, depthFormat, msaa, numMips, targetName );
	}

	internal void Return( RenderTarget source )
	{
		source.Loaned = false;
	}

	/// <summary>
	/// Called at the end of the frame. At this point none of the render targets that were loaned out
	/// should be being used, so we can put them all back in the pool.
	/// </summary>
	internal static void EndOfFrame()
	{
		lock ( _lock )
		{
			for ( int i = All.Count - 1; i >= 0; i-- )
			{
				All[i].FramesSinceUsed++;
				All[i].Loaned = false;

				if ( All[i].FramesSinceUsed < 8 ) continue;

				All[i].Destroy();
				All.RemoveAt( i );
			}
		}
	}

	/// <summary>
	/// Flush all the render targets out. Useful to do when screen size changes.
	/// </summary>
	internal static void Flush()
	{
		lock ( _lock )
		{
			foreach ( var entry in All )
			{
				// Defer deletion
				entry.Loaned = false;
			}
		}
	}
}
