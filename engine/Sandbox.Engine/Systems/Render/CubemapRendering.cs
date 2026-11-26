using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Provides functionality for rendering and filtering cubemap textures.
/// Used for environment mapping and image-based lighting in PBR rendering.
/// </summary>
internal static class CubemapRendering
{
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
	/// <param name="excludeTags">Objects with any of these tags will be excluded from the render.</param>
	/// <param name="renderer">The renderer to capture with, rather than the frame's (<see cref="SceneRenderer.Current"/>): for tools that compare them.</param>
	public static void Render( SceneWorld world, Texture cubemapTexture, Transform cubemapTransform, float znear, float zfar, GGXFilterType filterType, ITagSet excludeTags = null, ISceneRenderer renderer = null )
	{
		if ( !Graphics.IsAvailable )
			throw new Exception( "Tried to call CubemapRendering.Render from a dedicated server" );

		using var camera = new SceneCamera( "CubemapRendering" );
		camera.FieldOfView = 90;
		camera.ZNear = znear;
		camera.ZFar = zfar;
		camera.Position = cubemapTransform.Position;
		camera.Rotation = cubemapTransform.Rotation;
		camera.World = world;

		if ( excludeTags is not null )
		{
			camera.ExcludeTags.SetFrom( excludeTags );
		}

		// Hide In Game meshes are still visible in the editor, keep them out of bakes
		camera.ExcludeTags.Add( MeshComponent.HideInGameTag );

		if ( cubemapTexture.Depth != 6 ) throw new Exception( "Cubemap texture must have 6 faces" );
		if ( cubemapTexture.Width != cubemapTexture.Height ) throw new Exception( "Cubemap texture must be square" );
		if ( !cubemapTexture.Width.IsPowerOfTwo() ) throw new Exception( "Cubemap texture must be power of two" );
		if ( cubemapTexture.Mips != 7 ) throw new Exception( $"Cubemap texture must have 7 mip levels (this has {cubemapTexture.Mips})" );

		(renderer ?? SceneRenderer.Current).RenderToCube( camera, cubemapTexture, default, filterType );
	}
}
