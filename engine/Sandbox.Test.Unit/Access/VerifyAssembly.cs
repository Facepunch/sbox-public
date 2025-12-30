using System;
using System.Diagnostics;
using System.IO;

namespace Access;

[TestClass]
public partial class VerifyAssembly
{
	/// <summary>
	/// Assembly shouldn't be using a different name to its package name
	/// </summary>
	[TestMethod]
	[DataRow( "package.gio.box.dll" )]
	public void Assembly_Should_Not_Be_Renamed( string dllName )
	{
		var ac = new AccessControl();

		var input = System.IO.File.OpenRead( $"{System.Environment.CurrentDirectory}/unittest/{dllName}" );

		var result = ac.VerifyAssembly( input, out var trusted );

		Assert.AreNotEqual( 0, result.Errors.Count, "Should produce an error on renamed dll" );

		foreach ( var error in result.Errors )
		{
			Console.WriteLine( error );
		}

		Assert.IsFalse( result.Success );
	}

	/// <summary>
	/// Assembly shouldn't be using a different name to its package name
	/// </summary>
	//[TestMethod]
	//[DataRow( "package.facepunch.sandbox.dll" )]
	public void Should_Pass( string dllName )
	{
		var sw = Stopwatch.StartNew();

		var baseBytes = System.IO.File.ReadAllBytes( $"{System.Environment.CurrentDirectory}/unittest/package.base.dll" );
		var bytes = System.IO.File.ReadAllBytes( $"{System.Environment.CurrentDirectory}/unittest/{dllName}" );

		//for ( int i = 0; i < 100; i++ )
		{
			using ( new HeavyGarbageRegion() )
			{
				var ac = new AccessControl();

				{

					using var ms = new MemoryStream( baseBytes );
					var result = ac.VerifyAssembly( ms, out var trusted );

					foreach ( var error in result.Errors )
					{
						Console.WriteLine( error );
					}

					Assert.AreEqual( 0, result.Errors.Count, "Should produce no errors" );
					Assert.IsTrue( result.Success );
					Assert.AreNotEqual( null, trusted );
				}

				{
					using var ms = new MemoryStream( bytes );
					var result = ac.VerifyAssembly( ms, out var trusted );

					foreach ( var error in result.Errors )
					{
						Console.WriteLine( error );
					}

					Assert.AreEqual( 0, result.Errors.Count, "Should produce no errors" );
					Assert.IsTrue( result.Success );
					Assert.AreNotEqual( null, trusted );

					/*
					// I broke this sorry! - Sol
					var str = string.Join( "\n", result.Touched.Values.OrderByDescending( x => x.Count ).Select( x =>
					{
						return $"{x.Count}\t{x.Name} [{x.Type}]";
					} ) );

					System.IO.File.WriteAllText( $"c:\\ui\\{dllName}.txt", str );
					*/
				}

				ac.Dispose();
			}
		}

		Console.WriteLine( $"Took {sw.Elapsed.TotalMilliseconds:0.00}ms" );
	}
}
