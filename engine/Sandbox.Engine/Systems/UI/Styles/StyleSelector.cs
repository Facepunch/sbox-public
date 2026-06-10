namespace Sandbox.UI;

/// <summary>
/// A CSS selector like "Panel.button.red:hover .text"
/// </summary>
[SkipHotload]
public sealed class StyleSelector
{
	public StyleBlock Block;

	public string AsString;
	public string[] Classes { get; private set; }

	public string Element;

	/// <summary>
	/// The Id selector - minus the #
	/// https://developer.mozilla.org/en-US/docs/Web/CSS/ID_selectors
	/// </summary>
	public string Id { get; set; }
	public PseudoClass Flags;

	/// <summary>
	/// Descendant combinator
	/// A B
	/// Child combinator
	/// A > B
	/// Adjacent sibling combinator
	/// A + B
	/// General sibling combinator
	/// A ~B
	/// </summary>
	public StyleSelector Parent;
	public StyleSelector Not;
	public bool ImmediateParent;

	/// <summary>
	/// True if this has a universal selector (*)
	/// </summary>
	public bool UniversalSelector;

	/// <summary>
	/// For + combinator
	/// </summary>
	public bool AdjacentSibling;

	/// <summary>
	/// For ~ combinator
	/// </summary>
	public bool GeneralSibling;

	public StyleSelector[] AnyOf;
	public StyleSelector[] DecendantOf;
	public StyleSelector[] Has;
	public int SelfScore;

	public Func<IStyleTarget, bool> NthChild;

	public int Score
	{
		get
		{
			if ( Block != null )
			{
				// Scale specificity well above any LoadOrder so source order can never outrank it
				return Block.LoadOrder + SelfScore * 100000;
			}

			return SelfScore;
		}
	}

	// publuc ParentType [ Ascendant, Parent, Adjacent Sibling, General Sibling ]

	internal void SetClasses( string[] classes )
	{
		Classes = classes;
	}

	public void Finalize( StyleBlock block )
	{
		Block = block;

		UpdateScore();
	}

	int UpdateScore()
	{
		SelfScore = 0;

		//
		// https://developer.mozilla.org/en-US/docs/Learn/CSS/Building_blocks/Cascade_and_inheritance
		//

		// Id is the holy grail
		// "The selector with the greater value in the ID column wins no matter what the values are in the other columns"
		if ( Id != null ) SelfScore += 1000;

		// Elements score the lowest
		if ( Element != null ) SelfScore += 1;

		// Each class is counted as 10
		if ( Classes != null ) SelfScore += Classes.Count() * 10;

		// If we have any flags, count each one as 10
		if ( Flags != 0 ) SelfScore += ((int)Flags).BitsSet() * 10;

		// Nth child counts as a flag
		if ( NthChild != null ) SelfScore += 10;

		// :not doesn't count as anything special, but we do count its content score
		if ( Not != null ) SelfScore += Not.UpdateScore();

		// This isn't actually perfectly accurate and you might be able to engineer weird situations
		// but it should be fine 99% of the time. If it isn't then the move is to get rid of AnyOf and 
		// whatever and collapse the & stuff into individual rules
		if ( AnyOf != null && AnyOf.Length > 0 ) SelfScore += AnyOf.Max( x => x.UpdateScore() );
		if ( DecendantOf != null && DecendantOf.Length > 0 ) SelfScore += DecendantOf.Max( x => x.UpdateScore() );

		// Add our parents score
		if ( Parent != null ) SelfScore += Parent.UpdateScore();

		//
		// You'd think things like .a > .b would score higher than .a .b, but that isn't true
		//

		return SelfScore;
	}

	public bool TestBroadphase( IStyleTarget target )
	{
		//
		// If we have element names
		//
		if ( Element != null )
		{
			if ( target.ElementName != Element )
				return false;
		}

		//
		// Id check
		//
		if ( Id != null )
		{
			if ( target.Id == null )
				return false;

			if ( string.Compare( target.Id, Id, true ) != 0 )
				return false;
		}

		//
		// If we have a class, you better have one too
		//
		if ( Classes != null )
		{
			if ( !target.HasClasses( Classes ) )
				return false;
		}

		return true;
	}

