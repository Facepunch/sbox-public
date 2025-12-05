using NativeEngine;
using System.Linq;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Provides functionality for rendering and filtering cubemap textures.
/// Used for environment mapping and image-based lighting in PBR rendering.
/// </summary>
internal static class CubemapRendering
{
	static ComputeShader EnvmapFilter = new( "envmap_filtering_cs" );

	/// <summary>
	/// Specifies the quality level for GGX filtering of environment maps.
	/// </summary>
	public enum GGXFilterType
	{
		/// <summary>
		/// Faster filtering with lower sample count.
		/// </summary>
		Fast,

		/// <summary>
		/// Higher quality filtering with more samples.
		/// </summary>
		Quality
	};

	/// <summary>
	/// Renders a cubemap from a scene at the specified transform position and applies filtering.
	/// </summary>
	/// <param name="world">The scene world to render.</param>
	/// <param name="cubemapTexture">The texture to render the cubemap to.</param>
	/// <param name="cubemapTransform">The position and rotation of the cubemap camera.</param>
	/// <param name="znear">The near plane distance for the camera.</param>
	/// <param name="zfar">The far plane distance for the camera.</param>
	/// <param name="filterType">The quality level for GGX filtering.</param>
	public static void Render( SceneWorld world, Texture cubemapTexture, Transform cubemapTransform, float znear, float zfar, GGXFilterType filterType )
	{
		SceneCamera camera = new SceneCamera( "CubemapRendering" );
		camera.FieldOfView = 90;
		camera.ZNear = znear;
		camera.ZFar = zfar;
		camera.Position = cubemapTransform.Position;
		camera.Rotation = cubemapTransform.Rotation;
		camera.World = world;

		// We need to filter with GGX after rendering is done so that roughness levels sample correctly.
		// SceneCameras don't abstract Command Lists directly, so we hook into the render stage for same behavior.
		//
		// We render 6 faces, so we wait until the last face is done before filtering. Once we move to multiview we don't need to count faces.
		const int numFaces = 6;
		int i = 0;

		camera.OnRenderStageHook += ( stage, camera ) =>
		{
			if ( stage != Stage.AfterPostProcess )
				return;

			i++;
			// Only filter when the last face is rendered
			if ( i < numFaces )
				return;

			// Apply filtering after rendering is done
			Filter( cubemapTexture, filterType ).ExecuteOnRenderThread();
		};


		camera.RenderToCubeTexture( cubemapTexture );
	}

	/// <summary>
	/// Renders single face of a cubemap from a scene at the specified transform position and applies filtering.
	/// </summary>
	/// <param name="world">The scene world to render.</param>
	/// <param name="cubemapTexture">The texture to render the cubemap to.</param>
	/// <param name="cubemapTransform">The position and rotation of the cubemap camera.</param>
	/// <param name="znear">The near plane distance for the camera.</param>
	/// <param name="zfar">The far plane distance for the camera.</param>
	/// <param name="faceIndex">The face index to render (0-5).</param>
	/// <param name="filterType">The quality level for GGX filtering.</param>
	public static void RenderSingleFace( SceneWorld world, Texture cubemapTexture, Transform cubemapTransform, float znear, float zfar, int faceIndex, GGXFilterType filterType )
	{
		if ( faceIndex < 0 || faceIndex >= 6 )
			return;

		SceneCamera camera = new SceneCamera( "CubemapRendering" );
		camera.FieldOfView = 90;
		camera.ZNear = znear;
		camera.ZFar = zfar;
		camera.Position = cubemapTransform.Position;
		camera.Rotation = cubemapTransform.Rotation;
		camera.World = world;

		// Only apply GGX filtering after the last face is rendered
		if ( faceIndex == 5 )
		{
			camera.OnRenderStageHook += ( stage, cam ) =>
			{
				if ( stage != Stage.AfterPostProcess )
					return;

				Filter( cubemapTexture, filterType ).ExecuteOnRenderThread();
			};
		}

		camera.RenderToCubeTextureFace( cubemapTexture, faceIndex );
	}

