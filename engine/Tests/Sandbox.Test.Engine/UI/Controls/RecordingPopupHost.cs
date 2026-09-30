using Sandbox.UI;
using System.Collections.Generic;

namespace UITests.Controls;

/// <summary>
/// A popup host that records what it was asked and parents popups into a root of its own, the
/// way a window would.
/// </summary>
sealed class RecordingPopupHost : IPopupHost
{
	public readonly RootPanel Window = new();
	public readonly List<Popup> Shown = new();
	public Panel ShownFor;
	public Popup.PositionMode ShownAt;
	public int HideCount;

	public void ShowPopup( Popup popup, Panel source, Popup.PositionMode position, float offset )
	{
		Shown.Add( popup );
		ShownFor = source;
		ShownAt = position;
		popup.Parent = Window;
	}

	public void HidePopup( Popup popup )
	{
		Shown.Remove( popup );
		HideCount++;
	}
}
