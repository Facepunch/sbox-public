using Sandbox.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Access;

[TestClass]
public partial class Modifier
{
	public Modifier()
	{
		// Make sure Sandbox.Engine is resolvable
		var t = typeof( Sandbox.Application );
	}

	public static IEnumerable<object[]> TestAssemblies()
	{
		yield return new[] { "package.facepunch.platformer.dll" };
		yield return new[] { "package.facepunch.sandbox.dll" };
	}

	bool AssemblyHasReference( byte[] assembly, string referenceName )
	{
		using ( var peReader = new PEReader( new MemoryStream( assembly ), PEStreamOptions.LeaveOpen ) )
		{
			var reader = peReader.GetMetadataReader();

			var referenceNames = reader.AssemblyReferences
												.Select( reader.GetAssemblyReference )
												.Select( x => reader.GetString( x.Name ) )
												.ToArray();

			Console.WriteLine( $"References: {string.Join( ", ", referenceNames )}" );

			return referenceNames.Contains( referenceName );
		}

	}

	bool AssemblyIsNamed( byte[] assembly, string referenceName )
	{
		using ( var peReader = new PEReader( new MemoryStream( assembly ), PEStreamOptions.LeaveOpen ) )
		{
			var reader = peReader.GetMetadataReader();

			var def = reader.GetAssemblyDefinition();
			var assemblyName = def.GetAssemblyName();

			Console.WriteLine( $"assemblyName: {assemblyName}" );

			return assemblyName.Name == referenceName;
		}

	}

	//[TestMethod]
	//[DynamicData( nameof( TestAssemblies ), DynamicDataSourceType.Method )]
	public void ChangeReference( string dllName )
	{
		var mod = new AssemblyModifier();

		mod.ChangeReference["Sandbox.Game"] = "Garry.Sandbox";

		// set up access control

		var input = System.IO.File.ReadAllBytes( $"{System.Environment.CurrentDirectory}/unittest/{dllName}" );

		Assert.IsTrue( AssemblyHasReference( input, "Sandbox.Game" ) );

		var output = mod.Modify( input );

		Assert.IsFalse( AssemblyHasReference( output, "Sandbox.Game" ), "The reference didn't get removed" );
		Assert.IsTrue( AssemblyHasReference( output, "Garry.Sandbox" ), "The reference didn't get changed" );

		var asm = Assembly.Load( output );
		Assert.IsNotNull( asm );
	}

	//[TestMethod]
	//[DynamicData( nameof( TestAssemblies ), DynamicDataSourceType.Method )]
	public void ChangeReferences( string dllName )
	{
		var mod = new AssemblyModifier();

		mod.ChangeReference["Sandbox.Game"] = "Sandbox.Menu";
		mod.ChangeReference["Sandbox.Reflection"] = "Garry.Sandbox";

		// set up access control
		var input = System.IO.File.ReadAllBytes( $"{System.Environment.CurrentDirectory}/unittest/{dllName}" );

		Assert.IsTrue( AssemblyHasReference( input, "Sandbox.Game" ) );
		Assert.IsTrue( AssemblyHasReference( input, "Sandbox.Reflection" ) );

		var output = mod.Modify( input );

		Assert.IsFalse( AssemblyHasReference( output, "Sandbox.Game" ), "The reference didn't get removed" );
		Assert.IsFalse( AssemblyHasReference( output, "Sandbox.Reflection" ), "The reference didn't get removed" );
		Assert.IsTrue( AssemblyHasReference( output, "Garry.Sandbox" ), "The reference didn't get changed" );

		var asm = Assembly.Load( output );
		Assert.IsNotNull( asm );
	}

	//[TestMethod]
	//[DynamicData( nameof( TestAssemblies ), DynamicDataSourceType.Method )]
	public void Rename( string dllName )
	{
		var mod = new AssemblyModifier();

		mod.Rename = "Garrys.Big.Duck";

		// set up access control
		var input = System.IO.File.ReadAllBytes( $"{System.Environment.CurrentDirectory}/unittest/{dllName}" );

		Assert.IsTrue( AssemblyIsNamed( input, System.IO.Path.GetFileNameWithoutExtension( dllName ) ) );

		var output = mod.Modify( input );

		Assert.IsTrue( AssemblyIsNamed( output, "Garrys.Big.Duck" ) );

		var asm = Assembly.Load( output );
		Assert.IsNotNull( asm );
	}
}