	/// <summary>
	/// Test whether target passes our selector test. We use forceFlag to do alternate tests for flags like ::before and ::after.
	/// It's basically added to the target's pseudo class list for the test.
	/// </summary>
	public bool Test( IStyleTarget target, PseudoClass forceFlag = PseudoClass.None )
	{
		var pseudo = target.PseudoClass | forceFlag;

		// Not optional - need to match!
		if ( pseudo.Contains( PseudoClass.Before ) && !Flags.Contains( PseudoClass.Before ) ) return false;
		if ( pseudo.Contains( PseudoClass.After ) && !Flags.Contains( PseudoClass.After ) ) return false;

		//
		// If we have flags, you better match them 
		//
		if ( Flags != PseudoClass.None && (pseudo & Flags) != Flags )
			return false;

		//
		// :nth-child( 2 )
		//
		if ( NthChild != null && !NthChild( target ) )
		{
			return false;
		}

		if ( !TestBroadphase( target ) )
			return false;

		if ( Parent != null )
		{
			if ( AdjacentSibling )
			{
				// A + B: B's immediately-preceding sibling must match A
				var prev = GetPreviousSibling( target );
				if ( prev == null || !Parent.Test( prev ) )
					return false;
			}
			else if ( GeneralSibling )
			{
				// A ~ B: some preceding sibling of B must match A
				var prev = GetPreviousSibling( target );
				bool matched = false;

				while ( prev != null )
				{
					if ( Parent.Test( prev ) )
					{
						matched = true;
						break;
					}

					prev = GetPreviousSibling( prev );
				}

				if ( !matched )
					return false;
			}
			else if ( !Parent.TestParent( target.Parent, !ImmediateParent ) )
			{
				return false;
			}
		}

		if ( Has != null && Has.Length > 0 )
		{
			if ( !TestHas( target ) )
				return false;
		}

		if ( DecendantOf != null )
		{
			bool passed = false;
			foreach ( var p in DecendantOf )
			{
				if ( !p.TestParent( target.Parent, !ImmediateParent ) )
					continue;

				passed = true;
				break;
			}

			if ( !passed ) return false;
		}

		if ( AnyOf != null )
		{
			bool passed = false;
			foreach ( var p in AnyOf )
			{
				if ( !p.Test( target ) )
					continue;

				passed = true;
				break;
			}

			if ( !passed ) return false;
		}

		if ( Not != null )
		{
			if ( Not.Test( target ) )
				return false;
		}

		// UniversalSelector

		return true;
	}


	public bool TestParent( IStyleTarget target, bool recusive = true )
	{
		if ( target == null )
			return false;

		if ( Test( target ) )
			return true;

		if ( !recusive )
			return false;

		return TestParent( target.Parent );
	}

	private bool TestHas( IStyleTarget target )
	{
		foreach ( var hasSelector in Has )
		{
			if ( hasSelector.AdjacentSibling )
			{
				var nextSibling = GetNextSibling( target );

				if ( nextSibling != null && hasSelector.Test( nextSibling ) )
					return true;
			}
			else if ( hasSelector.GeneralSibling )
			{
				var sibling = GetNextSibling( target );

				while ( sibling != null )
				{
					if ( hasSelector.Test( sibling ) )
						return true;

					sibling = GetNextSibling( sibling );
				}
			}
			else if ( hasSelector.ImmediateParent )
			{
				foreach ( var child in target.Children ?? Enumerable.Empty<IStyleTarget>() )
				{
					if ( hasSelector.Test( child ) )
						return true;
				}
			}
			else
			{
				if ( TestDescendants( target, hasSelector ) )
					return true;
			}
		}

		return false;
	}

	private bool TestDescendants( IStyleTarget target, StyleSelector selector )
	{
		var children = target.Children;
		if ( children == null ) return false;

		foreach ( var child in children )
		{
			if ( selector.Test( child ) )
				return true;

			if ( TestDescendants( child, selector ) )
				return true;
		}

		return false;
	}

	private IStyleTarget GetNextSibling( IStyleTarget target )
	{
		var siblings = target.Parent?.Children;
		if ( siblings == null )
			return null;

		for ( int i = 0; i < siblings.Count; i++ )
		{
			if ( siblings[i] == target )
				return i + 1 < siblings.Count ? siblings[i + 1] : null;
		}

		return null;
	}

	private IStyleTarget GetPreviousSibling( IStyleTarget target )
	{
		var siblings = target.Parent?.Children;
		if ( siblings == null )
			return null;

		for ( int i = 0; i < siblings.Count; i++ )
		{
			if ( siblings[i] == target )
				return i > 0 ? siblings[i - 1] : null;
		}

		return null;
	}
}
