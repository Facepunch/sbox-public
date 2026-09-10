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

namespace Topten.RichTextKit
{
	/// <summary>
	/// Species the alignment of text within a text block
	/// </summary>
	public enum TextAlignment
	{
		/// <summary>
		/// Use base direction of the text block.
		/// </summary>
		Auto,

		/// <summary>
		/// Left-aligns text to a x-coord of 0.
		/// </summary>
		Left,

		/// <summary>
		/// Center aligns text between 0 and <see cref="TextBlock.MaxWidth"/> unless not
		/// specified in which case it uses the widest line in the text block.
		/// </summary>
		Center,

		/// <summary>
		/// Right aligns text <see cref="TextBlock.MaxWidth"/> unless not
		/// specified in which case it right aligns to the widest line in the text block.
		/// </summary>
		Right,

		/// <summary>
		/// Justifies text by spreading the spaces on each line (except the last line of a paragraph)
		/// so the line fills <see cref="TextBlock.MaxWidth"/>. Only left-to-right content is justified.
		/// </summary>
		Justify,
	}
}
