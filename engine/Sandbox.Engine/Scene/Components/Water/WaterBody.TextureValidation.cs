using NativeEngine;

namespace Sandbox;

public sealed partial class WaterBody
{
	/// <summary>
	/// Require the actual image dimension consumed by water's Texture2D shaders, including single-slice inputs.
	/// </summary>
	static bool IsTexture2D( Texture texture ) => texture is { IsValid: true, IsError: false, Depth: 1 }
		&& !texture.Desc.IsCube && !texture.Desc.IsArray
		&& (texture.Desc.m_nFlags & RuntimeTextureSpecificationFlags.TSPEC_VOLUME_TEXTURE) == 0;
}
