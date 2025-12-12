using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonTest.Diff;

[TestClass]
public class PartialApplication
{
	private static HashSet<Json.TrackedObjectDefinition> GetOrganizationDefinitions()
	{
		return new HashSet<Json.TrackedObjectDefinition>
		{
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Root",
				requiredFields: ["organization"],
				allowedAsRoot: true
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Organization",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Root"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Department",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Organization"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Employee",
				requiredFields: ["id", "name", "position"],
				idProperty: "id",
				parentType: "Department"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Project",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Department"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Task",
				requiredFields: ["id", "title"],
				idProperty: "id",
				parentType: "Project"
			)
		};
	}

	[TestMethod]
	public void PartialApply_MissingAddTarget()
	{
		// This test verifies what happens when trying to add an object to a parent that was removed

		var source = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Inc",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"employees": [
								{ "id": 1001, "name": "Alice", "position": "Developer" }
							],
							"projects": []
						},
						{
							"id": 102,
							"name": "Sales",
							"employees": [
								{ "id": 2001, "name": "Bob", "position": "Manager" }
							],
							"projects": []
						}
					]
				}
			}
			""" ) as JsonObject;

		var target = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Inc",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"employees": [
								{ "id": 1001, "name": "Alice", "position": "Developer" },
								{ "id": 1002, "name": "Charlie", "position": "QA Engineer" }
							],
							"projects": []
						},
						{
							"id": 102,
							"name": "Sales",
							"employees": [
								{ "id": 2001, "name": "Bob", "position": "Manager" },
								{ "id": 2002, "name": "Diana", "position": "Sales Rep" }
							],
							"projects": []
						}
					]
				}
			}
			""" ) as JsonObject;

		var definitions = GetOrganizationDefinitions();

		// Calculate patch from source to target
		var patch = Json.CalculateDifferences( source, target, definitions );

		// Modify source by removing a target parent
		// Remove Sales department (id: 102)
		var modifiedSource = source.DeepClone().AsObject();
		((JsonArray)modifiedSource["organization"]["departments"]).RemoveAt( 1 );

		// Apply patch to modified source
		var result = Json.ApplyPatch( modifiedSource, patch, definitions );

		// Verify: Charlie should be added to Engineering, but Diana can't be added since Sales dept was removed
		Assert.AreEqual( 1, ((JsonArray)result["organization"]["departments"]).Count );
		var engineering = ((JsonArray)result["organization"]["departments"])[0].AsObject();
		var engineeringEmployees = ((JsonArray)engineering["employees"]);

		Assert.AreEqual( 2, engineeringEmployees.Count );
		Assert.AreEqual( "Charlie", engineeringEmployees[1]["name"].GetValue<string>() );
		Assert.AreEqual( "QA Engineer", engineeringEmployees[1]["position"].GetValue<string>() );
	}

	[TestMethod]
	public void PartialApply_PropertyOverrides()
	{
		// This test verifies property overrides when some target objects are missing

		var source = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Inc",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"budget": 500000,
							"employees": [
								{
									"id": 1001,
									"name": "Alice",
									"position": "Developer",
									"skills": ["Java", "Python"]
								}
							]
						},
						{
							"id": 102,
							"name": "Marketing",
							"budget": 300000,
							"employees": [
								{
									"id": 2001,
									"name": "Bob",
									"position": "Manager",
									"skills": ["Communication", "Strategy"]
								}
							]
						}
					]
				}
			}
			""" ) as JsonObject;

		var target = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Corporation",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"budget": 650000,
							"employees": [
								{
									"id": 1001,
									"name": "Alice Smith",
									"position": "Senior Developer",
									"skills": ["Java", "Python", "React"]
								}
							]
						},
						{
							"id": 102,
							"name": "Marketing",
							"budget": 400000,
							"employees": [
								{
									"id": 2001,
									"name": "Bob Johnson",
									"position": "Marketing Director",
									"skills": ["Communication", "Strategy", "Analytics"]
								}
							]
						}
					]
				}
			}
			""" ) as JsonObject;

		var definitions = GetOrganizationDefinitions();

		// Calculate patch from source to target
		var patch = Json.CalculateDifferences( source, target, definitions );

		// Modify source by removing an employee
		var modifiedSource = source.DeepClone().AsObject();
		var departments = ((JsonArray)modifiedSource["organization"]["departments"]);
		var marketing = departments[1].AsObject();
		((JsonArray)marketing["employees"]).Clear(); // Remove Bob

		// Apply patch to modified source
		var result = Json.ApplyPatch( modifiedSource, patch, definitions );

		// Verify: Organization and Engineering properties should be updated, but Marketing employee props can't be
		Assert.AreEqual( "Acme Corporation", result["organization"]["name"].GetValue<string>() );

		var resultDepts = ((JsonArray)result["organization"]["departments"]);
		var resultEngineering = resultDepts[0].AsObject();
		var resultMarketing = resultDepts[1].AsObject();

		// Engineering budget should be updated
		Assert.AreEqual( 650000, resultEngineering["budget"].GetValue<int>() );

		// Engineering employee properties should be updated
		var aliceResult = ((JsonArray)resultEngineering["employees"])[0].AsObject();
		Assert.AreEqual( "Alice Smith", aliceResult["name"].GetValue<string>() );
		Assert.AreEqual( "Senior Developer", aliceResult["position"].GetValue<string>() );
		Assert.AreEqual( 3, aliceResult["skills"].AsArray().Count );

		// Marketing budget should be updated
		Assert.AreEqual( 400000, resultMarketing["budget"].GetValue<int>() );

		// But Marketing employees should be empty since we removed them
		Assert.AreEqual( 0, ((JsonArray)resultMarketing["employees"]).Count );
	}

	[TestMethod]
	public void PartialApply_MovedObjects()
	{
		// This test verifies moves when source or destination containers are removed

		var source = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Inc",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"employees": [],
							"projects": [
								{
									"id": 1001,
									"name": "Project Alpha",
									"tasks": [
										{ "id": 5001, "title": "Design Database", "status": "In Progress" }
									]
								}
							]
						},
						{
							"id": 102,
							"name": "Product",
							"employees": [],
							"projects": [
								{
									"id": 2001,
									"name": "Project Beta",
									"tasks": [
										{ "id": 6001, "title": "Define Requirements", "status": "Done" }
									]
								}
							]
						}
					]
				}
			}
			""" ) as JsonObject;

		var target = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Inc",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"employees": [],
							"projects": [
								{
									"id": 1001,
									"name": "Project Alpha",
									"tasks": [
										{ "id": 5001, "title": "Design Database", "status": "In Progress" },
										{ "id": 6001, "title": "Define Requirements", "status": "Done" }
									]
								}
							]
						},
						{
							"id": 102,
							"name": "Product",
							"employees": [],
							"projects": [
								{
									"id": 2001,
									"name": "Project Beta",
									"tasks": []
								}
							]
						}
					]
				}
			}
			""" ) as JsonObject;

		var definitions = GetOrganizationDefinitions();

		// Calculate patch from source to target
		var patch = Json.CalculateDifferences( source, target, definitions );

		// Modify source by removing Project Beta
		var modifiedSource = source.DeepClone().AsObject();
		var departments = ((JsonArray)modifiedSource["organization"]["departments"]);
		var product = departments[1].AsObject();
		((JsonArray)product["projects"]).Clear(); // Remove Project Beta

		// Apply patch to modified source
		var result = Json.ApplyPatch( modifiedSource, patch, definitions );

		// Verify: Task move attempt should be gracefully handled
		var resultDepts = ((JsonArray)result["organization"]["departments"]);
		var resultEngineering = resultDepts[0].AsObject();
		var engineeringProjects = ((JsonArray)resultEngineering["projects"]);
		var projectAlpha = engineeringProjects[0].AsObject();

		// Project Alpha tasks should NOT include the moved task since the source was removed
		var alphaTasks = ((JsonArray)projectAlpha["tasks"]);
		Assert.AreEqual( 1, alphaTasks.Count );
		Assert.AreEqual( "Design Database", alphaTasks[0]["title"].GetValue<string>() );
	}

	[TestMethod]
	public void PartialApply_RemovedObjects()
	{
		// This test verifies object removals when some objects are already removed

		var source = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Inc",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"employees": [
								{ "id": 1001, "name": "Alice", "position": "Developer" },
								{ "id": 1002, "name": "Bob", "position": "Designer" },
								{ "id": 1003, "name": "Charlie", "position": "Tester" }
							]
						}
					]
				}
			}
			""" ) as JsonObject;

		// Bob and Charlie removed
		var target = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Acme Inc",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"employees": [
								{ "id": 1001, "name": "Alice", "position": "Developer" }
							]
						}
					]
				}
			}
			""" ) as JsonObject;

		var definitions = GetOrganizationDefinitions();

		// Calculate patch from source to target
		var patch = Json.CalculateDifferences( source, target, definitions );

		// Modify source by already removing Charlie
		var modifiedSource = source.DeepClone().AsObject();
		var departments = ((JsonArray)modifiedSource["organization"]["departments"]);
		var engineering = departments[0].AsObject();
		var employees = ((JsonArray)engineering["employees"]);
		employees.RemoveAt( 2 ); // Remove Charlie

		// Apply patch to modified source
		var result = Json.ApplyPatch( modifiedSource, patch, definitions );

		// Verify: Bob should be removed, Charlie removal should be gracefully handled (already gone)
		var resultDepts = ((JsonArray)result["organization"]["departments"]);
		var resultEngineering = resultDepts[0].AsObject();
		var resultEmployees = ((JsonArray)resultEngineering["employees"]);

		// Should only have Alice left
		Assert.AreEqual( 1, resultEmployees.Count );
		Assert.AreEqual( "Alice", resultEmployees[0]["name"].GetValue<string>() );
	}

	[TestMethod]
	public void PartialApply_ComplexScenario()
	{
		// This test combines multiple patch operations on a more complex structure

		var source = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "TechCorp",
					"founded": 2010,
					"departments": [
						{
							"id": 101,
							"name": "Development",
							"employees": [
								{ "id": 1001, "name": "Alice", "position": "Developer", "level": 3 },
								{ "id": 1002, "name": "Bob", "position": "Architect", "level": 5 }
							],
							"projects": [
								{
									"id": 5001,
									"name": "Mobile App",
									"status": "In Progress",
									"tasks": [
										{ "id": 8001, "title": "UI Design", "priority": "High" },
										{ "id": 8002, "title": "API Integration", "priority": "Medium" }
									]
								}
							]
						},
						{
							"id": 102,
							"name": "QA",
							"employees": [
								{ "id": 2001, "name": "Charlie", "position": "Tester", "level": 2 }
							],
							"projects": [
								{
									"id": 6001,
									"name": "Test Automation",
									"status": "Planning",
									"tasks": [
										{ "id": 9001, "title": "Framework Selection", "priority": "Low" }
									]
								}
							]
						}
					]
				}
			}
			""" ) as JsonObject;

		// Bob moved to QA
		var target = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "TechCorp Global",
					"founded": 2010,
					"headquarters": "San Francisco",
					"departments": [
						{
							"id": 101,
							"name": "Engineering",
							"employees": [
								{ "id": 1001, "name": "Alice Smith", "position": "Lead Developer", "level": 4 }
							],
							"projects": [
								{
									"id": 5001,
									"name": "Mobile App v2",
									"status": "Active",
									"tasks": [
										{ "id": 8001, "title": "UI Design", "priority": "Critical" },
										{ "id": 8002, "title": "API Integration", "priority": "High" },
										{ "id": 8003, "title": "Performance Testing", "priority": "Medium" }
									]
								}
							]
						},
						{
							"id": 102,
							"name": "Quality Assurance",
							"employees": [
								{ "id": 2001, "name": "Charlie", "position": "QA Engineer", "level": 3 },
								{ "id": 1002, "name": "Bob", "position": "QA Architect", "level": 5 }
							],
							"projects": [
								{
									"id": 6001,
									"name": "Test Automation Framework",
									"status": "In Development",
									"tasks": [
										{ "id": 9001, "title": "Framework Selection", "priority": "Medium" },
										{ "id": 9002, "title": "CI/CD Integration", "priority": "High" }
									]
								}
							]
						},
						{
							"id": 103,
							"name": "Product Management",
							"employees": [
								{ "id": 3001, "name": "Diana", "position": "Product Manager", "level": 4 }
							],
							"projects": []
						}
					]
				}
			}
			""" ) as JsonObject;

		var definitions = GetOrganizationDefinitions();

		// Calculate patch from source to target
		var patch = Json.CalculateDifferences( source, target, definitions );

		// Modify source by removing multiple elements:
		// 1. Remove QA department (ID 102)
		// 2. Remove task from Mobile App (ID 8002)
		var modifiedSource = source.DeepClone().AsObject();
		var departments = ((JsonArray)modifiedSource["organization"]["departments"]);

		// Remove QA department
		departments.RemoveAt( 1 );

		// Remove API Integration task
		var development = departments[0].AsObject();
		var devProjects = ((JsonArray)development["projects"]);
		var mobileApp = devProjects[0].AsObject();
		var tasks = ((JsonArray)mobileApp["tasks"]);
		tasks.RemoveAt( 1 ); // Remove API Integration task

		// Apply patch to modified source
		var result = Json.ApplyPatch( modifiedSource, patch, definitions );

		// Verify the results:
		Assert.AreEqual( "TechCorp Global", result["organization"]["name"].GetValue<string>() );
		Assert.IsTrue( result["organization"].AsObject().ContainsKey( "headquarters" ) );

		var resultDepts = ((JsonArray)result["organization"]["departments"]);

		// Should have two departments: the original Development (now Engineering) and the new Product Management
		// QA department was removed from source so can't be in the result
		Assert.AreEqual( 2, resultDepts.Count );

		// First department (Engineering)
		// Get each department by ID rather than assuming a specific order
		// When we remove objects from the source we no longer can garantuee correct order so it's fine not to test for that
		var engineering = resultDepts.Cast<JsonObject>()
			.First( d => d["id"].GetValue<int>() == 101 );

		Assert.AreEqual( "Engineering", engineering["name"].GetValue<string>() );

		// Check Engineering projects
		var engProjects = ((JsonArray)engineering["projects"]);
		var mobileAppResult = engProjects[0].AsObject();
		Assert.AreEqual( "Mobile App v2", mobileAppResult["name"].GetValue<string>() );

		// Check tasks - should have the updated UI Design and the new Performance Testing
		// API Integration task was removed so patch shouldn't reintroduce it
		var resultTasks = ((JsonArray)mobileAppResult["tasks"]);
		Assert.AreEqual( 2, resultTasks.Count );

		var uiDesignTask = resultTasks.Cast<JsonObject>()
			.First( t => t["id"].GetValue<int>() == 8001 );
		var perfTestingTask = resultTasks.Cast<JsonObject>()
			.First( t => t["id"].GetValue<int>() == 8003 );

		// Check UI Design task properties
		Assert.AreEqual( "UI Design", uiDesignTask["title"].GetValue<string>() );
		Assert.AreEqual( "Critical", uiDesignTask["priority"].GetValue<string>() );

		// Check Performance Testing task properties
		Assert.AreEqual( "Performance Testing", perfTestingTask["title"].GetValue<string>() );
		Assert.AreEqual( "Medium", perfTestingTask["priority"].GetValue<string>() );

		// Second department should be Product Management (as it was added)
		var productMgmt = resultDepts.Cast<JsonObject>()
			.First( d => d["id"].GetValue<int>() == 103 );

		Assert.AreEqual( "Product Management", productMgmt["name"].GetValue<string>() );
		Assert.AreEqual( 1, ((JsonArray)productMgmt["employees"]).Count );
		Assert.AreEqual( "Diana", ((JsonArray)productMgmt["employees"])[0]["name"].GetValue<string>() );

		// Bob should NOT have moved to QA since QA department was removed from source
		var engEmployees = ((JsonArray)engineering["employees"]);
		Assert.AreEqual( 1, engEmployees.Count ); // Only Alice should remain
		Assert.AreEqual( "Alice Smith", engEmployees[0]["name"].GetValue<string>() ); // With updated name
	}
}
