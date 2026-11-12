using System.Text.Json.Nodes;
using Editor;

namespace Sandbox.Test;

[TestClass]
public class ResourceJsonMetadataTest
{
	[TestMethod]
	public void ComponentAndPolymorphicTypeNamesAreMetadata()
	{
		var owner = JsonNode.Parse( """{"__type":"Sandbox.Decal","$type":"Example.Jutsu"}""" ).AsObject();
		Assert.IsTrue( ResourceJsonMetadata.IsTypeMetadata( owner, "__type" ) );
		Assert.IsTrue( ResourceJsonMetadata.IsTypeMetadata( owner, "$type" ) );
	}

	[TestMethod]
	public void ActionGraphIdentifiersAreMetadataButResourceValuesAreNot()
	{
		var graph = JsonNode.Parse( """
			{"__guid":"graph-id","__version":9,"Nodes":[
				{"Id":1,"Type":"animation.jutsu","Properties":{"_type":"Example.Jutsu","Prefab":"effects/fire.prefab"}}
			]}
			""" );
		var node = graph["Nodes"][0].AsObject();
		var properties = node["Properties"].AsObject();
		Assert.IsTrue( ResourceJsonMetadata.IsTypeMetadata( node, "Type" ) );
		Assert.IsTrue( ResourceJsonMetadata.IsTypeMetadata( properties, "_type" ) );
		Assert.IsFalse( ResourceJsonMetadata.IsTypeMetadata( properties, "Prefab" ) );
	}

	[TestMethod]
	public void OrdinaryTypePropertiesStillRegisterResourceDependencies()
	{
		var owner = JsonNode.Parse( """{"Type":"effects/fire.jutsu","_type":"effects/ice.jutsu"}""" ).AsObject();
		Assert.IsFalse( ResourceJsonMetadata.IsTypeMetadata( owner, "Type" ) );
		Assert.IsFalse( ResourceJsonMetadata.IsTypeMetadata( owner, "_type" ) );
	}

	[TestMethod]
	public void AnUnrelatedNodesArrayIsNotAnActionGraph()
	{
		var owner = JsonNode.Parse( """{"Nodes":[{"Type":"effects/fire.jutsu"}]}""" )["Nodes"][0].AsObject();
		Assert.IsFalse( ResourceJsonMetadata.IsTypeMetadata( owner, "Type" ) );
	}
}
