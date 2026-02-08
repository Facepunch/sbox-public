// Description: Html Agility Pack - HTML Parsers, selectors, traversors, manupulators.
// Website & Documentation: http://html-agility-pack.net
// Forum & Issues: https://github.com/zzzprojects/html-agility-pack
// License: https://github.com/zzzprojects/html-agility-pack/blob/master/LICENSE
// More projects: http://www.zzzprojects.com/
// Copyright © ZZZ Projects Inc. 2014 - 2017. All rights reserved.


// ReSharper disable InconsistentNaming
using Sandbox.UI;

namespace Sandbox.Html;

public interface INode : IStyleTarget
{
	bool IsElement { get; }
	bool IsText { get; }
	bool IsComment { get; }
	bool IsDocument { get; }
	string OuterHtml { get; }
	string InnerHtml { get; }
	new IEnumerable<INode> Children { get; }

	string Name { get; }

	string GetAttribute( string name, string def = "" );
	int GetAttributeInt( string name, int def = 0 );
	float GetAttributeFloat( string name, float def = 0.0f );
	bool GetAttributeBool( string name, bool def = false );
	T GetAttribute<T>( string name, T def = default );

	internal void SetPseudoClass( PseudoClass c );

	public static INode Parse( string html )
	{
		var d = new Document();
		d.LoadHtml( html );
		return d.DocumentNode;
	}
}

/// <summary>
/// Represents an HTML node.
/// </summary>
[SkipHotload]
partial class Node : INode
{
	public static Node Parse( string html )
	{
		var d = new Document();
		d.LoadHtml( html );
		return d.DocumentNode;
	}

	PseudoClass _ps;

	string IStyleTarget.ElementName => Name;
	string IStyleTarget.Id => GetAttribute( "id" );
	PseudoClass IStyleTarget.PseudoClass => _ps;
	IStyleTarget IStyleTarget.Parent => ParentNode;
	int IStyleTarget.SiblingIndex => 0;

	void INode.SetPseudoClass( PseudoClass c ) => _ps = c;

	bool IStyleTarget.HasClasses( string[] classes )
	{
		if ( GetAttribute( "class" ) is not { } c )
			return false;

		var all = c.Split();
		return all.Any( classes.Contains );
	}

	public IEnumerable<INode> Children
	{
		get { return _childnodes ?? Enumerable.Empty<INode>(); }
	}

	internal const string DepthLevelExceptionMessage = "The document is too complex to parse";

	internal List<Attribute> _attributes;
	internal List<Node> _childnodes;
	internal Node _endnode;

	internal string _innerhtml;
	internal int _innerlength;
	internal int _innerstartindex;
	internal int _line;
	internal int _lineposition;
	internal int _namelength;
	internal int _namestartindex;
	internal NodeType _nodetype;
	internal string _outerhtml;
	internal int _outerlength;
	internal int _outerstartindex;
	private string _optimizedName;
	internal Document _ownerdocument;
	internal Node _parentnode;
	internal Node _prevnode;
	internal Node _prevwithsamename;
	internal bool _starttag;
	internal int _streamposition;
	internal bool _isImplicitEnd;


	/// <summary>
	/// Gets the name of a comment node. It is actually defined as '#comment'.
	/// </summary>
	internal static readonly string HtmlNodeTypeNameComment = "#comment";

	/// <summary>
	/// Gets the name of the document node. It is actually defined as '#document'.
	/// </summary>
	internal static readonly string HtmlNodeTypeNameDocument = "#document";

	/// <summary>
	/// Gets the name of a text node. It is actually defined as '#text'.
	/// </summary>
	internal static readonly string HtmlNodeTypeNameText = "#text";

	/// <summary>
	/// Initializes HtmlNode, providing type, owner and where it exists in a collection
	/// </summary>
	/// <param name="type"></param>
	/// <param name="ownerdocument"></param>
	/// <param name="index"></param>
	internal Node( NodeType type, Document ownerdocument, int index )
	{
		_nodetype = type;
		_ownerdocument = ownerdocument;
		_outerstartindex = index;

		switch ( type )
		{
			case NodeType.Comment:
				_endnode = this;
				break;

			case NodeType.Document:
				_optimizedName = HtmlNodeTypeNameDocument;
				_endnode = this;
				break;

			case NodeType.Text:
				_endnode = this;
				break;
		}

		if ( _ownerdocument.Openednodes != null )
		{
			if ( !Closed )
			{
				// we use the index as the key

				// -1 means the node comes from public
				if ( -1 != index )
				{
					_ownerdocument.Openednodes.Add( index, this );
				}
			}
		}

		if ( (-1 != index) || (type == NodeType.Comment) || (type == NodeType.Text) ) return;
		// innerhtml and outerhtml must be calculated
		SetChanged();
	}

