namespace Sandbox.UI;

public partial class ScriptControl
{
	/// <summary>
	/// Close completion, parameter help and hover documentation.
	/// </summary>
	public void DismissIntelliSense()
	{
		CloseCompletion();
		CloseHover();
		CloseSignature();
	}

	IntelliSensePopup CreatePopup( string className, bool ignoresInput )
	{
		var popup = new IntelliSensePopup( this )
		{
			TakesKeyboardFocus = false,
			IgnoresInput = ignoresInput,
			CloseWhenParentIsHidden = true
		};
		popup.AddClass( $"script-control-popup intellisense {className}" );
		popup.StyleSheet.Add( Sandbox.UI.StyleSheet.FromInline( Styles, "scriptcontrol" ) );
		popup.StyleSheet.Add( _themeStyleSheet );
		return popup;
	}

	void UpdatePopupTheme( IntelliSensePopup popup, StyleSheet previous )
	{
		if ( popup is null ) return;
		popup.StyleSheet.Remove( previous );
		popup.StyleSheet.Add( _themeStyleSheet );
	}

	void PositionPopups()
	{
		if ( UISystem is null || Entry.Box.Rect.Width <= 0 ) return;

		PlacePopup( _completionPopup, SourceCaretRect( _replacementSpan.Start ), above: false );
		if ( _completionPopup?.PopupSource is not null && _completionDetail is not null && _completionPopup.Box.Rect.Width > 0 )
		{
			_completionDetail.AnchorRect = _completionPopup.Box.Rect;
			if ( _completionDetail.PopupSource is null )
				_completionDetail.SetPositioning( _completionPopup, Popup.PositionMode.RightTop, 4 );
		}
		PlacePopup( _signaturePopup, SourceCaretRect( CaretOffset ), above: true );
		if ( _hoverPopup is not null )
			PlacePopup( _hoverPopup, SourceCaretRect( _hoverOffset ), above: true );
	}

	void PlacePopup( IntelliSensePopup popup, Rect anchor, bool above )
	{
		if ( popup is null ) return;
		popup.AnchorRect = anchor;
		if ( popup.PopupSource is null )
			popup.SetPositioning( Entry, above ? Popup.PositionMode.AboveLeft : Popup.PositionMode.BelowLeft, 4 );
	}

	public override void OnDeleted()
	{
		DismissIntelliSense();
		base.OnDeleted();
	}

	sealed class IntelliSensePopup( ScriptControl editor ) : Popup
	{
		public override void Tick()
		{
			if ( !editor.IsValid() || !editor.IsVisible )
			{
				Delete( true );
				return;
			}
			base.Tick();
		}

		public override void Delete( bool immediate = false )
		{
			if ( editor._completionPopup == this )
			{
				editor._completionPopup = null;
				editor._completions.Clear();
				editor.CloseCompletionDetail();
			}
			if ( editor._signaturePopup == this )
			{
				editor._signaturePopup = null;
				editor._signature = null;
			}
			if ( editor._completionDetail == this ) editor._completionDetail = null;
			if ( editor._hoverPopup == this ) editor._hoverPopup = null;
			base.Delete( true );
		}
	}
}
