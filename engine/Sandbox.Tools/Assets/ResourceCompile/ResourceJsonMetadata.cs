using System;
using System.Text.Json.Nodes;

namespace Editor;

internal static class ResourceJsonMetadata
{
	internal static bool IsTypeMetadata( JsonObject owner, string propertyName )
	{
		if ( propertyName.Equals( "__type", StringComparison.OrdinalIgnoreCase ) || propertyName == "$type" )
			return true;

		if ( propertyName == "Type" && IsActionGraphNode( owner ) )
			return true;

		return propertyName == "_type"
			&& owner.Parent is JsonObject node
			&& ReferenceEquals( node["Properties"], owner )
			&& IsActionGraphNode( node );
	}

	private static bool IsActionGraphNode( JsonObject node )
	{
		return node.Parent is JsonArray nodes
			&& nodes.Parent is JsonObject graph
			&& ReferenceEquals( graph["Nodes"], nodes )
			&& graph["__guid"] is JsonValue
			&& graph["__version"] is JsonValue;
	}
}