	/// <summary>
	/// Returns true if this is a html element (ie, not a comment or text)
	/// </summary>
	public bool IsElement => NodeType == NodeType.Element;

	/// <summary>
	/// Returns true if this is a comment
	/// </summary>
	public bool IsComment => NodeType == NodeType.Comment;

	/// <summary>
	/// Returns true if this is text
	/// </summary>
	public bool IsText => NodeType == NodeType.Text;

	/// <summary>
	/// Returns true if this is the root document
	/// </summary>
	public bool IsDocument => NodeType == NodeType.Document;

	/// <summary>
	/// Gets the collection of HTML attributes for this node. May not be null.
	/// </summary>
	public List<Attribute> Attributes
	{
		get
		{
			if ( !HasAttributes )
			{
				_attributes = new List<Attribute>();
			}

			return _attributes;
		}

	}

	/// <summary>
	/// Gets all the children of the node.
	/// </summary>
	public List<Node> ChildNodes
	{
		get { return _childnodes ?? (_childnodes = new List<Node>()); }

	}

	IReadOnlyList<IStyleTarget> IStyleTarget.Children
	{
		get { return _childnodes?.AsReadOnly(); }
	}

	/// <summary>
	/// Gets a value indicating if this node has been closed or not.
	/// </summary>
	internal bool Closed => _endnode != null;

	/// <summary>
	/// Gets a value indicating whether the current node has any attributes.
	/// </summary>
	public bool HasAttributes => _attributes != null && _attributes.Count > 0;

	/// <summary>
	/// Gets a value indicating whether this node has any child nodes.
	/// </summary>
	public bool HasChildNodes => _childnodes != null && _childnodes.Count > 0;

	/// <summary>
	/// Gets or Sets the HTML between the start and end tags of the object.
	/// </summary>
	public virtual string InnerHtml => _innerhtml ?? (_innerhtml = _ownerdocument.Text.Substring( _innerstartindex, _innerlength ));

	/// <summary>
	/// Gets the line number of this node in the document.
	/// </summary>
	internal int Line => _line;

	/// <summary>
	/// Gets the column number of this node in the document.
	/// </summary>
	public int LinePosition => _lineposition;

	/// <summary>
	/// Gets the stream position of the area between the opening and closing tag of the node, relative to the start of the document.
	/// </summary>
	public int InnerStartIndex => _innerstartindex;

	/// <summary>
	/// Gets or sets this node's name.
	/// </summary>
	public string Name => _optimizedName ?? (_optimizedName = _ownerdocument.Text.Substring( _namestartindex, _namelength ).ToLowerInvariant());

	/// <summary>
	/// Gets the type of this node.
	/// </summary>
	internal NodeType NodeType
	{
		get { return _nodetype; }
	}

	/// <summary>
	/// Gets or Sets the object and its content in HTML.
	/// </summary>
	public virtual string OuterHtml
	{
		get
		{
			return _outerhtml ?? (_outerhtml = _ownerdocument.Text.Substring( _outerstartindex, _outerlength ));
		}
	}

	/// <summary>
	/// Gets the <see cref="Document"/> to which this node belongs.
	/// </summary>
	internal Document OwnerDocument
	{
		get { return _ownerdocument; }
		set { _ownerdocument = value; }
	}

	/// <summary>
	/// Gets the parent of this node (for nodes that can have parents).
	/// </summary>
	public Node ParentNode
	{
		get { return _parentnode; }
		internal set { _parentnode = value; }
	}

	/// <summary>
	/// Gets the node immediately preceding this node.
	/// </summary>
	public Node PreviousSibling
	{
		get { return _prevnode; }
		internal set { _prevnode = value; }
	}

	/// <summary>
	/// The depth of the node relative to the opening root html element. This value is used to determine if a document has to many nested html nodes which can cause stack overflows
	/// </summary>
	public int Depth { get; set; }

	/// <summary>
	/// Returns a collection of all ancestor nodes of this element.
	/// </summary>
	/// <returns></returns>
	public IEnumerable<Node> Ancestors()
	{
		Node node = ParentNode;
		if ( node != null )
		{
			yield return node; //return the immediate parent node

			//now look at it's parent and walk up the tree of parents
			while ( node.ParentNode != null )
			{
				yield return node.ParentNode;
				node = node.ParentNode;
			}
		}
	}

	/// <summary>
	/// Get Ancestors with matching name
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	public IEnumerable<Node> Ancestors( string name )
	{
		for ( Node n = ParentNode; n != null; n = n.ParentNode )
			if ( n.Name == name )
				yield return n;
	}

	/// <summary>
	/// Returns a collection of all ancestor nodes of this element.
	/// </summary>
	/// <returns></returns>
	public IEnumerable<Node> AncestorsAndSelf()
	{
		for ( Node n = this; n != null; n = n.ParentNode )
			yield return n;
	}

