using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace JsonTest.Diff;

[TestClass]
public class Basic
{
	[TestMethod]
	public void RoundTrip_WithContainerPatternMatching()
	{
		var source = JsonNode.Parse(
			"""
			{
				"company": {
					"id": 100,
					"name": "Acme Inc",
					"departments": [
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
					]
				}
			}
			""" ) as JsonObject;

		var target = JsonNode.Parse(
			"""
			{
				"company": {
					"id": 100,
					"name": "Acme Corp",
					"departments": [
						{
							"id": 1,
							"name": "Engineering",
							"employees": [
								{ "id": 101, "name": "Alice", "role": "Senior Developer" },
								{ "id": 103, "name": "Eve", "role": "QA Engineer" }
							]
						},
						{
							"id": 2,
							"name": "Marketing",
							"employees": [
								{ "id": 201, "name": "Charlie", "role": "Director" }
							]
						}
					]
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

		JsonTestUtils.RunRoundTripTest( source, target, "Container Property Name Pattern Matching", definitions );
	}

	[TestMethod]
	public void RoundTrip_SimilarObjectsWithDifferentParents()
	{
		var definitions = new HashSet<Json.TrackedObjectDefinition>
		{
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Root",
				requiredFields: ["university"],
				allowedAsRoot: true
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "University",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Root"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "AcademicDepartment",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "University"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Student",
				requiredFields: ["id", "name", "major"],
				idProperty: "id",
				parentType: "University"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Company",
				requiredFields: ["id", "name", "employees"],
				idProperty: "id",
				parentType: "University"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "CompanyDepartment",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Company"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Employee",
				requiredFields: ["id", "name", "role"],
				idProperty: "id",
				parentType: "Company"
			)
		};

		// Modified JSON structure - change company from direct property to an array
		var sourceJson = JsonNode.Parse(
			"""
			{
				"university": {
					"id": 1,
					"name": "State University",
					"departments": [
						{
							"id": 101,
							"name": "Computer Science",
							"budget": 500000
						}
					],
					"students": [
						{
							"id": 1001,
							"name": "John",
							"major": "Computer Science"
						}
					],
					"companies": [
						{
							"id": 2,
							"name": "University Corp",
							"departments": [
								{
									"id": 201,
									"name": "HR",
									"budget": 300000
								}
							],
							"employees": [
								{
									"id": 2001,
									"name": "Jane",
									"role": "HR Manager"
								}
							]
						}
					]
				}
			}
			""" ).AsObject();

		var targetJson = JsonNode.Parse(
			"""
			{
				"university": {
					"id": 1,
					"name": "State University",
					"departments": [
						{
							"id": 101,
							"name": "Computer Science",
							"budget": 600000
						},
						{
							"id": 102,
							"name": "Mathematics",
							"budget": 400000
						}
					],
					"students": [
						{
							"id": 1001,
							"name": "John",
							"major": "Computer Science"
						},
						{
							"id": 1002,
							"name": "Alice",
							"major": "Mathematics"
						}
					],
					"companies": [
						{
							"id": 2,
							"name": "University Corp",
							"departments": [
								{
									"id": 201,
									"name": "HR",
									"budget": 350000
								},
								{
									"id": 202,
									"name": "Finance",
									"budget": 250000
								}
							],
							"employees": [
								{
									"id": 2001,
									"name": "Jane",
									"role": "HR Director"
								},
								{
									"id": 2002,
									"name": "Bob",
									"role": "Finance Manager"
								}
							]
						}
					]
				}
			}
			""" ).AsObject();

		JsonTestUtils.RunRoundTripTest( sourceJson, targetJson, "Similar Objects with Different Containers", definitions );
	}

	[TestMethod]
	public void RoundTrip_NestedObjectModifications()
	{
		var definitions = new HashSet<Json.TrackedObjectDefinition>
		{
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Root",
				requiredFields: ["products"],
				allowedAsRoot: true
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Product",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Root"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Category",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Product"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Specification",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Product"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "SpecificationDetail",
				requiredFields: ["id", "key"],
				idProperty: "id",
				parentType: "Specification"
			)
		};


		var sourceJson = JsonNode.Parse(
			"""
			{
				"products": [
					{
						"id": 1,
						"name": "Smartphone",
						"price": 699.99,
						"categories": [
							{
								"id": 101,
								"name": "Electronics",
								"description": "Electronic devices"
							}
						],
						"specifications": [
							{
								"id": 201,
								"name": "Display",
								"details": [
									{
										"id": 301,
										"key": "size",
										"value": "6.1 inch"
									},
									{
										"id": 302,
										"key": "resolution",
										"value": "1170 x 2532"
									}
								]
							},
							{
								"id": 202,
								"name": "Battery",
								"details": [
									{
										"id": 303,
										"key": "capacity",
										"value": "3240 mAh"
									}
								]
							}
						]
					}
				]
			}
			""" ).AsObject();

		var targetJson = JsonNode.Parse(
			"""
			{
				"products": [
					{
						"id": 1,
						"name": "Smartphone Pro",
						"price": 899.99,
						"inStock": true,
						"categories": [
							{
								"id": 101,
								"name": "Premium Electronics",
								"description": "High-end electronic devices"
							}
						],
						"specifications": [
							{
								"id": 201,
								"name": "Display",
								"details": [
									{
										"id": 301,
										"key": "size",
										"value": "6.7 inch"
									},
									{
										"id": 302,
										"key": "resolution",
										"value": "1290 x 2796"
									}
								]
							},
							{
								"id": 202,
								"name": "Battery",
								"details": [
									{
										"id": 303,
										"key": "capacity",
										"value": "4323 mAh"
									},
									{
										"id": 304,
										"key": "technology",
										"value": "Li-ion"
									}
								]
							}
						]
					}
				]
			}
			""" ).AsObject();

		JsonTestUtils.RunRoundTripTest( sourceJson, targetJson, "Nested Object Modifications", definitions );
	}

	[TestMethod]
	public void RoundTrip_ArrayReorderingAndRemovals()
	{
		var definitions = new HashSet<Json.TrackedObjectDefinition>
		{
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Root",
				requiredFields: ["playlist"],
				allowedAsRoot: true
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Playlist",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Root"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Song",
				requiredFields: ["id", "title"],
				idProperty: "id",
				parentType: "Playlist"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Artist",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Song"
			)
		};

		var sourceJson = JsonNode.Parse(
			"""
			{
				"playlist": {
					"id": 1,
					"name": "My Favorites",
					"createdAt": "2025-01-15",
					"songs": [
						{
							"id": 101,
							"title": "Song One",
							"duration": 180,
							"artist": {
								"id": 501,
								"name": "Artist A"
							}
						},
						{
							"id": 102,
							"title": "Song Two",
							"duration": 210,
							"artist": {
								"id": 502,
								"name": "Artist B"
							}
						},
						{
							"id": 103,
							"title": "Song Three",
							"duration": 195,
							"artist": {
								"id": 501,
								"name": "Artist A"
							}
						},
						{
							"id": 104,
							"title": "Song Four",
							"duration": 240,
							"artist": {
								"id": 503,
								"name": "Artist C"
							}
						}
					]
				}
			}
			""" ).AsObject();

		var targetJson = JsonNode.Parse(
			"""
			{
				"playlist": {
					"id": 1,
					"name": "My Top Picks",
					"createdAt": "2025-01-15",
					"updatedAt": "2025-02-27",
					"songs": [
						{
							"id": 104,
							"title": "Song Four",
							"duration": 240,
							"artist": {
								"id": 503,
								"name": "Artist C"
							}
						},
						{
							"id": 105,
							"title": "Song Five",
							"duration": 225,
							"artist": {
								"id": 504,
								"name": "Artist D"
							}
						},
						{
							"id": 101,
							"title": "Song One",
							"duration": 183,
							"artist": {
								"id": 501,
								"name": "Artist A"
							}
						}
					]
				}
			}
			""" ).AsObject();

		JsonTestUtils.RunRoundTripTest( sourceJson, targetJson, "Array Reordering and Removals", definitions );
	}

	[TestMethod]
	public void RoundTrip_ComplexNestedStructures()
	{
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
				type: "Project",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Organization"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Task",
				requiredFields: ["id", "title"],
				idProperty: "id",
				parentType: "Project"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "User",
				requiredFields: ["id", "name"],
				idProperty: "id",
				parentType: "Organization"
			),
			Json.TrackedObjectDefinition.CreatePresenceBasedDefinition(
				type: "Comment",
				requiredFields: ["id", "text"],
				idProperty: "id",
				parentType: "Task"
			)
		};

		var sourceJson = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Tech Solutions",
					"description": "Software development company",
					"projects": [
						{
							"id": 101,
							"name": "Website Redesign",
							"status": "In Progress",
							"tasks": [
								{
									"id": 1001,
									"title": "Design Homepage",
									"status": "Completed",
									"assignee": {
										"id": 501,
										"name": "Alice Johnson"
									},
									"comments": [
										{
											"id": 10001,
											"text": "Initial design completed",
											"author": {
												"id": 501,
												"name": "Alice Johnson"
											}
										}
									]
								},
								{
									"id": 1002,
									"title": "Implement Contact Form",
									"status": "In Progress",
									"assignee": {
										"id": 502,
										"name": "Bob Smith"
									},
									"comments": []
								}
							]
						}
					],
					"users": [
						{
							"id": 501,
							"name": "Alice Johnson",
							"role": "Designer"
						},
						{
							"id": 502,
							"name": "Bob Smith",
							"role": "Developer"
						}
					]
				}
			}
			""" ).AsObject();

		var targetJson = JsonNode.Parse(
			"""
			{
				"organization": {
					"id": 1,
					"name": "Tech Solutions Inc.",
					"description": "Enterprise software development company",
					"founded": "2010",
					"projects": [
						{
							"id": 101,
							"name": "Website Redesign",
							"status": "In Progress",
							"priority": "High",
							"tasks": [
								{
									"id": 1001,
									"title": "Design Homepage",
									"status": "Completed",
									"assignee": {
										"id": 501,
										"name": "Alice Johnson"
									},
									"comments": [
										{
											"id": 10001,
											"text": "Initial design completed",
											"author": {
												"id": 501,
												"name": "Alice Johnson"
											}
										},
										{
											"id": 10002,
											"text": "Design approved by client",
											"author": {
												"id": 503,
												"name": "Carol Williams"
											}
										}
									]
								},
								{
									"id": 1002,
									"title": "Implement Contact Form",
									"status": "Completed",
									"assignee": {
										"id": 502,
										"name": "Bob Smith"
									},
									"comments": [
										{
											"id": 10003,
											"text": "Form validation added",
											"author": {
												"id": 502,
												"name": "Bob Smith"
											}
										}
									]
								},
								{
									"id": 1003,
									"title": "Optimize Images",
									"status": "New",
									"assignee": {
										"id": 503,
										"name": "Carol Williams"
									},
									"comments": []
								}
							]
						},
						{
							"id": 102,
							"name": "Mobile App Development",
							"status": "New",
							"tasks": []
						}
					],
					"users": [
						{
							"id": 501,
							"name": "Alice Johnson",
							"role": "Senior Designer"
						},
						{
							"id": 502,
							"name": "Bob Smith",
							"role": "Lead Developer"
						},
						{
							"id": 503,
							"name": "Carol Williams",
							"role": "QA Specialist"
						}
					]
				}
			}
			""" ).AsObject();

		JsonTestUtils.RunRoundTripTest( sourceJson, targetJson, "Complex Nested Structures", definitions );
	}
}
