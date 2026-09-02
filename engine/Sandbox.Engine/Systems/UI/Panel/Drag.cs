using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.UI;

/// <summary>
/// An outgoing OS drag from a panel - files and/or text the user can drop on anything that
/// takes them, other windows, other apps and the desktop included. Fill it in, then
/// <see cref="Start"/>.
/// </summary>
public class Drag
{
	readonly Panel _panel;
	readonly List<string> _files = new();
	string _text;

	public Drag( Panel panel )
	{
		_panel = panel;
	}

	/// <summary>
	/// Carry this file. Call again to carry more than one.
	/// </summary>
	public void SetFile( string path )
	{
		_files.Add( path );
	}

	/// <summary>
	/// Carry this text.
	/// </summary>
	public void SetText( string text )
	{
		_text = text;
	}

	/// <summary>
	/// Start the drag. Blocks while the user drags it around - the panel windows keep
	/// painting through it - and returns what the receiver did with the payload once
	/// they let go. None when it was cancelled or nothing was carried.
	/// </summary>
	public DropAction Start()
	{
		if ( _files.Count == 0 && string.IsNullOrEmpty( _text ) )
			return DropAction.None;

		// The drag runs from the OS window the panel lives in, when it lives in one - the
		// payload works either way, the window just anchors the operation
		var root = _panel?.FindRootPanel();
		var window = PanelWindows.All.FirstOrDefault( x => x.Surface.Root == root );

		// This blocks from inside a frame - the mouse event that started it - and the OS drag
		// loop's pulses are the only frames until it lets go, so they have to be allowed to
		// run nested. Without this nothing repaints and the drag looks frozen.
		if ( window is not null ) window.AllowNestedFrame = true;

		try
		{
			return (DropAction)NativeEngine.PanelWindowNative.BeginDrag( window?.Handle ?? IntPtr.Zero, string.Join( '\n', _files ), _text ?? "" );
		}
		finally
		{
			if ( window is not null ) window.AllowNestedFrame = false;
		}
	}
}
