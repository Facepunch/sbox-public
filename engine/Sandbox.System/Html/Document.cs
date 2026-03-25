// Description: Html Agility Pack - HTML Parsers, selectors, traversors, manupulators.
// Website & Documentation: http://html-agility-pack.net
// Forum & Issues: https://github.com/zzzprojects/html-agility-pack
// License: https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE
// More projects: http://www.zzzprojects.com/
// Copyright � ZZZ Projects Inc. 2014 - 2017. All rights reserved.

using System.IO;

namespace Sandbox.Html;

/// <summary>
/// Represents a complete HTML document.
/// </summary>
internal class Document
{
	private int _c;
	private Attribute _currentattribute;
	private Node _currentnode;
	private Node _documentnode;
	private bool _fullcomment;
	private int _index;
	internal Dictionary<string, Node> Lastnodes = new Dictionary<string, Node>();
	private Node _lastparentnode;
	private int _line;
	private int _lineposition, _maxlineposition;
	internal Dictionary<int, Node> Openednodes;
	private List<ParseError> _parseerrors = new List<ParseError>();
	private ParseState _state;

	/// <summary>The HtmlDocument Text. Careful if you modify it.</summary>
	public string Text;

	/// <summary>True to stay backward compatible with previous version of HAP. This option does not guarantee 100% compatibility.</summary>
	public bool BackwardCompatibility = true;

	/// <summary>
	/// Defines if non closed nodes will be checked at the end of parsing. Default is true.
	/// </summary>
	public bool OptionCheckSyntax = true;

	/// <summary>
	/// Defines the maximum length of source text or parse errors. Default is 100.
	/// </summary>
	public int OptionExtractErrorSourceTextMaxLength = 100;

	/// <summary>
	/// The max number of nested child nodes.
	/// Added to prevent stackoverflow problem when a page has tens of thousands of opening html tags with no closing tags
	/// </summary>
	public int OptionMaxNestedChildNodes = 0;


	internal static readonly string HtmlExceptionRefNotChild = "Reference node must be a child of this node";
	internal static readonly string HtmlExceptionUseIdAttributeFalse = "You need to set UseIdAttribute property to true to enable this feature";
	internal static readonly string HtmlExceptionClassDoesNotExist = "Class name doesn't exist";
	internal static readonly string HtmlExceptionClassExists = "Class name already exists";


	/// <summary>
	/// Creates an instance of an HTML document.
	/// </summary>
	public Document()
	{
	}

	/// <summary>Gets the parsed text.</summary>
	/// <value>The parsed text.</value>
	public string ParsedText
	{
		get { return Text; }
	}

	/// <summary>
	/// Defines the max level we would go deep into the html document. If this depth level is exceeded, and exception is
	/// thrown.
	/// </summary>
	internal static int MaxDepthLevel => 256;

	/// <summary>
	/// Gets the root node of the document.
	/// </summary>
	public Node DocumentNode
	{
		get { return _documentnode; }
	}

	/// <summary>
	/// Gets a list of parse errors found in the document.
	/// </summary>
	public IEnumerable<ParseError> ParseErrors
	{
		get { return _parseerrors; }
	}

	/// <summary>
	/// Determines if the specified character is considered as a whitespace character.
	/// </summary>
	/// <param name="c">The character to check.</param>
	/// <returns>true if the specified character is considered as a whitespace character.</returns>
	public static bool IsWhiteSpace( int c )
	{
		if ( (c == 10) || (c == 13) || (c == 32) || (c == 9) )
		{
			return true;
		}

		return false;
	}


