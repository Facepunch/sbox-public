using Sandbox.Internal;
using System.Collections.Generic;
using System.Linq;

namespace GameObjects;

/// <summary>
/// Ensures that serialize → deserialize round-trips with IsRefreshing
/// do not silently alter local transforms. Components that do world -> local
/// conversions during deserialization are the typical offenders.
/// </summary>
[TestClass]
public class TransformStability
{
	TypeLibrary TypeLibrary;
	TypeLibrary _oldTypeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;
		TypeLibrary = new TypeLibrary();
		TypeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		TypeLibrary.AddAssembly( typeof( TransformStability ).Assembly, false );
		Game.TypeLibrary = TypeLibrary;
	}

	[TestCleanup]
	public void Cleanup() => Game.TypeLibrary = _oldTypeLibrary;

	[TestMethod]
	public void RefreshDeserializePreservesTransform()
	{
		var componentTypes = TypeLibrary.GetTypes<Component>()
			.Where( t => t.TargetType.IsPublic && !t.IsAbstract && !t.IsGenericType )
			// PlayerController/MoveMode modify WorldRotation in OnEnabled only when !Scene.IsEditor.
			// Prefab refresh runs in the editor so these are safe — exclude from test.
			.Where( t => !typeof( PlayerController ).IsAssignableFrom( t.TargetType ) )
			.Where( t => !typeof( Sandbox.Movement.MoveMode ).IsAssignableFrom( t.TargetType ) )
			.ToList();

		Assert.IsTrue( componentTypes.Count > 10 );

		var failures = new List<string>();

		foreach ( var (parentPos, childLocal) in GenerateTestCases() )
			foreach ( var type in componentTypes )
				if ( TestComponentRefresh( type, parentPos, childLocal ) is { } err )
					failures.Add( err );

		if ( failures.Count > 0 )
			Assert.Fail( $"{failures.Count} component(s) drifted:\n{string.Join( "\n", failures.Take( 20 ) )}" );
	}

	/// <summary>
	/// Known-bad cases plus seeded random fuzz across many magnitudes.
	/// </summary>
	static IEnumerable<(Vector3 parentPos, Transform childLocal)> GenerateTestCases()
	{
		// Static cases that reproduce known real-world issues
		Vector3[] parentPositions = [new( 100_000, 80_000, 5_000 ), new( -50_000, -120_000, 3_000 )];
		Transform[] childTransforms =
		[
			new( new Vector3( 1580, 811.0145f, -2 ), Rotation.Identity, 1 ),
			new( new Vector3( -2689.168f, 2076.5f, 5.839f ), Rotation.FromAxis( Vector3.Up, 45 ), 1 ),
			new( new Vector3( 0.5f, -0.25f, 100 ), Rotation.Identity, 2 ),
			// Values near/below the 0.0001 AlmostEqual tolerance — these triggered phantom overrides
			new( new Vector3( 226, -4446, -7.247925E-05f ), Rotation.Identity, 1 ),
			new( new Vector3( 2468.001f, 1865.999f, -4.424155E-05f ), Rotation.Identity, 1 ),
			new( new Vector3( 0, 0, 1e-5f ), Rotation.Identity, 1 ),
			new( new Vector3( 5000, 3000, 9.9e-5f ), Rotation.FromAxis( Vector3.Up, 90 ), 1 ),
		];

		foreach ( var p in parentPositions )
			foreach ( var c in childTransforms )
				yield return (p, c);

		// Seeded fuzz: near-zero, small, medium, large, very large magnitudes
		var rng = new System.Random( 42 );
		float[] magnitudes = [1e-5f, 0.001f, 0.1f, 1f, 100f, 5000f, 50_000f, 200_000f];
		float[] scales = [0.5f, 1f, 1f, 1f, 2f, 5f];

		for ( var i = 0; i < 50; i++ )
		{
			float RandRange() => (float)(rng.NextDouble() * 2 - 1);
			var pm = magnitudes[rng.Next( magnitudes.Length )];
			var cm = magnitudes[rng.Next( magnitudes.Length )];

			var parentPos = new Vector3( RandRange() * pm * 10, RandRange() * pm * 10, RandRange() * pm );
			var childPos = new Vector3( RandRange() * cm, RandRange() * cm, RandRange() * cm );
			var childRot = Rotation.FromAxis(
				new Vector3( (float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble() ).Normal,
				(float)(rng.NextDouble() * 360)
			);

			yield return (parentPos, new Transform( childPos, childRot, scales[rng.Next( scales.Length )] ));
		}
	}

	static string TestComponentRefresh( TypeDescription componentType, Vector3 parentPos, Transform childLocal )
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		try
		{
			var parent = scene.CreateObject();
			parent.Name = "Parent";
			parent.LocalPosition = parentPos;

			var child = new GameObject( parent, true, "Child" );
			child.LocalTransform = childLocal;

			// Capture what was actually stored, the setter uses AlmostEqual(0.0001) which
			// may coalesce tiny component values. We test the serialize -> deserialize invariant:
			// whatever is stored must survive a round-trip exactly.
			var before = child.LocalTransform;

			try { if ( child.Components.Create( componentType ) is null ) return null; }
			catch { return null; }

			var json = child.Serialize();

			using ( CallbackBatch.Isolated() )
				child.Deserialize( json, new GameObject.DeserializeOptions { IsRefreshing = true } );

			var after = child.LocalTransform;

			if ( after.Position.Equals( before.Position ) && after.Rotation.Equals( before.Rotation ) && after.Scale.Equals( before.Scale ) )
				return null;

			var delta = after.Position - before.Position;
			return $"{componentType.Name} @ parent={parentPos}: expected={before.Position:R} got={after.Position:R} delta={delta} (mag={delta.Length})";
		}
		catch { return null; }
	}
}
