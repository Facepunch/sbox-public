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
	public void DifferenceCalculatorOwnsItsSourceAndCanBeReused()
	{
		var definitions = BuildDefinitions();
		var source = Source();
		var expectedSource = source.DeepClone().AsObject();
		var calculate = Json.CreateDifferenceCalculator( source, definitions );
		source["company"]["departments"][0]["employees"][0]["role"] = "Changed after capture";

		foreach ( var target in new[] { Target(), expectedSource, Target() } )
		{
			var expected = target.DeepClone();
			var patch = calculate( target );
			Assert.IsTrue( JsonNode.DeepEquals( Json.SerializeAsObject( Json.CalculateDifferences( expectedSource, target, definitions ) ), Json.SerializeAsObject( patch ) ) );
			Assert.IsTrue( JsonNode.DeepEquals( expected, Json.ApplyPatch( expectedSource, patch, definitions ) ) );
			patch.PropertyOverrides.Clear();
			patch.AddedObjects.Clear();
		}
	}

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
	public void CalculateDifferences_PatchOwnsLeafData()
	{
		var defs = BuildDefinitions();
		var source = Source();
		var target = Target();
		var departments = target["company"]["departments"].AsArray();
		var modified = departments[0]["employees"][0];
		var added = departments[1]["employees"][0];
		modified["settings"] = JsonNode.Parse( """{"levels":[1,2,3]}""" );
		added["settings"] = JsonNode.Parse( """{"levels":[4,5,6]}""" );
		var expected = target.DeepClone();
		var patch = Json.CalculateDifferences( source, target, defs );

		// Mutating the target after diffing must not change additions or overrides.
		modified["settings"]["levels"][0] = 99;
		added["settings"]["levels"][0] = 99;
		Assert.IsTrue( JsonNode.DeepEquals( expected, Json.ApplyPatch( source, patch, defs ) ) );

		// Patch payloads must also be safe to edit without modifying the target.
		var before = target.ToJsonString();
		patch.AddedObjects.First( a => a.Id.IdValue == "201" ).Data["settings"]["levels"][0] = 42;
		patch.PropertyOverrides.First( p => p.Target.IdValue == "101" && p.Property == "settings" ).Value["levels"][0] = 42;
		Assert.AreEqual( before, target.ToJsonString() );
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
