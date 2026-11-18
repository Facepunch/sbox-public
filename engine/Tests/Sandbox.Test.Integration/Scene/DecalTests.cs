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
			var mirror = System.Type.GetType( "Sandbox.Rendering.Bridge.SceneMirror, Sandbox.Rendering", true );
			// A world's mirror, without the world: what the decal reads is its own settings
			mirror.GetMethod( "ApplyDecal", BindingFlags.Instance | BindingFlags.NonPublic ).Invoke( System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( mirror ), [source, decal, SceneObjectChange.Settings] );
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
			var apply = mirror.GetMethod( "ApplyDecal", BindingFlags.Instance | BindingFlags.NonPublic );
			var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( mirror );
			foreach ( var mode in new[] { DecalBlendMode.Multiply, DecalBlendMode.Additive, DecalBlendMode.Alpha } )
			{
				source.BlendMode = mode;
				apply.Invoke( instance, [source, decal, SceneObjectChange.Settings] );
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
