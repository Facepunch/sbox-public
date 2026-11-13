using System;
using System.Runtime.CompilerServices;

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

	[TestMethod, TestCategory( Allocations.Category )]
	public void GizmoScopesAndModelsDoNotAllocate()
	{
		using var instance = new Gizmo.Instance();
		var targets = new object[] { new(), new(), new() };

		Allocations.AssertSettles( "a frame of gizmo scopes and models", () =>
		{
			using var frame = instance.PushScope();

			for ( int i = 0; i < targets.Length; i++ )
			{
				using var objectScope = Gizmo.ObjectScope( targets[i], Transform.Zero );
				using var named = Gizmo.Scope( "named" );
				using var numbered = Gizmo.Scope( Gizmo.NumberedName( "light-", i ) );
				using var unnamed = Gizmo.Scope();

				Gizmo.Draw.Model( Model.Cube );
			}
		} );
	}

	/// <summary>
	/// Sprites drawn from different scopes share one batched object, but each still counts as an object of its scope,
	/// so unnamed scopes after it keep the names they'd have without batching and selection doesn't jump between them.
	/// </summary>
	[TestMethod]
	public void BatchedSpritesStillNumberTheScopesAfterThem()
	{
		using var instance = new Gizmo.Instance();
		using var frame = instance.PushScope();

		using ( Gizmo.Scope( "first" ) )
		{
			Gizmo.Draw.Sprite( Vector3.Zero, 1, Texture.White );
		}

		using ( Gizmo.Scope( "second" ) )
		{
			Gizmo.Draw.Sprite( Vector3.Zero, 1, Texture.White );

			using ( Gizmo.Scope() )
			{
				Assert.AreEqual( "/second/Scope1", Gizmo.Path );
			}
		}
	}

	/// <summary>
	/// Cached scope paths must be released once their scopes stop being drawn, including after a big scene shrinks
	/// back down, not kept for the life of the instance.
	/// </summary>
	[TestMethod]
	public void ScopePathsAreReleasedAfterTheSceneShrinks()
	{
		using var instance = new Gizmo.Instance();

		var path = DrawManyScopes( instance, count: 3000, frames: 2 );

		for ( int i = 0; i < 200; i++ )
		{
			using var frame = instance.PushScope();
		}

		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();

		Assert.IsFalse( path.IsAlive, "A scope path no longer drawn is still cached" );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	static WeakReference DrawManyScopes( Gizmo.Instance instance, int count, int frames )
	{
		WeakReference first = null;

		for ( int frame = 0; frame < frames; frame++ )
		{
			using var push = instance.PushScope();

			for ( int i = 0; i < count; i++ )
			{
				using var scope = Gizmo.Scope( $"object{i}" );
				first ??= new WeakReference( Gizmo.Path );
			}
		}

		return first;
	}
}
