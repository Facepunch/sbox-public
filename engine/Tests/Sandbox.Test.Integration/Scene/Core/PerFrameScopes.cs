namespace SceneTests.Core;

/// <summary>
/// Scopes pushed every frame by the game loop and editor viewports mustn't box.
/// </summary>
[TestClass]
public class PerFrameScopesTest
{
	[TestMethod]
	public void GizmoScopeRestoresThePreviousInstance()
	{
		using var outer = new Gizmo.Instance();
		using var inner = new Gizmo.Instance();
		var before = Gizmo.Active;

		using ( outer.PushScope() )
		{
			Assert.AreSame( outer, Gizmo.Active );

			using ( inner.PushScope() )
			{
				Assert.AreSame( inner, Gizmo.Active );
			}

			Assert.AreSame( outer, Gizmo.Active );
		}

		Assert.AreSame( before, Gizmo.Active );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void GizmoScopeDoesNotAllocate()
	{
		using var instance = new Gizmo.Instance();

		Allocations.AssertSettles( "100 gizmo instance scopes", () =>
		{
			for ( int i = 0; i < 100; i++ )
			{
				using var scope = instance.PushScope();
			}
		} );
	}

	[TestMethod, TestCategory( Allocations.Category )]
	public void NullableGizmoScopeDoesNotAllocate()
	{
		using var instance = new Gizmo.Instance();
		Gizmo.Instance missing = null;

		Allocations.AssertSettles( "100 nullable gizmo instance scopes", () =>
		{
			for ( int i = 0; i < 100; i++ )
			{
				using var scope = instance?.PushScope();
				using var none = missing?.PushScope();
			}
		} );
	}
}
