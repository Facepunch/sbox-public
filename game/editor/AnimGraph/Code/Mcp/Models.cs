using System.Text.Json.Nodes;

namespace Editor.AnimGraph;

public static partial class McpTools
{
	public sealed class EditOperation
	{
		/// <summary>
		/// add_node, set_node, remove_node, move_nodes, group_nodes, ungroup_node, layout_graph, connect, disconnect, set_graph,
		/// add_parameter, set_parameter, remove_parameter, add_item, set_item,
		/// remove_item, move_item, add_variation, set_variation, remove_variation,
		/// add_override, or remove_override.
		/// </summary>
		public string Operation { get; set; }

		/// <summary>
		/// The variation this operation targets. Defaults to the default variation.
		/// Node and item edits write that variation's data, which requires an override
		/// on the node first - add one with add_override.
		/// </summary>
		public string Variation { get; set; }

		/// <summary>Parent variation for add_variation. Defaults to Default.</summary>
		public string Parent { get; set; }

		/// <summary>
		/// Graph locator from animgraph_inspect. Defaults to root. Aliases can be
		/// used inside locators as @name after an earlier add_node or group_nodes operation.
		/// </summary>
		public string Graph { get; set; } = "root";

		/// <summary>Node id, or @alias from an earlier add_node or group_nodes operation.</summary>
		public string Node { get; set; }

		/// <summary>Node ids or @aliases for move_nodes or group_nodes. Internal connections are retained.</summary>
		public string[] Nodes { get; set; }

		/// <summary>Also arrange nested graphs and state layouts with layout_graph.</summary>
		public bool Recursive { get; set; }

		/// <summary>Destination graph locator for move_nodes. Disconnect connections to nodes outside the moved set first.</summary>
		public string TargetGraph { get; set; }

		/// <summary>Node class name for add_node, such as AnimationClipNode.</summary>
		public string Type { get; set; }

		/// <summary>Optional alias assigned by add_node, add_parameter or group_nodes, referenced later as @alias.</summary>
		public string Alias { get; set; }

		/// <summary>Property values for set_graph, add_node, group_nodes, set_node, add_parameter, set_parameter, or set_item.</summary>
		public JsonObject Values { get; set; }

		/// <summary>Collection property for add_item, set_item, remove_item, or move_item.</summary>
		public string Property { get; set; }

		/// <summary>Collection item value for add_item or replacement set_item.</summary>
		public JsonNode Value { get; set; }

		/// <summary>Collection index. -1 appends for add_item.</summary>
		public int Index { get; set; } = -1;

		/// <summary>Destination index for move_item.</summary>
		public int TargetIndex { get; set; } = -1;

		public string FromNode { get; set; }
		public string FromOutput { get; set; }
		public string ToNode { get; set; }

		/// <summary>Input plug name, or @alias of a GroupInputNode when connecting to its parent group.</summary>
		public string ToInput { get; set; }

		/// <summary>Parameter id, name, or @alias. Names the new parameter for add_parameter.</summary>
		public string Parameter { get; set; }

		/// <summary>Float, Bool, ID, Vector, Target, or BoneMask.</summary>
		public string ParameterType { get; set; }

		/// <summary>Create a virtual parameter instead of a control parameter.</summary>
		public bool Virtual { get; set; }
	}

	public class AssetResult
	{
		public string Path { get; set; }
		public string SourceFile { get; set; }
		public string Revision { get; set; }

		/// <summary>The variation the compile results belong to.</summary>
		public string Variation { get; set; }

		public bool? AssetCompiled { get; set; }
		public CompileResult Compile { get; set; }
	}

	public sealed class EditResult : AssetResult
	{
		public int OperationsApplied { get; set; }
		public Dictionary<string, string> Aliases { get; set; }
	}

	public sealed class CompileResult
	{
		public double ElapsedMilliseconds { get; set; }
		public int RootNodeIndex { get; set; }
		public CompileWarning[] Warnings { get; set; }
		public Dictionary<string, int> NodeIndices { get; set; }
	}

