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

using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Topten.RichTextKit.Utils;

namespace Topten.RichTextKit
{
	/// <summary>
	/// Represents a laid out line of text.
	/// </summary>
	public class TextLine
	{
		/// <summary>
		/// Constructs a new TextLine.
		/// </summary>
		public TextLine()
		{
		}

		/// <summary>
		/// Gets the set of text runs comprising this line.
		/// </summary>
		/// <remarks>
		/// Font runs are order logically (ie: in code point index order)
		/// but may have unordered <see cref="FontRun.XCoord"/>'s when right to
		/// left text is in use.
		/// </remarks>
		public IReadOnlyList<FontRun> Runs => RunsInternal;

		/// <summary>
		/// Gets the text block that owns this line.
		/// </summary>
		public TextBlock TextBlock
		{
			get;
			internal set;
		}

		/// <summary>
		/// Gets the next line in this text block, or null if this is the last line.
		/// </summary>
		public TextLine NextLine
		{
			get
			{
				int index = (TextBlock.Lines as List<TextLine>).IndexOf( this );
				if ( index < 0 || index + 1 >= TextBlock.Lines.Count )
					return null;
				return TextBlock.Lines[index + 1];
			}
		}

		/// <summary>
		/// Gets the previous line in this text block, or null if this is the first line.
		/// </summary>
		public TextLine PreviousLine
		{
			get
			{
				int index = (TextBlock.Lines as List<TextLine>).IndexOf( this );
				if ( index <= 0 )
					return null;
				return TextBlock.Lines[index - 1];
			}
		}

		/// <summary>
		/// Gets the y-coordinate of the top of this line, relative to the top of the text block.
		/// </summary>
		public float YCoord
		{
			get;
			internal set;
		}

		/// <summary>
		/// Gets the base line of this line (relative to <see cref="YCoord"/>)
		/// </summary>
		public float BaseLine
		{
			get;
			internal set;
		}

		/// <summary>
		/// Gets the maximum magnitude ascent of all font runs in this line.
		/// </summary>
		/// <remarks>
		/// The ascent is reported as a negative value from the base line.
		/// </remarks>
		public float MaxAscent
		{
			get;
			internal set;
		}


		/// <summary>
		/// Gets the maximum descent of all font runs in this line.
		/// </summary>
		/// <remarks>
		/// The descent is reported as a positive value from the base line.
		/// </remarks>
		public float MaxDescent
		{
			get;
			internal set;
		}

		/// <summary>
		/// Gets the text height of this line.
		/// </summary>
		/// <remarks>
		/// The text height of a line is the sum of the ascent and desent.
		/// </remarks>
		public float TextHeight => -MaxAscent + MaxDescent;

		/// <summary>
		/// Gets the height of this line
		/// </summary>
		/// <remarks>
		/// The height of a line is based on the font and <see cref="IStyle.LineHeight"/>
		/// value of all runs in this line.
		/// </remarks>
		public float Height
		{
			get;
			internal set;
		}

		/// <summary>
		/// The width of the content on this line, excluding trailing whitespace and overhang.
		/// </summary>
		public float Width
		{
			get;
			internal set;
		}

		/// <summary>
		/// Paint this line's background
		/// </summary>
		/// <param name="ctx">The paint context</param>
		internal void PaintBackground( PaintTextContext ctx )
		{
			foreach ( var r in Runs )
			{
				r.PaintBackground( ctx );
			}
		}

		/// <summary>
		/// Paint this line
		/// </summary>
		/// <param name="ctx">The paint context</param>
		internal void Paint( PaintTextContext ctx )
		{
			foreach ( var r in Runs )
			{
				r.Paint( ctx );
			}
		}

		/// <summary>
		/// Code point index of start of this line
		/// </summary>
		public int Start
		{
			get
			{
				var pl = PreviousLine;
				return PreviousLine == null ? 0 : PreviousLine.End;
			}
		}

		/// <summary>
		/// The length of this line in codepoints
		/// </summary>
		public int Length => End - Start;

		/// <summary>
		/// The code point index of the first character after this line
		/// </summary>
		public int End
		{
			get
			{
				// Get the last run that's not an ellipsis
				var lastRun = this.Runs.LastOrDefault( x => x.RunKind != FontRunKind.Ellipsis );

				// If last run found, then it's the end of the run, other wise it's the start index
				return lastRun == null ? Start : lastRun.End;
			}
		}

		/// <summary>
		/// Hit test this line, working out the cluster the x position is over
		/// and closest to.
		/// </summary>
		/// <remarks>
		/// This method only populates the code point indicies in the returned result
		/// and the line indicies will be -1
		/// </remarks>
		/// <param name="x">The xcoord relative to the text block</param>
		public HitTestResult HitTest( float x )
		{
			var htr = new HitTestResult();
			htr.OverLine = -1;
			htr.ClosestLine = -1;
			HitTest( x, ref htr );
			return htr;
		}


