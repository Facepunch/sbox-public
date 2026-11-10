namespace Facepunch.InteropGen;

/// <summary>
/// A simple indented text builder: tracks indentation and offers Write/WriteLine/StartBlock/EndBlock
/// for emitting source code into a StringBuilder.
/// </summary>
internal class CodeWriter
{
	public int Indent { get; set; }
	protected System.Text.StringBuilder Builder { get; set; } = new System.Text.StringBuilder();

	public void Write( string line, bool indent = false )
	{
		if ( indent )
		{
			_ = Builder.Append( new string( '\t', Indent ) );
		}

		_ = Builder.Append( line );
	}

	public void WriteLine( string line = "" )
	{
		_ = Builder.Append( new string( '\t', Indent ) );
		_ = Builder.AppendLine( line );
	}

	public void WriteLines( string text )
	{
		string[] lines = text.Split( '\n' );

		foreach ( string line in lines )
		{
			WriteLine( line.TrimEnd() );
		}
	}

	public void StartBlock( string line )
	{
		WriteLine( line );
		WriteLine( "{" );

		Indent++;
	}

	public void EndBlock( string line = "" )
	{
		Indent--;
		WriteLine( $"}}{line}" );
	}

	public override string ToString()
	{
		return Builder.ToString();
	}
}
