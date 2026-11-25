using Sandbox.Utility;
namespace Editor;

public static partial class EditorUtility
{
	/// <summary>
	/// Render a fresh model spawnicon and return its bake-cache key and encoded PNG.
	/// </summary>
	public static async Task<(string Key, byte[] Png)> RenderSpawnIconAsync( string modelPath )
	{
		using var icon = await Model.RenderSpawnIconAsync( modelPath, forBake: true );
		if ( icon is null ) throw new System.IO.InvalidDataException( $"Could not render model '{modelPath}'." );

		return (ThumbnailCache.GetBakedModelKey( modelPath ), icon.ToPng());
	}

	/// <summary>
	/// Load a float bitmap. This is usually a png, tga, exr, psd
	/// </summary>
	public static FloatBitmap LoadBitmap( string filename )
	{
		var bm = FloatBitMap_t.Create();

		if ( !bm.LoadFromFile( filename, FBMGammaType_t.FBM_GAMMA_LINEAR ) )
		{
			bm.Delete();
			return default;
		}

		return new FloatBitmap( bm );
	}
}
