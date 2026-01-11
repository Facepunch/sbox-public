// Description: Html Agility Pack - HTML Parsers, selectors, traversors, manupulators.
// Website & Documentation: http://html-agility-pack.net
// Forum & Issues: https://github.com/zzzprojects/html-agility-pack
// License: https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE
// More projects: http://www.zzzprojects.com/
// Copyright � ZZZ Projects Inc. 2014 - 2017. All rights reserved.

namespace Sandbox.Html
{
	/// <summary>
	/// Represents a parsing error found during document parsing.
	/// </summary>
	internal class ParseError
	{
		#region Fields

		private ParseErrorCode _code;
		private int _line;
		private int _linePosition;
		private string _reason;
		private string _sourceText;
		private int _streamPosition;

		#endregion

		#region Constructors

		internal ParseError(
			ParseErrorCode code,
			int line,
			int linePosition,
			int streamPosition,
			string sourceText,
			string reason )
		{
			_code = code;
			_line = line;
			_linePosition = linePosition;
			_streamPosition = streamPosition;
			_sourceText = sourceText;
			_reason = reason;
		}

		#endregion

		#region Properties

		/// <summary>
		/// Gets the type of error.
		/// </summary>
		public ParseErrorCode Code
		{
			get { return _code; }
		}

		/// <summary>
		/// Gets the line number of this error in the document.
		/// </summary>
		public int Line
		{
			get { return _line; }
		}

		/// <summary>
		/// Gets the column number of this error in the document.
		/// </summary>
		public int LinePosition
		{
			get { return _linePosition; }
		}

		/// <summary>
		/// Gets a description for the error.
		/// </summary>
		public string Reason
		{
			get { return _reason; }
		}

		/// <summary>
		/// Gets the full text of the line containing the error.
		/// </summary>
		public string SourceText
		{
			get { return _sourceText; }
		}

		/// <summary>
		/// Gets the absolute stream position of this error in the document, relative to the start of the document.
		/// </summary>
		public int StreamPosition
		{
			get { return _streamPosition; }
		}

		#endregion
	}
}
