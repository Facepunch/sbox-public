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
	public void LayerMatchIsKeptAsNativeKeepsIt()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = true;
		try
		{
			var so = new SceneObject( world, Model.Cube );
			Assert.AreEqual( so.native.GetLayerMatchIDValue(), so.LayerMatchToken );
			foreach ( var layer in System.Enum.GetValues<SceneRenderLayer>() )
			{
				so.RenderLayer = layer;
				Assert.AreEqual( so.native.GetLayerMatchIDValue(), so.LayerMatchToken, $"{layer}" );
			}
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}

	[TestMethod]
	public void LodOverrideIsKeptAsNativeKeepsIt()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = true;
		try
		{
			var so = new SceneObject( world, Model.Cube );
			Assert.AreEqual( so.native.GetCurrentLODLevel(), so.LodOverride );
			foreach ( var lod in new[] { 0, 3, 7, 8, -1, 2, -5 } )
			{
				so.LodOverride = lod;
				Assert.AreEqual( so.native.GetCurrentLODLevel(), so.LodOverride, $"{lod}" );
			}
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}

	[TestMethod]
	public void LightGroupAndRenderOrderAreReadOnceFromWhatNativeMade()
	{
		var world = new SceneWorld();
		SceneObject.WritesNativeOverride = false;
		try
		{
			// What native code gave the object when it made it (a map's light group and overlay order) is read once
			var so = new SceneObject( world, Model.Cube );
			so.native.GetAttributesPtrForModify().SetIntValue( new StringToken( "LightGroup" ), 5 );
			so.native.GetAttributesPtrForModify().SetIntValue( new StringToken( "RenderOrder" ), -3 );
			Assert.AreEqual( 5, so.LightGroup );
			Assert.AreEqual( -3, so.RenderOrder );

			so.native.GetAttributesPtrForModify().SetIntValue( new StringToken( "LightGroup" ), 6 );
			Assert.AreEqual( 5, so.LightGroup, "managed code owns it from then on" );
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}

	[TestMethod]
	public void AttributesAreManagedWhenNothingReadsNative()
	{
		var world = new SceneWorld();
		try
		{
			SceneObject.WritesNativeOverride = false;
			var so = new SceneObject( world, Model.Cube );
			so.Attributes.Set( "skin_tint", 0.5f );
			Assert.IsTrue( so.Attributes.IsManaged, "nothing reads native's: the set is managed only" );
			Assert.AreEqual( 0.5f, so.Attributes.GetFloat( "skin_tint" ) );
			Assert.AreSame( so.Attributes, so.Attributes.ManagedSet );

			SceneObject.WritesNativeOverride = true;
			var native = new SceneObject( world, Model.Cube );
			native.Attributes.Set( "skin_tint", 0.5f );
			Assert.IsFalse( native.Attributes.IsManaged, "native's set while something reads it" );
			Assert.AreEqual( 0.5f, native.native.GetAttributesPtrForModify().GetFloatValue( new StringToken( "skin_tint" ), 0 ) );
		}
		finally
		{
			SceneObject.WritesNativeOverride = null;
			world.Delete();
		}
	}

	/// <summary>
	/// Children move with their parent in managed code as native moves linked twins (<c>CHILD_SCENEOBJECT_INHERIT_TRANSFORM</c>),
	/// and go when it's deleted.
	/// </summary>
	[TestMethod]
	public void ChildrenMoveAsNativeMovesThem()
	{
		var world = new SceneWorld();
		try
		{
			var start = new Transform( new Vector3( 10, 20, 30 ), Rotation.From( 10, 20, 30 ), 2 );
			var childAt = new Transform( new Vector3( 50, -20, 5 ), Rotation.From( -40, 5, 90 ), 0.5f );
			var grandchildAt = new Transform( new Vector3( 0, 64, 0 ), Rotation.From( 0, 45, 0 ), 1.5f );

			// Managed: children kept here
			var parent = new SceneObject( world, Model.Cube, start );
			var child = new SceneObject( world, Model.Cube, childAt );
			var grandchild = new SceneObject( world, Model.Cube, grandchildAt );
			parent.AddChild( "child", child );
			child.AddChild( "grandchild", grandchild );
			Assert.AreSame( parent, child.Parent );

			// Native: twins linked natively, moved by native
			var nativeParent = new SceneObject( world, Model.Cube, start );
			var nativeChild = new SceneObject( world, Model.Cube, childAt );
			var nativeGrandchild = new SceneObject( world, Model.Cube, grandchildAt );
			nativeParent.native.AddChildObject( new StringToken( "child" ), nativeChild, 0x02 );
			nativeChild.native.AddChildObject( new StringToken( "grandchild" ), nativeGrandchild, 0x02 );

			foreach ( var to in new[] { new Transform( new Vector3( -100, 5, 0 ), Rotation.From( 0, 90, 0 ), 2 ), new Transform( new Vector3( 3, 4, 5 ), Rotation.From( 30, -60, 10 ), 1 ), new Transform( Vector3.Zero, Rotation.Identity, 0.25f ) } )
			{
				parent.Transform = to;
				nativeParent.native.SetTransform( to );
				Near( nativeChild.native.GetCTransform(), child.Transform, $"child, parent at {to}" );
				Near( nativeGrandchild.native.GetCTransform(), grandchild.Transform, $"grandchild, parent at {to}" );
				Near( child.Transform, child.native.GetCTransform(), "native gets the child's transform: render-mesh traces read it" );
			}

			parent.Delete();
			Assert.IsFalse( child.IsValid(), "children go with their parent" );
			Assert.IsFalse( grandchild.IsValid() );
		}
		finally
		{
			world.Delete();
		}

		static void Near( Transform expected, Transform actual, string message )
		{
			Assert.AreEqual( 0, expected.Position.Distance( actual.Position ), 0.01f, message );
			Assert.AreEqual( 0, (expected.Scale - actual.Scale).Length, 0.001f, message );
			Assert.AreEqual( 0, expected.Rotation.Distance( actual.Rotation ), 0.05f, message );
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
