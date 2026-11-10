using System.Text.Json.Nodes;

namespace Editor;

public static class SceneEditor
{
	/// <summary>
	/// Is there a <see cref="Component"/> type in the clipboard?
	/// </summary>
	public static bool HasComponentInClipboard()
	{
		return IsComponentJson( EditorUtility.Clipboard.Paste() );
	}

	/// <summary>
	/// Does this text look like a serialized <see cref="Component"/> - a json object
	/// whose __type is a known component type?
	/// </summary>
	internal static bool IsComponentJson( string text )
	{
		try
		{
			if ( JsonNode.Parse( text ) is JsonObject jso )
			{
				var componentType = TypeLibrary.GetType<Component>( (string)jso["__type"] );
				return componentType is not null;
			}
		}
		catch
		{
			// Do nothing.
		}

		return false;
	}
}
