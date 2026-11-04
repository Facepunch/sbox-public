namespace Sandbox;

public sealed partial class Script
{
	/// <summary>
	/// A script failure thrown by ScriptSystem.Run, with a stable diagnostic code.
	/// </summary>
	[Expose]
	public sealed class ScriptException : Exception
	{
		/// <summary>
		/// Identifies the kind of script failure.
		/// </summary>
		public string Code { get; }

		internal ScriptException( string code, string message, Exception innerException ) : base( message, innerException )
		{
			Code = code;
		}
	}
}
