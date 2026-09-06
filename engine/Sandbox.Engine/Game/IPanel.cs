using Sandbox.UI;
using System;

namespace Sandbox.Internal;



public interface IPanel : IValid
{
	IPanel Parent { get; }
	IEnumerable<IPanel> Children { get; }
	int ChildrenCount { get; }
	string ElementName { get; }

	/// <summary>
	/// The Id of the element ( id="foo" )
	/// </summary>
	string Id { get; }

	/// <summary>
	/// If the panel created by razor, this is the file in which it was defined
	/// </summary>
	[Hide]
	string SourceFile { get; }

	/// <summary>
	/// If the panel was created by razor, this is the line in which it was defined
	/// </summary>
	[Hide]
	int SourceLine { get; }

	public bool IsMainMenu { get; }
	public bool IsGame { get; }
	public bool IsVisible { get; }
	public bool IsVisibleSelf { get; }

	/// <summary>
	/// If true then this panel (or its ancestor) has pointer-events: all
	/// </summary>
	public bool WantsPointerEvents { get; }
	public string Classes { get; }

	public Rect Rect { get; }
	public Rect InnerRect { get; }
	public Rect OuterRect { get; }

	public Matrix? GlobalMatrix { get; }

	public IPanel GetPanelAt( Vector2 point, bool visibleOnly, bool needPointerEvents = false );
	public bool IsAncestor( IPanel panel );

	public void Delete( bool immediate );

	internal static HashSet<IPanel> InspectablePanels = new();

	internal static HashSet<IPanel> GetAllRootPanels() => InspectablePanels;

	/// <summary>
	/// Procedural classes such as :hover and :active
	/// </summary>
	public PseudoClass PseudoClass { get; set; }

	public PanelInputType ButtonInput { get; set; }

	/// <summary>
	/// Get all style blocks active on this panel
	/// </summary>
	public IEnumerable<IStyleBlock> ActiveStyleBlocks { get; }

}
