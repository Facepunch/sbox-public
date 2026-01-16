using Facepunch.ActionGraphs;
using System.Collections.Generic;
using Sandbox;

namespace Editor.ActionGraphs;

#nullable enable

/// <summary>
/// Node creation menu entry for nodes that get / set graph variables.
/// </summary>
public class VariableNodeType : LibraryNodeType
{
	[Event( GetLocalNodeTypesEvent.EventName )]
	public static void OnGetLocalNodeTypes( GetLocalNodeTypesEvent ev )
	{
		foreach ( var variable in ev.ActionGraph.Variables.Values )
		{
			ev.Output.Add( new VariableNodeType( variable ) );
		}
	}

	public Variable Variable { get; }

	private static IReadOnlyList<Menu.PathElement> GetPath( Variable variable )
	{
		var typeDesc = TypeLibrary.GetType( variable.Type );

		return new Menu.PathElement[]
		{
			new( "Graph", IsHeading: true ),
			new( "Variables", "Storage for values, scoped to each time the graph runs." ),
			new( $"{variable.Name} ({typeDesc.Title})", typeDesc.Icon ?? "check_box_outline_blank" )
		};
	}

	public VariableNodeType( Variable variable )
		: base(
			EditorNodeLibrary.Variable,
			GetPath( variable ),
			new Dictionary<string, object?> { { ParameterNames.Variable, variable } } )
	{
		Variable = variable;
	}
}
