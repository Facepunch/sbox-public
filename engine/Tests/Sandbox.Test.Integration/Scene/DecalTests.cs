using Sandbox.Rendering;
using System.Reflection;

namespace SceneTests;

[TestClass]
public class DecalTests
{
	[TestMethod]
	public void DefinitionsDefaultToAlphaBlending()
	{
		var definition = new DecalDefinition();
		Assert.AreEqual( DecalBlendMode.Alpha, definition.BlendMode );
		Assert.IsTrue( typeof( DecalBlendMode ).GetCustomAttributesData().Any( attribute => attribute.AttributeType.Name == "ExposeAttribute" ) );
	}

	[TestMethod]
	public void SceneObjectPublishesBlendModeWithoutChangingNativeLayout()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var decal = new DecalSceneObject( scene.SceneWorld );
		Assert.AreEqual( DecalBlendMode.Alpha, decal.BlendMode );
		decal.BlendMode = DecalBlendMode.Multiply;
		Assert.AreEqual( DecalBlendMode.Multiply, decal.BlendMode );
		decal.BlendMode = DecalBlendMode.Alpha;
		Assert.AreEqual( DecalBlendMode.Alpha, decal.BlendMode );
		decal.Delete();
		scene.Destroy();
	}

	[TestMethod]
	public void ManagedRendererMirrorsBlendModeChanges()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var source = new DecalSceneObject( scene.SceneWorld );
			var decal = new Borealis.DecalObject( Transform.Zero );
			var mirror = System.Type.GetType( "Sandbox.Rendering.Bridge.SceneMirror, Sandbox.Rendering", true );
			var apply = mirror.GetMethod( "ApplyDecal", BindingFlags.Static | BindingFlags.NonPublic );
			foreach ( var mode in new[] { DecalBlendMode.Multiply, DecalBlendMode.Additive, DecalBlendMode.Alpha } )
			{
				source.BlendMode = mode;
				apply.Invoke( null, [source, decal, SceneObjectChange.Settings] );
				Assert.AreEqual( (Borealis.DecalBlendMode)mode, decal.BlendMode );
			}
			source.Delete();
		}
		finally
		{
			scene.Destroy();
		}
	}
}
