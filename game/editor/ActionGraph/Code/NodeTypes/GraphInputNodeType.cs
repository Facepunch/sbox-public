using System.Collections.Generic;
using System.Linq;
using Facepunch.ActionGraphs;
using Sandbox;
using DisplayInfo = Sandbox.DisplayInfo;

namespace Editor.ActionGraphs;

#nullable enable

/// <summary>
/// Node creation menu entry for getting an input from the current graph.
/// </summary>
public class GraphInputNodeType : LibraryNodeType
{
	[Event( GetLocalNodeTypesEvent.EventName )]
	public static void OnGetLocalNodeTypes( GetLocalNodeTypesEvent ev )
	{
		foreach ( var input in ev.ActionGraph.Inputs.Values.Where( x => !x.IsSignal ) )
		{
			ev.Output.Add( new GraphInputNodeType( input ) );
		}
	}

	public InputDefinition Input { get; }

	private static IReadOnlyList<Menu.PathElement> GetPath( InputDefinition input )
	{
		var display = DisplayInfo.ForType( input.Type );
		var title = input.IsTarget ? "This" : input.Display.Title;
		var icon = input.IsTarget ? "person" : display.Icon ?? "check_box_outline_blank";

		return new Menu.PathElement[]
		{
			new( "Graph", IsHeading: true ),
			new( "Inputs", "Inputs for the current graph." ),
			new( $"{title} ({display.Name})", icon, input.Display.Description )
		};
	}

	public GraphInputNodeType( InputDefinition input )
		: base( EditorNodeLibrary.InputValue,
			GetPath( input ),
			new Dictionary<string, object?> { { ParameterNames.Name, input.Name } } )
	{
		Input = input;
	}
}
