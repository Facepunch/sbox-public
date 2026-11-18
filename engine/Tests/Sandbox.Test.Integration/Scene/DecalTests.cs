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

	/// <summary>
	/// A decal's settings are kept in managed code, start as native's defaults, and reach the native twin only while
	/// something in the process reads it (<see cref="SceneObject.WritesNative"/>). The renderer reads the managed ones.
	/// </summary>
	[TestMethod]
	public void SettingsAreManagedAndReachTheNativeTwinOnlyWhenRead()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var source = new DecalSceneObject( scene.SceneWorld );
			var native = (NativeEngine.CDecalSceneObject)source.native;
			Assert.AreEqual( (Color)native.m_vColorTint, source.Color, "starts as native's default" );
			Assert.IsNull( source.ColorTexture );

			SceneObject.WritesNativeOverride = true;
			source.ColorTexture = Texture.White;
			source.Color = Color.Red;
			source.SortOrder = 7;
			source.CoverageRange = 0.5f;
			Assert.AreSame( Texture.White, Texture.FromNative( native.m_hColor ), "written through while read" );
			Assert.AreEqual( new Vector4( 1, 0, 0, 1 ), native.m_vColorTint );
			Assert.AreEqual( 7u, native.m_nSortOrder );
			Assert.AreEqual( 0.5f, native.m_flCoverageRange );

			SceneObject.WritesNativeOverride = false;
			source.ColorTexture = null;
			source.Color = Color.Blue;
			source.SortOrder = 9;
			Assert.IsNull( source.ColorTexture );
			Assert.AreEqual( Color.Blue, source.Color );
			Assert.AreEqual( 9u, source.SortOrder );
			Assert.AreSame( Texture.White, Texture.FromNative( native.m_hColor ), "not written while nothing reads it" );
			Assert.AreEqual( 7u, native.m_nSortOrder );

			var decal = new Borealis.DecalObject( Transform.Zero );
			using var mirror = NewMirror( scene.SceneWorld );
			mirror.GetType().GetMethod( "ApplyDecal", BindingFlags.Instance | BindingFlags.NonPublic ).Invoke( mirror, [source, decal, SceneObjectChange.Settings] );
			Assert.AreEqual( Color.Blue, decal.Tint, "the renderer reads the managed settings" );
			Assert.AreEqual( 9u, decal.SortOrder );
			Assert.AreEqual( 0.5f, decal.CoverageRange );
			source.Delete();
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			scene.Destroy();
		}
	}

	/// <summary>A mirror of <paramref name="world"/> (it holds the textures its decals draw with), disposed by the caller.</summary>
	static System.IDisposable NewMirror( SceneWorld world )
		=> (System.IDisposable)System.Activator.CreateInstance( System.Type.GetType( "Sandbox.Rendering.Bridge.SceneMirror, Sandbox.Rendering", true ), [world] );

	[TestMethod]
	public void ManagedRendererMirrorsBlendModeChanges()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		try
		{
			var source = new DecalSceneObject( scene.SceneWorld );
			var decal = new Borealis.DecalObject( Transform.Zero );
			using var mirror = NewMirror( scene.SceneWorld );
			var apply = mirror.GetType().GetMethod( "ApplyDecal", BindingFlags.Instance | BindingFlags.NonPublic );
			foreach ( var mode in new[] { DecalBlendMode.Multiply, DecalBlendMode.Additive, DecalBlendMode.Alpha } )
			{
				source.BlendMode = mode;
				apply.Invoke( mirror, [source, decal, SceneObjectChange.Settings] );
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
