using Microsoft.AspNetCore.Components;
using Sandbox.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.UI;

public partial class TextEntry
{
	/// <summary>
	/// If set, visually signals when the input text is shorter than this value. Will also set <see cref="HasValidationErrors"/> accordingly.
	/// </summary>
	[Category( "Validation" )]
	public int? MinLength { get; set; }

	/// <summary>
	/// If set, visually signals when the input text is longer than this value. Will also set <see cref="HasValidationErrors"/> accordingly.
	/// </summary>
	[Category( "Validation" )]
	public int? MaxLength { get; set; }

	/// <summary>
	/// If set, will block the input of any character that doesn't match. Will also set <see cref="HasValidationErrors"/> accordingly.
	/// </summary>
	[Category( "Validation" )]
	public string CharacterRegex { get; set; }

	/// <summary>
	/// If set, <see cref="HasValidationErrors"/> will return true if doesn't match regex.
	/// </summary>
	[Category( "Validation" )]
	public string StringRegex { get; set; }

	/// <summary>
	/// When set to true, ensures only numeric values can be typed. Also applies <see cref="FixNumeric"/> on text.
	/// </summary>
	[Category( "Validation" )]
	public bool Numeric { get; set; } = false;

	/// <summary>
	/// With <see cref="Numeric"/>, only allow whole numbers - no decimal point.
	/// </summary>
	[Category( "Validation" )]
	public bool WholeNumbers { get; set; } = false;

	/// <summary>
	/// If true then this control has validation errors and the input shouldn't be accepted.
	/// </summary>
	public bool HasValidationErrors { get; set; }

	/// <summary>
	/// Update the validation state of this control.
	/// </summary>
	public void UpdateValidation()
	{
		HasValidationErrors = false;

		if ( MinLength.HasValue && TextLength < MinLength )
		{
			HasValidationErrors = true;
		}

		if ( MaxLength.HasValue && TextLength > MaxLength )
		{
			HasValidationErrors = true;
		}

		if ( StringRegex != null )
		{
			HasValidationErrors = HasValidationErrors || !System.Text.RegularExpressions.Regex.IsMatch( Text, StringRegex );
		}

		if ( CharacterRegex != null )
		{
			// oof
			foreach ( var chr in Text )
			{
				HasValidationErrors = HasValidationErrors || !System.Text.RegularExpressions.Regex.IsMatch( chr.ToString(), CharacterRegex );
			}
		}

		SetClass( "invalid", HasValidationErrors );
	}

	/// <summary>
	/// Called when a character is typed by the player.
	/// </summary>
	/// <param name="c">The typed character to test.</param>
	/// <returns> Return true to allow the character to be typed.</returns>
	public virtual bool CanEnterCharacter( char c )
	{
		if ( CharacterRegex != null )
		{
			if ( !System.Text.RegularExpressions.Regex.IsMatch( c.ToString(), CharacterRegex ) )
				return false;
		}

		if ( !Multiline )
		{
			if ( c == '\n' ) return false;
			if ( c == '\r' ) return false;
		}

		if ( Numeric )
		{
			if ( char.IsDigit( c ) ) return true;

			// One decimal separator, and a minus only when negatives are allowed at all
			if ( c == '.' || c == ',' ) return !WholeNumbers && !Text.Contains( '.' ) && !Text.Contains( ',' );
			if ( c == '-' ) return !Text.Contains( '-' ) && (MinValue is null || MinValue < 0);

			return false;
		}

		return true;
	}
}