	/// <summary>
	/// Loads the HTML document from the specified TextReader.
	/// </summary>
	/// <param name="reader">The TextReader used to feed the HTML data into the document. May not be null.</param>
	public void Load( TextReader reader )
	{
		// all Load methods pass down to this one
		if ( reader == null )
			throw new ArgumentNullException( "reader" );

		if ( OptionCheckSyntax )
			Openednodes = new Dictionary<int, Node>();
		else
			Openednodes = null;

		Text = reader.ReadToEnd();
		_documentnode = CreateNode( NodeType.Document, 0 );
		Parse();

		if ( !OptionCheckSyntax || Openednodes == null ) return;
		foreach ( Node node in Openednodes.Values )
		{
			if ( !node._starttag ) // already reported
			{
				continue;
			}

			AddError(
				ParseErrorCode.TagNotClosed,
				node._line, node._lineposition,
				node._streamposition, string.Empty,
				"End tag </" + node.Name + "> was not found" );
		}

		// we don't need this anymore
		Openednodes.Clear();
	}

	/// <summary>
	/// Loads the HTML document from the specified string.
	/// </summary>
	/// <param name="html">String containing the HTML document to load. May not be null.</param>
	public void LoadHtml( string html )
	{
		if ( html == null )
		{
			throw new ArgumentNullException( "html" );
		}

		using ( StringReader sr = new StringReader( html ) )
		{
			Load( sr );
		}
	}

	internal Node CreateNode( NodeType type, int index )
	{
		switch ( type )
		{
			case NodeType.Text:
				return new TextNode( this, index );

			default:
				return new Node( type, this, index );
		}
	}

	internal void SetIdForNode( Node node, string id )
	{
	}

	internal void UpdateLastParentNode()
	{
		do
		{
			if ( _lastparentnode.Closed )
				_lastparentnode = _lastparentnode.ParentNode;
		} while ( (_lastparentnode != null) && (_lastparentnode.Closed) );

		if ( _lastparentnode == null )
			_lastparentnode = _documentnode;
	}

	private void AddError( ParseErrorCode code, int line, int linePosition, int streamPosition, string sourceText, string reason )
	{
		ParseError err = new ParseError( code, line, linePosition, streamPosition, sourceText, reason );
		_parseerrors.Add( err );
		return;
	}

	private void CloseCurrentNode()
	{
		if ( _currentnode.Closed ) // text or document are by def closed
			return;

		Node prev = Lastnodes.GetValueOrDefault( _currentnode.Name );

		// find last node of this kind
		if ( prev == null )
		{
			throw new System.Exception( "Couldn't find previous tag" );
		}

		Lastnodes[_currentnode.Name] = prev._prevwithsamename;
		prev.CloseNode( _currentnode );

		// we close this node, get grandparent
		if ( _lastparentnode != null )
		{
			UpdateLastParentNode();
		}
	}

	private void DecrementPosition()
	{
		_index--;
		if ( _lineposition == 0 )
		{
			_lineposition = _maxlineposition;
			_line--;
		}
		else
		{
			_lineposition--;
		}
	}

	private void IncrementPosition()
	{
		_index++;
		_maxlineposition = _lineposition;
		if ( _c == 10 )
		{
			_lineposition = 0;
			_line++;
		}
		else
		{
			_lineposition++;
		}
	}

	private bool IsValidTag()
	{
		bool isValidTag = _c == '<' && _index < Text.Length && (Char.IsLetter( Text[_index] ) || Text[_index] == '/' || Text[_index] == '?' || Text[_index] == '!' || Text[_index] == '%');
		return isValidTag;
	}

	private bool NewCheck()
	{
		if ( _c != '<' || !IsValidTag() )
		{
			return false;
		}

		if ( !PushNodeEnd( _index - 1, true ) )
		{
			// stop parsing
			_index = Text.Length;
			return true;
		}

		_state = ParseState.WhichTag;
		if ( (_index - 1) <= (Text.Length - 2) )
		{
			if ( Text[_index] == '!' || Text[_index] == '?' )
			{
				PushNodeStart( NodeType.Comment, _index - 1, _lineposition - 1 );
				PushNodeNameStart( true, _index );
				PushNodeNameEnd( _index + 1 );
				_state = ParseState.Comment;
				if ( _index < (Text.Length - 2) )
				{
					if ( (Text[_index + 1] == '-') &&
						(Text[_index + 2] == '-') )
					{
						_fullcomment = true;
					}
					else
					{
						_fullcomment = false;
					}
				}

				return true;
			}
		}

		PushNodeStart( NodeType.Element, _index - 1, _lineposition - 1 );
		return true;
	}