	/// <summary>
	/// Gets all ancestor nodes and the current node
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	public IEnumerable<Node> AncestorsAndSelf( string name )
	{
		for ( Node n = this; n != null; n = n.ParentNode )
			if ( n.Name == name )
				yield return n;
	}

	/// <summary>
	/// Adds the specified node to the end of the list of children of this node.
	/// </summary>
	/// <param name="newChild">The node to add. May not be null.</param>
	/// <returns>The node added.</returns>
	public Node AppendChild( Node newChild )
	{
		if ( newChild == null )
		{
			throw new ArgumentNullException( "newChild" );
		}

		ChildNodes.Add( newChild );
		newChild.SetParent( this );

		_ownerdocument.SetIdForNode( newChild, newChild.GetId() );
		SetChildNodesId( newChild );

		SetChanged();
		return newChild;
	}

	/// <summary>Sets child nodes identifier.</summary>
	/// <param name="childNode">The child node.</param>
	public void SetChildNodesId( Node childNode )
	{
		foreach ( Node child in childNode.ChildNodes )
		{
			_ownerdocument.SetIdForNode( child, child.GetId() );
			SetChildNodesId( child );
		}
	}

	/// <summary>
	/// Gets all Descendant nodes in enumerated list
	/// </summary>
	/// <returns></returns>
	public IEnumerable<Node> Descendants()
	{
		// DO NOT REMOVE, the empty method is required for Fizzler third party library
		return Descendants( 0 );
	}

	/// <summary>
	/// Gets all Descendant nodes in enumerated list
	/// </summary>
	/// <returns></returns>
	public IEnumerable<Node> Descendants( int level )
	{
		if ( level > Document.MaxDepthLevel )
		{
			throw new ArgumentException( Node.DepthLevelExceptionMessage );
		}

		foreach ( Node node in ChildNodes )
		{
			yield return node;

			foreach ( Node descendant in node.Descendants( level + 1 ) )
			{
				yield return descendant;
			}
		}
	}

	/// <summary>
	/// Get all descendant nodes with matching name
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	public IEnumerable<Node> Descendants( string name )
	{
		foreach ( Node node in Descendants() )
			if ( String.Equals( node.Name, name, StringComparison.OrdinalIgnoreCase ) )
				yield return node;
	}

	/// <summary>
	/// Returns a collection of all descendant nodes of this element, in document order
	/// </summary>
	/// <returns></returns>
	public IEnumerable<Node> DescendantsAndSelf()
	{
		yield return this;

		foreach ( Node n in Descendants() )
		{
			Node el = n;
			if ( el != null )
				yield return el;
		}
	}

	/// <summary>
	/// Gets all descendant nodes including this node
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	public IEnumerable<Node> DescendantsAndSelf( string name )
	{
		yield return this;

		foreach ( Node node in Descendants() )
			if ( node.Name == name )
				yield return node;
	}

	/// <summary>
	/// Gets first generation child node matching name
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	public Node Element( string name )
	{
		foreach ( Node node in ChildNodes )
			if ( node.Name == name )
				return node;
		return null;
	}

	/// <summary>
	/// Gets matching first generation child nodes matching name
	/// </summary>
	/// <param name="name"></param>
	/// <returns></returns>
	public IEnumerable<Node> Elements( string name )
	{
		foreach ( Node node in ChildNodes )
			if ( node.Name == name )
				yield return node;
	}

	/// <summary>
	/// Helper method to get the value of an attribute of this node. If the attribute is not found, the default value will be returned.
	/// </summary>
	/// <param name="name">The name of the attribute to get. May not be <c>null</c>.</param>
	/// <param name="def">The default value to return if not found.</param>
	/// <returns>The value of the attribute if found, the default value if not found.</returns>
	public string GetAttribute( string name, string def = null )
	{
		if ( name == null )
			throw new ArgumentNullException( "name" );

		if ( !HasAttributes )
			return def;

		Attribute att = Attributes.FirstOrDefault( x => string.Compare( x.Name, name, true ) == 0 );
		if ( att == null )
			return def;

		return att.Value;
	}

	public T GetAttribute<T>( string name, T def = default )
	{
		var str = GetAttribute( name, null );
		if ( string.IsNullOrEmpty( str ) ) return def;

		if ( str.TryToType( typeof( T ), out var val ) )
			return (T)val;

		return def;
	}

	public int GetAttributeInt( string name, int def )
	{
		return GetAttribute( name, def.ToString() ).ToInt();
	}

	public float GetAttributeFloat( string name, float def )
	{
		return GetAttribute( name, def.ToString() ).ToFloat();
	}

