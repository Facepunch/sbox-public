using Borealis.Backend;
using Borealis.Source2.Models;

namespace BorealisGpuTests;

/// <summary>
/// What a map loads goes when it's unloaded: a material's texture borrowed outside the worlds doesn't keep the material
/// (a map's sky, first read by a water body's reflection before the map's world held it, stayed: 64 MiB a cube), and a
/// model made at run time goes once no world holds it (a game's procedural model held a vertex page and a 56 MiB index page).
/// </summary>
[TestClass]
[DoNotParallelize]
public class ContentLifetimeTests : GpuTest
{
	[TestMethod]
	public void ABorrowedMaterialGoesWithTheWorldThatHeldIt()
	{
		const string sky = "materials/skybox/skybox_day_01.vmat";
		using var backend = new BackendDevice( Device );
		if ( backend.LoadedMaterialTexture( sky, "g_tSkyTexture" ) is null ) Assert.Inconclusive( "No compiled sky material" );
		var materials = backend.MaterialsFreed;
		var textures = backend.TexturesFreed;

		// A world holds it, then goes
		backend.ModelMaterial( sky );
		backend.ReleaseModelMaterial( sky );
		Assert.AreEqual( materials + 1, backend.MaterialsFreed, "the borrowed material went with the world" );
		Assert.IsTrue( backend.TexturesFreed > textures, "and its sky texture with it" );
		Device.WaitIdle();
	}

	[TestMethod]
	public void ARunTimeModelNoWorldHoldsIsFreed()
	{
		using var backend = new BackendDevice( Device ) { GpuDrivenGeometry = true };
		if ( backend.Content.TryRead( "models/dev/box.vmdl" ) is not { } data ) { Assert.Inconclusive( "models/dev/box.vmdl isn't built" ); return; }
		var mesh = BackendModel.Create( backend, ModelFile.Read( data, backend.Content.TryRead ) );
		var pages = backend.GpuDrivenTables.Bytes.Pages;
		Assert.IsTrue( pages > 0, "it's in the pages" );

		var grace = BackendDevice.ModelGraceSeconds;
		BackendDevice.ModelGraceSeconds = 0;
		try
		{
			// A world draws it, then goes: it's freed at the next frame's start, its pages with it
			backend.TrackModel( mesh );
			backend.HoldModel( mesh );
			backend.BeginFrame();
			Assert.IsFalse( ((BackendModel)mesh.Geometry).Freed, "held, it stays" );
			backend.ReleaseModel( mesh );
			backend.BeginFrame();
			Assert.IsTrue( ((BackendModel)mesh.Geometry).Freed, "let go, it's freed" );
			Assert.AreEqual( 1, backend.ModelsFreed );
			Assert.AreEqual( 0L, backend.GpuDrivenTables.Bytes.Pages, "and its pages, the ones being filled too" );
		}
		finally
		{
			BackendDevice.ModelGraceSeconds = grace;
		}

		Device.WaitIdle();
	}
}
