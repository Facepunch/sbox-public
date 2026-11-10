using Sandbox;
using System;

[AttributeUsage( AttributeTargets.Method )]
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Instance, "OnMethodInvoked" )]
[CodeGenerator( CodeGeneratorFlags.WrapMethod | CodeGeneratorFlags.Static, "WrapCall.OnMethodInvokedStatic" )]
public class WrapCall : Attribute
{
	public static async Task<T> OnMethodInvokedStatic<T>( WrappedMethod<Task<T>> m, params object[] args )
	{
		return await m.Resume();
	}
	
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
	public static void TestWrappedStaticCall( string arga )
	{
		
	}
	
	[WrapCall]
	public static async Task<bool> TestAsyncTaskCall( string hello, int foo )
	{
		return await Task.FromResult( true );
	}

	[WrapCall]
	public static void TestWrappedStaticCallNoArg()
	{

	}

	[WrapCall]
	public void TestWrappedInstanceCallNoArg()
	{

	}

	[WrapCall]
	public void TestWrappedInstanceCall( string arga )
	{
		
	}
	
	[WrapCall]
	public void MyGenericCall<T>( int a, int b )
	{
		return default;
	}
	
	[WrapCall]
	public Task<T> MyGenericCallAsync<T>( int a )
	{
		return default;
	}
	
	[WrapCall]
	public void ExpressionBodiedBroadcast() => Log.Info( "Test." );

	[WrapCall]
	public bool TestWrappedInstanceCallReturnType( string arga )
	{
		return true;
	}
	
	internal async Task<T> OnMethodInvoked<T>( WrappedMethod<Task<T>> m, int a )
	{
		return await m.Resume();
	}
	
	internal void OnMethodInvoked( WrappedMethod m, params object[] argumentList )
	{
		
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
