using System;

namespace SceneTests.Components;

/// <summary>
/// Pins ModelCollider: building physics shapes from a model's physics parts,
/// inheriting the model from a sibling renderer, swapping models at runtime and
/// staying inert without a model.
/// </summary>
[TestClass]
public class ModelColliderTest
{
	private static Model CitizenModel => Model.Load( "models/citizen/citizen.vmdl" );

	/// <summary>
	/// A model collider builds shapes from the model's physics parts and traces
	/// hit them.
	/// </summary>
	[TestMethod]
	public void ShapesFromModelPhysics()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var collider = go.Components.Create<ModelCollider>();
		collider.Model = CitizenModel;

		Assert.IsTrue( collider.Shapes.Count > 0, "the model should produce physics shapes" );

		var hit = scene.Trace.Ray( new Vector3( -100, 0, 40 ), new Vector3( 100, 0, 40 ) ).Run();
		Assert.IsTrue( hit.Hit, "the citizen physics should be solid" );
		Assert.AreEqual( go, hit.GameObject );

		Assert.IsTrue( collider.LocalBounds.Size.z > 40f, $"{collider.LocalBounds}" );
	}

	/// <summary>
	/// When no model is set the collider takes the model from a renderer on the
	/// same object when it wakes up.
	/// </summary>
	[TestMethod]
	public void ModelComesFromRendererWhenUnset()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.Components.Create<ModelRenderer>().Model = CitizenModel;
		var collider = go.Components.Create<ModelCollider>();

		Assert.AreEqual( CitizenModel, collider.Model );
		Assert.IsTrue( collider.Shapes.Count > 0 );
	}

	/// <summary>
	/// No model, no shapes - the collider stays inert and its bounds fall back to
	/// a point.
	/// </summary>
	[TestMethod]
	public void NoModelMeansNoShapes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		go.WorldPosition = new Vector3( 100, 0, 0 );
		var collider = go.Components.Create<ModelCollider>();

		Assert.IsNull( collider.Model );
		Assert.AreEqual( 0, collider.Shapes.Count );
		Assert.AreEqual( 0.1f, collider.GetWorldBounds().Size.x, 0.01f );
	}

	/// <summary>
	/// Swapping the model rebuilds the shapes - also on a rigidbody, where the
	/// body's mass properties get reapplied.
	/// </summary>
	[TestMethod]
	public void ModelSwapRebuildsShapes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var rb = go.Components.Create<Rigidbody>();
		rb.Gravity = false;
		var collider = go.Components.Create<ModelCollider>();
		collider.Model = CitizenModel;

		var shapeCount = collider.Shapes.Count;
		Assert.IsTrue( shapeCount > 0 );

		// Clearing the model destroys the shapes...
		collider.Model = null;
		Assert.AreEqual( 0, collider.Shapes.Count );

		// ...and assigning one builds them again
		collider.Model = CitizenModel;
		Assert.AreEqual( shapeCount, collider.Shapes.Count );
	}

	/// <summary>
	/// A model reload notification rebuilds the shapes in place.
	/// </summary>
	[TestMethod]
	public void ModelReloadRebuildsShapes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var collider = go.Components.Create<ModelCollider>();
		collider.Model = CitizenModel;

		var shapeCount = collider.Shapes.Count;
		Assert.IsTrue( shapeCount > 0 );

		((IHasModel)collider).OnModelReloaded();

		Assert.AreEqual( shapeCount, collider.Shapes.Count, "the rebuilt shape set should match" );

		var hit = scene.Trace.Ray( new Vector3( -100, 0, 40 ), new Vector3( 100, 0, 40 ) ).Run();
		Assert.IsTrue( hit.Hit, "the collider should still be solid after the reload" );
	}
}
