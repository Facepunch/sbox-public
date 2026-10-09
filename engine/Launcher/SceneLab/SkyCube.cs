namespace Sandbox.SceneLab;

/// <summary>
/// A small sky cubemap made at runtime: blue overhead, pale at the horizon, dark below, warmer toward +x
/// and cooler toward -x - so a fog or reflection that samples it the wrong way round shows. Core's cubemaps
/// are cube arrays, which cubemap fog can't sample.
/// </summary>
internal static class SkyCube
{
	const int Size = 64;

	public static Texture Create()
	{
		var data = new byte[Size * Size * 4 * 6];
		var i = 0;

		for ( int face = 0; face < 6; face++ )
		{
			for ( int y = 0; y < Size; y++ )
			{
				for ( int x = 0; x < Size; x++ )
				{
					var u = (x + 0.5f) / Size * 2 - 1;
					var v = (y + 0.5f) / Size * 2 - 1;
					var color = Sky( Direction( face, u, v ).Normal );

					var c = color.ToColor32();
					data[i++] = c.r;
					data[i++] = c.g;
					data[i++] = c.b;
					data[i++] = 255;
				}
			}
		}

		return Texture.CreateCube( Size, Size, ImageFormat.RGBA8888 ).WithData( data ).WithName( "scenelab_sky" ).Finish();
	}

	/// <summary>
	/// The direction a texel of a face looks along, in the cube's standard face order (+x, -x, +y, -y, +z, -z).
	/// </summary>
	static Vector3 Direction( int face, float u, float v ) => face switch
	{
		0 => new Vector3( 1, -v, -u ),
		1 => new Vector3( -1, -v, u ),
		2 => new Vector3( u, 1, v ),
		3 => new Vector3( u, -1, -v ),
		4 => new Vector3( u, -v, 1 ),
		_ => new Vector3( -u, -v, -1 ),
	};

	static Color Sky( Vector3 direction )
	{
		var horizon = new Color( 0.75f, 0.78f, 0.8f );
		var zenith = new Color( 0.25f, 0.45f, 0.85f );
		var ground = new Color( 0.22f, 0.2f, 0.18f );

		var sky = direction.z >= 0 ? Color.Lerp( horizon, zenith, direction.z ) : Color.Lerp( horizon, ground, MathF.Min( -direction.z * 4, 1 ) );

		// Warm toward +x, cool toward -x
		var warmth = direction.x * 0.15f;
		return new Color( sky.r + warmth, sky.g, sky.b - warmth );
	}
}
