namespace Sandbox.UI;

//
// Mouse and keyboard. There is one highlight, and both move it: hovering a row highlights it and,
// after a moment, opens its submenu; the keyboard walks it and opens and closes levels. A key
// press takes the highlight off the mouse until the mouse moves again.
//
// A root row - a heading in a bar, say - belongs to whatever it's in: its clicks and hovers pass
// through, and keys its menu doesn't use bubble up from the list to the row and on up the tree.
//
public partial class Menu
{
	/// <summary>
	/// How long the cursor rests on a submenu row before it opens, in seconds.
	/// </summary>
	internal static float SubmenuOpenDelay = 0.1f;

	/// <summary>
	/// How long the cursor can be on another row before an open submenu closes, in seconds.
	/// Long enough to move diagonally into the submenu.
	/// </summary>
	internal static float SubmenuCloseDelay = 0.2f;

	Menu _hoverRow;
	float _hoverSince;

	/// <summary>
	/// The row with the keyboard or hover highlight, in this menu's list.
	/// </summary>
	public Menu Highlighted { get; private set; }

	void SetHighlighted( Menu row )
	{
		if ( Highlighted == row ) return;

		Highlighted?.SetClass( "active", false );
		Highlighted = row;
		Highlighted?.SetClass( "active", true );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );
		if ( ParentMenu is null ) return;

		e.StopPropagation();
		Activate();
	}

	protected override void OnMouseOver( MousePanelEvent e )
	{
		base.OnMouseOver( e );
		MouseOnRow();
	}

	protected override void OnMouseMove( MousePanelEvent e )
	{
		base.OnMouseMove( e );
		MouseOnRow();
	}

	protected override void OnMouseOut( MousePanelEvent e )
	{
		base.OnMouseOut( e );
		ParentMenu?.OnRowLeft( this );
	}

	void MouseOnRow()
	{
		ParentMenu?.OnRowHovered( this );
	}

	/// <summary>
	/// Do what a click on this row does. A submenu row opens, a checkable row toggles, a command
	/// runs and takes the whole menu down with it.
	/// </summary>
	public void Activate()
	{
		if ( IsSeparator || !Enabled ) return;

		if ( ParentMenu is null ) return;

		if ( HasOptions )
		{
			ParentMenu.SetHighlighted( this );
			Open();
			return;
		}

		if ( Checkable )
		{
			Checked = !Checked;
			Toggled?.Invoke( Checked );
		}
		else
		{
			Clicked?.Invoke();
		}

		if ( !StaysOpen ) RootMenu.Close();
	}

	void OnRowHovered( Menu row )
	{
		SetHighlighted( row );

		if ( _hoverRow == row ) return;

		_hoverRow = row;
		_hoverSince = RealTime.Now;
	}

	/// <summary>
	/// The mouse left a row. Its highlight goes with it, and so does the submenu it was about to
	/// open - unless that submenu is already up, which keeps the row lit like Windows does.
	/// </summary>
	void OnRowLeft( Menu row )
	{
		if ( _hoverRow == row ) _hoverRow = null;
		if ( Highlighted == row && !row.IsOpen ) SetHighlighted( null );
	}

	/// <summary>
	/// Runs every frame the list is up. Opens the hovered submenu and closes the one the cursor
	/// left, each after its delay.
	/// </summary>
	void TickHover()
	{
		if ( _hoverRow is null ) return;

		var openChild = _options.FirstOrDefault( x => x.IsOpen );
		var waited = RealTime.Now - _hoverSince;

		if ( _hoverRow.HasOptions && _hoverRow.Enabled && !_hoverRow.IsOpen )
		{
			if ( waited < SubmenuOpenDelay ) return;

			openChild?.Close();
			_hoverRow.Open();
			return;
		}

		if ( openChild is null || openChild == _hoverRow ) return;

		// The cursor made it into the submenu - leave it up
		if ( openChild.ListPanel is { } list && list.HasHovered ) return;

		if ( waited >= SubmenuCloseDelay )
		{
			openChild.Close();
		}
	}

	/// <summary>
	/// A key pressed while this menu's list has focus. Returns whether it was used.
	/// </summary>
	bool OnKey( ButtonEvent e )
	{
		// The keyboard has the highlight now - the mouse gets it back when it moves
		_hoverRow = null;

		switch ( e.Button )
		{
			case "down":
				MoveHighlight( 1 );
				return true;

			case "up":
				MoveHighlight( -1 );
				return true;

			case "right":
				return OpenHighlightedSubmenu();

			case "left":
				if ( ParentMenu is null ) return false;
				Close();
				return true;

			case "enter":
			case "pad_enter":
			case "space":
				if ( !OpenHighlightedSubmenu() ) Highlighted?.Activate();
				return true;

			case "escape":
				Close();
				return true;
		}

		if ( e.Button.Length == 1 && char.IsLetterOrDigit( e.Button[0] ) )
		{
			JumpTo( e.Button[0] );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Open the highlighted row's submenu with its first row highlighted, the way the keyboard
	/// enters one. False if the highlighted row isn't an open-able submenu.
	/// </summary>
	bool OpenHighlightedSubmenu()
	{
		if ( Highlighted is not { HasOptions: true, Enabled: true } submenu ) return false;

		submenu.Open();
		submenu.MoveHighlight( 1 );
		return true;
	}

	/// <summary>
	/// Move the highlight up or down, skipping rows that can't be picked, wrapping at the ends.
	/// </summary>
	void MoveHighlight( int direction )
	{
		var count = _options.Count;
		if ( count == 0 ) return;

		var index = Highlighted is null ? -1 : _options.IndexOf( Highlighted );
		if ( index < 0 && direction < 0 ) index = count;

		for ( int i = 0; i < count; i++ )
		{
			index = (index + direction + count) % count;

			var row = _options[index];
			if ( row.IsSeparator || !row.Enabled ) continue;

			SetHighlighted( row );
			return;
		}
	}

	/// <summary>
	/// Highlight the next row after the current one whose text starts with this letter.
	/// </summary>
	void JumpTo( char letter )
	{
		var count = _options.Count;
		var start = Highlighted is null ? -1 : _options.IndexOf( Highlighted );

		for ( int i = 1; i <= count; i++ )
		{
			var row = _options[(start + i) % count];
			if ( row.IsSeparator || !row.Enabled ) continue;
			if ( string.IsNullOrEmpty( row.Text ) ) continue;
			if ( char.ToLowerInvariant( row.Text[0] ) != char.ToLowerInvariant( letter ) ) continue;

			SetHighlighted( row );
			return;
		}
	}
}
