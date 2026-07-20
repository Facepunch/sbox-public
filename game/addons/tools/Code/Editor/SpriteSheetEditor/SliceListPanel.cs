using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.SpriteSheetEditor;

/// <summary>
/// Every slice on the sheet, and somewhere to rename them.
/// <para>
/// Renaming is not a tidying-up job here - a modular rig is assembled by name, so the parts have to
/// become <c>L_Thigh</c> and <c>R_Calf</c> before any of it is useful. Enter commits and drops into
/// the next one, so a whole character can be labelled without touching the mouse.
/// </para>
/// </summary>
public class SliceListPanel : Widget
{
	readonly Window _editor;

	LineEdit _filter;
	ScrollArea _scroller;
	Label _summary;

	readonly List<SliceRow> _rows = new();

	public SliceListPanel( Window editor ) : base( null )
	{
		_editor = editor;

		Name = "Slices";
		WindowTitle = "Slices";
		SetWindowIcon( "list" );

		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		var header = Layout.AddRow();
		header.Spacing = 4;

		_filter = new LineEdit( this ) { PlaceholderText = "Filter" };
		_filter.TextChanged += _ => Rebuild();
		header.Add( _filter );

		_summary = new Label( "" ) { Color = Theme.TextControl.WithAlpha( 0.6f ) };
		header.Add( _summary );

		_scroller = new ScrollArea( this );
		_scroller.Canvas = new Widget();
		_scroller.Canvas.Layout = Layout.Column();
		_scroller.Canvas.Layout.Spacing = 1;
		_scroller.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		_scroller.Canvas.HorizontalSizeMode = SizeMode.Flexible;

		Layout.Add( _scroller, 1 );

		_editor.OnSheetLoaded += Rebuild;
		_editor.OnSheetModified += Rebuild;
		_editor.OnSelectionChanged += SyncSelection;

		Rebuild();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		_editor.OnSheetLoaded -= Rebuild;
		_editor.OnSheetModified -= Rebuild;
		_editor.OnSelectionChanged -= SyncSelection;
	}

	void Rebuild()
	{
		// Rebuilding while someone is mid-rename would yank the box out from under them.
		if ( _rows.Any( r => r.IsEditing ) ) return;

		var layout = _scroller.Canvas.Layout;
		layout.Clear( true );
		_rows.Clear();

		var slices = _editor.Sheet?.Slices ?? new List<SpriteSheet.Slice>();
		var filter = _filter?.Text ?? "";

		var visible = string.IsNullOrWhiteSpace( filter )
			? slices
			: slices.Where( s => s.Name.Contains( filter, StringComparison.OrdinalIgnoreCase ) ).ToList();

		foreach ( var slice in visible )
		{
			var row = new SliceRow( _editor, this, slice );
			layout.Add( row );
			_rows.Add( row );
		}

		layout.AddStretchCell();

		_summary.Text = visible.Count == slices.Count
			? $"{slices.Count}"
			: $"{visible.Count} of {slices.Count}";

		SyncSelection();
	}

	void SyncSelection()
	{
		foreach ( var row in _rows ) row.UpdateSelected();
	}

	/// <summary>
	/// Move editing to the row after this one, so a sheet can be labelled straight down the list.
	/// </summary>
	public void EditNextAfter( SliceRow row )
	{
		var index = _rows.IndexOf( row );
		if ( index < 0 || index + 1 >= _rows.Count ) return;

		_rows[index + 1].BeginEditing();
	}
}

/// <summary>
/// One row: a name you can edit in place, and its size.
/// </summary>
public class SliceRow : Widget
{
	readonly Window _editor;
	readonly SliceListPanel _panel;
	readonly Guid _sliceId;

	LineEdit _name;
	Label _size;
	bool _selected;

	public bool IsEditing { get; private set; }

	public SliceRow( Window editor, SliceListPanel panel, SpriteSheet.Slice slice ) : base( null )
	{
		_editor = editor;
		_panel = panel;
		_sliceId = slice.Id;

		FixedHeight = 24;

		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( 6, 0, 6, 0 );
		Layout.Spacing = 6;

		_name = new LineEdit( slice.Name, this );
		_name.MinimumWidth = 120;
		_name.EditingFinished += CommitName;
		_name.ReturnPressed += () => { CommitName(); _panel.EditNextAfter( this ); };
		Layout.Add( _name, 1 );

		_size = new Label( $"{(int)slice.Rect.Width} x {(int)slice.Rect.Height}" )
		{
			Color = Theme.TextControl.WithAlpha( 0.5f )
		};
		Layout.Add( _size );

		UpdateSelected();
	}

	SpriteSheet.Slice Slice => _editor.Sheet?.GetSlice( _sliceId );

	public void BeginEditing()
	{
		_editor.Select( Slice );

		IsEditing = true;
		_name.Focus();
		_name.SelectAll();
	}

	void CommitName()
	{
		IsEditing = false;

		var slice = Slice;
		if ( slice is null ) return;

		var wanted = _name.Text?.Trim();
		if ( string.IsNullOrWhiteSpace( wanted ) || wanted == slice.Name )
		{
			_name.Text = slice.Name;
			return;
		}

		var unique = _editor.Sheet.GetUniqueName( wanted, slice );

		_editor.ExecuteUndoableAction( "Rename Slice", () => slice.Name = unique );

		_name.Text = unique;
	}

	public void UpdateSelected()
	{
		_selected = _editor.Selection.Contains( _sliceId );
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( !e.LeftMouseButton ) return;

		_editor.Select( Slice, add: e.HasCtrl || e.HasShift );
	}

	protected override void OnPaint()
	{
		if ( _selected )
		{
			Paint.SetBrushAndPen( Theme.Primary.WithAlpha( 0.25f ) );
			Paint.DrawRect( LocalRect );
		}
		else if ( Paint.HasMouseOver )
		{
			Paint.SetBrushAndPen( Color.White.WithAlpha( 0.05f ) );
			Paint.DrawRect( LocalRect );
		}
	}
}
