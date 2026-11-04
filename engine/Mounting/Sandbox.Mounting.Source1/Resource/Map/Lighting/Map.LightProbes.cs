using System;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private BspProbeTextures _probeTextures;
	internal int ProbeTextureCount => _probeTextures.Count;
	internal long ProbeTextureBytes => _probeTextures.ByteCount;

	private void BuildLightProbes( BspFile bsp, GameObject world, bool skybox = false )
	{
		_probeTextures = new BspProbeTextures( ResourcePrefix );
		new MapLightingBuilder( bsp, this, _lighting ).BuildLightProbes( world, skybox );
	}

	internal Texture CreateProbeTexture( Vector3Int counts, int depth, ImageFormat format, ReadOnlySpan<byte> data ) =>
		_probeTextures.GetOrCreate( counts, depth, format, data );
}
