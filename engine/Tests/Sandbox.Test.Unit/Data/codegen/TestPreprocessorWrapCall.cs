using Sandbox;
using System;

[AttributeUsage( AttributeTargets.Method )]
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Instance, "OnMethodInvoked" )]
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Static, "WrapCall.OnMethodInvokedStatic" )]
public class WrapCall : Attribute
{
	public static void OnMethodInvokedStatic<T1>( WrappedMethod m, T1 arga )
	{
		m.Resume();
	}

	public static void OnMethodInvokedStatic( WrappedMethod m )
	{
		m.Resume();
	}
}

public partial class TestWrapCall
{
	[WrapCall]
	public void TestPreprocessorWrappedMethod()
	{
#if true
		if ( true )
		{
			// Hello there!
		}
#endif

#if SERVER
		// Server-side code only
#endif

#if !SERVER
		// Client-side code only
#endif
	}

	[WrapCall]
	public void AnotherTest()
	{
#if true
		if ( true )
		{
			// Hello there!
		}
#endif

		Log.Info( $"Another line - so the trivia is empty below" );
	}

	internal bool OnMethodInvoked<T1>( WrappedMethod<bool> m, T1 arga )
	{
		
	}

	internal void OnMethodInvoked<T1>( WrappedMethod m, T1 arga )
	{
		
	}

	internal void OnMethodInvoked( WrappedMethod m )
	{
		m.Resume();
	}
}
