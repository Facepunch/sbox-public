using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Sandbox.UI;

/// <summary>
/// Builds the window's titlebar, contents, and minimized restore button.
/// </summary>
public partial class FloatingWindow
{
	protected override void BuildRenderTree( RenderTreeBuilder tree )
	{
		tree.OpenElement<FloatingWindowTitleBar>( 0 );
		tree.AddAttribute( 1, "class", "floating-window-header" );

		tree.OpenElement<Label>( 2 );
		tree.AddAttribute( 3, "class", "floating-window-icon" );
		tree.AddAttribute( 4, "text", Icon );
		tree.CloseElement();

		tree.OpenElement<Label>( 5 );
		tree.AddAttribute( 6, "class", "floating-window-title" );
		tree.AddAttribute( 7, "text", Title );
		tree.CloseElement();

		if ( ShowMinimizeButton )
		{
			tree.OpenElement<Button>( 8 );
			tree.AddAttribute( 9, "class", "floating-window-nodrag floating-window-control" );
			tree.AddAttribute( 10, "icon", "remove" );
			tree.AddAttribute( 11, "onclick", (Action)Minimize );
			tree.CloseElement();
		}

		if ( ShowCloseButton )
		{
			tree.OpenElement<Button>( 12 );
			tree.AddAttribute( 13, "class", "floating-window-nodrag floating-window-control" );
			tree.AddAttribute( 14, "icon", "close" );
			tree.AddAttribute( 15, "onclick", (Action)RequestClose );
			tree.CloseElement();
		}

		tree.CloseElement();

		tree.OpenElement<Panel>( 17 );
		tree.AddAttribute( 18, "class", "floating-window-content" );
		tree.AddContent( 19, ChildContent );
		tree.CloseElement();

		tree.OpenElement<Button>( 20 );
		tree.AddAttribute( 21, "class", "floating-window-restore" );
		tree.AddAttribute( 22, "text", Icon );
		tree.AddAttribute( 23, "onclick", (Action)Restore );
		tree.AddAttribute( 24, "tooltip", $"Restore {Title}" );
		tree.CloseElement();
	}

	protected override string GetRenderTreeChecksum() => $"{BuildHash()}";
}
