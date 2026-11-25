using System;
using System.Text.Json.Nodes;

namespace JsonTests;

[TestClass]
public class MakeIdGuidsUniqueTest
{
	static readonly Guid Root = Guid.NewGuid();
	static readonly Guid Child = Guid.NewGuid();
	static readonly Guid Component = Guid.NewGuid();
	static readonly Guid PrefabSide = Guid.NewGuid();
	static readonly Guid Instance = Guid.NewGuid();
	static readonly Guid Outside = Guid.NewGuid();

	static JsonObject Create() => JsonNode.Parse( $$"""
	{
		"__guid": "{{Root}}",
		"Components": [ { "__guid": "{{Component}}", "Target": { "go": "{{Child}}" }, "Elsewhere": "{{Outside}}", "List": [ "{{Child}}", "not a guid", 3 ] } ],
		"Children": [ { "__guid": "{{Child}}", "Owner": "{{Component}}", "__PrefabIdToInstanceId": { "{{PrefabSide}}": "{{Instance}}" }, "Ref": "{{Instance}}" } ]
	}
	""" ).AsObject();

	[TestMethod]
	public void RemapsIdsAndEveryReference()
	{
		var json = Create();
		var translate = SceneUtility.MakeIdGuidsUnique( json );

		foreach ( var id in new[] { Root, Child, Component, Instance } )
		{
			Assert.IsTrue( translate.ContainsKey( id ), $"{id}" );
			Assert.AreNotEqual( id, translate[id] );
		}

		Assert.IsFalse( translate.ContainsKey( Outside ) );
		Assert.IsFalse( translate.ContainsKey( PrefabSide ) );

		var component = json["Components"][0];
		var child = json["Children"][0];

		Assert.AreEqual( translate[Root], json["__guid"].GetValue<Guid>() );
		Assert.AreEqual( translate[Component], component["__guid"].GetValue<Guid>() );
		Assert.AreEqual( translate[Child], component["Target"]["go"].GetValue<Guid>() );
		Assert.AreEqual( translate[Child], component["List"][0].GetValue<Guid>() );
		Assert.AreEqual( "not a guid", component["List"][1].GetValue<string>() );
		Assert.AreEqual( Outside, component["Elsewhere"].GetValue<Guid>() );
		Assert.AreEqual( translate[Child], child["__guid"].GetValue<Guid>() );
		Assert.AreEqual( translate[Component], child["Owner"].GetValue<Guid>() );
		Assert.AreEqual( translate[Instance], child["__PrefabIdToInstanceId"][PrefabSide.ToString()].GetValue<Guid>() );
		Assert.AreEqual( translate[Instance], child["Ref"].GetValue<Guid>() );
	}

	[TestMethod]
	public void KeepsTheGivenRootId()
	{
		var json = Create();
		var root = Guid.NewGuid();
		var translate = SceneUtility.MakeIdGuidsUnique( json, root );

		Assert.AreEqual( root, translate[Root] );
		Assert.AreEqual( root, json["__guid"].GetValue<Guid>() );
	}
}
