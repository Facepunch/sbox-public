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

	/// <summary>
	/// A static decal skips its per frame update entirely, and still picks up its properties, its definition,
	/// the definition's textures and a swapped definition list entry.
	/// </summary>
	[TestMethod]
	public void StaticDecalSkipsUntilSomethingChanges()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var go = scene.CreateObject();
			var decal = go.AddComponent<Decal>();
			decal.Decals = [new DecalDefinition { ColorTexture = Texture.White }];
			decal.Rotation = 0;
			go.Enabled = false;
			go.Enabled = true;

			var preRender = typeof( Decal ).GetMethod( "OnPreRender", BindingFlags.Instance | BindingFlags.NonPublic );
			var sceneObject = (DecalSceneObject)typeof( Decal ).GetField( "_sceneObject", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( decal );
			void PreRender() => preRender.Invoke( decal, null );

			PreRender();
			PreRender();

			// Nothing it reads changed, so it doesn't even build its state: forgetting what it last applied doesn't make it write
			sceneObject.Color = Color.Blue;
			typeof( Decal ).GetField( "_applied", BindingFlags.Instance | BindingFlags.NonPublic ).SetValue( decal, null );
			PreRender();
			Assert.AreEqual( Color.Blue, sceneObject.Color );

			decal.ColorTint = Color.Green;
			PreRender();
			Assert.AreEqual( Color.Green, sceneObject.Color );

			decal.Decals[0].Tint = Color.Red;
			PreRender();
			Assert.AreEqual( Color.Red * Color.Green, sceneObject.Color );

			decal.Decals[0].ColorTexture = Texture.Black;
			PreRender();
			Assert.AreEqual( Texture.Black, sceneObject.ColorTexture );

			decal.Decals[0] = new DecalDefinition { ColorTexture = Texture.White, BlendMode = DecalBlendMode.Additive };
			PreRender();
			Assert.AreEqual( DecalBlendMode.Additive, sceneObject.BlendMode );
			Assert.AreEqual( Texture.White, sceneObject.ColorTexture );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// A curve evaluated per frame keeps the decal updating every frame.
	/// </summary>
	[TestMethod]
	public void DecalWithPerFrameCurveKeepsUpdating()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var go = scene.CreateObject();
			var decal = go.AddComponent<Decal>();
			decal.Decals = [new DecalDefinition { ColorTexture = Texture.White }];
			decal.ColorMix = new ParticleFloat( 0.2f, 0.8f ) { Evaluation = ParticleFloat.EvaluationType.Frame };
			go.Enabled = false;
			go.Enabled = true;

			var preRender = typeof( Decal ).GetMethod( "OnPreRender", BindingFlags.Instance | BindingFlags.NonPublic );
			var sceneObject = (DecalSceneObject)typeof( Decal ).GetField( "_sceneObject", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( decal );

			preRender.Invoke( decal, null );
			sceneObject.ColorMix = 5;
			preRender.Invoke( decal, null );
			Assert.IsTrue( sceneObject.ColorMix <= 0.8f, "A per frame curve is evaluated again every frame" );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
