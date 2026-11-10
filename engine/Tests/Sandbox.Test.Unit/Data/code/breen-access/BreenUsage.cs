public static class BreenUsage
{
	public static object Create() => new Breen.ScriptSystem( new() { Resolver = new Breen.DefaultResolver() } );
}
