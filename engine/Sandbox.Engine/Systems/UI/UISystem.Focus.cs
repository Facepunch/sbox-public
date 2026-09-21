using Sandbox.UI;

namespace Sandbox;

partial class UISystem
{
	/// <summary>
	/// Give focus to this panel, or the nearest ancestor that accepts it. The change doesn't
	/// land until the next tick.
	/// </summary>
	internal bool SetFocus( Panel panel )
	{
		if ( panel is null ) return false;
		if ( NextFocus == panel ) return true;

		//
		// Note that we're not judging eligibility based on styles here. That happens in the tick,
		// because those styles might not have been calculated yet.
		//

		if ( panel.AcceptsFocus )
		{
			NextFocus = panel;
			FocusPendingChange = true;
			return true;
		}

		return SetFocus( panel.Parent );
	}

	/// <summary>
	/// Take focus away from this panel, giving it to its parent if that'll have it.
	/// </summary>
	internal bool ClearFocus( Panel panel )
	{
		NextFocus = null;
		FocusPendingChange = true;

		SetFocus( panel?.Parent );

		return true;
	}

	/// <summary>
	/// Take focus away from whatever has it.
	/// </summary>
	internal bool ClearFocus()
	{
		if ( CurrentFocus is null )
			return false;

		NextFocus = null;
		FocusPendingChange = true;
		return true;
	}

	/// <summary>
	/// Releases current and pending focus before a subtree leaves this system. The blur event travels with the panel.
	/// </summary>
	internal void ReleaseFocusSubtree( Panel subtree )
	{
		if ( NextFocus?.AncestorsAndSelf.Contains( subtree ) == true )
		{
			NextFocus = null;
			FocusPendingChange = false;
		}

		if ( CurrentFocus?.AncestorsAndSelf.Contains( subtree ) != true ) return;
		var focused = CurrentFocus;
		CurrentFocus = null;
		Panel.Switch( PseudoClass.Focus, false, focused );
		focused.CreateEvent( new PanelEvent( "onblur", focused ) );
	}

	/// <summary>
	/// Settle the focus for this frame - drop it if it's become ineligible, then move it to
	/// whatever asked for it, sending blur and focus events on the way.
	/// </summary>
	internal void TickFocus()
	{
		//
		// If our focus became ineligible then defocus
		//
		if ( CurrentFocus is not null && !IsEligibleForFocus( CurrentFocus ) )
		{
			if ( !FocusPendingChange || NextFocus == CurrentFocus )
			{
				NextFocus = null;
				FocusPendingChange = true;
			}
		}

		//
		// Don't swap to an ineligible panel
		//
		if ( FocusPendingChange && NextFocus is not null && !NextFocus.AcceptsFocus )
		{
			NextFocus = null;
			FocusPendingChange = false;
		}

		if ( FocusPendingChange )
		{
			FocusPendingChange = false;

			if ( CurrentFocus != NextFocus )
			{
				if ( CurrentFocus is not null )
				{
					Panel.Switch( PseudoClass.Focus, false, CurrentFocus, NextFocus );
					CurrentFocus.CreateEvent( new PanelEvent( "onblur", CurrentFocus ) );
				}

				CurrentFocus = NextFocus;

				Panel.Switch( PseudoClass.Focus, true, CurrentFocus, null );
				CurrentFocus?.CreateEvent( new PanelEvent( "onfocus", CurrentFocus ) );

			}
		}

		NextFocus = null;
	}

	static bool IsEligibleForFocus( Panel panel )
	{
		if ( !panel.IsVisible ) return false;
		if ( !panel.AcceptsFocus ) return false;

		return true;
	}
}
