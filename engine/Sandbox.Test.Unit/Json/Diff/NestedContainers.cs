using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonTest.Diff;

[TestClass]
public class NestedContainerTests
{
	[TestMethod]
	public void RoundTrip_NestedDictionaryContainers()
	{
		var source = JsonNode.Parse(
			"""
			{
				"company": {
					"id": 100,
					"name": "Acme Inc",
					"departments": {
						"Engineering": {
							"id": 1,
							"name": "Engineering",
							"employees": [
								{ "id": 101, "name": "Alice", "role": "Developer" },
								{ "id": 102, "name": "Bob", "role": "Designer" }
							]
						},
						"Marketing": {
							"id": 2,
							"name": "Marketing",
							"employees": [
								{ "id": 201, "name": "Charlie", "role": "Manager" }
							]
						}
					}
				}
			}
			""" ) as JsonObject;

		var target = JsonNode.Parse(
			"""
			{
				"company": {
					"id": 100,
					"name": "Acme Corporation",
					"departments": {
						"Engineering": {
							"id": 1,
							"name": "Engineering",
							"budget": 750000,
							"employees": [
								{ "id": 101, "name": "Alice", "role": "Lead Developer" },
								{ "id": 103, "name": "David", "role": "Junior Developer" }
							]
						},
						"Marketing": {
							"id": 2,
							"name": "Marketing",
							"budget": 500000,
							"employees": [
								{ "id": 201, "name": "Charlie", "role": "Marketing Director" }
							]
						},
						"Sales": {
							"id": 3,
							"name": "Sales",
							"employees": [
								{ "id": 301, "name": "Eve", "role": "Sales Rep" }
							]
						}
					}
				}
			}
			""" ) as JsonObject;

		var definitions = new HashSet<Json.TrackedObjectDefinition>
		{
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Root",
				requiredFields: ["company"],
				allowedAsRoot: true
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Company",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Root"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Department",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Company"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Employee",
				requiredFields: ["id", "name", "role"],
				idProperty: "id",
				parentType: "Department"
			)
		};

		JsonTestUtils.RunRoundTripTest( source, target, "Nested Dictionary Containers", definitions );
	}

	[TestMethod]
	public void RoundTrip_NestedNamespacedArrayContainers()
	{
		var source = JsonNode.Parse(
			"""
			{
				"company": {
					"id": 100,
					"name": "Acme Inc",
					"departments": {
						"active": [
							{
								"id": 1,
								"name": "Engineering",
								"employees": [
									{ "id": 101, "name": "Alice", "role": "Developer" },
									{ "id": 102, "name": "Bob", "role": "Designer" }
								]
							},
							{
								"id": 2,
								"name": "Marketing",
								"employees": [
									{ "id": 201, "name": "Charlie", "role": "Manager" }
								]
							}
						],
						"archived": [
							{
								"id": 3,
								"name": "Sales",
								"employees": [
									{ "id": 301, "name": "Dave", "role": "Sales Rep" }
								]
							}
						]
					}
				}
			}
			""" ) as JsonObject;

		var target = JsonNode.Parse(
			"""
			{
				"company": {
					"id": 100,
					"name": "Acme Inc",
					"departments": {
						"active": [
							{
								"id": 1,
								"name": "Engineering",
								"employees": [
									{ "id": 101, "name": "Alice", "role": "Lead Developer" },
									{ "id": 102, "name": "Bob", "role": "Designer" }
								]
							},
							{
								"id": 4,
								"name": "Research",
								"employees": [
									{ "id": 401, "name": "Eve", "role": "Researcher" }
								]
							}
						],
						"archived": [
							{
								"id": 3,
								"name": "Sales",
								"employees": [
									{ "id": 301, "name": "Dave", "role": "Sales Rep" }
								]
							},
							{
								"id": 2,
								"name": "Marketing",
								"employees": [
									{ "id": 201, "name": "Charlie", "role": "Manager" }
								]
							}
						]
					}
				}
			}
			""" ) as JsonObject;

		var definitions = new HashSet<Json.TrackedObjectDefinition>
		{
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Root",
				requiredFields: ["company"],
				allowedAsRoot: true
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Company",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Root"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Department",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Company"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Employee",
				requiredFields: ["id", "name", "role"],
				idProperty: "id",
				parentType: "Department"
			)
		};

		JsonTestUtils.RunRoundTripTest( source, target, "Nested Namespaced Array Containers", definitions );
	}

	[TestMethod]
	public void RoundTrip_MixedNestedContainers()
	{
		var source = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "TechCorp",
					"structure": {
						"departments": [
							{
								"id": 101,
								"name": "Engineering",
								"teams": {
									"Frontend": {
										"id": 201,
										"name": "Frontend Team",
										"members": [
											{ "id": 1001, "name": "Alice", "role": "Developer" },
											{ "id": 1002, "name": "Bob", "role": "Designer" }
										]
									},
									"Backend": {
										"id": 202,
										"name": "Backend Team",
										"members": [
											{ "id": 1003, "name": "Charlie", "role": "Developer" }
										]
									}
								}
							},
							{
								"id": 102,
								"name": "Marketing",
								"teams": {
									"Digital": {
										"id": 203,
										"name": "Digital Marketing",
										"members": [
											{ "id": 1004, "name": "Diana", "role": "Manager" }
										]
									}
								}
							}
						]
					}
				}
			}
			""" ) as JsonObject;

		var target = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "TechCorp Global",
					"structure": {
						"departments": [
							{
								"id": 101,
								"name": "Engineering",
								"teams": {
									"Frontend": {
										"id": 201,
										"name": "Frontend Development",
										"members": [
											{ "id": 1001, "name": "Alice", "role": "Lead Developer" },
											{ "id": 1002, "name": "Bob", "role": "Designer" },
											{ "id": 1005, "name": "Frank", "role": "Junior Developer" }
										]
									},
									"Backend": {
										"id": 202,
										"name": "Backend Team",
										"members": [
											{ "id": 1003, "name": "Charlie", "role": "Senior Developer" }
										]
									},
									"QA": {
										"id": 204,
										"name": "Quality Assurance",
										"members": [
											{ "id": 1006, "name": "Grace", "role": "QA Engineer" }
										]
									}
								}
							},
							{
								"id": 102,
								"name": "Marketing & Sales",
								"teams": {
									"Digital": {
										"id": 203,
										"name": "Digital Marketing",
										"members": [
											{ "id": 1004, "name": "Diana", "role": "Director" }
										]
									},
									"Sales": {
										"id": 205,
										"name": "Sales Team",
										"members": [
											{ "id": 1007, "name": "Henry", "role": "Sales Manager" }
										]
									}
								}
							}
						]
					}
				}
			}
			""" ) as JsonObject;

		var definitions = new HashSet<Json.TrackedObjectDefinition>
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
				type: "Team",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Department"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Member",
				requiredFields: ["id", "name", "role"],
				idProperty: "id",
				parentType: "Team"
			)
		};

		JsonTestUtils.RunRoundTripTest( source, target, "Mixed Nested Containers", definitions );
	}
}
