namespace Borealis.Backend;

/// <summary>
/// An annotation's argument with its conditionals taken, as vfx evaluates annotation expressions with a material's
/// features (<c>Filter( F_TEXTURE_FILTERING == 0 ? ANISOTROPIC : ( F_TEXTURE_FILTERING == 1 ? BILINEAR : ... ) )</c>).
/// Conditions compare names and integers (<c>== != &lt; &gt; &lt;= &gt;=</c>, <c>&amp;&amp; || !</c>); a name
/// reads its value, 0 where there's none.
/// </summary>
internal static class AnnotationExpression
{
	/// <summary>The branch <paramref name="expression"/> takes with these values, trimmed; the expression itself when it has no conditional.</summary>
	public static string Choose( string expression, Func<string, int?> values )
	{
		var text = Unwrap( expression );
		var question = TopLevel( text, "?", 0 );
		if ( question < 0 ) return text;

		// The ':' that pairs with this '?', past nested conditionals in the true branch
		var depth = 0;
		var colon = -1;
		for ( int i = question + 1; i < text.Length && colon < 0; i++ )
		{
			switch ( text[i] )
			{
				case '(': depth++; break;
				case ')': depth--; break;
				case '?' when depth == 0: depth += 1000; break;
				case ':' when depth >= 1000: depth -= 1000; break;
				case ':' when depth == 0: colon = i; break;
			}
		}

		if ( colon < 0 ) return text;
		return Condition( text[..question], values ) != 0 ? Choose( text[(question + 1)..colon], values ) : Choose( text[(colon + 1)..], values );
	}

	/// <summary>A condition's value: comparisons 1 or 0, a name its value.</summary>
	static int Condition( string text, Func<string, int?> values )
	{
		text = Unwrap( text );
		var either = TopLevel( text, "||", 0 );
		if ( either >= 0 ) return Condition( text[..either], values ) != 0 || Condition( text[(either + 2)..], values ) != 0 ? 1 : 0;
		var both = TopLevel( text, "&&", 0 );
		if ( both >= 0 ) return Condition( text[..both], values ) != 0 && Condition( text[(both + 2)..], values ) != 0 ? 1 : 0;

		foreach ( var op in Comparisons )
		{
			var at = TopLevel( text, op, 0 );
			if ( at < 0 ) continue;
			var a = Condition( text[..at], values );
			var b = Condition( text[(at + op.Length)..], values );
			return op switch
			{
				"==" => a == b,
				"!=" => a != b,
				"<=" => a <= b,
				">=" => a >= b,
				"<" => a < b,
				_ => a > b,
			} ? 1 : 0;
		}

		if ( text.StartsWith( '!' ) ) return Condition( text[1..], values ) == 0 ? 1 : 0;
		if ( int.TryParse( text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number ) ) return number;
		if ( text.Equals( "true", StringComparison.OrdinalIgnoreCase ) ) return 1;
		return values?.Invoke( text ) ?? 0;
	}

	// Two-character ones first, so "<=" isn't read as "<"
	static readonly string[] Comparisons = ["==", "!=", "<=", ">=", "<", ">"];

	/// <summary>The text trimmed, without parentheses around all of it.</summary>
	static string Unwrap( string text )
	{
		text = text.Trim();
		while ( text.Length >= 2 && text[0] == '(' && Closing( text, 0 ) == text.Length - 1 ) text = text[1..^1].Trim();
		return text;
	}

	static int Closing( string text, int open )
	{
		var depth = 0;
		for ( int i = open; i < text.Length; i++ )
		{
			if ( text[i] == '(' ) depth++;
			else if ( text[i] == ')' && --depth == 0 ) return i;
		}

		return -1;
	}

	/// <summary>Where <paramref name="token"/> first is outside parentheses and conditionals, from <paramref name="from"/>; -1 for nowhere.</summary>
	static int TopLevel( string text, string token, int from )
	{
		var depth = 0;
		for ( int i = from; i < text.Length; i++ )
		{
			var c = text[i];
			if ( c == '(' ) depth++;
			else if ( c == ')' ) depth--;
			else if ( depth == 0 && string.CompareOrdinal( text, i, token, 0, token.Length ) == 0 )
			{
				// "<" isn't the start of "<=", nor "=" of "==" read as a comparison of its own
				if ( token.Length == 1 && i + 1 < text.Length && text[i + 1] == '=' ) continue;
				if ( token is "<" or ">" && i > 0 && text[i - 1] is '<' or '>' ) continue;
				return i;
			}
			else if ( depth == 0 && token != "?" && c == '?' ) return -1;
		}

		return -1;
	}
}