	private void Parse()
	{
		int lastquote = 0;

		Lastnodes = new Dictionary<string, Node>();
		_c = 0;
		_fullcomment = false;
		_parseerrors = new List<ParseError>();
		_line = 1;
		_lineposition = 0;
		_maxlineposition = 0;

		_state = ParseState.Text;
		_documentnode._innerlength = Text.Length;
		_documentnode._outerlength = Text.Length;

		_lastparentnode = _documentnode;
		_currentnode = CreateNode( NodeType.Text, 0 );
		_currentattribute = null;

		_index = 0;
		PushNodeStart( NodeType.Text, 0, _lineposition );
		while ( _index < Text.Length )
		{
			_c = Text[_index];
			IncrementPosition();

			switch ( _state )
			{
				case ParseState.Text:
					if ( NewCheck() )
						continue;
					break;

				case ParseState.WhichTag:
					if ( NewCheck() )
						continue;
					if ( _c == '/' )
					{
						PushNodeNameStart( false, _index );
					}
					else
					{
						PushNodeNameStart( true, _index - 1 );
						DecrementPosition();
					}

					_state = ParseState.Tag;
					break;

				case ParseState.Tag:
					if ( NewCheck() )
						continue;
					if ( IsWhiteSpace( _c ) )
					{
						PushNodeNameEnd( _index - 1 );
						if ( _state != ParseState.Tag )
							continue;
						_state = ParseState.BetweenAttributes;
						continue;
					}

					if ( _c == '/' )
					{
						PushNodeNameEnd( _index - 1 );
						if ( _state != ParseState.Tag )
							continue;
						_state = ParseState.EmptyTag;
						continue;
					}

					if ( _c == '>' )
					{
						//// CHECK if parent is compatible with end tag
						//if (IsParentIncompatibleEndTag())
						//{
						//    _state = ParseState.Text;
						//    PushNodeStart(HtmlNodeType.Text, _index);
						//    break;
						//}

						PushNodeNameEnd( _index - 1 );
						if ( _state != ParseState.Tag )
							continue;
						if ( !PushNodeEnd( _index, false ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						if ( _state != ParseState.Tag )
							continue;
						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
					}

					break;

				case ParseState.BetweenAttributes:
					if ( NewCheck() )
						continue;

					if ( IsWhiteSpace( _c ) )
						continue;

					if ( (_c == '/') || (_c == '?') )
					{
						_state = ParseState.EmptyTag;
						continue;
					}

					if ( _c == '>' )
					{
						if ( !PushNodeEnd( _index, false ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						if ( _state != ParseState.BetweenAttributes )
							continue;
						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
						continue;
					}

					PushAttributeNameStart( _index - 1, _lineposition - 1 );
					_state = ParseState.AttributeName;
					break;

				case ParseState.EmptyTag:
					if ( NewCheck() )
						continue;

					if ( _c == '>' )
					{
						if ( !PushNodeEnd( _index, true ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						if ( _state != ParseState.EmptyTag )
							continue;
						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
						continue;
					}

					// we may end up in this state if attributes are incorrectly seperated
					// by a /-character. If so, start parsing attribute-name immediately.
					if ( !IsWhiteSpace( _c ) )
					{
						// Just do nothing and push to next one!
						DecrementPosition();
						_state = ParseState.BetweenAttributes;
						continue;
					}
					else
					{
						_state = ParseState.BetweenAttributes;
					}

					break;

				case ParseState.AttributeName:
					if ( NewCheck() )
						continue;

					if ( IsWhiteSpace( _c ) )
					{
						PushAttributeNameEnd( _index - 1 );
						_state = ParseState.AttributeBeforeEquals;
						continue;
					}

					if ( _c == '=' )
					{
						PushAttributeNameEnd( _index - 1 );
						_state = ParseState.AttributeAfterEquals;
						continue;
					}

					if ( _c == '>' )
					{
						PushAttributeNameEnd( _index - 1 );
						if ( !PushNodeEnd( _index, false ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						if ( _state != ParseState.AttributeName )
							continue;
						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
						continue;
					}

					break;

				case ParseState.AttributeBeforeEquals:
					if ( NewCheck() )
						continue;

					if ( IsWhiteSpace( _c ) )
						continue;
					if ( _c == '>' )
					{
						if ( !PushNodeEnd( _index, false ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						if ( _state != ParseState.AttributeBeforeEquals )
							continue;
						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
						continue;
					}

					if ( _c == '=' )
					{
						_state = ParseState.AttributeAfterEquals;
						continue;
					}

					// no equals, no whitespace, it's a new attrribute starting
					_state = ParseState.BetweenAttributes;
					DecrementPosition();
					break;

				case ParseState.AttributeAfterEquals:
					if ( NewCheck() )
						continue;

					if ( IsWhiteSpace( _c ) )
						continue;

					if ( (_c == '\'') || (_c == '"') )
					{
						_state = ParseState.QuotedAttributeValue;
						PushAttributeValueStart( _index, _c );
						lastquote = _c;
						continue;
					}

					if ( _c == '>' )
					{
						if ( !PushNodeEnd( _index, false ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						if ( _state != ParseState.AttributeAfterEquals )
							continue;
						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
						continue;
					}

					PushAttributeValueStart( _index - 1 );
					_state = ParseState.AttributeValue;
					break;

				case ParseState.AttributeValue:
					if ( NewCheck() )
						continue;

					if ( IsWhiteSpace( _c ) )
					{
						PushAttributeValueEnd( _index - 1 );
						_state = ParseState.BetweenAttributes;
						continue;
					}

					if ( _c == '>' )
					{
						PushAttributeValueEnd( _index - 1 );
						if ( !PushNodeEnd( _index, false ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						if ( _state != ParseState.AttributeValue )
							continue;
						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
						continue;
					}

					break;

				case ParseState.QuotedAttributeValue:
					if ( _c == lastquote )
					{
						PushAttributeValueEnd( _index - 1 );
						_state = ParseState.BetweenAttributes;
						continue;
					}

					break;

				case ParseState.Comment:
					if ( _c == '>' )
					{
						if ( _fullcomment )
						{
							if ( ((Text[_index - 2] != '-') || (Text[_index - 3] != '-'))
								&&
								((Text[_index - 2] != '!') || (Text[_index - 3] != '-') ||
								 (Text[_index - 4] != '-')) )
							{
								continue;
							}
						}

						if ( !PushNodeEnd( _index, false ) )
						{
							// stop parsing
							_index = Text.Length;
							break;
						}

						_state = ParseState.Text;
						PushNodeStart( NodeType.Text, _index, _lineposition );
						continue;
					}

					break;

				case ParseState.PcData:
					// look for </tag + 1 char

					// check buffer end
					if ( (_currentnode._namelength + 3) <= (Text.Length - (_index - 1)) )
					{
						if ( string.Compare( Text.Substring( _index - 1, _currentnode._namelength + 2 ),
								"</" + _currentnode.Name, StringComparison.OrdinalIgnoreCase ) == 0 )
						{
							int c = Text[_index - 1 + 2 + _currentnode.Name.Length];
							if ( (c == '>') || (IsWhiteSpace( c )) )
							{
								// add the script as a text node
								Node script = CreateNode( NodeType.Text,
									_currentnode._outerstartindex +
									_currentnode._outerlength );
								script._outerlength = _index - 1 - script._outerstartindex;
								script._streamposition = script._outerstartindex;
								script._line = _currentnode.Line;
								script._lineposition = _currentnode.LinePosition + _currentnode._namelength + 2;

								_currentnode.AppendChild( script );

								PushNodeStart( NodeType.Element, _index - 1, _lineposition - 1 );
								PushNodeNameStart( false, _index - 1 + 2 );
								_state = ParseState.Tag;
								IncrementPosition();
							}
						}
					}

					break;
			}
		}

		// TODO: Add implicit end here?


		// finish the current work
		if ( _currentnode._namestartindex > 0 )
		{
			PushNodeNameEnd( _index );
		}

		PushNodeEnd( _index, false );

		// we don't need this anymore
		Lastnodes.Clear();

		DocumentNode.FixSelfClosingTags();
	}

	// In this moment, we don't have value.
	// Potential: "\"", "'", "[", "]", "<", ">", "-", "|", "/", "\\"
	private static List<string> BlockAttributes = new List<string>() { "\"", "'" };

	private void PushAttributeNameEnd( int index )
	{
		_currentattribute._namelength = index - _currentattribute._namestartindex;

		if ( _currentattribute.Name != null && !BlockAttributes.Contains( _currentattribute.Name ) )
		{
			_currentnode.Attributes.Add( _currentattribute );
		}
	}

	private void PushAttributeNameStart( int index, int lineposition )
	{
		_currentattribute = new Attribute( this );
		_currentattribute._namestartindex = index;
	}

	private void PushAttributeValueEnd( int index )
	{
		_currentattribute._valuelength = index - _currentattribute._valuestartindex;
	}

	private void PushAttributeValueStart( int index )
	{
		PushAttributeValueStart( index, 0 );
	}

	private void PushAttributeValueStart( int index, int quote )
	{
		_currentattribute._valuestartindex = index;
	}

	private bool PushNodeEnd( int index, bool close )
	{
		_currentnode._outerlength = index - _currentnode._outerstartindex;

		if ( (_currentnode._nodetype == NodeType.Text) || (_currentnode._nodetype == NodeType.Comment) )
		{
			// forget about void nodes
			if ( _currentnode._nodetype != NodeType.Comment && _currentnode._outerlength > 0 )
			{
				_currentnode._innerlength = _currentnode._outerlength;
				_currentnode._innerstartindex = _currentnode._outerstartindex;
				if ( _lastparentnode != null )
				{
					_lastparentnode.AppendChild( _currentnode );
				}
			}
		}
		else
		{
			if ( (_currentnode._starttag) && (_lastparentnode != _currentnode) )
			{
				// add to parent node
				if ( _lastparentnode != null )
				{
					_lastparentnode.AppendChild( _currentnode );
				}

				// remember last node of this kind
				Node prev = Lastnodes.GetValueOrDefault( _currentnode.Name );

				_currentnode._prevwithsamename = prev;
				Lastnodes[_currentnode.Name] = _currentnode;

				// change parent?
				if ( (_currentnode.NodeType == NodeType.Document) ||
					(_currentnode.NodeType == NodeType.Element) )
				{
					_lastparentnode = _currentnode;
				}
			}
		}

		if ( _currentnode.Name == "img" || _currentnode.Name == "br" || _currentnode.Name == "video" )
		{
			close = true;
		}

		if ( (close) || (!_currentnode._starttag) )
		{
			CloseCurrentNode();
		}

		return true;
	}

	private void PushNodeNameEnd( int index )
	{
		_currentnode._namelength = index - _currentnode._namestartindex;
	}

	private void PushNodeNameStart( bool starttag, int index )
	{
		_currentnode._starttag = starttag;
		_currentnode._namestartindex = index;
	}

	private void PushNodeStart( NodeType type, int index, int lineposition )
	{
		_currentnode = CreateNode( type, index );
		_currentnode._line = _line;
		_currentnode._lineposition = lineposition;
		_currentnode._streamposition = index;
	}

	private enum ParseState
	{
		Text,
		WhichTag,
		Tag,
		BetweenAttributes,
		EmptyTag,
		AttributeName,
		AttributeBeforeEquals,
		AttributeAfterEquals,
		AttributeValue,
		Comment,
		QuotedAttributeValue,
		PcData
	}
}
