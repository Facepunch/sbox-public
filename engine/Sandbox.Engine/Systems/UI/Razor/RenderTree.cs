using Microsoft.AspNetCore.Components;
using System.Text;

namespace Sandbox.UI;

/// <summary>
/// This is a tree renderer for panels. If we ever use razor on other ui we'll want to make a copy of
/// this class and do the specific things to that.
/// </summary>
public partial class PanelRenderTreeBuilder : Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder
{
	Panel Parent;


	int contentHash = 0;
	StringBuilder contentBuilder = new();

	void FlushContent()
	{
		if ( contentBuilder.Length == 0 ) return;

		var block = GetBlock( contentHash );
		var label = CurrentScope.Element as Label;

		if ( label == null && (contentBuilder.Length > 0 || block.ElementPanel.IsValid()) )
			label = block.FindOrCreateElement( "label", CurrentScope.Element ?? Parent ) as Label;

		if ( label != null )
		{
			CurrentScope.ChildIndex++;
			label.SetContent( contentBuilder.ToString() );
		}

		contentBuilder.Clear();
	}

	internal PanelRenderTreeBuilder( Panel panel )
	{
		Parent = panel;

		RootBlock = new Block();
		RootBlock.ElementPanel = panel;
		RootBlock.IsRootElement = true;
	}

	/// <summary>
	/// Called once before building the tree
	/// </summary>
	internal virtual void Start()
	{
		stack.Clear();

		CurrentScope = default;

		RootBlock.Reset();
		RootBlock.WasSeen = true;
	}

	/// <summary>
	/// Called once after building the tree
	/// </summary>
	internal virtual void Finish()
	{
		RootBlock.DestroyUnseen();
	}

	Block GetBlock( int hash )
	{
		var currentBlock = CurrentScope.Block ?? RootBlock;
		return currentBlock.GetChild( hash );
	}

	string sourceFile;
	int sourceLine;
	int sourceColumn;

	/// <summary>
	/// Add the current source location. Used to record in which file the element was created.
	/// </summary>
	public override void AddLocation( string filename, int line, int column )
	{
		sourceFile = filename;
		sourceLine = line;
		sourceColumn = column;
	}


	public override void OpenElement( int sequence, string elementName ) => OpenElement( sequence, elementName, null );

	/// <summary>
	/// Start working on this element
	/// </summary>
	public override void OpenElement( int sequence, string elementName, object key = null )
	{
		FlushContent();

		var parentElement = CurrentScope.Element ?? Parent;
		var childIndex = CurrentScope.ChildIndex;
		CurrentScope.ChildIndex++;

		PushScope( sequence, key );

		//Log.Info( $"OpenElement {CurrentScope.Sequence}.{CurrentScope.Loop} [{CurrentScope.Element}] [{elementName}]" );

		var block = GetBlock( CurrentScope.Hash );

		//
		// If we're a <root> and we are actually at the root, then we act like it's the root panel.
		//
		if ( string.Equals( "root", elementName, StringComparison.OrdinalIgnoreCase ) && CurrentScope.Element == null )
		{
			block.IsRootElement = true;
			block.ElementPanel = Parent;

			CurrentScope.Block = block;
		}
		else
		{
			var element = block.FindOrCreateElement( elementName, parentElement );

			element.Parent.SetChildIndex( element, childIndex );
			element.SourceFile = sourceFile;
			element.SourceLine = sourceLine;
			//element.SourecColumn = column;
			CurrentScope.Block = block;

		}

	}

	/// <summary>
	/// Handles "style" and "class" attributes..
	/// </summary>
	public void AddAttributeObject( int sequence, string attrName, object value )
	{
		var scope = CurrentScope;
		scope.Sequence = sequence;

		// Only set the value if it changed

		if ( CurrentBlock.CheckCacheValue( HashCode.Combine( scope.Element, attrName ), value?.GetHashCode() ?? 0 ) )
			return;

		scope.Element?.SetPropertyObject( attrName, value );
	}

