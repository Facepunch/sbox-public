using System.Text.Json.Nodes;

namespace Editor;

public static class ClipboardTools
{
	public static void CopyProperties( string groupName, IEnumerable<SerializedProperty> properties )
	{
		var json = JsonNode.Parse( "{}" );
		var parentType = properties.FirstOrDefault()?.Parent?.TypeName ?? null;
		json["_parentType"] = parentType?.ToString() ?? "Unknown";
		json["_group"] = groupName;
		var newProperties = new JsonArray();
		foreach ( var prop in properties )
		{
			newProperties.Add( new Dictionary<string, string>(){
				{ "Name", prop.Name },
				{ "Value", Json.Serialize(prop.GetValue<object>()) }
			} );
		}
		json["_properties"] = newProperties;
		EditorUtility.Clipboard.Copy( json.ToJsonString() );
	}

	public static void PasteProperties( string groupName, IEnumerable<SerializedProperty> properties )
	{
		if ( !CanPasteProperties( groupName, properties ) ) return;
		var clipboard = EditorUtility.Clipboard.Paste();
		var json = JsonNode.Parse( clipboard );
		var jsonProperties = json["_properties"];
		if ( jsonProperties is JsonArray propertiesArray )
		{
			foreach ( var prop in properties )
			{
				var property = propertiesArray.FirstOrDefault( x => x["Name"].ToString() == prop.Name );
				if ( property is null ) continue;
				if ( property["Value"] is null )
				{
					prop.SetValue<object>( null );
				}
				else
				{
					prop.SetValue( Json.Deserialize( property["Value"].ToString(), typeof( object ) ) );
				}
			}
		}
	}

	public static bool CanPasteProperties( string groupName, IEnumerable<SerializedProperty> properties )
	{
		var clipboard = EditorUtility.Clipboard.Paste();
		if ( string.IsNullOrWhiteSpace( clipboard ) ) return false;
		if ( !clipboard.StartsWith( "{" ) ) return false;

		try
		{
			var json = JsonNode.Parse( clipboard );
			if ( json is null ) return false;
			if ( json["_group"] is null || json["_group"].ToString() != groupName ) return false;
			var parentType = properties.FirstOrDefault()?.Parent?.TypeName ?? null;
			if ( parentType == null ) return false;
			if ( json["_parentType"] is null || json["_parentType"].ToString() != parentType.ToString() ) return false;
			return true;
		}
		catch ( JsonException )
		{
			return false;
		}
	}
}
