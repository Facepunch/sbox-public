//
//
// Selectors = [selector], [selector], [selector]
// Selector = [rule] [rule] [rule] [rule]
// rule = ELEMENT.Class.Class.Class:hover:and:stuff
//

using System.Globalization;
using System.Text.RegularExpressions;

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

			// Only the first part of a chain hangs off the enclosing block. A copy on later parts
			// shares ImmediateParent with the combinator, so > would demand the block be an
			// immediate parent too.
			if ( lastRule != null )
				rule.DecendantOf = null;

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

			ParseNthChild( rule, inner?.Trim() ?? string.Empty );

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

			case "focus-within":
				rule.Flags |= PseudoClass.FocusWithin;
				break;

			case "root":
				rule.Flags |= PseudoClass.Root;
				break;

			case "disabled":
				rule.Flags |= PseudoClass.Disabled;
				break;

			case "enabled":
				rule.Flags |= PseudoClass.Enabled;
				break;

			case "checked":
				rule.Flags |= PseudoClass.Checked;
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
		if ( int.TryParse( inner, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int intValue ) )
		{
			rule.NthChild = p => intValue > 0 && p.SiblingIndex + 1 == intValue;
			return;
		}

		if ( string.Equals( inner, "odd", StringComparison.OrdinalIgnoreCase ) )
		{
			rule.NthChild = p => p.SiblingIndex % 2 == 0;
			return;
		}

		if ( string.Equals( inner, "even", StringComparison.OrdinalIgnoreCase ) )
		{
			rule.NthChild = p => p.SiblingIndex % 2 == 1;
			return;
		}

		// Allow whitespace around the offset sign, but reject forms such as "2 n" and "n+-1".
		var match = Regex.Match( inner, @"^([+-]?[0-9]*)[nN](?:\s*([+-])\s*([0-9]+))?$" );
		if ( !match.Success )
			throw new System.Exception( $"Invalid nth-child expression \"{inner}\"" );

		var coefficient = match.Groups[1].Value switch
		{
			"" or "+" => "1",
			"-" => "-1",
			var value => value
		};

		int b = 0;
		if ( !int.TryParse( coefficient, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int a )
			|| (match.Groups[2].Success && !int.TryParse( match.Groups[2].Value + match.Groups[3].Value,
				NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out b )) )
			throw new System.Exception( $"Invalid nth-child expression \"{inner}\"" );

		rule.NthChild = p =>
		{
			// n must be a non-negative integer and the resulting child index must be positive.
			// Widen before subtracting so large signed coefficients and offsets cannot overflow.
			long index = (long)p.SiblingIndex + 1;
			long difference = index - b;
			return index > 0 && (a == 0 ? difference == 0 : difference % a == 0 && difference / a >= 0);
		};
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
