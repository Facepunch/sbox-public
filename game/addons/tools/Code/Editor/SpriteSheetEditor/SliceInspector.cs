using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Editor.SpriteSheetEditor;

/// <summary>
/// Properties of whatever is selected, plus a quick way to drop the pivot on one of the nine usual
/// spots.
/// </summary>
public class SliceInspector : Widget
{
	readonly Window _editor;

	ScrollArea _scroller;
	JsonObject _stateBeforeEdit;

	public SliceInspector( Window editor ) : base( null )
	{
		_editor = editor;

		Name = "Slice";
		WindowTitle = "Slice";
		SetWindowIcon( "edit" );

		MinimumWidth = 280;

		Layout = Layout.Column();

		_scroller = new ScrollArea( this );
		_scroller.Canvas = new Widget();
		_scroller.Canvas.Layout = Layout.Column();
		_scroller.Canvas.Layout.Margin = 8;
		_scroller.Canvas.Layout.Spacing = 8;
		_scroller.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroller.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		Layout.Add( _scroller );

		_editor.OnSelectionChanged += Rebuild;
		_editor.OnSheetLoaded += Rebuild;

		Rebuild();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		_editor.OnSelectionChanged -= Rebuild;
		_editor.OnSheetLoaded -= Rebuild;
	}

	void Rebuild()
	{
		var layout = _scroller.Canvas.Layout;
		layout.Clear( true );

		layout.Add( BuildSheetSection() );

		var selected = _editor.SelectedSlices.ToList();

		if ( selected.Count == 0 )
		{
			var hint = new Label(
				_editor.SourceWidth > 0
					? "Nothing selected.\n\nDrag on the sheet to draw a slice, or use Slice to cut the whole thing at once."
					: "No image yet.\n\nPick one with the Image button to start slicing." )
			{
				WordWrap = true,
				Color = Theme.TextControl.WithAlpha( 0.6f )
			};

			layout.Add( hint );
			layout.AddStretchCell();
			return;
		}

		layout.Add( BuildPivotPresets( selected ) );

		var controlSheet = new ControlSheet();

		if ( selected.Count == 1 )
		{
			var serialized = selected[0].GetSerialized();
			controlSheet.AddObject( serialized );
			HookUndo( serialized, "Edit Slice" );
		}
		else
		{
			// Several at once. MultiSerializedObject writes every edit to all of them, which is what
			// is wanted here - setting a border or an alignment across a selection.
			var multi = new MultiSerializedObject();
			foreach ( var slice in selected ) multi.Add( slice.GetSerialized() );
			multi.Rebuild();

			// Name is per-slice identity; offering one box to rename several at once would just
			// collapse them onto a single name and then de-duplicate it.
			controlSheet.AddObject( multi, prop => prop.Name != nameof( SpriteSheet.Slice.Name ) );
			HookUndo( multi, $"Edit {selected.Count} Slices" );
		}

		layout.Add( controlSheet );
		layout.AddStretchCell();
	}

	/// <summary>
	/// The sheet itself: which image it cuts up, and what counts as empty space on it.
	/// </summary>
	Widget BuildSheetSection()
	{
		var holder = new Widget();
		holder.Layout = Layout.Column();
		holder.Layout.Spacing = 4;

		holder.Layout.Add( new Label( "Sheet" ) { Color = Theme.TextControl.WithAlpha( 0.7f ) } );

		if ( _editor.Sheet is null ) return holder;

		var serialized = _editor.Sheet.GetSerialized();

		// Slices have their own panel and their own list; showing them again as a raw array here would
		// be unusable at any real slice count.
		var sheetControls = new ControlSheet();
		sheetControls.AddObject( serialized, prop => prop.Name != nameof( SpriteSheet.Slices ) );
		holder.Layout.Add( sheetControls );

		if ( _editor.SourceWidth > 0 )
		{
			var detect = new Button( "Detect Background", "colorize" );
			detect.ToolTip = "Look at the corners of the image and treat that colour as empty space";
			detect.Clicked = () => { _editor.DetectBackground(); Rebuild(); };
			holder.Layout.Add( detect );

			// The state that makes automatic slicing look broken, called out where it can be fixed.
			if ( !_editor.Sheet.KeyBackground && !HasAnyTransparency() )
			{
				holder.Layout.Add( new Label(
					"This image has no transparency, so everything on it counts as artwork and slicing " +
					"will find one big rectangle. Set a background colour to say what is empty." )
				{
					WordWrap = true,
					Color = Theme.Yellow
				} );
			}
		}

		HookUndo( serialized, "Edit Sheet" );

		return holder;
	}

	/// <summary>
	/// Whether the image has any see-through pixels at all. Sampled rather than exhaustive - this only
	/// decides whether to show a hint, and walking 16 million pixels to draw a label would be absurd.
	/// </summary>
	bool HasAnyTransparency()
	{
		var pixels = _editor.SourcePixels;
		if ( pixels is null || pixels.Length == 0 ) return true;

		var step = Math.Max( 1, pixels.Length / 4096 );

		for ( int i = 0; i < pixels.Length; i += step )
		{
			if ( pixels[i].a < 255 ) return true;
		}

		return false;
	}

