using System.Text.RegularExpressions;

namespace Facepunch.InteropGen.Parsers;

/// <summary>
/// Parses the body of a class: its functions, inline functions (pushing a <see cref="BodyParser"/>),
/// variables and attributes, until the closing brace.
/// </summary>
internal class ClassParser : BaseParser
{
	private readonly Class Class;

	public ClassParser( Definition definition, Class c )
	{
		this.definition = definition;
		Class = c;
	}

	public override void ParseLine( string line )
	{
		string trimmedLine = line.Trim();

		if ( trimmedLine == "{" )
		{
			return;
		}

		if ( trimmedLine == "}" )
		{
			Finished = true;
			return;
		}

		Function inline_func = Function.ParseInline( line );
		if ( inline_func != null )
		{
			inline_func.Class = Class;
			inline_func.TakeAttributes( Attributes );
			Class.Functions.Add( inline_func );

			subParser.Push( new BodyParser( definition, inline_func ) );
			return;
		}

		Function func = Function.Parse( line );
		if ( func != null )
		{
			func.Class = Class;
			func.TakeAttributes( Attributes );
			Class.Functions.Add( func );
			return;
		}

		Variable var = Variable.Parse( line );
		if ( var != null )
		{
			var.TakeAttributes( Attributes );
			Class.Variables.Add( var );
			return;
		}

		Match attribute = AttributeRegex.Match( trimmedLine );
		if ( attribute.Success )
		{
			Attributes.Add( attribute.Groups[1].Value );
			return;
		}

		base.ParseLine( line );
	}
}
