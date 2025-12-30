using Facepunch.ActionGraphs;
using Sandbox.Engine;
using System.Reflection;

namespace Sandbox;

public static partial class Game
{
	/// <summary>
	/// Provides access to the global <see cref="Sandbox.Internal.TypeLibrary"/> for the current game context.
	/// <para>
	/// The <c>TypeLibrary</c> is a runtime reflection system that describes types, their members, and relationships in the game and engine assemblies. It allows you to
	/// find and create types by name and id. It's basically a sandboxed version of the .net reflection system.
	/// </para>
	/// </summary>
	public static Sandbox.Internal.TypeLibrary TypeLibrary
	{
		get => GlobalContext.Current.TypeLibrary;
		internal set => GlobalContext.Current.TypeLibrary = value;
	}

	/// <summary>
	/// Allows access to the cookies for the current game. The cookies are used to store persistent data across game sessions, such as user preferences or session data.
	/// Internally the cookies are encoded to JSON and stored in a file on disk.
	/// </summary>
	public static CookieContainer Cookies
	{
		get => GlobalContext.Current.Cookies;
		internal set => GlobalContext.Current.Cookies = value;
	}

	/// <summary>
	/// Lets you get translated phrases from the localization system
	/// </summary>
	public static LanguageContainer Language
	{
		get => GlobalContext.Current.Language;
		internal set => GlobalContext.Current.Language = value;
	}

	/// <summary>
	/// A library of node definitions for action graphs.
	/// </summary>
	internal static NodeLibrary NodeLibrary
	{
		get => GlobalContext.Current.NodeLibrary;
		set => GlobalContext.Current.NodeLibrary = value;
	}

	private static void AddNodesFromAssembly( Assembly asm )
	{
		var result = NodeLibrary.AddAssembly( asm );

		foreach ( var error in result.Errors )
		{
			Log.Error( $"{error.Key}: {error.Value}" );
		}
	}

	/// <summary>
	/// Returns true only when current code is running in the menu.
	/// </summary>
	internal static bool IsMenu => GlobalContext.Current == GlobalContext.Menu;

}
