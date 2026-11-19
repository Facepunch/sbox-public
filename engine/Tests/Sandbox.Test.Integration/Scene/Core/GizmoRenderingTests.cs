using System;

namespace SceneTests.Core;

[TestClass, DoNotParallelize]
public class GizmoRenderingTests
{
	[TestInitialize]
	public void Initialize() => ThreadSafe.MarkMainThread();

	[TestMethod]
	public void PublicScopeSignaturesRemainCompatible()
	{
		Assert.AreEqual( typeof( IDisposable ), typeof( Gizmo ).GetMethod( "Scope", [typeof( string )] ).ReturnType );
		Assert.AreEqual( typeof( IDisposable ), typeof( Gizmo.Instance ).GetMethod( "Push" ).ReturnType );
		Assert.AreEqual( typeof( IDisposable ), typeof( Gizmo.GizmoHitbox ).GetMethod( "LineScope" ).ReturnType );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ObjectAndAutomaticScopePathsHaveSeparateCacheEntries( bool objectFirst )
	{
		using var instance = new Gizmo.Instance();
		for ( var i = 0; i < 2; ++i )
		{
			var isObject = i == 0 ? objectFirst : !objectFirst;
			instance.scope = default;
			instance.SetScopePath( null, 0, objectScope: isObject );
			var expected = isObject ? "object-0" : "/Scope0";
			Assert.AreEqual( expected, instance.scope.Path );
			Assert.AreEqual( Gizmo.HashString( expected ), instance.scope.PathHash );
		}
	}

	[TestMethod]
	public void BatchingPreservesAutomaticScopeNumbers()
	{
		using var instance = new Gizmo.Instance();
		using var frame = instance.Push();
		using ( Gizmo.Scope( "first" ) ) Gizmo.Draw.Sprite( 0, 1, Texture.White );
		using ( Gizmo.Scope( "second" ) )
		{
			Gizmo.Draw.Sprite( 0, 1, Texture.White );
			using var scope = Gizmo.Scope();
			Assert.AreEqual( "/second/Scope1", Gizmo.Path );
		}
	}

	[TestMethod]
	public void ModelsDoNotSplitSpritesFromTheirSeparateOverlayLayer()
	{
		using var instance = new Gizmo.Instance();
		using ( instance.Push() )
		{
			Gizmo.Draw.Sprite( 0, 1, Texture.White );
			Gizmo.Draw.Model( Model.Cube );
			Gizmo.Draw.Color = Color.Red;
			Gizmo.Draw.Sprite( Vector3.Up, 1, Texture.White );
		}
		Assert.AreEqual( 2, instance.World.SceneObjects.Count );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void LargeSphereBuffersSettleWithoutAllocating()
	{
		using var instance = new Gizmo.Instance();
		Allocations.AssertSettles( "uncached sphere resolutions", () =>
		{
			using var frame = instance.Push();
			Gizmo.Draw.LineSphere( Vector3.Zero, 16, 129 );
			Gizmo.Draw.SolidSphere( Vector3.Zero, 16, 129, 129 );
		} );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void StableScopePathsAndLineScopesDoNotAllocate()
	{
		using var instance = new Gizmo.Instance();
		var owner = new object();
		Allocations.AssertSettles( "named, automatic, object and line scopes", () =>
		{
			using var frame = instance.Push();
			for ( var i = 0; i < 100; ++i )
			{
				using var obj = Gizmo.ObjectScope( owner, Transform.Zero );
				using var named = Gizmo.Scope( "bounds" );
				using var automatic = Gizmo.Scope();
				using var lines = Gizmo.Hitbox.LineScope();
			}
		} );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void DeepPublicScopesDoNotAllocateAfterWarmup()
	{
		using var instance = new Gizmo.Instance();
		var scopes = new IDisposable[512];
		Allocations.AssertSettles( "512 nested public scopes", () =>
		{
			using var frame = instance.Push();
			for ( var i = 0; i < scopes.Length; ++i ) scopes[i] = Gizmo.Scope( "nested" );
			for ( var i = scopes.Length - 1; i >= 0; --i ) { scopes[i].Dispose(); scopes[i] = null; }
		} );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void DrawingStableObjectsDoesNotAllocate()
	{
		using var instance = new Gizmo.Instance();
		var objects = Enumerable.Range( 0, 100 ).Select( _ => new object() ).ToArray();
		Allocations.AssertSettles( "100 object bounding boxes", () =>
		{
			using var frame = instance.Push();
			for ( var i = 0; i < objects.Length; ++i )
			{
				using var scope = Gizmo.ObjectScope( objects[i], new Transform( Vector3.Left * i ) );
				Gizmo.Draw.LineBBox( BBox.FromPositionAndSize( 0, 16 ) );
			}
		} );
		Assert.AreEqual( 1, instance.World.SceneObjects.Count );
	}

	[TestMethod]
	public void CompatibleGeometryBatchesAcrossObjectScopes()
	{
		using var instance = new Gizmo.Instance();
		using ( instance.Push() )
		{
			for ( var i = 0; i < 20; ++i )
			{
				using var scope = Gizmo.ObjectScope( new object(), new Transform( Vector3.Left * i ) );
				Gizmo.Draw.LineBBox( BBox.FromPositionAndSize( 0, 16 ) );
			}
		}
		Assert.AreEqual( 1, instance.World.SceneObjects.Count );
		using ( instance.Push() ) { }
		Assert.AreEqual( 0, instance.World.SceneObjects.Count( x => x.RenderingEnabled ) );
		for ( var i = 0; i < 65; ++i ) { using var frame = instance.Push(); }
		Assert.AreEqual( 0, instance.World.SceneObjects.Count );
	}

	[TestMethod]
	public void RenderStateChangesSplitBatches()
	{
		using var instance = new Gizmo.Instance();
		using ( instance.Push() )
		{
			Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( 0, 16 ) );
			Gizmo.Draw.CullBackfaces = true;
			Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( 0, 16 ) );
			Gizmo.Draw.IgnoreDepth = true;
			Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( 0, 16 ) );
		}
		Assert.AreEqual( 3, instance.World.SceneObjects.Count );
	}

	[TestMethod]
	public void NestedInstancesKeepTheirOwnGeometry()
	{
		using var outer = new Gizmo.Instance();
		using var inner = new Gizmo.Instance();
		using ( outer.Push() )
		{
			using var scope = Gizmo.Scope( "outer", Vector3.Left );
			Gizmo.Draw.Line( 0, Vector3.Up );
			using ( inner.Push() ) Gizmo.Draw.Line( 0, Vector3.Forward );
			Assert.AreEqual( "/outer", Gizmo.Path );
			Assert.AreEqual( Vector3.Left, Gizmo.Transform.Position );
			Gizmo.Draw.Line( 0, Vector3.Down );
		}
		Assert.AreEqual( 2, outer.World.SceneObjects.Count );
		Assert.AreEqual( 1, inner.World.SceneObjects.Count );
	}

	[TestMethod]
	public void AutomaticScopeNamesAndStateAreRestored()
	{
		using var instance = new Gizmo.Instance();
		using var frame = instance.Push();
		using ( Gizmo.Scope() ) Assert.AreEqual( "/Scope0", Gizmo.Path );
		using ( Gizmo.Scope() ) Assert.AreEqual( "/Scope1", Gizmo.Path );
		using ( Gizmo.Scope( null, Transform.Zero ) ) Assert.AreEqual( "/", Gizmo.Path );
		Assert.AreEqual( "", Gizmo.Path );
		Assert.AreEqual( Transform.Zero, Gizmo.Transform );
	}

	[TestMethod]
	public void TextSeparatesGeometrySubmissions()
	{
		using var instance = new Gizmo.Instance();
		var draw = Gizmo.Draw;
		using ( instance.Push() )
		{
			draw.Line( 0, Vector3.Up );
			draw.ScreenText( "between lines", 0 );
			draw.Line( 0, Vector3.Down );
		}
		Assert.AreEqual( 3, instance.World.SceneObjects.Count );
	}

	[TestMethod]
	public void FlushHidesUnusedGeometryBeforeCameraSubmission()
	{
		using var instance = new Gizmo.Instance();
		using ( instance.Push() )
		{
			Gizmo.Draw.Line( 0, Vector3.Up );
			Gizmo.Draw.SolidBox( BBox.FromPositionAndSize( 0, 16 ) );
		}
		using ( instance.Push() )
		{
			Gizmo.Draw.Line( 0, Vector3.Up );
			Gizmo.Draw.End();
			Assert.AreEqual( 1, instance.World.SceneObjects.Count( x => x.RenderingEnabled ) );
		}
	}

	[TestMethod]
	public void RetainedBuffersOnlyUploadChangedVertices()
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );
		var world = new SceneWorld();
		var batch = new VertexSceneObject( world )
		{
			State = new( Graphics.PrimitiveType.Lines, Material.Load( "materials/gizmo/line.vmat" ), null, false, false, 1, Color.White ),
			Transform = Transform.Zero
		};
		try
		{
			Submit( Vector3.Up );
			Assert.AreEqual( 1, batch.UploadCount );
			Submit( Vector3.Up );
			Assert.AreEqual( 1, batch.UploadCount );
			batch.Transform = new Transform( Vector3.Left );
			Submit( Vector3.Up );
			Assert.AreEqual( 1, batch.UploadCount );
			Submit( Vector3.Down );
			Assert.AreEqual( 2, batch.UploadCount );
			batch.Write();
			Assert.IsFalse( batch.RenderingEnabled );
			Submit( Vector3.Down );
			Assert.IsTrue( batch.RenderingEnabled );
			Assert.AreEqual( 1, world.SceneObjects.Count );
		}
		finally
		{
			batch.Delete();
			world.Delete();
		}

		void Submit( Vector3 end )
		{
			batch.Vertices.Add( new Vertex( Vector3.Zero, Color.White ) );
			batch.Vertices.Add( new Vertex( end, Color.White ) );
			batch.Write();
		}
	}
}
