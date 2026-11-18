namespace SceneTests;

/// <summary>
/// A scene object's tags, visibility and tint are kept in managed code, read as native would give them back. Tags and
/// visibility still reach native, which render-mesh traces filter by; the tint only while something reads native.
/// </summary>
[TestClass]
public class SceneObjectStateTests
{
	[TestMethod]
	public void TintReadsBackAsNativeKeepsIt()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = true;
		try
		{
			var so = new SceneObject( world, Model.Cube );
			foreach ( var tint in new[] { Color.White, new Color( 0.5f, 0.25f, 0.1f, 0.5f ), new Color( 2.0f, 1.5f, 0.0f, 1.5f ), new Color( -0.1f, 0.999f, 0.004f, -1 ) } )
			{
				so.ColorTint = tint;
				Assert.AreEqual( (Color)so.native.GetTintRGBA(), so.ColorTint, $"{tint}" );
			}

			SceneObject.WritesNativeOverride = false;
			so.ColorTint = Color.Red;
			Assert.AreEqual( Color.Red, so.ColorTint );
			Assert.AreNotEqual( Color.Red, (Color)so.native.GetTintRGBA(), "the tint isn't written while nothing reads native" );
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}

	[TestMethod]
	public void TagsAndVisibilityStayManagedAndReachNative()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = false;
		try
		{
			// An object native code made and tagged before anything managed asks: its tags are read once
			var so = NativeEngine.CSceneSystem.CreateSkyBox( Material.Load( "materials/skybox/skybox_day_01.vmat" ).native, world );
			so.native.AddTag( StringToken.FindOrCreate( "world" ) );
			Assert.IsTrue( so.Tags.Has( "world" ) );

			so.Tags.Add( "a" );
			so.Tags.Add( "b" );
			so.Tags.Add( "a" );
			so.Tags.Remove( "world" );
			CollectionAssert.AreEqual( new[] { StringToken.FindOrCreate( "a" ), StringToken.FindOrCreate( "b" ) }, so.TagTokens.ToArray() );

			var native = Enumerable.Range( 0, so.native.GetTagCount() ).Select( so.native.GetTagAt ).ToArray();
			CollectionAssert.AreEqual( so.TagTokens.ToArray(), native, "native gets every tag change: render-mesh traces filter by them" );

			so.RenderingEnabled = false;
			Assert.IsFalse( so.RenderingEnabled );
			Assert.IsFalse( so.native.IsRenderingEnabled(), "native gets visibility: render-mesh traces skip hidden objects" );
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}
}
