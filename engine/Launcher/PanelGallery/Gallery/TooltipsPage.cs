namespace Sandbox.PanelGallery;

/// <summary>
/// Tooltips - the same Panel.Tooltip the game uses, opening in OS windows of their own so they
/// can hang outside the window. Rest the cursor on things.
/// </summary>
public class TooltipsPage : GalleryPage
{
	public TooltipsPage() : base( "Tooltips", "Panel.Tooltip and Panel.OnTooltip. Each one is its own OS window - it should wait half a second, then follow you along a row without the wait, and never take the focus or the mouse." )
	{
		var row = Case( "Plain text" );

		row.AddChild( new Sandbox.UI.Button( "Save", "save", "flatbutton", null ) { Tooltip = "Save the scene" } );
		row.AddChild( new Sandbox.UI.Button( "Undo", "undo", "flatbutton", null ) { Tooltip = "Undo the last change" } );
		row.AddChild( new Sandbox.UI.Button( "Redo", "redo", "flatbutton", null ) { Tooltip = "Redo the change you just undid" } );
		row.AddChild( new Sandbox.UI.Button( "Nothing", "block", "flatbutton", null ) );

		row = Case( "Long text wraps" );

		row.AddChild( new Sandbox.UI.Button( "Hover for an essay", "article", "flatbutton", null )
		{
			Tooltip = "A tooltip can say quite a lot. This one goes on for long enough that it has to wrap onto several lines, which it should do at a sensible width rather than stretching across the whole screen."
		} );

		row = Case( "Rich content - OnTooltip" );

		row.AddChild( new Sandbox.UI.Button( "Several labels", "format_bold", "flatbutton", null )
		{
			OnTooltip = tooltip =>
			{
				tooltip.Add.Label( "Transform", "tooltip-title" );
				tooltip.Add.Label( "Position, rotation and scale of the object." );
				tooltip.Add.Label( "Component - Sandbox.Transform", "tooltip-detail" );
			}
		} );

		row.AddChild( new Sandbox.UI.Button( "With an image", "image", "flatbutton", null )
		{
			Tooltip = "checker.vtex",
			OnTooltip = tooltip =>
			{
				var image = new Sandbox.UI.Image { Texture = Checkerboard() };
				image.AddClass( "tooltip-image" );
				tooltip.AddChild( image );
				tooltip.Add.Label( "64 x 64 - generated", "tooltip-detail" );
			}
		} );

		row = Case( "A tooltip on the container covers what's in it" );

		var group = row.Add.Panel( "tooltip-group" );
		group.Tooltip = "The whole row shares this one";
		group.AddChild( new Sandbox.UI.Button( "One", null, "flatbutton", null ) );
		group.AddChild( new Sandbox.UI.Button( "Two", null, "flatbutton", null ) );
		group.AddChild( new Sandbox.UI.Button( "Three, with its own", null, "flatbutton", null ) { Tooltip = "Except this one" } );

		row = Case( "Text that changes while it's up" );

		row.AddChild( new Clock() );

		row = Case( "Custom tooltip panel - CreateTooltipPanel override" );

		row.AddChild( new CustomTooltipButton() );

		row = Case( "Doesn't steal the keyboard" );

		row.AddChild( new Sandbox.UI.TextEntry { Placeholder = "Type here, then hover the button", Tooltip = "The caret should stay put" } );
		row.AddChild( new Sandbox.UI.Button( "Hover me while typing", "keyboard", "flatbutton", null ) { Tooltip = "Still typing in the box?" } );

		var edge = Case( "Hangs outside the window" );
		edge.AddClass( "tooltip-edge" );

		edge.AddChild( new Sandbox.UI.Button( "Right at the edge", "east", "flatbutton", null )
		{
			Tooltip = "This should be allowed to cross the window's edge rather than being squashed inside it, because it's a window of its own."
		} );
	}

	/// <summary>
	/// A panel whose tooltip text keeps changing - the plain text path updates in place while it's up.
	/// </summary>
	class Clock : Sandbox.UI.Button
	{
		public Clock() : base( "What time is it?", "schedule", "flatbutton", null )
		{
		}

		public override void Tick()
		{
			base.Tick();
			Tooltip = $"{DateTime.Now:HH:mm:ss}";
		}
	}

	/// <summary>
	/// The old way still works - a panel that builds its own tooltip panel from scratch.
	/// </summary>
	class CustomTooltipButton : Sandbox.UI.Button
	{
		public CustomTooltipButton() : base( "Custom panel", "build", "flatbutton", null )
		{
		}

		public override bool HasTooltip => true;

		protected override Panel CreateTooltipPanel()
		{
			var panel = new Panel();
			panel.AddClass( "tooltip custom-tooltip" );
			panel.Add.Icon( "build", "icon" );
			panel.Add.Label( "Built by an override of CreateTooltipPanel" );
			return panel;
		}
	}

	static Texture Checkerboard()
	{
		const int size = 64;
		var data = new byte[size * size * 4];

		for ( int y = 0; y < size; y++ )
		{
			for ( int x = 0; x < size; x++ )
			{
				var light = ((x / 8) + (y / 8)) % 2 == 0;
				var i = (y * size + x) * 4;

				data[i + 0] = (byte)(light ? 235 : 60);
				data[i + 1] = (byte)(light ? 235 : 60);
				data[i + 2] = (byte)(light ? 235 : 60);
				data[i + 3] = 255;
			}
		}

		return Texture.Create( size, size ).WithData( data ).Finish();
	}
}
