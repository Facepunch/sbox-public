namespace Facepunch.EndTestLauncher;

internal abstract class Step( string name )
{
	public string Name { get; init; } = name;

	public ExitCode Run()
	{
		return RunInternal();
	}

	protected abstract ExitCode RunInternal();
}
