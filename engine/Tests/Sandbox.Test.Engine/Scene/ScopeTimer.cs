using Sandbox.Diagnostics;
using System;

public class ScopeTimer : IDisposable
{

	FastTimer timer;
	string Name { get; set; }

	public ScopeTimer( string name )
	{
		Name = name;
		timer.Start();
	}

	public void Dispose()
	{
		System.Console.WriteLine( $"{Name} took {timer.ElapsedMilliSeconds} ms" );
	}
}
