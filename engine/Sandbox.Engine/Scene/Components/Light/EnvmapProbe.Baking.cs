using Editor;
using System.Threading;

namespace Sandbox;

public partial class EnvmapProbe
{
	/// <summary>
	/// Bake this envmap now. This will stop it being dynamic if it was.
	/// </summary>
	[Order( 10000 )]
	[Button( "Bake Texture", "panorama_photosphere" ), WideMode( HasLabel = false ), HideIf( nameof( Mode ), (int)EnvmapProbeMode.CustomTexture )]
	public async Task Bake( CancellationToken ct = default )
	{
		if ( Scene.Editor is null )
		{
			// We could probably let them bake it, but not save to disk?
			Log.Warning( "EnvmapProbe.Bake is an editor feature." );
			return;
		}

		var sceneFolder = Scene.Editor.GetSceneFolder();
		if ( sceneFolder is null ) return;

		var cubemapSize = (int)Resolution;

		using var tex = Texture.CreateCube( cubemapSize, cubemapSize )
							.WithUAVBinding()
							.WithMips( 7 )
							.WithFormat( ImageFormat.RGBA16161616F )
							.Finish();

		if ( ct.IsCancellationRequested )
			return;

		RenderCubemap( tex, CubemapRendering.GGXFilterType.Quality );

		if ( ct.IsCancellationRequested )
			return;

		string filename = $"/envmap/bake_{Id}.vtex_c";

		var vtexBytes = await tex.SaveToVtexAsync();

		if ( ct.IsCancellationRequested )
			return;

		var path = sceneFolder.WriteFile( filename, vtexBytes );

		BakedTexture = await Texture.LoadAsync( path );

		// Make envmap baked
		Mode = EnvmapProbeMode.Baked;

		UpdateSceneObject();
	}

	[Menu( "Editor", "Scene/Bake Envmaps", "panorama_photosphere", Priority = 1000 )]
	public static async Task BakeAll()
	{
		if ( Application.Editor is null )
			return;

		var components = Application.Editor.Scene.GetAll<EnvmapProbe>().Where( x => x.Mode == EnvmapProbeMode.Baked ).ToArray();

		await Application.Editor.ForEachAsync( components, "Baking EnvMap Probes", ( x, ct ) => x.Bake( ct ) );
	}
}
