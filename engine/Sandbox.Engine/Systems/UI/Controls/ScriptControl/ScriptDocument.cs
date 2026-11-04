using System;
using System.Globalization;

namespace Sandbox.UI;

/// <summary>
/// Maps the scripting API's UTF-16 offsets to the UI's text-element positions.
/// </summary>
internal sealed class ScriptDocument
{
	public string Text { get; }
	public int[] Elements { get; }
	public int[] Lines { get; }

	public ScriptDocument( string text )
	{
		Text = text ?? "";
		Elements = StringInfo.ParseCombiningCharacters( Text );
		var lines = new List<int> { 0 };
		for ( var i = 0; i < Text.Length; i++ )
		{
			if ( Text[i] == '\n' )
			{
				lines.Add( i + 1 );
			}
		}
		Lines = lines.ToArray();
	}

	public int ToOffset( int element ) => element >= Elements.Length ? Text.Length : Elements[Math.Max( 0, element )];

	public int ToElement( int offset )
	{
		if ( offset >= Text.Length )
		{
			return Elements.Length;
		}

		// An offset inside a combining sequence belongs to that whole text element.
		var i = Array.BinarySearch( Elements, Math.Max( 0, offset ) );
		return i >= 0 ? i : Math.Max( 0, ~i - 1 );
	}

	public int LineAt( int offset )
	{
		var i = Array.BinarySearch( Lines, Math.Clamp( offset, 0, Text.Length ) );
		return i >= 0 ? i : ~i - 1;
	}

	public int LineEnd( int line, bool includeNewline = false )
		=> line + 1 < Lines.Length ? Lines[line + 1] - (includeNewline ? 0 : 1) : Text.Length;
}
