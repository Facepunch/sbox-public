using Drag = Sandbox.UI.Drag;
using DropAction = Sandbox.UI.DropAction;

namespace Sandbox.PanelGallery;

/// <summary>
/// OS drag &amp; drop, both directions. Drag files or text in from an explorer window - each
/// zone answers whether it'd take the payload, which is what the drag cursor shows - and drag
/// the chips out to land files or text on the desktop or another app.
/// </summary>
public class DropPage : GalleryPage
{
	readonly Sandbox.UI.Label output;

	public DropPage() : base( "Drag & Drop", "OS drops in, OS drags out. Drag a file over the zones - the cursor answers per zone - and drag the chips out to the desktop." )
	{
		var zones = Case( "Drop targets" );

		zones.AddChild( new DropZone( "Files", "file_copy",
			drop => drop.Files.Count > 0 ? DropAction.Copy : DropAction.None,
			drop => Say( $"files: {string.Join( ", ", drop.Files.Select( System.IO.Path.GetFileName ) )}" ) ) );

		zones.AddChild( new DropZone( "Text", "notes",
			drop => string.IsNullOrEmpty( drop.Text ) ? DropAction.None : DropAction.Move,
			drop => Say( $"text: {drop.Text}" ) ) );

		zones.AddChild( new DropZone( "Nothing", "block",
			drop => DropAction.None,
			drop => { } ) );

		var sources = Case( "Drag out" );

		var textChip = Chip( sources, "notes", "Drag this text" );
		textChip.AddEventListener( "onmousedown", () =>
		{
			var drag = new Drag( textChip );
			drag.SetText( "Dropped out of the s&box panel gallery" );
			Say( $"text drag → {drag.Start()}" );
		} );

		var fileChip = Chip( sources, "description", "Drag this file" );
		fileChip.AddEventListener( "onmousedown", () =>
		{
			var path = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "sbox-drag-demo.txt" );
			System.IO.File.WriteAllText( path, "Dragged out of the s&box panel gallery.\n" );

			var drag = new Drag( fileChip );
			drag.SetFile( path );
			Say( $"file drag → {drag.Start()}" );
		} );

		// Selected text drags out of an entry, and text drops into one - so a selection can
		// go to the other entry, to the Text zone, or out to another app entirely
		var entries = Case( "Text entries" );

		var from = entries.AddChild<Sandbox.UI.TextEntry>();
		from.AddClass( "drag-entry" );
		from.Text = "Select some of this text and drag it";

		var to = entries.AddChild<Sandbox.UI.TextEntry>();
		to.AddClass( "drag-entry" );
		to.Placeholder = "Drop text here";

		output = Output();
	}

	void Say( string text ) => output.Text = text;

	static Panel Chip( Panel parent, string icon, string title )
	{
		var chip = parent.Add.Panel( "drag-chip" );
		chip.Add.Icon( icon, "icon" );
		chip.Add.Label( title );
		return chip;
	}

	/// <summary>
	/// A drop target. Judges every hover - the answer is what the drag cursor shows - and
	/// lights up while it's accepting. What lands goes to the callback.
	/// </summary>
	class DropZone : Panel
	{
		readonly Func<DropEvent, DropAction> judge;
		readonly Action<DropEvent> landed;

		public DropZone( string title, string icon, Func<DropEvent, DropAction> judge, Action<DropEvent> landed )
		{
			this.judge = judge;
			this.landed = landed;

			AddClass( "drop-zone" );
			this.Add.Icon( icon, "icon" );
			this.Add.Label( title );
		}

		protected override void OnDrop( PanelEvent e )
		{
			if ( e is not DropEvent drop ) return;

			drop.Action = judge( drop );
			SetClass( "armed", drop.Action != DropAction.None && !drop.IsDrop );

			if ( drop.Action == DropAction.None ) return;

			drop.StopPropagation();

			if ( drop.IsDrop ) landed( drop );
		}

		protected override void OnDragLeave( PanelEvent e )
		{
			if ( e is DropEvent ) SetClass( "armed", false );
		}
	}
}
