using System.Globalization;

namespace Sandbox
{
	/// <summary>
	/// A lightweight string parser with cursor-based navigation.
	/// Designed for parsing text files, CSS, and other structured text formats.
	/// Uses ref struct to stay stack-allocated for performance.
	/// </summary>
	internal ref struct Parse
	{
		/// <summary>Source file name for error reporting</summary>
		public string FileName;

		/// <summary>The text being parsed</summary>
		public string Text;

		/// <summary>Current position in the text</summary>
		public int Pointer;
		int lineOffset;

		public Parse( string value, string filename = "nofile", int lineOffset = 0 ) : this()
		{
			FileName = filename;
			Text = value;

			this.lineOffset = lineOffset;
		}



		public int Length => Text.Length;
		public bool IsEnd => Pointer >= Length;
		public char Current => IsEnd ? '\0' : Text[Pointer];
		public char Next => Pointer + 1 >= Length ? '\0' : Text[Pointer + 1];
		public bool IsWhitespace => char.IsWhiteSpace( Current );
		public bool IsNewline => Current == '\n' || Current == '\r';
		public bool IsDigit => char.IsDigit( Current );
		public bool IsLetter => char.IsLetter( Current );

		public Parse JumpToEndOfLine( bool afterNewline )
		{
			var p = this;

			while ( !p.IsEnd && !p.IsNewline )
			{
				p.Pointer++;
			}

			if ( afterNewline )
			{
				while ( !p.IsEnd && p.IsNewline )
				{
					p.Pointer++;
				}
			}

			return p;
		}
		public bool IsOneOf( string chars )
		{
			if ( chars == null )
				return false;

			return chars.IndexOf( Current ) >= 0;
		}

		public string Read( int chars )
		{
			if ( chars <= 0 ) throw new System.Exception( $"Tried to read {chars} chars" );

			var result = Text.Substring( Pointer, chars );
			Pointer += chars;
			return result;
		}

		public string ReadRemaining( bool acceptNone = false )
		{
			if ( IsEnd && acceptNone ) return string.Empty;
			if ( IsEnd ) throw new System.Exception( $"Tried to ReadRemaining but we're at the end" );

			var result = Text.Substring( Pointer );
			Pointer = Length;
			return result;
		}

		public Parse SkipWhitespaceAndNewlines( string andCharacters = null )
		{
			while ( !IsEnd )
			{
				if ( !IsWhitespace && !IsNewline && !IsOneOf( andCharacters ) )
					return this;

				Pointer++;
			}

			return this;
		}

		public string ReadUntilWhitespaceOrNewlineOrEnd( string andCharacters = null )
		{
			var p = this;

			while ( true )
			{
				if ( p.IsEnd || p.IsNewline || p.IsWhitespace || IsOneOf( andCharacters ) )
					return this.Read( p.Pointer - Pointer );

				p.Pointer++;
			}
		}

		public string ReadUntilWhitespaceOrNewlineOrEndAndObeyBrackets()
		{
			var p = this;
			int inside = 0;

			while ( true )
			{
				var lineEnder = p.IsNewline || p.IsWhitespace;
				if ( inside > 0 ) lineEnder = false;

				if ( p.IsEnd || lineEnder )
					return this.Read( p.Pointer - Pointer );

				if ( p.Is( '(' ) ) inside++;
				if ( p.Is( ')' ) ) inside--;

				p.Pointer++;
			}
		}

		public string ReadInnerBrackets( char inner = '(', char outer = ')' )
		{
			int inside = 0;
			int iStart = Pointer;
			int iEnd;

			while ( true )
			{
				if ( IsEnd )
					return null;

				if ( Is( inner ) )
				{
					if ( inside == 0 ) iStart = Pointer + 1;
					inside++;
				}

				if ( Is( outer ) )
				{
					inside--;
					if ( inside == 0 )
					{
						iEnd = Pointer;
						Pointer++;
						break;
					}
				}

				Pointer++;
			}

			return Text.Substring( iStart, iEnd - iStart );
		}

		public string ReadWord( string endOnCharacter = null, bool readUntilEnd = false )
		{
			var p = this;

			while ( true )
			{
				if ( p.IsEnd && !readUntilEnd )
					return null;

				if ( p.IsEnd || p.IsWhitespace || p.IsNewline || p.IsOneOf( endOnCharacter ) )
					return this.Read( p.Pointer - Pointer );

				p.Pointer++;
			}
		}

		public string ReadChars( string chars = null, bool readUntilEnd = false )
		{
			var p = this;

			while ( true )
			{
				if ( p.IsEnd && !readUntilEnd )
					return null;

				if ( p.IsEnd || !p.IsOneOf( chars ) )
				{
					if ( p.Pointer == Pointer ) return null;

					return this.Read( p.Pointer - Pointer );
				}

				p.Pointer++;
			}
		}

		/// <summary>
		/// Reads a sentence until the next statement divided by ,
		/// Returns the sentence
		/// </summary>
		public string ReadSentence()
		{
			var p = this;
			while ( !p.Is( "," ) && !p.IsEnd )
			{
				if ( p.Is( "(" ) )
				{
					while ( !p.Is( ")" ) && !p.IsEnd )
					{
						p.Pointer++;
					}
				}
				else
					p.Pointer++;
			}

			return this.Read( p.Pointer - Pointer );
		}

		public string ReadUntil( string c1 )
		{
			var p = this;

			while ( !p.IsEnd )
			{
				if ( p.IsOneOf( c1 ) )
					return this.Read( p.Pointer - Pointer );

				p.Pointer++;
			}

			return null;
		}

		public string ReadUntilOrEnd( string c1, bool acceptNone = false )
		{
			var p = this;

			while ( !p.IsEnd )
			{
				if ( p.IsOneOf( c1 ) )
				{
					if ( p.Pointer == Pointer )
						return string.Empty;

					return this.Read( p.Pointer - Pointer );
				}

				p.Pointer++;
			}

			return this.ReadRemaining( acceptNone );
		}

		public (string, string) ReadKeyValue()
		{
			var key = ReadUntilOrEnd( ":" );
			if ( string.IsNullOrWhiteSpace( key ) ) throw new System.Exception( $"Expected key {FileAndLine}" );
			Pointer++;

			if ( IsEnd ) throw new System.Exception( $"Expected value {FileAndLine}" );

			var value = ReadUntilOrEnd( ";" );
			if ( string.IsNullOrWhiteSpace( value ) ) throw new System.Exception( $"Expected value {FileAndLine}" );
			Pointer++;

			return (key.Trim(), value.Trim());
		}

		public bool TryReadTime( out float val )
		{
			val = 0;
			var p = this;

			p = p.SkipWhitespaceAndNewlines();

			var numStart = p.Pointer;

			while ( !p.IsEnd )
			{
				if ( p.IsDigit || p.Current == '.' )
				{
					p.Pointer++;
					continue;
				}

				if ( p.Current == 's' || p.Current == 'S' )
				{
					var len = p.Pointer - numStart;
					var str = p.Text.Substring( numStart, len );

					if ( !float.TryParse( str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed ) )
						return false;

					Pointer = p.Pointer + 1;

					val = parsed * 1000.0f;
					return true;
				}

				if ( p.Current == 'm' || p.Current == 'M' )
				{
					if ( p.Next != 's' && p.Next != 'S' )
						return false;

					var len = p.Pointer - numStart;
					var str = p.Text.Substring( numStart, len );


					if ( !float.TryParse( str, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed ) )
						return false;

					Pointer = p.Pointer + 2;
					val = parsed;
					return true;
				}

				return false;
			}

			return false;
		}

		internal bool TryReadLength( out Sandbox.UI.Length outval )
		{
			outval = 0;
			var p = this;

			if ( p.IsEnd ) return false;
			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return false;

			var numStart = p.Pointer;

			var w = p.ReadWord( ")", true );
			if ( w == null ) return false;

			var v = Sandbox.UI.Length.Parse( w );
			if ( !v.HasValue ) return false;

			outval = v.Value;
			Pointer = p.Pointer;
			return true;
		}

		internal bool TryReadRepeat( out string outval )
		{
			outval = "";
			var p = this;

			p = p.SkipWhitespaceAndNewlines();

			if ( !p.IsLetter )
				return false;

			var w = p.ReadWord( null, true );
			switch ( w )
			{
				case "no-repeat":
				case "repeat-x":
				case "repeat-y":
				case "repeat":
				case "clamp":
					outval = w;
					break;
				default:
					return false;
			}

			Pointer += w.Length;
			return true;
		}

		internal bool TryReadMaskMode( out string outval )
		{
			outval = "";
			var p = this;

			p = p.SkipWhitespaceAndNewlines();

			if ( !p.IsLetter )
				return false;

			var w = p.ReadWord( null, true );
			switch ( w )
			{
				case "match-source":
				case "alpha":
				case "luminance":
					outval = w;
					break;
				default:
					return false;
			}

			Pointer += w.Length;
			return true;
		}

		internal bool TryReadLineStyle( out string outval )
		{
			outval = "";
			var p = this;

			p = p.SkipWhitespaceAndNewlines();

			if ( !p.IsLetter )
				return false;

			var w = p.ReadWord( null, true );
			switch ( w )
			{
				case "none":
				case "solid":
					// case "double":
					// case "dotted":
					// case "dashed":
					// case "inset":
					// case "outset":
					// case "ridge":
					// case "groove":
					// case "hidden":
					outval = w;
					break;
				default:
					return false;
			}

			Pointer += w.Length;
			return true;
		}

		public bool TryReadFloat( out float outval )
		{
			outval = 0;
			var p = this;

			if ( p.IsEnd ) return false;
			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return false;

			var w = p.ReadChars( "-0123456789.Ee", true );
			if ( w == null )
				return false;

			if ( !float.TryParse( w, NumberStyles.Float, CultureInfo.InvariantCulture, out outval ) )
				return false;

			Pointer += w.Length;

			// if it ends in f, skip it
			if ( Current == 'f' )
				Pointer++;

			return true;
		}

		internal bool TryReadColor( out Color outval )
		{
			outval = default;
			var p = this;

			if ( p.IsEnd ) return false;
			p = p.SkipWhitespaceAndNewlines();
			if ( p.IsEnd ) return false;

			int inside = 0;
			var numStart = p.Pointer;

			while ( !p.IsEnd )
			{
				if ( p.Current == '(' )
					inside++;

				if ( p.Current == ')' )
					inside--;

				if ( inside < 0 )
					return false;

				if ( inside == 0 && p.IsOneOf( " ;\t\n\r," ) )
					break;

				p.Pointer++;
			}

			if ( numStart == p.Pointer )
				return false;

			var c = p.Text.Substring( numStart, p.Pointer - numStart );

			var color = Color.Parse( c );
			if ( !color.HasValue ) return false;

			Pointer = p.Pointer;
			outval = color.Value;
			return true;
		}

		/// <summary>
		/// <para>
		/// Typically used to parse shorthand position &amp; size combinations, like those seen inside
		/// mask and background shorthands.
		/// <code>&lt;position&gt; [ / &lt;size&gt; ]</code>
		/// </para>
		/// </summary>
		internal bool TryReadPositionAndSize( out Sandbox.UI.Length positionX, out Sandbox.UI.Length positionY, out Sandbox.UI.Length sizeX, out Sandbox.UI.Length sizeY )
		{
			// Initial values
			positionX = 0;
			positionY = 0;
			sizeX = UI.Length.Auto;
			sizeY = UI.Length.Auto;

			//
			// <position>
			//
			if ( TryReadLength( out positionX ) )
			{
				if ( !TryReadLength( out positionY ) )
					positionY = positionX;

				SkipWhitespaceAndNewlines();

				//
				// [ / <size> ]?
				//
				if ( TrySkip( "/" ) )
				{
					// We have a size
					if ( !TryReadLength( out sizeX ) )
						return false; // Invalid - expected a length

					if ( !TryReadLength( out sizeY ) )
						return true; // We don't require a length here
				}

				SkipWhitespaceAndNewlines();
				return true;

			}

			return false;
		}

		internal bool TryReadShadowInset( out bool isInset )
		{
			isInset = false;
			var p = this;

			p = p.SkipWhitespaceAndNewlines();

			if ( !p.IsLetter )
				return false;

			var w = p.ReadWord( ",", true );
			switch ( w )
			{
				case "inset":
					isInset = true;
					break;
				default:
					return false;
			}

			Pointer += w.Length;
			return true;
		}

		/// <summary>
		/// Return true if the string at the pointer is this
		/// </summary>
		public bool Is( string v, int offset = 0, bool ignorecase = false )
		{
			var len = v.Length;

			for ( int i = 0; i < len; i++ )
			{
				if ( !Is( v[i], i, ignorecase ) )
					return false;
			}

			return true;
		}

		/// <summary>
		/// Skip this string if it exists
		/// </summary>
		public bool TrySkip( string v, int offset = 0, bool ignorecase = false )
		{
			var len = v.Length;

			for ( int i = 0; i < len; i++ )
			{
				if ( !Is( v[i], i, ignorecase ) )
					return false;
			}

			Pointer += len;
			return true;
		}

		/// <summary>
		/// Skip comma and then possible whitespace
		/// </summary>
		public bool TrySkipCommaSeparation()
		{
			SkipWhitespaceAndNewlines();

			if ( Current != ',' ) return false;

			Read( 1 );
			SkipWhitespaceAndNewlines();
			return true;
		}

		/// <summary>
		/// Return true if the char at the pointer is this
		/// </summary>
		public bool Is( char v, int offset = 0, bool ignorecase = false )
		{
			var ptr = Pointer + offset;
			if ( ptr >= Length ) return false;
			if ( ptr < 0 ) return false;

			if ( ignorecase )
				return char.ToLowerInvariant( Text[ptr] ) == char.ToLowerInvariant( v );

			return Text[ptr] == v;
		}

		/// <summary>
		/// Get the line we're currently on
		/// </summary>
		public int CurrentLine
		{
			get
			{
				var substr = Text.Substring( 0, Math.Min( Pointer, Text.Length ) );
				var lines = substr.Count( x => x == '\n' );
				return lines + lineOffset;
			}
		}

		public string FileAndLine => $"[{FileName}:{CurrentLine}]";
	}
}
