// RichTextKit
// Copyright © 2019-2020 Topten Software. All Rights Reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may 
// not use this product except in compliance with the License. You may obtain 
// a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software 
// distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
// WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
// License for the specific language governing permissions and limitations 
// under the License.

using System;
using System.IO;
using System.Xml.Schema;

namespace Topten.RichTextKit
{
	/// <summary>
	/// Represents a range of code points in a text document
	/// </summary>
	public struct TextRange
	{
		/// <summary>
		/// Initializes a TextRange
		/// </summary>
		/// <param name="start">The code point index of the start of the range</param>
		/// <param name="end">The code point index of the end of the range</param>
		/// <param name="altPosition">Whether the caret at the end of the range should be displayed in its alternative position</param>
		public TextRange( int start, int end, bool altPosition = false )
		{
			Start = start;
			End = end;
			AltPosition = altPosition;
		}

		/// <summary>
		/// Initializes a TextRange with a non-range position
		/// </summary>
		/// <param name="position">The code point index of the position</param>
		/// <param name="altPosition">Whether the caret should be displayed in its alternative position</param>
		public TextRange( int position, bool altPosition = false )
		{
			Start = position;
			End = position;
			AltPosition = altPosition;
		}

		/// <summary>
		/// Initializes a TextRange from a caret position
		/// </summary>
		/// <param name="position">The code point index of the position</param>
		public TextRange( CaretPosition position )
		{
			Start = position.CodePointIndex;
			End = position.CodePointIndex;
			AltPosition = position.AltPosition;
		}

		/// <summary>
		/// The code point index of the start of the range
		/// </summary>
		public int Start;

		/// <summary>
		/// The code point index of the end of the range
		/// </summary>
		public int End;

		/// <summary>
		/// True if the end of the range should be displayed
		/// with the caret in the alt position
		/// </summary>
		public bool AltPosition;

		/// <summary>
		/// Get the length of this range
		/// </summary>
		/// <remarks>
		/// Will return negative if the range isn't normalized
		/// </remarks>
		public int Length => End - Start;

		/// <summary>
		/// Offset this text range by the specified amount
		/// </summary>
		/// <param name="delta">The number of code points to offset the range by</param>
		/// <returns>A new TextRange</returns>
		public TextRange Offset( int delta )
		{
			return new TextRange( Start + delta, End + delta, AltPosition );
		}

		/// <summary>
		/// Returns the reversed text range
		/// </summary>
		public TextRange Reversed
		{
			get
			{
				return new TextRange( End, Start, false );
			}
		}

		/// <summary>
		/// Returns the normalized version of the text range
		/// </summary>
		public TextRange Normalized
		{
			get
			{
				if ( Start > End )
					return Reversed;
				else
					return this;
			}
		}

		/// <summary>
		/// Compare this text range to another for equality
		/// </summary>
		/// <param name="other">The text range to compare to</param>
		/// <returns>True if the text ranges are equal</returns>
		public bool IsEqual( TextRange other )
		{
			return other.Start == Start &&
				   other.End == End &&
				   other.AltPosition == AltPosition;
		}

		/// <summary>
		/// Check if this is actually a range
		/// </summary>
		public bool IsRange => Start != End;

		/// <summary>
		/// Get the end of the range closer to the start of the document
		/// </summary>
		public int Minimum => Math.Min( Start, End );

		/// <summary>
		/// Get the end of the range closer to the end of the document
		/// </summary>
		public int Maximum => Math.Max( Start, End );

		/// <summary>
		/// Gets the end of the range as a caret position
		/// </summary>
		public CaretPosition CaretPosition
		{
			get
			{
				return new CaretPosition( End, AltPosition );
			}
		}

		/// <summary>
		/// Clamp the text range to a document length
		/// </summary>
		/// <param name="maxCodePointIndex">The max code point index</param>
		/// <returns>A clamped TextRange</returns>
		public TextRange Clamp( int maxCodePointIndex )
		{
			var newStart = Start;
			if ( newStart < 0 )
				newStart = 0;
			if ( newStart > maxCodePointIndex )
				newStart = maxCodePointIndex;

			var newEnd = End;
			if ( newEnd < 0 )
				newEnd = 0;
			if ( newEnd > maxCodePointIndex )
				newEnd = maxCodePointIndex;

			return new TextRange( newStart, newEnd, AltPosition );
		}


		/// <summary>
		/// Create an updated text range that tries to represent the same
		/// piece of text from before an edit to after the edit.
		/// </summary>
		/// <param name="codePointIndex">The position of the edit</param>
		/// <param name="oldLength">The length of text deleted</param>
		/// <param name="newLength">The length of text inserted</param>
		/// <returns>An updated text range</returns>
		public TextRange UpdateForEdit( int codePointIndex, int oldLength, int newLength )
		{
			int delta = newLength - oldLength;

			// After this range?
			if ( codePointIndex > Maximum )
				return this;

			// Before this range?
			if ( codePointIndex + oldLength <= Minimum )
				return new TextRange( Start + delta, End + delta, AltPosition );

			// Entire range?
			if ( codePointIndex <= Minimum && codePointIndex + oldLength >= Maximum )
				return new TextRange( codePointIndex, codePointIndex, AltPosition );

			// Inside this range?
			if ( codePointIndex >= Minimum && codePointIndex + oldLength <= Maximum )
			{
				if ( Start < End )
				{
					return new TextRange( Start, End + delta, AltPosition );
				}
				else
				{
					return new TextRange( Start + delta, End, AltPosition );
				}
			}

			// Overlap start of this range?
			if ( codePointIndex < Minimum && codePointIndex + oldLength >= Minimum )
			{
				if ( Start < End )
					return new TextRange( codePointIndex + newLength, End + delta, AltPosition );
				else
					return new TextRange( Start + delta, codePointIndex + newLength, AltPosition );
			}

			// Overlap end of this range?
			if ( codePointIndex >= Minimum && codePointIndex + oldLength > Maximum )
			{
				if ( Start < End )
					return new TextRange( Start, codePointIndex, AltPosition );
				else
					return new TextRange( codePointIndex, End, AltPosition );
			}

			// Should never get here.
			return new TextRange();
		}

		/// <summary>
		/// Create a new range that is the union of two other ranges.  The
		/// union is the smallest range that contains the other two other 
		/// ranges.
		/// </summary>
		/// <remarks>
		/// The returned range is configured such that the 'b' range
		/// is used for the end position (ie: the caret)
		/// </remarks>
		/// <param name="a">The first text range</param>
		/// <param name="b">The second text range</param>
		/// <returns>A range that encompasses both ranges</returns>
		public static TextRange Union( TextRange a, TextRange b )
		{
			if ( a.Minimum <= b.Minimum )
			{
				return new TextRange(
					Math.Min( a.Minimum, b.Minimum ),
					Math.Max( a.Maximum, b.Maximum ), b.AltPosition );
			}
			else
			{
				return new TextRange(
					Math.Max( a.Maximum, b.Maximum ),
					Math.Min( a.Minimum, b.Minimum ), b.AltPosition );
			}
		}

		/// <inheritdoc />
		public override string ToString()
		{
			return $"{Start} → {End} (len: {Length})";
		}
	}
}
