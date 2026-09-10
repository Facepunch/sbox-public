using System;

namespace AccessTests;

[TestClass]
public partial class VerifyAssemblyTest
{
	/// <summary>
	/// Assembly shouldn't be using a different name to its package name
	/// </summary>
	[TestMethod]
	[DataRow( "package.gio.box.dll" )]
	public void Assembly_Should_Not_Be_Renamed( string dllName )
	{
		using var ac = new AccessControl();

		using var input = System.IO.File.OpenRead( $"{System.Environment.CurrentDirectory}/unittest/{dllName}" );

		var result = ac.VerifyAssembly( input, out var trusted );

		Assert.AreNotEqual( 0, result.Errors.Count, "Should produce an error on renamed dll" );

		foreach ( var error in result.Errors )
		{
			Console.WriteLine( error );
		}

		Assert.IsFalse( result.Success );

		trusted?.Dispose();
	}
}
