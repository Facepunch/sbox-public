using Editor;
using System.Threading;

namespace Sandbox;

public partial class EnvmapProbe : ISceneCompile
{
	// A manual bake may still be saving or loading its texture when the editor clears this probe.
	int bakeGeneration;

	string BakeFilename => $"envmap/{Id.ToShortHash()}.vtex_c";

	SceneCompileWork ISceneCompile.GetWork()
	{
		return new( "Envmaps", $"Bake envmap for {GameObject.Name}",
			Mode == EnvmapProbeMode.Baked ? context => Bake( context.CancellationToken, context.Progress ) : null, Order: 20 )
		{
			Outputs = [BakeFilename],
			Clear = _ => ClearBake()
		};
	}

	void ClearBake()
	{
		bakeGeneration++;
		if ( BakedTexture is null )
			return;

		BakedTexture = null;
		Scene.Editor.MarkBakeReferencesChanged();
		UpdateSceneObject();
	}

	/// <summary>
	/// Bake this envmap now. This will stop it being dynamic if it was.
	/// </summary>
	[Order( 10000 )]
	[Button( "Bake Texture", "panorama_photosphere" ), WideMode( HasLabel = false ), HideIf( nameof( Mode ), (int)EnvmapProbeMode.CustomTexture )]
	public Task Bake( CancellationToken ct = default ) => Bake( ct, null );

	async Task Bake( CancellationToken ct, IProgressSection progress )
	{
		if ( Scene.Editor is null )
		{
			// We could probably let them bake it, but not save to disk?
			Log.Warning( "EnvmapProbe.Bake is an editor feature." );
			return;
		}

		var sceneFolder = Scene.Editor.GetSceneFolder();
		if ( sceneFolder is null )
		{
			if ( progress is not null ) throw new InvalidOperationException( "The envmap needs a saved scene." );
			return;
		}

		var generation = bakeGeneration;
		var cubemapSize = (int)Resolution;

		using var tex = Texture.CreateCube( cubemapSize, cubemapSize )
							.WithUAVBinding()
							.WithMips( 7 )
							.WithFormat( ImageFormat.RGBA16161616F )
							.Finish();

		if ( ct.IsCancellationRequested )
			return;

		if ( progress is not null ) progress.TotalCount = 3;
		RenderCubemap( tex, CubemapRendering.GGXFilterType.Quality, RenderExcludeTags );
		if ( progress is not null ) progress.Current = 1;

		if ( ct.IsCancellationRequested )
			return;

		var vtexBytes = await tex.SaveToVtexAsync();
		if ( progress is not null ) progress.Current = 2;

		if ( ct.IsCancellationRequested )
			return;

		if ( generation != bakeGeneration )
			return;

		var path = Resource.FixPath( sceneFolder.WriteFile( BakeFilename, vtexBytes ) );
		NativeEngine.g_pResourceSystem.ReloadResource( path );

		var saved = await Texture.LoadAsync( path );
		if ( generation != bakeGeneration )
			return;

		if ( !saved.IsValid() || saved.IsError )
			throw new InvalidOperationException( $"Could not load baked envmap '{path}'." );

		saved.RefreshNativeDescription();
		if ( BakedTexture != saved || Mode != EnvmapProbeMode.Baked )
		{
			if ( progress is null ) Scene.Editor.HasUnsavedChanges = true;
			else Scene.Editor.MarkBakeReferencesChanged();
		}

		BakedTexture = saved;
		Mode = EnvmapProbeMode.Baked;

		UpdateSceneObject();
		if ( progress is not null ) progress.Current = 3;
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
