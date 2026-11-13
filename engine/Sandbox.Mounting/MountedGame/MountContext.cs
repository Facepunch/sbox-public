namespace Sandbox.Mounting;

public ref struct MountContext
{
	private BaseGameMount source;

	internal MountContext( BaseGameMount source )
	{
		this.source = source;
	}

	// progress output, error recording etc
	public void AddError( string v )
	{
		// TODO
	}

	public void Add( ResourceType type, string path, ResourceLoader entry )
	{
		entry.InitializeInternal( type, path, source );
	}
}
