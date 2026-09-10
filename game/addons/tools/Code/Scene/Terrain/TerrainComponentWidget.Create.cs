namespace Editor.TerrainEditor;

partial class TerrainComponentWidget
{
	public class CreateOptions
	{
		public enum HeightMapSizes
		{
			[Title( "256x256" )] S256 = 256,
			[Title( "512x512" )] S512 = 512,
			[Title( "1024x1024" )] S1024 = 1024,
			[Title( "2048x2048" )] S2048 = 2048,
			[Title( "4096x4096" )] S4096 = 4096,
			[Title( "8192x8192" )] S8192 = 8192,
		}
	}

	static ushort[] ResampleHeightmap( Span<ushort> original, int originalSize, int newSize )
		=> TerrainImportHelper.ResampleHeightmap( original, originalSize, newSize );

	static int RoundDownToPowerOfTwo( int value )
		=> TerrainImportHelper.RoundDownToPowerOfTwo( value );
}
