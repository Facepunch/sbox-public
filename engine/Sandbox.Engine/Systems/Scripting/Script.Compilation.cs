namespace Sandbox;

public sealed partial class Script
{
	/// <summary>
	/// Checks that the script compiles with its supplied arguments without executing it.
	/// Arguments remain available for the next Run. Failure sets LastError.
	/// </summary>
	public bool Compile() => Compile<Breen.ScriptInputs>( default, false );

	internal bool Compile<TInputs>( Breen.ScriptInvocation<TInputs> invocation, bool hasArguments = true )
		where TInputs : Breen.IScriptInputs, allows ref struct
	{
		_system.CheckAccess();
		if ( _running )
		{
			LastError = new( "SCR_REENTRY", "A script cannot compile during execution." );
			return false;
		}
		_running = true;
		try
		{
			var result = hasArguments ? invocation.Compile() : _instance.Compile();
			LastError = result.Success ? null : result.Reason.ToEngineError();
			return result.Success;
		}
		finally
		{
			_running = false;
		}
	}
}