		/// <summary>
		/// Hit test this line, working out the cluster the x position is over
		/// and closest to.
		/// </summary>
		/// <param name="x">The xcoord relative to the text block</param>
		/// <param name="htr">HitTestResult to be filled out</param>
		internal void HitTest( float x, ref HitTestResult htr )
		{
			// Working variables
			float closestXPosition = 0;
			int closestCodePointIndex = -1;

			if ( Runs.Count > 0 )
			{
				// If caret is beyond the end of the line...
				var lastRun = Runs[Runs.Count - 1];
				if ( (lastRun.Direction == TextDirection.LTR && x >= lastRun.XCoord + lastRun.Width) ||
					(lastRun.Direction == TextDirection.RTL && x < lastRun.XCoord) )
				{
					// Special handling for clicking after a soft line break ('\n') in which case
					// the caret should be positioned before the new line character, not after it 
					// as this would cause the cursor to appear on the next line).
					if ( lastRun.RunKind == FontRunKind.TrailingWhitespace || lastRun.RunKind == FontRunKind.Ellipsis )
					{
						if ( lastRun.CodePoints.Length > 0 &&
							(lastRun.CodePoints[lastRun.CodePoints.Length - 1] == '\n') ||
							(lastRun.CodePoints[lastRun.CodePoints.Length - 1] == 0x2029)
							)
						{
							htr.ClosestCodePointIndex = lastRun.End - 1;
							return;
						}
					}
				}
			}

			// Check all runs
			foreach ( var r in Runs )
			{
				// Ignore ellipsis runs
				if ( r.RunKind == FontRunKind.Ellipsis )
					continue;

				if ( x < r.XCoord )
				{
					// Before the run...
					updateClosest( r.XCoord, r.Direction == TextDirection.LTR ? r.Start : r.End, r.Direction );
				}
				else if ( x >= r.XCoord + r.Width )
				{
					// After the run...
					updateClosest( r.XCoord + r.Width, r.Direction == TextDirection.RTL ? r.Start : r.End, r.Direction );
				}
				else
				{
					// Inside the run
					for ( int i = 0; i < r.Clusters.Length; )
					{
						// Get the xcoord of this cluster
						var codePointIndex = r.Clusters[i];
						var xcoord1 = r.GetXCoordOfCodePointIndex( codePointIndex );

						// Find the code point of the next cluster
						var j = i;
						while ( j < r.Clusters.Length && r.Clusters[j] == r.Clusters[i] )
							j++;

						// Get the xcoord of other side of this cluster
						int codePointIndexOther;
						if ( r.Direction == TextDirection.LTR )
						{
							if ( j == r.Clusters.Length )
							{
								codePointIndexOther = r.End;
							}
							else
							{
								codePointIndexOther = r.Clusters[j];
							}
						}
						else
						{
							if ( i > 0 )
							{
								codePointIndexOther = r.Clusters[i - 1];
							}
							else
							{
								codePointIndexOther = r.End;
							}
						}

						// Gethte xcoord of the other side of the cluster
						var xcoord2 = r.GetXCoordOfCodePointIndex( codePointIndexOther );

						// Ensure order correct for easier in-range check
						if ( xcoord1 > xcoord2 )
						{
							var temp = xcoord1;
							xcoord1 = xcoord2;
							xcoord2 = temp;
						}

						// On the character?
						if ( x >= xcoord1 && x < xcoord2 )
						{
							// Store this as the cluster the point is over
							htr.OverCodePointIndex = codePointIndex;

							// Don't move to the rhs (or lhs) of a line break
							if ( r.CodePoints[codePointIndex - r.Start] == '\n' )
							{
								htr.ClosestCodePointIndex = codePointIndex;
							}
							else
							{
								// Work out if position is closer to the left or right side of the cluster
								if ( x < (xcoord1 + xcoord2) / 2 )
								{
									htr.ClosestCodePointIndex = r.Direction == TextDirection.LTR ? codePointIndex : codePointIndexOther;
								}
								else
								{
									htr.ClosestCodePointIndex = r.Direction == TextDirection.LTR ? codePointIndexOther : codePointIndex;
								}
							}
							if ( htr.ClosestCodePointIndex == End )
							{
								htr.AltCaretPosition = true;
							}
							return;
						}

						// Move to the next cluster
						i = j;
					}
				}
			}

			// Store closest character
			htr.ClosestCodePointIndex = closestCodePointIndex;

			if ( htr.ClosestCodePointIndex == End )
			{
				htr.AltCaretPosition = true;
			}

			// Helper for updating closest caret position
			void updateClosest( float xPosition, int codePointIndex, TextDirection dir )
			{
				if ( closestCodePointIndex == -1 || Math.Abs( xPosition - x ) < Math.Abs( closestXPosition - x ) )
				{
					closestXPosition = xPosition;
					closestCodePointIndex = codePointIndex;
				}
			}
		}

		internal void UpdateOverhang( SKRect textRect, ref SKRect overhang )
		{
			foreach ( var r in Runs )
			{
				r.UpdateOverhang( textRect, ref overhang );
			}
		}

		/// <summary>
		/// Internal List of runs
		/// </summary>
		internal List<FontRun> RunsInternal = new List<FontRun>();

		internal static ThreadLocal<ObjectPool<TextLine>> Pool = new ThreadLocal<ObjectPool<TextLine>>( () => new ObjectPool<TextLine>()
		{
			Cleaner = ( r ) =>
			{
				r.TextBlock = null;
				r.RunsInternal.Clear();
			}
		} );
	}
}
