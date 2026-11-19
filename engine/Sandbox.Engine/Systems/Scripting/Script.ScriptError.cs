namespace Sandbox;

public sealed partial class Script
{
	/// <summary>
	/// A script failure, with a UTF-16 source range suitable for highlighting in an editor.
	/// </summary>
	[Expose]
	public sealed class ScriptError
	{
		/// <summary>
		/// A stable identifier for the kind of failure.
		/// </summary>
		public string Code { get; }

		/// <summary>
		/// A description of the failure.
		/// </summary>
		public string Message { get; }

		/// <summary>
		/// The first character of the affected source range.
		/// </summary>
		public int Start { get; }

		/// <summary>
		/// The length of the affected source range.
		/// </summary>
		public int Length { get; }

		/// <summary>
		/// The originating script filename, or an empty string for anonymous source.
		/// </summary>
		public string FilePath { get; }

		/// <summary>
		/// The one-based source line, or zero when no location is available.
		/// </summary>
		public int Line { get; }

		/// <summary>
		/// The one-based source column, or zero when no location is available.
		/// </summary>
		public int Column { get; }

		internal ScriptError( string code, string message, int start = 0, int length = 0, string filePath = "", int line = 0, int column = 0 )
		{
			Code = code;
			Message = message;
			Start = start;
			Length = length;
			FilePath = filePath;
			Line = line;
			Column = column;
		}

		/// <inheritdoc/>
		public override string ToString() => string.IsNullOrEmpty( FilePath ) ? Message : $"{FilePath}({Line},{Column}): {Message}";
	}
}
