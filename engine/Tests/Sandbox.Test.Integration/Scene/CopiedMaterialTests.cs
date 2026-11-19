namespace SceneTests;

/// <summary>
/// A renderer's materials copied onto another scene object (<c>ModelRenderer.CopyMaterialsTo</c>, the first-person body's
/// shadow copies) draw on it as on the renderer's own, per index overrides too.
/// </summary>
[TestClass]
public class CopiedMaterialTests
{
	static readonly System.Reflection.MethodInfo Compare = System.Type.GetType( "Sandbox.Rendering.Bridge.SceneNative, Sandbox.Rendering", true ).GetMethod( "CompareDraws" );

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

			var differences = (System.Collections.Generic.List<string>)Compare.Invoke( null, [copy] );
			Assert.AreEqual( 0, differences.Count, string.Join( "\n", differences ) );
			Assert.AreEqual( "", copy.CompareFlags(), "flags" );
		}
		finally
		{
			copy.Delete();
		}
	}
}
