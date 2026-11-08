using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class VmtFile
{
	private enum TokenKind { End, Value, Open, Close, Assignment, Condition }
	private readonly record struct Token( string Text, TokenKind Kind, int Line );

	private sealed class Parser( string text, string path, LoadContext context )
	{
		private int position;
		private int line = 1;
		private Token? next;
		public List<string> Includes { get; } = new();
		public List<string> Bases { get; } = new();

		private Token Peek() => next ??= ReadToken();

		private Token Take()
		{
			var token = Peek();
			next = null;
			return token;
		}

		private char ReadCharacter()
		{
			var character = text[position++];
			if ( character == '\n' ) line++;
			return character;
		}

		private Token ReadToken()
		{
			while ( position < text.Length )
			{
				if ( char.IsWhiteSpace( text[position] ) )
				{
					ReadCharacter();
					continue;
				}
				if ( position + 1 < text.Length && text[position] == '/' && text[position + 1] == '/' )
				{
					while ( position < text.Length && text[position] != '\n' )
						position++;
					continue;
				}
				break;
			}
			var tokenLine = line;
			if ( position == text.Length ) return new( null, TokenKind.End, tokenLine );
			var first = ReadCharacter();
			if ( first is '{' or '}' or '=' )
				return new( first.ToString(), first == '{' ? TokenKind.Open : first == '}' ? TokenKind.Close : TokenKind.Assignment, tokenLine );
			if ( first == '[' )
			{
				var conditionStart = position;
				while ( position < text.Length && text[position] != ']' )
					ReadCharacter();
				if ( position == text.Length )
					throw new InvalidDataException( $"Unterminated VMT condition in {path}:{tokenLine}." );
				var condition = text[conditionStart..position];
				ReadCharacter();
				return new( condition, TokenKind.Condition, tokenLine );
			}
			if ( first == '"' )
			{
				StringBuilder value = null;
				var segmentStart = position;
				while ( position < text.Length )
				{
					var c = ReadCharacter();
					if ( c == '"' )
					{
						var result = value is null
							? text[segmentStart..(position - 1)]
							: value.Append( text, segmentStart, position - segmentStart - 1 ).ToString();
						return new( result, TokenKind.Value, tokenLine );
					}
					if ( c == '\\' && position < text.Length && text[position] == '"' )
					{
						value ??= new StringBuilder();
						value.Append( text, segmentStart, position - segmentStart - 1 );
						value.Append( ReadCharacter() );
						segmentStart = position;
					}
				}
				throw new InvalidDataException( "Unterminated quoted VMT value." );
			}
			var start = position - 1;
			while ( position < text.Length && !char.IsWhiteSpace( text[position] ) && text[position] is not '{' and not '}' and not '"' )
			{
				if ( text[position] == '/' && position + 1 < text.Length && text[position + 1] == '/' )
					break;
				position++;
			}
			return new( text[start..position], TokenKind.Value, tokenLine );
		}

		private Token ReadValue()
		{
			var token = Take();
			if ( token.Kind != TokenKind.Value )
				throw new InvalidDataException( $"Expected a VMT value in {path}:{token.Line}." );
			return token;
		}

		public List<VmtNode> ReadDocument()
		{
			var roots = new List<VmtNode>();
			while ( Peek().Kind != TokenKind.End )
			{
				var name = ReadValue();
				if ( name.Text.Equals( "#base", StringComparison.OrdinalIgnoreCase )
					|| name.Text.Equals( "#include", StringComparison.OrdinalIgnoreCase ) )
				{
					var reference = ReadValue();
					if ( ReadConditions() )
						(name.Text.Equals( "#base", StringComparison.OrdinalIgnoreCase ) ? Bases : Includes).Add( reference.Text );
					continue;
				}
				if ( name.Text.StartsWith( '#' ) )
					throw new NotSupportedException( $"Unsupported VMT directive {name.Text} in {path}:{name.Line}." );
				var accepted = ReadConditions();
				if ( Take().Kind != TokenKind.Open || string.IsNullOrWhiteSpace( name.Text ) )
					throw new InvalidDataException( $"Expected a named VMT shader block in {path}:{name.Line}." );
				var root = new VmtNode( name.Text, null, ReadBlockContents( 0 ) );
				accepted &= ReadConditions();
				if ( accepted ) roots.Add( root );
			}
			return roots;
		}

		private bool ReadConditions()
		{
			var accepted = true;
			while ( Peek().Kind == TokenKind.Condition )
			{
				var condition = Take();
				accepted &= new Condition( condition.Text, path, condition.Line, context ).Evaluate();
			}
			return accepted;
		}

		private bool ReadKeyCondition( Token key, out string name )
		{
			var separator = key.Text.IndexOf( '?' );
			name = separator < 0 ? key.Text : key.Text[(separator + 1)..];
			if ( separator < 0 ) return true;
			var symbol = key.Text[..separator];
			var negate = false;
			while ( symbol.StartsWith( '!' ) )
			{
				negate = !negate;
				symbol = symbol[1..];
			}
			if ( symbol.StartsWith( '$' ) ) symbol = symbol[1..];
			if ( symbol.Length == 0 || name.Length == 0 )
			{
				context.Report( path, key.Line, VmtDiagnosticKind.InvalidCondition,
					$"Invalid conditional material key {key.Text}; entry excluded." );
				return false;
			}
			var value = context.Resolve( symbol );
			if ( value == null )
			{
				context.Report( path, key.Line, VmtDiagnosticKind.UnknownCondition,
					$"Unknown material condition {symbol} in key {key.Text}; entry excluded." );
				return false;
			}
			return negate ? !value.Value : value.Value;
		}

		private List<VmtNode> ReadBlockContents( int depth )
		{
			if ( depth >= 64 )
				throw new InvalidDataException( "VMT block nesting limit exceeded." );
			var children = new List<VmtNode>();
			while ( true )
			{
				if ( Peek().Kind == TokenKind.Close )
				{
					Take();
					return children;
				}
				var key = ReadValue();
				var accepted = ReadKeyCondition( key, out var name );
				accepted &= ReadConditions();
				if ( Peek().Kind == TokenKind.Assignment )
				{
					Take();
					accepted &= ReadConditions();
				}
				var value = Take();
				VmtNode node;
				if ( value.Kind == TokenKind.Open )
					node = new VmtNode( name, null, ReadBlockContents( depth + 1 ) );
				else if ( value.Kind == TokenKind.Value )
					node = new VmtNode( name, value.Text, Array.Empty<VmtNode>() );
				else
					throw new InvalidDataException( $"Missing VMT value for {key.Text} in {path}:{key.Line}." );
				accepted &= ReadConditions();
				if ( key.Text.StartsWith( '#' ) )
					context.Report( path, key.Line, VmtDiagnosticKind.UnsupportedDirective,
						$"Directive {key.Text} inside a block is retained as data, not executed." );
				if ( accepted ) children.Add( node );
			}
		}
	}
}
