using System;
using System.Reflection;

namespace ReflectionTests;

[TestClass]
[DoNotParallelize]
public class NullStaticReferencesTests
{
	sealed class Held { }

	static class Holder
	{
		public static int[,] Table = { { 1, 2 }, { 3, 4 } };
		public static int[] Values = { 5, 6 };
		public static object[] Things = { new Held(), "kept" };
		public static Held Single = new();
	}

	/// <summary>A type whose static constructor fails, like a game type first touched by the teardown sweep.</summary>
	static class Uninitialisable
	{
		public static Held First = new();
		public static object[] Second = { new Held() };
		public static Held Third = new();

		static Uninitialisable() => throw new InvalidOperationException( "script host disposed" );
	}

	static bool TryClear( object value, Type target )
	{
		var method = typeof( Sandbox.ReflectionUtility ).GetMethod( "TryClearCollectionOfType", BindingFlags.NonPublic | BindingFlags.Static );
		return (bool)method.Invoke( null, new[] { value, target } );
	}

	/// <summary>
	/// A multi-dimensional array can't be indexed through IList: clearing must skip it rather than throw (a game's
	/// int[,] static logged a warning per field at every game close).
	/// </summary>
	[TestMethod]
	public void MultiDimensionalAndValueArraysAreSkipped()
	{
		Assert.IsFalse( TryClear( new int[2, 2], typeof( Held ) ) );
		Assert.IsFalse( TryClear( new object[2, 2], typeof( Held ) ) );
		Assert.IsFalse( TryClear( new[] { 1, 2 }, typeof( Held ) ) );
	}

	[TestMethod]
	public void MatchingStaticsAreNulledAndOthersKept()
	{
		Sandbox.ReflectionUtility.NullStaticReferencesOfType( typeof( Holder ).Assembly, typeof( Held ) );

		Assert.IsNull( Holder.Single );
		Assert.IsNull( Holder.Things[0] );
		Assert.AreEqual( "kept", Holder.Things[1] );
		Assert.AreEqual( 4, Holder.Table[1, 1] );
		Assert.AreEqual( 6, Holder.Values[1] );
	}

	/// <summary>
	/// A type whose static constructor first runs, and fails, during the sweep holds nothing: it is skipped without a
	/// warning (a dedicated server logged one with a stack trace per field of each such game type at every quit).
	/// </summary>
	[TestMethod]
	public void TypesWhoseInitializerFailsAreSkippedQuietly()
	{
		var warnings = new System.Collections.Generic.List<string>();
		void OnMessage( Sandbox.LogEvent e ) { if ( e.Message.Contains( nameof( Uninitialisable ) ) ) warnings.Add( e.Message ); }
		Sandbox.Diagnostics.Logging.OnEmitted += OnMessage;

		try
		{
			Sandbox.ReflectionUtility.NullStaticReferencesOfType( typeof( Uninitialisable ).Assembly, typeof( Held ) );
		}
		finally
		{
			Sandbox.Diagnostics.Logging.OnEmitted -= OnMessage;
		}

		Assert.AreEqual( 0, warnings.Count, string.Join( "\n", warnings ) );
	}
}
