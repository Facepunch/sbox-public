using System;

namespace SceneTests.GameObjects;

/// <summary>
/// Pins the world/local transform math of the hierarchy: how positions, rotations
/// and scales compose through parents, and the guards on the setters.
/// </summary>
[TestClass]
[DoNotParallelize]
public class TransformMathTest : SceneTest
{
	/// <summary>
	/// A child's world position is its local position offset by the parent.
	/// </summary>
	[TestMethod]
	public void TranslationComposes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.WorldPosition = new Vector3( 100, 200, 300 );

		var child = new GameObject( parent );
		child.LocalPosition = new Vector3( 10, 20, 30 );

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 110, 220, 330 ) ), $"{child.WorldPosition}" );
	}

	/// <summary>
	/// A rotated parent rotates its children's local offsets: yaw 90 maps local +x
	/// onto world +y.
	/// </summary>
	[TestMethod]
	public void RotationComposes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.WorldPosition = new Vector3( 100, 0, 0 );
		parent.WorldRotation = Rotation.FromYaw( 90 );

		var child = new GameObject( parent );
		child.LocalPosition = new Vector3( 10, 0, 0 );

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 100, 10, 0 ), 0.001f ), $"{child.WorldPosition}" );

		// The child's world rotation inherits the parent's
		Assert.IsTrue( child.WorldRotation.Forward.AlmostEqual( new Vector3( 0, 1, 0 ), 0.001f ) );
	}

	/// <summary>
	/// A scaled parent scales its children's local offsets and their world scale.
	/// </summary>
	[TestMethod]
	public void ScaleComposes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.LocalScale = new Vector3( 2 );

		var child = new GameObject( parent );
		child.LocalPosition = new Vector3( 10, 0, 0 );

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 20, 0, 0 ) ), $"{child.WorldPosition}" );
		Assert.IsTrue( child.WorldScale.AlmostEqual( new Vector3( 2 ) ), $"{child.WorldScale}" );
	}

	/// <summary>
	/// Non-uniform parent scale applies per component to child offsets.
	/// </summary>
	[TestMethod]
	public void NonUniformScaleComposes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.LocalScale = new Vector3( 2, 1, 3 );

		var child = new GameObject( parent );
		child.LocalPosition = new Vector3( 10, 10, 10 );

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 20, 10, 30 ) ), $"{child.WorldPosition}" );
	}

	/// <summary>
	/// Setting a world position on a child of a transformed parent must produce the
	/// matching local position - the inverse transform.
	/// </summary>
	[TestMethod]
	public void WorldSetterUpdatesLocal()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		parent.WorldPosition = new Vector3( 100, 0, 0 );
		parent.WorldRotation = Rotation.FromYaw( 90 );

		var child = new GameObject( parent );
		child.WorldPosition = new Vector3( 100, 50, 0 );

		Assert.IsTrue( child.LocalPosition.AlmostEqual( new Vector3( 50, 0, 0 ), 0.001f ), $"{child.LocalPosition}" );

		// And setting world rotation back to identity produces the inverse local rotation
		child.WorldRotation = Rotation.Identity;
		Assert.IsTrue( child.LocalRotation.Forward.AlmostEqual( new Vector3( 0, -1, 0 ), 0.001f ), $"{child.LocalRotation.Forward}" );
	}

	/// <summary>
	/// World transforms through a deep chain must match composing the local
	/// transforms manually with Transform.ToWorld.
	/// </summary>
	[TestMethod]
	public void DeepChainMatchesManualComposition()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var local = new Transform( new Vector3( 10, 5, 1 ), Rotation.FromYaw( 36 ), 1.1f );

		var expected = Transform.Zero;
		GameObject current = null;

		for ( int i = 0; i < 10; i++ )
		{
			var go = current is null ? scene.CreateObject() : new GameObject( current );
			go.LocalTransform = local;
			current = go;

			expected = expected.ToWorld( local );
		}

		Assert.IsTrue( current.WorldPosition.AlmostEqual( expected.Position, 0.01f ),
			$"{current.WorldPosition} vs {expected.Position}" );
		Assert.IsTrue( current.WorldScale.AlmostEqual( expected.Scale, 0.001f ) );
	}

	/// <summary>
	/// Setting a NaN world position must throw instead of silently poisoning the
	/// hierarchy.
	/// </summary>
	[TestMethod]
	public void WorldPositionNaNThrows()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 1, 2, 3 );

		Assert.ThrowsException<ArgumentOutOfRangeException>( () =>
			go.WorldPosition = new Vector3( float.NaN, 0, 0 ) );

		// The previous position is untouched
		Assert.IsTrue( go.WorldPosition.AlmostEqual( new Vector3( 1, 2, 3 ) ) );
	}

	/// <summary>
	/// LerpTo interpolates the transform halfway between two states.
	/// </summary>
	[TestMethod]
	public void LerpTo()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = Vector3.Zero;

		go.Transform.LerpTo( new Transform( new Vector3( 100, 0, 0 ) ), 0.5f );

		Assert.IsTrue( go.WorldPosition.AlmostEqual( new Vector3( 50, 0, 0 ), 0.001f ), $"{go.WorldPosition}" );
	}

	/// <summary>
	/// Setting a whole world or local transform containing a NaN position is silently
	/// ignored with a warning - the lenient counterpart to the throwing WorldPosition
	/// setter, pinned as it behaves today.
	/// </summary>
	[TestMethod]
	public void NaNTransformsAreIgnored()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 1, 2, 3 );

		go.WorldTransform = new Transform( new Vector3( float.NaN, 0, 0 ) );
		Assert.IsTrue( go.WorldPosition.AlmostEqual( new Vector3( 1, 2, 3 ) ), $"world set: {go.WorldPosition}" );

		go.LocalTransform = new Transform( new Vector3( 0, float.NaN, 0 ) );
		Assert.IsTrue( go.WorldPosition.AlmostEqual( new Vector3( 1, 2, 3 ) ), $"local set: {go.WorldPosition}" );
	}

	/// <summary>
	/// Moving a parent moves all descendants with it, preserving their local offsets.
	/// </summary>
	[TestMethod]
	public void MovingParentCarriesChildren()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var parent = scene.CreateObject();
		var child = new GameObject( parent );
		child.LocalPosition = new Vector3( 10, 0, 0 );
		var grandchild = new GameObject( child );
		grandchild.LocalPosition = new Vector3( 0, 5, 0 );

		parent.WorldPosition = new Vector3( 0, 0, 100 );

		Assert.IsTrue( child.WorldPosition.AlmostEqual( new Vector3( 10, 0, 100 ) ), $"{child.WorldPosition}" );
		Assert.IsTrue( grandchild.WorldPosition.AlmostEqual( new Vector3( 10, 5, 100 ) ), $"{grandchild.WorldPosition}" );
		Assert.IsTrue( grandchild.LocalPosition.AlmostEqual( new Vector3( 0, 5, 0 ) ) );
	}
}
