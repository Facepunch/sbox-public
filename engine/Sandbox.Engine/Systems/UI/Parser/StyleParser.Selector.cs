//
//
// Selectors = [selector], [selector], [selector]
// Selector = [rule] [rule] [rule] [rule]
// rule = ELEMENT.Class.Class.Class:hover:and:stuff
//

namespace Sandbox.UI;

internal static partial class StyleParser
{
	/// <summary>
	/// Here we divide the selectors into groups
	/// .fucker, .cocks, .hairy
	/// </summary>
	public static List<StyleSelector> Selector( string rule_string, StyleBlock parent = null )
	{
		var list = new List<StyleSelector>();

		var p = new Parse( rule_string );

		while ( !p.IsEnd )
		{
			p = p.SkipWhitespaceAndNewlines();

			if ( p.IsEnd )
				break;

			if ( p.Current == ',' )
				throw new System.Exception( $"Invalid Selector: {rule_string}" );

			var group = p.ReadUntilOrEnd( "," );

			group = group.Trim();

			var ss = ParseSelector( group, parent );
			if ( ss == null )
				return null;

			list.Add( ss );

			if ( !p.IsEnd && p.Current == ',' )
				p.Pointer++;
		}

		return list;
	}

	public static StyleSelector ParseSelector( string rule_string, StyleBlock parent = null )
	{
		var p = new Parse( rule_string );

		StyleSelector lastRule = null;

		while ( !p.IsEnd )
		{
			p = p.SkipWhitespaceAndNewlines();

			if ( p.IsEnd )
				break;

			bool immediateParent = false;
			bool adjacentSibling = false;
			bool generalSibling = false;

			if ( p.Is( '>' ) )
			{
				p.Pointer++;
				p = p.SkipWhitespaceAndNewlines();
				immediateParent = true;
			}
			else if ( p.Is( '+' ) )
			{
				p.Pointer++;
				p = p.SkipWhitespaceAndNewlines();
				adjacentSibling = true;
			}
			else if ( p.Is( '~' ) )
			{
				p.Pointer++;
				p = p.SkipWhitespaceAndNewlines();
				generalSibling = true;
			}

			var selector = p.ReadUntilWhitespaceOrNewlineOrEndAndObeyBrackets();

			var rule = ParseSingleSelector( selector, parent );

			if ( rule == null )
				return null;

			rule.Parent = lastRule;
			rule.ImmediateParent = immediateParent;
			rule.AdjacentSibling = adjacentSibling;
			rule.GeneralSibling = generalSibling;
			lastRule = rule;
		}

		if ( lastRule != null )
		{
			lastRule.AsString = rule_string.Trim();

			if ( parent != null )
			{
				var parentRules = string.Join( ", ", parent.Selectors.Select( x => x.AsString ) );
				if ( lastRule.AsString.StartsWith( '&' ) )
				{
					lastRule.AsString = parentRules + lastRule.AsString.Substring( 1 );
				}
				else
				{
					lastRule.AsString = parentRules + " " + lastRule.AsString;
				}
			}
		}

		return lastRule;
	}

	/// <summary>
	/// Parse a single rule, which as "panel.closed.error:hover"
	/// </summary>
	/// <returns></returns>
	public static StyleSelector ParseSingleSelector( string rule_string, StyleBlock parent )
	{
		var seperators = ".:";
		var rule = new StyleSelector();
		rule.AsString = rule_string.Trim();

		var p = new Parse( rule_string );

		p = p.SkipWhitespaceAndNewlines();

		List<string> ruleClasses = null;

		//
		// If our selector starts with & we need to match any of the parent block's selectors
		//
		if ( p.Current == '&' )
		{
			p.Pointer++;

			if ( parent == null )
				throw new System.Exception( $"Starts with & but has no parent block \"{rule_string}\"" );

			rule.AnyOf = parent.Selectors;
		}
		else if ( p.Current == '>' )
		{
			p.Pointer++;

			if ( parent == null )
				throw new System.Exception( $"Starts with > but has no parent block \"{rule_string}\"" );

			rule.DecendantOf = parent.Selectors;
			rule.ImmediateParent = true;
		}
		else if ( parent != null )
		{
			//
			// If we have a parent block, our parent needs to conform to its rules
			//
			rule.DecendantOf = parent.Selectors;
		}

		while ( !p.IsEnd )
		{
			//
			// Class
			//
			if ( p.Current == '.' )
			{
				p.Pointer++;

				if ( p.IsEnd || p.IsOneOf( seperators ) )
					throw new System.Exception( $"Invalid Rule \"{rule_string}\"" );

				var classname = p.ReadUntilOrEnd( ".:#" ).ToLowerInvariant();

				ruleClasses ??= new();
				ruleClasses.Add( classname );
			}
			else if ( p.Current == '#' )
			{
				p.Pointer++;

				if ( p.IsEnd || p.IsOneOf( seperators ) )
					throw new System.Exception( $"Invalid Rule \"{rule_string}\"" );

				var id = p.ReadUntilOrEnd( ".:#" ).ToLower();
				rule.Id = id;
			}
			else if ( p.Current == ':' )
			{
				// there might be 2, skip them all
				while ( p.Current == ':' )
					p.Pointer++;

				if ( p.IsEnd || p.IsOneOf( seperators ) )
					throw new System.Exception( $"Invalid Rule \"{rule_string}\"" );

				ReadPseudoClass( rule, ref p );
			}
			else if ( p.Current == '*' )
			{
				p.Pointer++;
				rule.UniversalSelector = true;

				if ( !p.IsEnd && !p.IsOneOf( seperators ) )
					throw new System.Exception( $"Invalid Rule \"{rule_string}\"" );
			}
			else
			{
				rule.Element = p.ReadUntilOrEnd( ".:#" ).ToLower();
			}
		}

		if ( ruleClasses != null )
		{
			rule.SetClasses( ruleClasses.ToArray() );
		}

		return rule;
	}