	/// <summary>
	/// Handles "style" and "class" attributes..
	/// </summary>
	public void AddAttributeString( int sequence, string attrName, string value )
	{
		var scope = CurrentScope;
		scope.Sequence = sequence;

		// Only set the value if it changed

		if ( CurrentBlock.CheckCacheValue( HashCode.Combine( scope.Element, attrName ), value?.GetHashCode() ?? 0 ) )
			return;

		scope.Element?.SetProperty( attrName, value );
	}

	/// <summary>
	/// Styles from a style block
	/// </summary>
	public override void AddStyleDefinitions( int sequence, string styles )
	{
		var scope = CurrentScope;
		scope.Sequence = sequence;

		// only if it changed
		if ( CurrentBlock.CheckCacheValue( HashCode.Combine( scope.Element, sequence ), styles?.GetHashCode() ?? 0 ) )
			return;

		var sheet = StyleSheet.FromString( styles, sourceFile, null );
		var fileName = $"__Razor.{scope.Hash}";
		sheet.FileName = fileName;

		Parent.StyleSheet.Remove( sheet.FileName );
		Parent.StyleSheet.Add( sheet );
	}


	/// <summary>
	///  <![CDATA[ <Icon OnSomething=@Function></Icon> ]]>
	/// </summary>
	public override void AddAttribute<T>( int sequence, Action<T> value )
	{
		var scope = CurrentScope;
		scope.Sequence = sequence;

		// Only cache if it changed - the value is a delegate so we can't just use the hashcode
		// plus it doesn't matter - because it shouldn't really change unless they're doing something weird

		if ( CurrentBlock.CheckCacheValue( HashCode.Combine( scope.Element, sequence ), 1 ) )
			return;

		var e = (T)(object)scope.Element;
		if ( e == null ) return;

		value?.Invoke( e );
	}

	/// <summary>
	/// Finish working on this element
	/// </summary>
	public override void CloseElement()
	{
		FlushContent();

		var panel = CurrentScope.Element;
		//Log.Info( $"CloseElement {CurrentScope.Sequence}" );
		PopScope();
	}

	/// <summary>
	/// Handles text content within an element
	/// </summary>
	public override void AddContent<T>( int sequence, T content )
	{
		if ( content is RenderFragment frag )
		{
			frag( this );
			return;
		}

		var scope = CurrentScope;
		scope.Sequence = sequence;
		scope.Loop = CurrentBlock.Increment( sequence );
		scope.Hash = HashCode.Combine( scope.Loop, scope.Sequence );
		contentHash = scope.Hash;

		contentBuilder.Append( content );

		//Log.Info( $"	AddContentT {scope.Sequence} [{content}]" );
	}

	/// <summary>
	/// Delete all of the elements created by this render tree
	/// </summary>
	internal void Clear()
	{
		RootBlock?.Destroy();

		RootBlock = new Block();
		RootBlock.ElementPanel = Parent;
		RootBlock.IsRootElement = true;

		// if we applied any stylesheets to the root, we should remove them now
		Parent.StyleSheet.Remove( "*__Razor*" );
	}

	/// <summary>
	/// Implements @ref
	/// </summary>
	public override void AddReferenceCapture<T>( int sequence, T current, Action<T> value )
	{
		var block = CurrentBlock;

		// we should have a parent block for this to be happening!
		Assert.NotNull( block );

		if ( block.ReferenceClearer != null )
			return;

		if ( block.ElementPanel is T t && t != null )
		{
			value?.Invoke( t );
			block.ReferenceClearer = () => value( default );
		}
	}

	public override void SetRenderFragment<T>( Action<T, RenderFragment> setter, RenderFragment builder )
	{
		var block = CurrentBlock;
		if ( block.ElementPanel is not T t ) return;

		setter( t, builder );

		if ( t is Panel p )
		{
			p.OnRenderFragmentChanged( Parent );
		}
	}

	public override void SetRenderFragmentWithContext<T, U>( Func<T, RenderFragment<U>> getter, Action<T, RenderFragment<U>> setter, RenderFragment<U> builder )
	{
		var block = CurrentBlock;
		if ( block.ElementPanel is not T t ) return;

		setter( t, builder );

		if ( t is Panel p )
		{
			p.OnRenderFragmentChanged( Parent );
		}
	}
}
