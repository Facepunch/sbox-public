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
			var decal = new Sandbox.SceneRenderer.DecalObject( Transform.Zero );
			var mirror = typeof( Sandbox.SceneRenderer.DecalObject ).Assembly.GetType( "Sandbox.SceneRenderer.Bridge.SceneMirror", true );
			var apply = mirror.GetMethod( "ApplyDecal", BindingFlags.Static | BindingFlags.NonPublic );
			foreach ( var mode in new[] { DecalBlendMode.Multiply, DecalBlendMode.Additive, DecalBlendMode.Alpha } )
			{
				source.BlendMode = mode;
				apply.Invoke( null, [source, decal, SceneObjectChange.Settings] );
				Assert.AreEqual( mode, decal.BlendMode );
			}
			source.Delete();
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void SceneObjectFollowsChangesAfterSettling()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var go = scene.CreateObject();
			var decal = go.AddComponent<Decal>();
			decal.Decals = [new DecalDefinition()];
			decal.Rotation = 0;
			go.Enabled = false;
			go.Enabled = true;

			var preRender = typeof( Decal ).GetMethod( "OnPreRender", BindingFlags.Instance | BindingFlags.NonPublic );
			var sceneObject = (DecalSceneObject)typeof( Decal ).GetField( "_sceneObject", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( decal );
			void PreRender() => preRender.Invoke( decal, null );

			PreRender();
			PreRender();
			Assert.AreEqual( Vector3.Zero, sceneObject.Transform.Position );

			go.WorldPosition = new Vector3( 10, 20, 30 );
			PreRender();
			Assert.AreEqual( new Vector3( 10, 20, 30 ), sceneObject.Transform.Position );

			decal.ColorTint = Color.Red;
			PreRender();
			Assert.AreEqual( Color.Red, sceneObject.Color );

			decal.Decals[0].BlendMode = DecalBlendMode.Multiply;
			PreRender();
			Assert.AreEqual( DecalBlendMode.Multiply, sceneObject.BlendMode );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
