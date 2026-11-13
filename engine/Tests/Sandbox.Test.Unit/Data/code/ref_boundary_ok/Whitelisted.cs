// Names Sandbox.ConCmdAttribute (Sandbox.System), which IS in the compiler reference whitelist, so it binds.
class Example
{
	static System.Type Get() => typeof( Sandbox.ConCmdAttribute );
}