	public sealed class CompileWarning
	{
		public string Message { get; set; }
		public string Node { get; set; }
		public string NodeType { get; set; }

		/// <summary>"Warning", or "Error" when it stops the graph compiling.</summary>
		public string Severity { get; set; }
	}

	public sealed class InspectionResult
	{
		public string Path { get; set; }
		public string Revision { get; set; }
		public string Model { get; set; }
		public string Skeleton { get; set; }

		/// <summary>The variation this inspection resolved node data for.</summary>
		public string Variation { get; set; }

		public VariationInfo[] Variations { get; set; }
		public ParameterInfo[] Parameters { get; set; }
		public GraphInfo[] Graphs { get; set; }
	}

	public sealed class VariationInfo
	{
		public string Id { get; set; }

		/// <summary>The variation it inherits from, null for the default variation.</summary>
		public string Parent { get; set; }

		/// <summary>The model this variation animates.</summary>
		public string Model { get; set; }

		/// <summary>The skeleton this variation's clips are compiled against.</summary>
		public string Skeleton { get; set; }

		/// <summary>How many nodes give this variation its own data.</summary>
		public int Overrides { get; set; }
	}

	public sealed class ParameterInfo
	{
		public string Id { get; set; }
		public string Name { get; set; }
		public string Group { get; set; }
		public string Type { get; set; }
		public bool Virtual { get; set; }
		public string Graph { get; set; }
		public JsonObject Values { get; set; }
	}

	public sealed class GraphInfo
	{
		public string Id { get; set; }
		public string Owner { get; set; }
		public string Kind { get; set; }
		public NodeInfo[] Nodes { get; set; }
	}

	public sealed class NodeInfo
	{
		public string Id { get; set; }
		public string Type { get; set; }
		public string Title { get; set; }
		public Vector2 Position { get; set; }
		public JsonObject Properties { get; set; }

		/// <summary>Where this node's data comes from, only on nodes whose data varies.</summary>
		public NodeVariationInfo Variation { get; set; }

		public PlugInfo[] Inputs { get; set; }
		public PlugInfo[] Outputs { get; set; }
	}

	public sealed class NodeVariationInfo
	{
		/// <summary>The variation the resolved data came from - itself when overridden.</summary>
		public string Source { get; set; }

		/// <summary>Whether the inspected variation has its own data for this node.</summary>
		public bool HasOverride { get; set; }

		/// <summary>Every variation that gives this node its own data.</summary>
		public string[] OverriddenBy { get; set; }

		/// <summary>The resolved data for the inspected variation.</summary>
		public JsonObject Data { get; set; }
	}

	public sealed class NamesResult
	{
		public string Path { get; set; }
		public string Revision { get; set; }
		public VariationNames[] Variations { get; set; }

		/// <summary>ID parameters that only accept a fixed set of values.</summary>
		public IdParameterNames[] IdParameters { get; set; }
	}

	public sealed class VariationNames
	{
		public string Variation { get; set; }
		public string Model { get; set; }
		public string Skeleton { get; set; }

		/// <summary>Values for properties edited by "animgraph.bone".</summary>
		public string[] Bones { get; set; }

		/// <summary>Values for properties edited by "animgraph.bonemask".</summary>
		public string[] BoneMasks { get; set; }

		/// <summary>Values for properties with a resource type of "anim", such as AnimationName.</summary>
		public string[] Clips { get; set; }
	}

	public sealed class IdParameterNames
	{
		public string Name { get; set; }
		public string[] Options { get; set; }
	}

	public sealed class PlugInfo
	{
		public string Name { get; set; }
		public string Title { get; set; }
		public string Type { get; set; }
		public ConnectionInfo Connection { get; set; }
	}

	public sealed class ConnectionInfo
	{
		public string Node { get; set; }
		public string Output { get; set; }
	}
}
