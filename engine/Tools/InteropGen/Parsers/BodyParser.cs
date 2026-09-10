using System.Text;

namespace Facepunch.InteropGen.Parsers;

/// <summary>
/// Captures the braced native body of an inline function verbatim, tracking nesting so it stops at
/// the matching closing brace.
/// </summary>
internal class BodyParser : BaseParser
{
	private readonly Function Func;

	public BodyParser( Definition definition, Function f )
	{
		this.definition = definition;
		Func = f;
	}

	private int Scopes = 0;

	public override void ParseLine( string line )
	{
		string trimmed = line.Trim();

		if ( trimmed == "{" )
		{
			Scopes++;

			if ( Scopes == 1 )
			{
				Func.Body = new StringBuilder();
				return;
			}
		}

		if ( trimmed == "}" )
		{
			Scopes--;

			if ( Scopes == 0 )
			{
				Finished = true;
				return;
			}
		}

		_ = Func.Body.AppendLine( line );
	}
}
