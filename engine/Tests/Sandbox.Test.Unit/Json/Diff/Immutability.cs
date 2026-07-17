using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace JsonTests.Diff;

[TestClass]
public class ImmutabilityTest
{
	private static HashSet<Json.TrackedObjectDefinition> BuildDefinitions() =>
	[
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
			type: "Root", requiredFields: ["company"], allowedAsRoot: true
		),
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
			type: "Department", requiredFields: ["id", "name"], idProperty: "id", parentType: "Root"
		),
		Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
			type: "Employee", requiredFields: ["id", "name", "role"], idProperty: "id", parentType: "Department"
		),
	];

	private static JsonObject Source() => JsonNode.Parse(
		"""
		{
			"company": { "departments": [
				{ "id": 1, "name": "Engineering", "employees": [
					{ "id": 101, "name": "Alice", "role": "Developer" }
				]}
			]}
		}
		""" ).AsObject();

	private static JsonObject Target() => JsonNode.Parse(
		"""
		{
			"company": { "departments": [
				{ "id": 1, "name": "Engineering", "budget": 500000, "employees": [
					{ "id": 101, "name": "Alice", "role": "Senior Developer" }
				]},
				{ "id": 2, "name": "Marketing", "employees": [
					{ "id": 201, "name": "Charlie", "role": "Manager" }
				]}
			]}
		}
		""" ).AsObject();

	[TestMethod]
	public void CalculateDifferences_DoesNotMutateOldRoot()
	{
		var defs = BuildDefinitions();
		var oldRoot = Source();
		var before = oldRoot.ToJsonString();
		Json.CalculateDifferences( oldRoot, Target(), defs );
		Assert.AreEqual( before, oldRoot.ToJsonString() );
	}

	[TestMethod]
	public void CalculateDifferences_DoesNotMutateNewRoot()
	{
		var defs = BuildDefinitions();
		var newRoot = Target();
		var before = newRoot.ToJsonString();
		Json.CalculateDifferences( Source(), newRoot, defs );
		Assert.AreEqual( before, newRoot.ToJsonString() );
	}

	[TestMethod]
	public void ApplyPatch_DoesNotMutateSource()
	{
		var defs = BuildDefinitions();
		var source = Source();
		var patch = Json.CalculateDifferences( source, Target(), defs );
		var before = source.ToJsonString();
		Json.ApplyPatch( source, patch, defs );
		Assert.AreEqual( before, source.ToJsonString() );
	}

	[TestMethod]
	public void ApplyPatch_PatchIsReusable()
	{
		var defs = BuildDefinitions();
		var target = Target();
		var patch = Json.CalculateDifferences( Source(), target, defs );
		Assert.IsTrue( JsonNode.DeepEquals( Json.ApplyPatch( Source(), patch, defs ), target ) );
		Assert.IsTrue( JsonNode.DeepEquals( Json.ApplyPatch( Source(), patch, defs ), target ) );
	}

	[TestMethod]
	public void AddedObject_DataDoesNotContainTrackedChildren()
	{
		// Dept 2 is added with employee 201 inside it. AddedObject.Data must not embed the
		// employee — employees are tracked separately and would appear twice if included.
		var defs = BuildDefinitions();
		var patch = Json.CalculateDifferences( Source(), Target(), defs );
		Assert.IsTrue( patch.AddedObjects.Any( a => a.Id.IdValue == "2" ), "Department 2 should be in AddedObjects" );
		var addedDept = patch.AddedObjects.First( a => a.Id.IdValue == "2" );
		if ( addedDept.Data.TryGetPropertyValue( "employees", out var emp ) )
			Assert.AreEqual( 0, emp?.AsArray()?.Count ?? 0 );
		Assert.IsTrue( JsonNode.DeepEquals( Json.ApplyPatch( Source(), patch, defs ), Target() ) );
	}
}
