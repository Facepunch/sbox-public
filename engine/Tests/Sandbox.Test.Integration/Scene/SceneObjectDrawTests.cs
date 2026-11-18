namespace SceneTests;

/// <summary>
/// A scene object's draws worked out in managed code (<c>SceneObject.GetDraws</c>: body groups, material groups, whole and
/// attribute material overrides, each draw's translucency and shadow fast path) are native's
/// (<c>RenderTools::GetSceneObjectDraw</c>) after every change.
/// </summary>
[TestClass]
public class SceneObjectDrawTests
{
	static readonly System.Reflection.MethodInfo Compare = System.Type.GetType( "Sandbox.Rendering.Bridge.SceneNative, Sandbox.Rendering", true ).GetMethod( "CompareDraws" );

	static void Same( SceneObject so, string after )
	{
		Assert.IsTrue( so.ResolvesDraws, "made by managed code" );
		var differences = (System.Collections.Generic.List<string>)Compare.Invoke( null, [so] );
		Assert.AreEqual( 0, differences.Count, $"after {after}:\n{string.Join( "\n", differences )}" );
	}

	[TestMethod]
	public void DrawsAreNativesAfterEveryChange()
	{
		var world = new SceneWorld();
		try
		{
			var human = Model.Load( "models/citizen_human/human.vmdl" );
			var white = Material.Load( "materials/default/white.vmat" );
			var glass = Material.Load( "materials/dev/debug_ghost_translucent.vmat" );

			foreach ( var so in new SceneObject[] { new SceneObject( world, human ), new SceneModel( world, human, Transform.Zero ) } )
			{
				Same( so, "creation" );

				so.MeshGroupMask = 1;
				Same( so, "mesh group mask 1" );
				so.MeshGroupMask = ulong.MaxValue;
				Same( so, "every mesh group" );

				so.SetMaterialOverride( white, "skin" );
				Same( so, "a skin override" );
				so.SetMaterialOverride( glass, "skin" );
				Same( so, "a translucent skin override" );
				so.SetMaterialOverride( glass, "eyes", 1 );
				Same( so, "an eyes override" );
				so.SetMaterialOverride( null, "skin" );
				Same( so, "the skin override removed" );

				for ( int g = 0; g < human.MaterialGroupCount; g++ )
				{
					so.SetMaterialGroup( human.GetMaterialGroupName( g ) );
					Same( so, $"material group {g}" );
				}

				so.SetMaterialGroup( "no such group" );
				Same( so, "a missing material group" );

				so.SetMaterialOverride( glass );
				Same( so, "a whole translucent override" );
				so.SetMaterialGroup( human.MaterialGroupCount > 0 ? human.GetMaterialGroupName( 0 ) : "default" );
				Same( so, "a material group after a whole override" );
				so.SetMaterialOverride( white );
				so.SetMaterialOverride( null );
				Same( so, "the whole override removed" );

				so.ClearMaterialOverride();
				Same( so, "overrides cleared" );

				so.SetMaterialOverride( white, "skin" );
				so.Model = Model.Load( "models/dev/box.vmdl" );
				Same( so, "a new model" );
				so.Model = human;
				Same( so, "the first model again" );
			}

			// Native works a shared mesh's flags out again in place when an object drawing it without draw data of its own changes
			// material, for every object drawing it
			var a = new SceneObject( world, human );
			var b = new SceneObject( world, human );
			b.SetMaterialOverride( glass, "skin" );
			Same( a, "another object's attribute override" );
			Same( b, "its attribute override" );
			a.SetMaterialOverride( white );
			b.SetMaterialOverride( null, "skin" );
			Same( a, "its whole override, and another's override removed" );
			Same( b, "its override removed" );
		}
		finally
		{
			world.Delete();
		}
	}
}
