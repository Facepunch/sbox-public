using Sandbox.Mounting;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace MountingTests;

/// <summary>
/// A mount that needs no files, for a mount assembly a test makes up: its ident is its type name. Abstract, so
/// the test assembly's own mounts stay as they are.
/// </summary>
public abstract class TestConsoleMount : BaseGameMount
{
	public override string Ident => GetType().Name.ToLowerInvariant();
	public override string Title => GetType().Name;

	protected override void Initialize( InitializeContext context ) => IsInstalled = true;

	protected override Task Mount( MountContext context )
	{
		IsMounted = true;
		return Task.CompletedTask;
	}
}

[TestClass]
public class MountConsoleTests
{
	const string Variable = "testmount_volume";
	const string Command = "testmount_ping";

	static Assembly BuildMountAssembly( params string[] mounts )
	{
		var assembly = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( $"TestConsoleMounts{Guid.NewGuid():N}" ), AssemblyBuilderAccess.Run );
		var module = assembly.DefineDynamicModule( "main" );

		foreach ( var name in mounts )
		{
			var mount = module.DefineType( name, TypeAttributes.Public | TypeAttributes.Class, typeof( TestConsoleMount ) );
			mount.DefineDefaultConstructor( MethodAttributes.Public );
			mount.CreateType();
		}

		// public static class Settings { [ConVar( Variable )] public static string Volume { get; set; } [ConCmd( Command )] public static void Ping() { } }
		var settings = module.DefineType( "Settings", TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.Abstract | TypeAttributes.Sealed );
		var field = settings.DefineField( "_volume", typeof( string ), FieldAttributes.Private | FieldAttributes.Static );
		var accessor = MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

		var get = settings.DefineMethod( "get_Volume", accessor, typeof( string ), Type.EmptyTypes );
		var il = get.GetILGenerator();
		il.Emit( OpCodes.Ldsfld, field );
		il.Emit( OpCodes.Ret );

		var set = settings.DefineMethod( "set_Volume", accessor, null, [typeof( string )] );
		il = set.GetILGenerator();
		il.Emit( OpCodes.Ldarg_0 );
		il.Emit( OpCodes.Stsfld, field );
		il.Emit( OpCodes.Ret );

		var property = settings.DefineProperty( "Volume", PropertyAttributes.None, typeof( string ), null );
		property.SetGetMethod( get );
		property.SetSetMethod( set );
		property.SetCustomAttribute( new CustomAttributeBuilder( typeof( ConVarAttribute ).GetConstructor( [typeof( string ), typeof( ConVarFlags )] ), [Variable, default( ConVarFlags )] ) );

		var ping = settings.DefineMethod( "Ping", MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, null, Type.EmptyTypes );
		ping.GetILGenerator().Emit( OpCodes.Ret );
		ping.SetCustomAttribute( new CustomAttributeBuilder( typeof( ConCmdAttribute ).GetConstructor( [typeof( string ), typeof( ConVarFlags )] ), [Command, default( ConVarFlags )] ) );

		// The assembly its types say they are from, which is not the builder
		return settings.CreateType().Assembly;
	}

	static bool Known( string name ) => ConVarSystem.Members.ContainsKey( name );

	[TestMethod]
	public async Task AMountsVariableIsUnknownUntilItIsMountedAndGoneAfter()
	{
		var assembly = BuildMountAssembly( "ConsoleMountSolo" );

		Sandbox.Mounting.Directory.AddTypeAssembly( assembly );
		Sandbox.Mounting.Directory.AddAssembly( assembly );

		try
		{
			Assert.IsNotNull( Sandbox.Mounting.Directory.Get( "consolemountsolo" ) );
			Assert.IsFalse( Known( Variable ), "Loading the assembly alone registers nothing" );
			Assert.IsNull( ConsoleSystem.GetValue( Variable ) );

			await Sandbox.Mounting.Directory.Mount( "consolemountsolo" );

			Assert.IsTrue( Known( Variable ) );
			Assert.IsTrue( Known( Command ) );
			ConsoleSystem.SetValue( Variable, "0.5" );
			Assert.AreEqual( "0.5", ConsoleSystem.GetValue( Variable ) );
			Assert.AreEqual( "0.5", assembly.GetType( "Settings" ).GetProperty( "Volume" ).GetValue( null ), "It is the mount's own variable" );

			await Sandbox.Mounting.Directory.SetMountState( "consolemountsolo", false );

			Assert.IsFalse( Known( Variable ) );
			Assert.IsFalse( Known( Command ) );
			Assert.IsNull( ConsoleSystem.GetValue( Variable ) );

			await Sandbox.Mounting.Directory.Mount( "consolemountsolo" );
			Assert.IsTrue( Known( Variable ) );

			Sandbox.Mounting.Directory.RemoveAssembly( assembly );
			Assert.IsFalse( Known( Variable ), "Removing the assembly removes what it registered" );
		}
		finally
		{
			Sandbox.Mounting.Directory.RemoveAssembly( assembly );
		}
	}

	[TestMethod]
	public async Task TwoMountsFromOneAssemblyKeepItUntilTheLastIsUnmounted()
	{
		var assembly = BuildMountAssembly( "ConsoleMountA", "ConsoleMountB" );

		Sandbox.Mounting.Directory.AddTypeAssembly( assembly );
		Sandbox.Mounting.Directory.AddAssembly( assembly );

		try
		{
			await Sandbox.Mounting.Directory.Mount( "consolemounta" );
			await Sandbox.Mounting.Directory.Mount( "consolemountb" );
			Assert.IsTrue( Known( Variable ) );

			await Sandbox.Mounting.Directory.SetMountState( "consolemounta", false );
			Assert.IsTrue( Known( Variable ), "The assembly's other mount still needs it" );

			await Sandbox.Mounting.Directory.SetMountState( "consolemountb", false );
			Assert.IsFalse( Known( Variable ) );
		}
		finally
		{
			Sandbox.Mounting.Directory.RemoveAssembly( assembly );
		}
	}

	[TestMethod]
	public async Task AnAssemblyThatWasNotLoadedAsAMountAssemblyIsLeftAlone()
	{
		// One that came through the package system has its console members registered with its package
		var assembly = BuildMountAssembly( "ConsoleMountPackaged" );
		Sandbox.Mounting.Directory.AddAssembly( assembly, exposeMountTypes: true );

		try
		{
			await Sandbox.Mounting.Directory.Mount( "consolemountpackaged" );
			Assert.IsTrue( Sandbox.Mounting.Directory.Get( "consolemountpackaged" ).IsMounted );
			Assert.IsFalse( Known( Variable ) );
		}
		finally
		{
			Sandbox.Mounting.Directory.RemoveAssembly( assembly );
		}
	}
}
