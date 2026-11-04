namespace Sandbox.UI;

internal sealed partial class PanelLayout
{
	// Shared with our inline descendants, or borrowed from an ancestor.
	private InlineFormattingContext _inlineContext;

	internal InlineFormattingContext InlineContext => _inlineContext;
	internal bool HasInlineContent => _inlineContext?.Root == this;
	internal bool IsInlineParticipant => _inlineContext is not null && _inlineContext.Root != this;
	internal string SelectedInlineText => _inlineContext?.SelectedText;

	internal void PrepareInlineContent()
	{
		if ( !InlineFormattingContext.CanFormat( _panel ) )
		{
			if ( HasInlineContent ) ReleaseInlineContext();
			return;
		}

		if ( !HasInlineContent )
		{
			ReleaseInlineContext();
			_inlineContext = new InlineFormattingContext( _panel );
		}

		_inlineContext.Update();
		Node.InlineContent = _inlineContext;
	}

	internal void JoinInlineContext( InlineFormattingContext context )
	{
		if ( _inlineContext == context ) return;

		ReleaseInlineContext();
		_inlineContext = context;
	}

	internal void LeaveInlineContext( InlineFormattingContext context )
	{
		// Reparenting may have already moved us to another context.
		if ( _inlineContext != context ) return;

		_inlineContext = null;
		_node?.MarkDirty();
	}

	private void ReleaseInlineContext()
	{
		var context = _inlineContext;
		if ( context is null ) return;

		_inlineContext = null;

		if ( context.Root == this )
		{
			Node.InlineContent = null;
			context.Dispose();
		}
		else
		{
			context.Invalidate();
		}
	}

	internal void FinalizeInlineContent()
	{
		if ( !HasInlineContent ) return;

		_inlineContext.FinalizeLayout();
	}

	internal void DrawInlineContent( Painter painter )
	{
		if ( !HasInlineContent ) return;

		_inlineContext.Draw( painter );
	}

	internal bool ContainsInlineContent( Vector2 position )
	{
		return _inlineContext.Contains( _panel, position );
	}

	internal bool SelectInlineText( Vector2 start, Vector2 end )
	{
		if ( !HasInlineContent ) return false;

		_inlineContext.Select( start, end );
		return true;
	}

	internal bool SetInlineSelection( int start, int end )
	{
		if ( !HasInlineContent ) return false;

		_inlineContext.SetSelection( start, end );
		return true;
	}
}
