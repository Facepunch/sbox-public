namespace Sandbox;

/// <summary>
/// Provides utilities for working with threads, particularly for identifying
/// and asserting code is running on the main thread.
/// </summary>
public static class ThreadSafe
{
	[ThreadStatic]
	static bool isMainThread;

	/// <summary>
	/// Gets the current thread's managed thread ID.
	/// </summary>
	public static int CurrentThreadId => System.Threading.Thread.CurrentThread.ManagedThreadId;

	/// <summary>
	/// Gets the current thread's name, or null if unnamed.
	/// </summary>
	public static string CurrentThreadName => System.Threading.Thread.CurrentThread.Name;

	/// <summary>
	/// Returns true if currently executing on the main thread.
	/// </summary>
	public static bool IsMainThread => isMainThread;

	/// <summary>
	/// Marks the current thread as the main thread.
	/// This is called internally during engine initialization.
	/// </summary>
	internal static void MarkMainThread()
	{
		isMainThread = true;
	}

	[ThreadStatic]
	static bool isRenderThread;

	/// <summary>
	/// Whether this is the game window's render thread: it draws the frames the main thread hands over, with the main thread's
	/// rendering code, so it counts as the main thread for that code (<see cref="IsMainThread"/>) while running no game code.
	/// </summary>
	internal static bool IsRenderThread => isRenderThread;

	/// <summary>
	/// Mark the current thread as the render thread (<see cref="IsRenderThread"/>), which counts as the main thread for the
	/// rendering code it runs.
	/// </summary>
	internal static void MarkRenderThread()
	{
		isMainThread = true;
		isRenderThread = true;
	}

	/// <summary>
	/// Throws an exception if not called from the main thread.
	/// Useful for enforcing thread safety on main-thread-only APIs.
	/// </summary>
	/// <param name="memberName">Automatically filled with the calling method name</param>
	/// <exception cref="System.Exception">Thrown if not on the main thread</exception>
	public static void AssertIsMainThread( [System.Runtime.CompilerServices.CallerMemberName] string memberName = "" )
	{
		if ( IsMainThread ) return;

		throw new System.Exception( $"{memberName} must be called on the main thread!" );
	}

	/// <summary>
	/// Throws an exception if called from the main thread.
	/// Useful for enforcing that blocking operations don't run on the main thread.
	/// </summary>
	/// <exception cref="System.Exception">Thrown if on the main thread</exception>
	public static void AssertIsNotMainThread()
	{
		if ( !IsMainThread ) return;

		throw new System.Exception( "This function must not be called on the main thread!" );
	}

}
