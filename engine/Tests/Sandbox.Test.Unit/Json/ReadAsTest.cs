using System;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JsonTests;

[TestClass]
public class ReadAsTest
{
	/// <summary>
	/// Parsed values take the direct path, built ones the node path, both give what JsonNode.Deserialize gives.
	/// </summary>
	[TestMethod]
	public void MatchesNodeDeserialize()
	{
		var guid = Guid.NewGuid();
		var parsed = JsonNode.Parse( $$"""{ "Id": "{{guid}}", "Name": "a \"b\" é", "Position": "1,2.5,-3", "Bytes": "AQID", "Missing": null }""" ).AsObject();
		var built = new JsonObject { ["Id"] = guid, ["Name"] = "a \"b\" é", ["Position"] = "1,2.5,-3", ["Bytes"] = "AQID" };

		foreach ( var json in new[] { parsed, built } )
		{
			Assert.AreEqual( json["Id"].Deserialize<Guid>(), json["Id"].ReadAs<Guid>() );
			Assert.AreEqual( json["Name"].Deserialize<string>(), json["Name"].ReadAs<string>() );
			Assert.AreEqual( json["Position"].Deserialize<Vector3>(), json["Position"].ReadAs<Vector3>() );
			CollectionAssert.AreEqual( json["Bytes"].Deserialize<byte[]>(), json["Bytes"].ReadAs<byte[]>() );
			Assert.IsNull( json["Missing"]?.ReadAs<Vector3>() );
			Assert.AreEqual( json["Name"].Deserialize<string>(), json.GetPropertyValue( "Name", "" ) );
		}
	}
}
