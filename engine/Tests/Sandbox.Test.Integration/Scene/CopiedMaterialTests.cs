namespace SceneTests;

/// <summary>
/// A renderer's materials copied onto another scene object (<c>ModelRenderer.CopyMaterialsTo</c>, the first-person body's
/// shadow copies) draw on it as on the renderer's own, per index overrides too.
/// </summary>
[TestClass]
public class CopiedMaterialTests
{
	[TestMethod]
	public void PerIndexOverridesReachTheCopysDraws()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var renderer = scene.CreateObject().Components.Create<ModelRenderer>();
		renderer.Model = Model.Load( "models/citizen_human/human.vmdl" );
		renderer.Materials.SetOverride( 0, Material.Load( "materials/dev/debug_ghost_translucent.vmat" ) );

		var copy = new SceneModel( scene.SceneWorld, renderer.Model, Transform.Zero );
		try
		{
			renderer.CopyMaterialsTo( copy );

			// As native drew the copy (RenderTools::GetSceneObjectDraw), and its flags (NativeGolden)
			NativeGolden.CheckDraws( "copied/draws", copy );
			NativeGolden.Check( "copied/flags", NativeGolden.Native( copy, "flags", () => ((ulong)copy.CurrentFlags).ToString( "X" ) ), 0 );
		}
		finally
		{
			copy.Delete();
		}
	}
}
