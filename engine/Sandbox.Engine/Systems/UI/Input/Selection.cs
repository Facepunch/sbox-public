namespace Sandbox.UI;

internal class Selection
{
	Panel SelectionStart;
	Vector2 SelectionStartPos;
	Vector2 SelectionEndPos;
	BaseVirtualPanel SelectionList;

	public void UpdateSelection( Panel root, Panel hovered, bool dragging, bool started, bool ended, Vector2 pos )
	{
		if ( SelectionList is { IsValid: false } )
			SelectionList = null;

		if ( started )
		{
			SelectionList?.UnselectAllInChildren();
			SelectionList = null;
			SelectionStart = null;

			if ( hovered == null )
				return;

			ClearSelection();

			SelectionStart = hovered;
			for ( var panel = hovered; panel != null; panel = panel.Parent )
			{
				if ( panel is BaseVirtualPanel list && list.AllowChildSelection )
				{
					SelectionList = list;
					list.BeginSelection( hovered );
					break;
				}
			}

			SelectionStartPos = SelectionStart.ScreenPositionToPanelPosition( pos );
			SelectionEndPos = SelectionStartPos;
			return;
		}

		if ( SelectionStart == null )
			return;

		if ( dragging || ended )
		{
			var hash = HashCode.Combine( SelectionStart, SelectionStartPos, SelectionEndPos );

			SelectionEndPos = SelectionStart.ScreenPositionToPanelPosition( pos );
			var newHash = HashCode.Combine( SelectionStart, SelectionStartPos, SelectionEndPos );
			if ( newHash != hash )
			{
				SelectionEvent e = new SelectionEvent( "ondragselect", SelectionStart );
				e.StartPoint = SelectionStart.PanelPositionToScreenPosition( SelectionStartPos );
				e.EndPoint = SelectionStart.PanelPositionToScreenPosition( SelectionEndPos );
				e.SelectionRect = new Rect( e.StartPoint );
				e.SelectionRect = e.SelectionRect.AddPoint( e.EndPoint );

				SelectionStart.CreateEvent( e );
				if ( ended && SelectionList != null )
					SelectionStart.RunPendingEvents();
			}
		}

		if ( ended )
			SelectionList?.EndSelection();
	}

	internal void EndSelection()
	{
		SelectionList?.EndSelection();
		SelectionList = null;
	}

	void ClearSelection()
	{

	}
}
