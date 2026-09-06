
namespace Sandbox.UI;

public partial class Panel
{
	/// <summary>
	/// This is the style that we computed last. If you're looking to see which
	/// styles are set on this panel then this is what you're looking for.
	/// </summary>
	[Hide]
	public Styles ComputedStyle { get; internal set; }

	/// <summary>
	/// A importance sorted list of style blocks that are active on this panel
	/// </summary>
	[Hide]
	public IEnumerable<IStyleBlock> ActiveStyleBlocks => Style?.LastActiveRules?.Select( x => x.Block ) ?? Enumerable.Empty<IStyleBlock>();

	/// <summary>
	/// Allows you to set styles specifically on this panel. Setting the style will
	/// only affect this panel and no others and will override any other styles.
	/// </summary>
	[Hide]
	public PanelStyle Style { get; private set; }

	/// <summary>
	/// Try to find <c>@keyframes</c> CSS rule with given name in <see cref="AllStyleSheets"/>.
	/// </summary>
	/// <param name="name">The name to search for.</param>
	/// <param name="keyframes">The keyframes, if any are found, or <see langword="null"/>.</param>
	/// <returns><see langword="true"/> if <c>@keyframes</c> with given name were found.</returns>
	public bool TryFindKeyframe( string name, out KeyFrames keyframes )
	{
		// TODO: optimization - cache found keyframes? Clear on load?

		keyframes = null;

		foreach ( var sheet in AllStyleSheets )
		{
			if ( sheet.KeyFrames.TryGetValue( name, out var keyframe ) )
			{
				keyframes = keyframe;
				return true;
			}
		}

		return false;
	}

	internal void MarkStylesRebuilt()
	{
		_pendingRebuildRoot = null;
		_pendingRebuildRoot_Ancestors = null;
		_pendingRebuildRoot_Children = null;
	}

	// The root whose next rebuild pass this panel is queued on. A panel moved to another tree
	// before that pass ran queues again on the root it's in.
	RootPanel _pendingRebuildRoot;
	RootPanel _pendingRebuildRoot_Ancestors;
	RootPanel _pendingRebuildRoot_Children;

	/// <summary>
	/// Should be called when something happens that means that this panel's stylesheets need to be
	/// re-evaluated. Like becoming hovered or classes changed. You don't call this when changing styles
	/// directly on the panel, just on anything that will change which stylesheets should get selected.
	/// </summary>
	/// <param name="ancestors">Also re-evaluate all ancestor panels. (for <c>:has()</c>)</param>
	/// <param name="descendants">Also re-evaluate all child panels. (for parent selectors)</param>
	/// <param name="root">Root panel cache so we don't need to keep looking it up.</param>
	protected void StyleSelectorsChanged( bool ancestors, bool descendants, RootPanel root = null )
	{
		root ??= FindRootPanel();
		if ( root == null )
			return;

		if ( ancestors && _pendingRebuildRoot_Ancestors != root )
		{
			_pendingRebuildRoot_Ancestors = root;
			Parent?.StyleSelectorsChanged( true, false, root );
		}

		if ( descendants && _pendingRebuildRoot_Children != root && HasChildren )
		{
			_pendingRebuildRoot_Children = root;

			for ( int i = 0; i < _children.Count; i++ )
			{
				_children[i]?.StyleSelectorsChanged( false, true, root );
			}
		}

		if ( _pendingRebuildRoot != root )
		{
			_pendingRebuildRoot = root;
			root.AddToBuildStyleRulesList( this );
		}
	}

}