	private static void ReadPseudoClass( StyleSelector rule, ref Parse p )
	{
		if ( p.Is( "has(", 0, true ) )
		{
			p.Pointer += 3;
			var inner = p.ReadInnerBrackets();
			rule.Has = ParseHasSelectors( inner );
			return;
		}

		if ( p.Is( "not(", 0, true ) )
		{
			p.Pointer += 3;
			var inner = p.ReadInnerBrackets();
			if ( string.IsNullOrEmpty( inner ) ) return;

			rule.Not = ParseSelector( inner );
			return;
		}

		if ( p.Is( "nth-child(", 0, true ) )
		{
			p.Pointer += "nth-child".Length;
			var inner = p.ReadInnerBrackets();
			if ( string.IsNullOrEmpty( inner ) ) return;

			ParseNthChild( rule, inner.Trim() );

			return;
		}

		var flagname = p.ReadUntilOrEnd( ".:" ).ToLowerInvariant();

		switch ( flagname )
		{
			case "hover":
				rule.Flags |= PseudoClass.Hover;
				break;

			case "active":
				rule.Flags |= PseudoClass.Active;
				break;

			case "focus":
				rule.Flags |= PseudoClass.Focus;
				break;

			case "intro":
				rule.Flags |= PseudoClass.Intro;
				break;

			case "outro":
				rule.Flags |= PseudoClass.Outro;
				break;

			case "empty":
				rule.Flags |= PseudoClass.Empty;
				break;

			case "first-child":
				rule.Flags |= PseudoClass.FirstChild;
				break;

			case "last-child":
				rule.Flags |= PseudoClass.LastChild;
				break;

			case "only-child":
				rule.Flags |= PseudoClass.OnlyChild;
				break;

			case "before":
				rule.Flags |= PseudoClass.Before;
				break;

			case "after":
				rule.Flags |= PseudoClass.After;
				break;

			default:
				throw new System.Exception( $"Unsupported Pseudo Class \"{flagname}\"" );
		}
	}

	private static void ParseNthChild( StyleSelector rule, string inner )
	{
		if ( int.TryParse( inner, out int intValue ) )
		{
			rule.NthChild = ( p ) => (p.SiblingIndex + 1) == intValue;
			return;
		}

		if ( string.Equals( inner, "odd", StringComparison.OrdinalIgnoreCase ) )
		{
			rule.NthChild = ( p ) => p.SiblingIndex % 2 == 0;
			return;
		}

		if ( string.Equals( inner, "even", StringComparison.OrdinalIgnoreCase ) )
		{
			rule.NthChild = ( p ) => p.SiblingIndex % 2 == 1;
			return;
		}

		throw new System.Exception( $"unsupported NthChild \"{inner}\"" );
	}

	private static StyleSelector[] ParseHasSelectors( string inner )
	{
		if ( string.IsNullOrWhiteSpace( inner ) )
			return null;

		var selectors = new List<StyleSelector>();

		foreach ( var part in inner.Split( ',' ) )
		{
			var trimmed = part.Trim();
			if ( string.IsNullOrEmpty( trimmed ) ) continue;

			var selector = ParseHasSelector( trimmed );
			if ( selector == null ) continue;

			selectors.Add( selector );
		}

		if ( selectors.Count == 0 )
			return null;

		return selectors.ToArray();
	}

	private static StyleSelector ParseHasSelector( string selectorString )
	{
		if ( string.IsNullOrWhiteSpace( selectorString ) )
			return null;

		var p = new Parse( selectorString );
		p = p.SkipWhitespaceAndNewlines();

		// Handle different combinator types at the start
		bool isDirectChild = false;
		bool isAdjacent = false;
		bool isGeneral = false;

		if ( !p.IsEnd && p.Current == '>' )
		{
			isDirectChild = true;
			p.Pointer++;
			p = p.SkipWhitespaceAndNewlines();
		}
		else if ( !p.IsEnd && p.Current == '+' )
		{
			isAdjacent = true;
			p.Pointer++;
			p = p.SkipWhitespaceAndNewlines();
		}
		else if ( !p.IsEnd && p.Current == '~' )
		{
			isGeneral = true;
			p.Pointer++;
			p = p.SkipWhitespaceAndNewlines();
		}

		if ( p.IsEnd ) return null;

		var remaining = p.ReadRemaining();
		if ( string.IsNullOrWhiteSpace( remaining ) )
			return null;

		var selector = ParseSelector( remaining );

		if ( selector != null )
		{
			if ( isDirectChild ) selector.ImmediateParent = true;
			else if ( isAdjacent ) selector.AdjacentSibling = true;
			else if ( isGeneral ) selector.GeneralSibling = true;
		}

		return selector;
	}

}
