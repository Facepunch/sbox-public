using Sandbox.Utility;
using System.Threading;

namespace Sandbox.Resources;

[Title( "Random Noise" )]
[Icon( "casino" )]
[ClassName( "random" )]
[Expose]
public class RandomTextureGenerator : TextureGenerator
{
	public enum NoiseType
	{
		[Icon( "casino" )]
		[Title( "Pure Random" )]
		Random,

		[Icon( "gradient" )]
		[Title( "Perlin Noise" )]
		Perlin,

		[Icon( "gradient" )]
		[Title( "Simplex Noise" )]
		Simplex
	}

	public NoiseType Type { get; set; } = NoiseType.Random;

	public int Seed { get; set; } = 0;

	public Vector2Int Size { get; set; } = 64;

	[HideIf( nameof( Type ), NoiseType.Random )]
	public Vector3 Offset { get; set; } = new Vector3( 0, 0, 0 );

	[HideIf( nameof( Type ), NoiseType.Random )]
	public float Scale { get; set; } = 5.0f;

	[Range( 1, 8 )]
	[HideIf( nameof( Type ), NoiseType.Random )]
	public int Octaves { get; set; } = 1;

	public Gradient Gradient { get; set; } = new Gradient( new Gradient.ColorFrame( 0.0f, Color.Black ), new Gradient.ColorFrame( 1.0f, Color.White ) );

	[Header( "Normal Map" ), Title( "Height to Normal" )]
	public bool ConvertHeightToNormals { get; set; }

	[ShowIf( nameof( ConvertHeightToNormals ), true )]
	public float NormalScale { get; set; } = 1;

	[Hide]
	public override bool CacheToDisk => true;

	protected override async ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		var w = Size.x.Clamp( 1, 1024 * 4 );
		var h = Size.y.Clamp( 1, 1024 * 4 );
		var scale = (Type != NoiseType.Random) ? Scale : 1f;
		var pixels = new Color[w * h];
		long seed = Seed;
		INoiseField noise = null;

		switch ( Type )
		{
			case NoiseType.Perlin:
				if ( Octaves > 1 )
				{
					// Use fractal noise field for multiple octaves
					noise = Noise.PerlinField( new Noise.FractalParameters(
						Seed: Seed,
						Octaves: Octaves
					) );
				}
				else
				{
					noise = Noise.PerlinField( new Noise.Parameters( Seed ) );
				}
				break;
			case NoiseType.Simplex:
				if ( Octaves > 1 )
				{
					// Use fractal noise field for multiple octaves
					noise = Noise.SimplexField( new Noise.FractalParameters(
						Seed: Seed,
						Octaves: Octaves
					) );
				}
				else
				{
					noise = Noise.SimplexField( new Noise.Parameters( Seed ) );
				}
				break;
			default:
				seed = (long)(IntToRandomFloat( Seed ) * int.MaxValue);
				break;
		}

		await Sandbox.Utility.Parallel.ForAsync( 0, w * h, ct, ( i, t ) =>
		{
			var x = i % w;
			var y = i / w;
			var noiseValue = 0f;
			if ( noise is not null )
			{
				noiseValue = noise.Sample( (x + Offset.x) * scale, (y + Offset.y) * scale, Offset.z * scale );
			}
			else
			{
				noiseValue = IntToRandomFloat( seed + i );
			}
			pixels[i] = Gradient.Evaluate( noiseValue );
			return ValueTask.CompletedTask;
		} );

		using var bitmap = new Bitmap( w, h, false );
		bitmap.SetPixels( pixels );

		using var normalMap = ConvertHeightToNormals ? bitmap.HeightmapToNormalMap( NormalScale ) : null;
		var target = normalMap ?? bitmap;

		ct.ThrowIfCancellationRequested();

		return target.ToTexture();
	}

	public static float IntToRandomFloat( long seed )
	{
		unchecked
		{
			// Multiply the seed by a large prime number
			seed = (seed ^ 0x6D2B79F5) * 0x1B873593;
			seed = (seed ^ (seed >> 13)) * 0x85EBCA6B;
			seed = seed ^ (seed >> 16);

			// Normalize to a float in the range [0, 1)
			return (seed & 0x7FFFFFFF) / (float)int.MaxValue;
		}
	}
}
