using Sandbox;
using System;
using System.Collections.Generic;

public class ConsoleInput
{
	public Action<string> OnInputText { get; set; }

	string inputString = "";
	List<string> history = new();
	int historyIndex = -1;
	string historyDraft = "";
	string[] statusText = new string[3] { "", "", "" };

	public bool valid => Console.BufferWidth > 0;
	public int lineWidth => Console.BufferWidth - 1;

	public void SetStatus( int index, string value )
	{
		statusText[index] = value;
	}

	public void ClearLine( int numLines )
	{
		Console.CursorLeft = 0;

		for ( int i = 0; i < numLines; i++ )
			Console.WriteLine( "".PadRight( lineWidth ) );

		Console.CursorTop -= numLines;
		Console.CursorLeft = 0;
	}

	public void RedrawInputLine()
	{
		var oldBackgroundColor = Console.BackgroundColor;
		var oldForegroundColor = Console.ForegroundColor;
		Console.CursorVisible = false;

		try
		{
			Console.ForegroundColor = ConsoleColor.White;
			Console.CursorLeft = 0;
			Console.WriteLine( "".PadRight( lineWidth ) );

			for ( int i = 0; i < statusText.Length; i++ )
			{
				Console.CursorLeft = 0;
				Console.WriteLine( statusText[i].PadRight( lineWidth ) );
			}

			Console.CursorTop -= statusText.Length + 1;
			Console.CursorLeft = 0;

			Console.BackgroundColor = ConsoleColor.Black;
			Console.ForegroundColor = ConsoleColor.Green;

			ClearLine( 1 );

			if ( inputString.Length == 0 )
			{
				// do nothing
			}
			else if ( inputString.Length < lineWidth - 2 )
			{
				Console.Write( inputString );
			}
			else
			{
				Console.Write( inputString.Substring( inputString.Length - (lineWidth - 2) ) );
			}
		}
		catch { }

		Console.CursorVisible = true;
		Console.BackgroundColor = oldBackgroundColor;
		Console.ForegroundColor = oldForegroundColor;
	}

	internal void OnBackspace()
	{
		if ( inputString.Length < 1 ) return;

		inputString = inputString.Substring( 0, inputString.Length - 1 );
		RedrawInputLine();
	}

	internal void OnEscape()
	{
		inputString = "";
		historyIndex = -1;
		historyDraft = "";
		RedrawInputLine();
	}

	internal void OnEnter()
	{
		ClearLine( statusText.Length );
		Console.ForegroundColor = ConsoleColor.Green;
		Console.WriteLine( "> " + inputString );

		var strtext = inputString;
		inputString = "";
		historyIndex = -1;
		historyDraft = "";

		if ( !string.IsNullOrWhiteSpace( strtext ) && (history.Count == 0 || history[^1] != strtext) )
			history.Add( strtext );

		if ( OnInputText != null )
		{
			OnInputText( strtext );
		}

		RedrawInputLine();
	}

	internal void OnHistoryUp()
	{
		if ( history.Count == 0 ) return;

		if ( historyIndex == -1 )
		{
			historyDraft = inputString;
			historyIndex = history.Count - 1;
		}
		else if ( historyIndex > 0 )
		{
			historyIndex--;
		}

		inputString = history[historyIndex];
		RedrawInputLine();
	}

	internal void OnHistoryDown()
	{
		if ( historyIndex == -1 ) return;

		historyIndex++;

		if ( historyIndex >= history.Count )
		{
			historyIndex = -1;
			inputString = historyDraft;
		}
		else
		{
			inputString = history[historyIndex];
		}

		RedrawInputLine();
	}

	internal float nextUpdate = 0;

	public void Update()
	{
		if ( !valid )
			return;

		if ( nextUpdate < RealTime.Now )
		{
			RedrawInputLine();
			nextUpdate = RealTime.Now + 0.5f;
		}

		try
		{
			if ( !Console.KeyAvailable ) return;
		}
		catch ( Exception )
		{
			return;
		}

		var key = Console.ReadKey();

		if ( key.Key == ConsoleKey.Enter )
		{
			OnEnter();
			return;
		}

		if ( key.Key == ConsoleKey.Backspace )
		{
			OnBackspace();
			return;
		}

		if ( key.Key == ConsoleKey.Escape )
		{
			OnEscape();
			return;
		}

		if ( key.Key == ConsoleKey.UpArrow )
		{
			OnHistoryUp();
			return;
		}

		if ( key.Key == ConsoleKey.DownArrow )
		{
			OnHistoryDown();
			return;
		}

		if ( key.KeyChar != '\u0000' )
		{
			inputString += key.KeyChar;
			RedrawInputLine();
			return;
		}
	}
}
