namespace Sandbox.UI;

/// <summary>
/// A CSS rule - ie ".chin { width: 100%; height: 100%; }"
/// </summary>
[SkipHotload]
public sealed class StyleBlock : IStyleBlock
{
	internal int LoadOrder = 0;

	// True if any of this block's selectors target ::before / ::after, so the style build can skip the
	// per-block pseudo-element probe for the (vast majority of) blocks that don't use them.
	internal bool HasBefore;
	internal bool HasAfter;

	/// <summary>
	/// A list of appropriate selectors for this block (ie ".button")
	/// </summary>
	public StyleSelector[] Selectors { get; private set; }

	/// <summary>
	/// A list of selectors for this block
	/// </summary>
	public IEnumerable<string> SelectorStrings => Selectors.Select( x => x.AsString );

	/// <summary>
	/// Get the list of raw style values
	/// </summary>
	public List<IStyleBlock.StyleProperty> GetRawValues()
	{
		return Styles.RawValues.Values.ToList();
	}

	internal bool IsEmpty => Styles?.RawValues?.Count == 0;

	/// <summary>
	/// Update a raw style value
	/// </summary>
	public bool SetRawValue( string key, string value, string originalValue = null )
	{
		if ( !Styles.RawValues.TryGetValue( key, out var prop ) )
		{
			prop.Name = key;
			prop.OriginalValue = value;
		}

		prop.Value = value;

		if ( originalValue != null )
			prop.OriginalValue = originalValue;

		var success = Styles.Set( key, value );
		prop.IsValid = success;

		Styles.RawValues[key] = prop;
		Styles.MarkPanelsDirty();

		return success;
	}

	/// <summary>
	/// The filename of the file containing this style block (or null if none)
	/// </summary>
	public string FileName { get; internal set; }

	/// <summary>
	/// The absolute on disk filename for this style block (or null if not on disk)
	/// </summary>
	public string AbsolutePath { get; internal set; }

	/// <summary>
	/// The line in the file containing this style block
	/// </summary>
	public int FileLine { get; internal set; }

	/// <summary>
	/// The styles that are defined in this block
	/// </summary>
	public Styles Styles;

	/// <summary>
	/// Test whether target passes our selector tests. We use forceFlag to do alternate tests for flags like ::before and ::after.
	/// It's basically added to the target's pseudo class list for the test.
	/// </summary>
	public StyleSelector Test( IStyleTarget target, PseudoClass forceFlag = PseudoClass.None )
	{
		if ( Selectors == null ) return null;

		// If this is a before or after then use the parent class with the added pseudo class
		if ( target.IsBeforeOrAfter )
		{
			if ( target.PseudoClass.Contains( PseudoClass.Before ) ) forceFlag = PseudoClass.Before;
			if ( target.PseudoClass.Contains( PseudoClass.After ) ) forceFlag = PseudoClass.After;

			target = target.Parent;
		}

		for ( int i = 0; i < Selectors.Length; i++ )
		{
			var item = Selectors[i];

			if ( item.Test( target, forceFlag ) )
				return item;
		}

		return null;
	}

	/// <summary>
	/// Tests a few broadphase conditions to build a list of feasible
	/// styleblocks tailored for a panel.
	/// </summary>
	public bool TestBroadphase( IStyleTarget target )
	{
		if ( Selectors == null ) return false;

		// We need to check the parent for pseudo classes like :before and :after
		if ( target.IsBeforeOrAfter ) target = target.Parent;

		// target might have become null if target didn't have a parent above
		if ( target == null ) return false;

		for ( int i = 0; i < Selectors.Length; i++ )
		{
			var item = Selectors[i];

			if ( item.TestBroadphase( target ) )
				return true;
		}

		return false;
	}

	public bool SetSelector( string selector, StyleBlock parent = null )
	{
		var selectors = StyleParser.Selector( selector, parent );
		if ( selectors == null ) return false;

		Selectors = selectors.ToArray();

		foreach ( var s in Selectors )
		{
			s.Finalize( this );

			if ( (s.Flags & PseudoClass.Before) != 0 ) HasBefore = true;
			if ( (s.Flags & PseudoClass.After) != 0 ) HasAfter = true;
		}

		return true;
	}
}