	public bool GetAttributeBool( string name, bool def )
	{
		return GetAttribute( name, def ? "true" : "false" ).ToBool();
	}

	/// <summary>Removes all id for node described by node.</summary>
	/// <param name="node">The node.</param>
	internal void RemoveAllIDforNode( Node node )
	{
		foreach ( Node nodeChildNode in node.ChildNodes )
		{
			_ownerdocument.SetIdForNode( null, nodeChildNode.GetId() );
			RemoveAllIDforNode( nodeChildNode );
		}
	}

	/// <summary>
	/// Removes the specified child node.
	/// </summary>
	/// <param name="oldChild">The node being removed. May not be <c>null</c>.</param>
	/// <returns>The node removed.</returns>
	internal Node RemoveChild( Node oldChild )
	{
		if ( oldChild == null )
		{
			throw new ArgumentNullException( "oldChild" );
		}

		_childnodes?.Remove( oldChild );

		_ownerdocument.SetIdForNode( null, oldChild.GetId() );
		RemoveAllIDforNode( oldChild );
		SetChanged();
		return oldChild;
	}

	/// <summary>
	/// Sets the parent Html node and properly determines the current node's depth using the parent node's depth.
	/// </summary>
	internal void SetParent( Node parent )
	{
		if ( parent == null )
			return;

		ParentNode = parent;
		if ( OwnerDocument.OptionMaxNestedChildNodes > 0 )
		{
			Depth = parent.Depth + 1;
			if ( Depth > OwnerDocument.OptionMaxNestedChildNodes )
				throw new Exception( string.Format( "Document has more than {0} nested tags. This is likely due to the page not closing tags properly.", OwnerDocument.OptionMaxNestedChildNodes ) );
		}
	}

	internal void SetChanged()
	{
		if ( ParentNode != null )
		{
			ParentNode.SetChanged();
		}
	}

	internal void UpdateLastNode()
	{
		Node newLast = null;
		if ( _prevwithsamename == null || !_prevwithsamename._starttag )
		{
			if ( _ownerdocument.Openednodes != null )
			{
				foreach ( var openNode in _ownerdocument.Openednodes )
				{
					if ( (openNode.Key < _outerstartindex || openNode.Key > (_outerstartindex + _outerlength)) && openNode.Value.Name == Name )
					{
						if ( newLast == null && openNode.Value._starttag )
						{
							newLast = openNode.Value;
						}
						else if ( newLast != null && newLast.InnerStartIndex < openNode.Key && openNode.Value._starttag )
						{
							newLast = openNode.Value;
						}
					}
				}
			}
		}
		else
		{
			newLast = _prevwithsamename;
		}


		if ( newLast != null )
		{
			_ownerdocument.Lastnodes[newLast.Name] = newLast;
		}
	}

	internal void CloseNode( Node endnode, int level = 0 )
	{
		if ( level > Document.MaxDepthLevel )
		{
			throw new ArgumentException( Node.DepthLevelExceptionMessage );
		}

		if ( !Closed )
		{
			_endnode = endnode;

			if ( _ownerdocument.Openednodes != null )
				_ownerdocument.Openednodes.Remove( _outerstartindex );

			Node self = _ownerdocument.Lastnodes.GetValueOrDefault( Name );
			if ( self == this )
			{
				_ownerdocument.Lastnodes.Remove( Name );
				_ownerdocument.UpdateLastParentNode();


				if ( _starttag && !String.IsNullOrEmpty( Name ) )
				{
					UpdateLastNode();
				}
			}

			if ( endnode == this )
				return;

			// create an inner section
			_innerstartindex = _outerstartindex + _outerlength;
			_innerlength = endnode._outerstartindex - _innerstartindex;

			// update full length
			_outerlength = (endnode._outerstartindex + endnode._outerlength) - _outerstartindex;
		}
	}

	internal string GetId()
	{
		return GetAttribute( "id", string.Empty );
	}

	private string GetRelativeXpath()
	{
		if ( ParentNode == null )
			return Name;
		if ( NodeType == NodeType.Document )
			return string.Empty;

		int i = 1;
		foreach ( Node node in ParentNode.ChildNodes )
		{
			if ( node.Name != Name ) continue;

			if ( node == this )
				break;

			i++;
		}

		return Name + "[" + i + "]";
	}

	internal void FixSelfClosingTags()
	{
		if ( !HasChildNodes )
			return;

		foreach ( var child in ChildNodes.ToArray() )
		{
			child.FixSelfClosingTags();

			if ( child.Closed ) continue;

			var index = ChildNodes.IndexOf( child );

			foreach ( var gchild in child.ChildNodes )
			{
				ChildNodes.Insert( ++index, gchild );
			}

			child.ChildNodes.Clear();
		}
	}
}
