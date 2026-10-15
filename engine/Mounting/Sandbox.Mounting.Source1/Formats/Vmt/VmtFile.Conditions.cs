using System;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VmtFile
{
	private sealed class Condition( string expression, string path, int line, LoadContext context )
	{
		private int position;
		private string error;

		public bool Evaluate()
		{
			var value = ReadExpression( 0 );
			SkipWhiteSpace();
			if ( position != expression.Length )
				error ??= "Unexpected token.";
			if ( error != null )
			{
				context.Report( path, line, VmtDiagnosticKind.InvalidCondition,
					$"Invalid condition [{expression}]: {error} Entry excluded." );
				return false;
			}
			return value == true;
		}

		private void SkipWhiteSpace()
		{
			while ( position < expression.Length && char.IsWhiteSpace( expression[position] ) ) position++;
		}

		private bool Consume( string token )
		{
			SkipWhiteSpace();
			if ( !expression.AsSpan( position ).StartsWith( token.AsSpan(), StringComparison.Ordinal ) ) return false;
			position += token.Length;
			return true;
		}

		private bool? ReadExpression( int depth )
		{
			var value = ReadTerm( depth );
			// Source's CExpressionEvaluator applies && and || left-to-right, at equal precedence.
			while ( error == null )
			{
				if ( Consume( "&&" ) )
					value &= ReadTerm( depth );
				else if ( Consume( "||" ) )
					value |= ReadTerm( depth );
				else
					break;
			}
			return value;
		}

		private bool? ReadTerm( int depth )
		{
			if ( depth >= 64 )
			{
				error = "Expression nesting limit exceeded.";
				return null;
			}
			if ( Consume( "!" ) ) return !ReadTerm( depth + 1 );
			if ( Consume( "(" ) )
			{
				var value = ReadExpression( depth + 1 );
				if ( !Consume( ")" ) ) error ??= "Missing closing parenthesis.";
				return value;
			}
			SkipWhiteSpace();
			if ( position < expression.Length && expression[position] == '$' )
			{
				var start = ++position;
				while ( position < expression.Length && (char.IsAsciiLetterOrDigit( expression[position] ) || expression[position] == '_') ) position++;
				if ( position == start )
				{
					error = "Expected a symbol after '$'.";
					return null;
				}
				var symbol = expression[start..position];
				var value = context.Resolve( symbol );
				if ( value == null )
					context.Report( path, line, VmtDiagnosticKind.UnknownCondition,
						$"Unknown condition symbol ${symbol} in [{expression}]; unresolved entries are excluded." );
				return value;
			}
			if ( position < expression.Length && char.IsAsciiDigit( expression[position] ) )
			{
				var value = false;
				while ( position < expression.Length && char.IsAsciiDigit( expression[position] ) )
					value |= expression[position++] != '0';
				return value;
			}
			error ??= "Expected a symbol, integer, '!' or parenthesis.";
			return null;
		}
	}
}
