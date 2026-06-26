using System.Linq;

namespace SceneTests.Prefab;

/// <summary>
/// Regression tests for Facepunch/sbox-public#11309: a prefab instance dropped a ModelRenderer
/// RenderOptions value (a get-only IJsonPopulator property) when cloned, baking a spurious override.
/// </summary>
[TestClass]
public class PrefabInstanceRenderOptionsTest
{
	static readonly string _renderOptionsPrefabSource = """"
    {
        "__guid": "b1d1e1f1-0000-4000-8000-000000000001",
        "Name": "RenderOptionsCube",
        "Position": "0,0,0",
        "Enabled": true,
        "Components": [
            {
                "__type": "ModelRenderer",
                "__guid": "b1d1e1f1-0000-4000-8000-000000000002",
                "Model": "models/dev/box.vmdl",
                "RenderType": "On",
                "RenderOptions": {
                    "GameLayer": false,
                    "OverlayLayer": false,
                    "BloomLayer": true,
                    "AfterUILayer": false
                },
                "Tint": "1,0,0,1"
            }
        ],
        "Children": []
    }
    """";

	[TestMethod]
	public void RenderOptionsSurviveGameObjectClone()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var mr = go.Components.Create<ModelRenderer>();
		mr.RenderOptions.Game = false;
		mr.RenderOptions.Bloom = true;

		var clone = go.Clone();
		var clonedMr = clone.Components.Get<ModelRenderer>();

		Assert.IsNotNull( clonedMr );
		Assert.IsFalse( clonedMr.RenderOptions.Game );
		Assert.IsTrue( clonedMr.RenderOptions.Bloom );

		go.Destroy();
		clone.Destroy();
		scene.ProcessDeletes();
	}

	[TestMethod]
	public void PrefabInstanceKeepsRenderOptionsWithoutSpuriousOverride()
	{
		var prefabLocation = "___render_options_prefab_test.prefab";

		using var prefab = SceneTests.Helpers.RegisterPrefabFromJson( prefabLocation, _renderOptionsPrefabSource );
		var pfile = ResourceLibrary.Get<PrefabFile>( prefabLocation );
		var prefabScene = SceneUtility.GetPrefabScene( pfile );

		Assert.IsFalse( prefabScene.Components.Get<ModelRenderer>().RenderOptions.Game );

		var scene = new Scene();
		using var sceneScope = scene.Push();

		var instance = prefabScene.Clone( Vector3.Zero );
		var mr = instance.Components.Get<ModelRenderer>();

		Assert.IsFalse( mr.RenderOptions.Game );
		Assert.IsTrue( mr.RenderOptions.Bloom );

		instance.PrefabInstance.RefreshPatch();

		var renderOptionsOverrides = instance.PrefabInstance.Patch.PropertyOverrides
			.Count( x => x.Property == "RenderOptions" );

		Assert.AreEqual( 0, renderOptionsOverrides );

		instance.Destroy();
		scene.ProcessDeletes();
	}
}