	/// <summary>
	/// Applies filtering to a cubemap texture, generating both downsample and GGX filtering.
	/// </summary>
	/// <param name="cubemapTexture">The cubemap texture to filter.</param>
	/// <param name="filterType">The quality level for GGX filtering.</param>
	/// <exception cref="Exception">Thrown when the cubemap texture doesn't meet requirements.</exception>
	internal static CommandList Filter( Texture cubemapTexture, GGXFilterType filterType )
	{
		if ( cubemapTexture.Depth != 6 ) throw new Exception( "Cubemap texture must have 6 faces" );
		if ( cubemapTexture.Width != cubemapTexture.Height ) throw new Exception( "Cubemap texture must be square" );
		if ( cubemapTexture.Width.IsPowerOfTwo() == false ) throw new Exception( "Cubemap texture must be power of two" );
		if ( cubemapTexture.Mips != 7 ) throw new Exception( "Cubemap texture must have 7 mip levels" );

		// Merge into a single command list for callers
		var filter = new CommandList( "Cubemap.Filter" );
		filter.InsertList( FilterDownsample( cubemapTexture ) );
		filter.InsertList( FilterGGX( cubemapTexture, filterType ) );
		return filter;
	}

	/// <summary>
	/// Downsamples a cubemap texture to generate its mip chain.
	/// </summary>
	/// <param name="cubemapTexture">The cubemap texture to downsample.</param>
	internal static CommandList FilterDownsample( Texture cubemapTexture )
	{
		var cmd = new CommandList( "Cubemap.FilterDownsample" );

		// Set static attributes once
		cmd.Attributes.Set( "DownsampleInput", cubemapTexture );
		cmd.Attributes.SetCombo( "D_PASS", 0 );

		for ( int i = 1; i < cubemapTexture.Mips; i++ )
		{
			var size = cubemapTexture.Width >> i;
			cmd.Attributes.Set( "DownsampleOutput", cubemapTexture, i );
			cmd.Attributes.Set( "Size", size );
			cmd.DispatchCompute( EnvmapFilter, size, size, 6 );

			// Barrier for this mip before next iteration using CommandList helper
			cmd.ResourceBarrierTransition( cubemapTexture, ResourceState.UnorderedAccess, i );
		}

		return cmd;
	}

	/// <summary>
	/// Applies GGX filtering to a cubemap texture for image-based lighting.
	/// This generates the pre-filtered environment map used in PBR workflows.
	/// </summary>
	/// <param name="cubemapTexture">The cubemap texture to filter.</param>
	/// <param name="filterType">The quality level for GGX filtering.</param>
	internal static CommandList FilterGGX( Texture cubemapTexture, GGXFilterType filterType )
	{
		var cmd = new CommandList( "Cubemap.FilterGGX" );

		cmd.Attributes.Set( "Source", cubemapTexture );
		cmd.Attributes.Set( "Destination1", cubemapTexture, 1 );
		cmd.Attributes.Set( "Destination2", cubemapTexture, 2 );
		cmd.Attributes.Set( "Destination3", cubemapTexture, 3 );
		cmd.Attributes.Set( "Destination4", cubemapTexture, 4 );
		cmd.Attributes.Set( "Destination5", cubemapTexture, 5 );
		cmd.Attributes.Set( "Destination6", cubemapTexture, 6 );
		cmd.Attributes.SetCombo( "D_QUALITY", (int)filterType );
		cmd.Attributes.SetCombo( "D_PASS", 1 );
		cmd.Attributes.Set( "BaseResolution", cubemapTexture.Width );

		for ( int i = 0; i < 2; i++ )
		{
			cmd.DispatchCompute( EnvmapFilter, SumOfSquaresTwo( cubemapTexture.Width / 2 ), 6, 1 );
			cmd.ResourceBarrierTransition( cubemapTexture, ResourceState.UnorderedAccess, cubemapTexture.Mips );
		}

		return cmd;
	}

	/// <summary>
	/// Calculates the sum of squares of powers of two up to n.
	/// Formula: 1² + 2² + 4² + 8² + ... + (2^k)² where 2^k ≤ n
	/// Used for determining compute shader dispatch dimensions.
	/// </summary>
	/// <param name="n">The upper limit for powers of two.</param>
	/// <returns>The sum of squares of powers of two up to n.</returns>
	internal static int SumOfSquaresTwo( int n )
	{
		int sum = 0;
		for ( int i = 1; i <= n; i *= 2 )
		{
			sum += i * i;
		}
		return sum;
	}
}