	/// <summary>
	/// The nine standard pivot spots as a 3x3 pad. Faster than a dropdown when the answer is
	/// "bottom-centre", and it leaves the dragging for the joints that are not on a standard spot.
	/// </summary>
	Widget BuildPivotPresets( List<SpriteSheet.Slice> selected )
	{
		var holder = new Widget();
		holder.Layout = Layout.Column();
		holder.Layout.Spacing = 4;

		holder.Layout.Add( new Label( "Pivot" ) { Color = Theme.TextControl.WithAlpha( 0.7f ) } );

		var grid = Layout.Grid();
		grid.Spacing = 2;

		var rows = new[]
		{
			new[] { SpriteSheet.PivotAlignment.TopLeft, SpriteSheet.PivotAlignment.Top, SpriteSheet.PivotAlignment.TopRight },
			new[] { SpriteSheet.PivotAlignment.Left, SpriteSheet.PivotAlignment.Center, SpriteSheet.PivotAlignment.Right },
			new[] { SpriteSheet.PivotAlignment.BottomLeft, SpriteSheet.PivotAlignment.Bottom, SpriteSheet.PivotAlignment.BottomRight }
		};

		var shared = selected.Select( s => s.Alignment ).Distinct().ToList();
		var common = shared.Count == 1 ? shared[0] : (SpriteSheet.PivotAlignment?)null;

		for ( int y = 0; y < rows.Length; y++ )
		{
			for ( int x = 0; x < rows[y].Length; x++ )
			{
				var alignment = rows[y][x];

				// The active spot is filled in so the current pivot is readable at a glance.
				Button button = common == alignment
					? new Button.Primary( "", IconFor( alignment ) )
					: new Button.Clear( "", IconFor( alignment ) );

				button.FixedWidth = 30;
				button.FixedHeight = 26;
				button.ToolTip = alignment.ToString();
				button.Clicked = () => ApplyAlignment( selected, alignment );

				grid.AddCell( x, y, button );
			}
		}

		holder.Layout.Add( grid );

		if ( common == SpriteSheet.PivotAlignment.Custom )
		{
			holder.Layout.Add( new Label( "Placed by hand. Drag the pivot on the sheet, or pick a spot above." )
			{
				WordWrap = true,
				Color = Theme.TextControl.WithAlpha( 0.5f )
			} );
		}

		return holder;
	}

	static string IconFor( SpriteSheet.PivotAlignment alignment ) => alignment switch
	{
		SpriteSheet.PivotAlignment.TopLeft => "north_west",
		SpriteSheet.PivotAlignment.Top => "north",
		SpriteSheet.PivotAlignment.TopRight => "north_east",
		SpriteSheet.PivotAlignment.Left => "west",
		SpriteSheet.PivotAlignment.Right => "east",
		SpriteSheet.PivotAlignment.BottomLeft => "south_west",
		SpriteSheet.PivotAlignment.Bottom => "south",
		SpriteSheet.PivotAlignment.BottomRight => "south_east",
		_ => "filter_center_focus"
	};

	void ApplyAlignment( List<SpriteSheet.Slice> selected, SpriteSheet.PivotAlignment alignment )
	{
		_editor.ExecuteUndoableAction( "Set Pivot", () =>
		{
			foreach ( var slice in selected ) slice.SetAlignment( alignment );
		} );

		Rebuild();
	}

	/// <summary>
	/// Turn control-sheet edits into undo entries. The whole resource is snapshotted, which is what
	/// the sprite editor does too - slices are small and it avoids having to describe every possible
	/// edit as its own operation.
	/// </summary>
	void HookUndo( SerializedObject serialized, string title )
	{
		_stateBeforeEdit = _editor.Sheet.Serialize();

		serialized.OnPropertyChanged += prop =>
		{
			if ( prop is null ) return;

			// Renaming has to keep names unique, or two slices become indistinguishable in the list
			// and one of them stops being findable by name.
			if ( prop.Name == nameof( SpriteSheet.Slice.Name ) )
			{
				var slice = _editor.SelectedSlices.FirstOrDefault();
				if ( slice is not null )
				{
					var unique = _editor.Sheet.GetUniqueName( slice.Name, slice );
					if ( unique != slice.Name ) slice.Name = unique;
				}
			}

			var before = _stateBeforeEdit;
			var after = _editor.Sheet.Serialize();

			_editor.UndoStack.Insert( $"{title} ({prop.Name})",
				() => { _editor.Sheet.Deserialize( before ); _editor.OnSheetModified?.Invoke(); },
				() => { _editor.Sheet.Deserialize( after ); _editor.OnSheetModified?.Invoke(); } );

			_stateBeforeEdit = after;

			_editor.SetModified();
			_editor.OnSheetModified?.Invoke();
		};
	}
}
