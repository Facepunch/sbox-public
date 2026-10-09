using NativeEngine;
using Sandbox.Engine;
using Sandbox.UI;
using UITests.Controls;
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EngineTests;

[TestClass, DoNotParallelize]
public unsafe class WindowInputTests
{
	static bool relative, failCapture;
	static int screensaverDisables, screensaverEnables;
	static int creates, destroys, shows, hides, overrideCalls, stops, starts;
	static IntPtr relativeWindow, textWindow, selected;
	static int nativeFocus;
	static bool appActive;
	static IntPtr keyboardFocus;
	static Sdl.Rect textArea;
	static int surfacesDestroyed;
	static bool failColor;
	static (int, int) hotspot;
	static (int, float, float) warp;

	[TestMethod]
	public void PlayFocusDoesNotControlApplicationActivation() => WithInput( () =>
	{
		SetActivation( false );
		appActive = true; // Focus notifications can arrive before the next activation poll.
		WindowInput.OnEditorGameFocusChange( 20, true );
		WindowInput.SetRelativeMouseMode( true );
		Assert.IsTrue( WindowInput.GetRelativeMouseMode() );
		Assert.AreEqual( (IntPtr)10, relativeWindow );
		Assert.AreEqual( 1, overrideCalls );
		WindowInput.OnEditorGameFocusChange( 20, false );
		Assert.IsTrue( WindowInput.IsAppActive() );
		Assert.IsFalse( WindowInput.HasMouseFocus() );
		Assert.IsFalse( WindowInput.GetRelativeMouseMode() );
		Assert.AreEqual( 0, nativeFocus );
		Assert.AreEqual( 2, overrideCalls );
		SetActivation( false );
		WindowInput.OnEditorGameFocusChange( 20, true );
		WindowInput.SetRelativeMouseMode( true );
		Assert.IsFalse( WindowInput.GetRelativeMouseMode(), "Capture requires application activation as well as play focus." );
		SetActivation( true );
		Assert.IsTrue( WindowInput.HasMouseFocus() );
		Assert.IsTrue( Application.IsFocused );
		var panel = new FakePanelWindow { Handle = 30 };
		PanelWindows.Register( panel );
		try
		{
			keyboardFocus = panel.Handle;
			SetActivation( false );
			Assert.IsTrue( WindowInput.IsAppActive(), "An SDL panel keeps the editor active even when Qt is inactive." );
			Assert.IsFalse( WindowInput.HasMouseFocus(), "Panel focus must not retain play-widget capture." );
			Assert.IsFalse( Application.IsFocused );
		}
		finally { PanelWindows.Unregister( panel ); }
	} );

	[TestMethod]
	public void StandalonePanelFocusDoesNotCaptureTheGame() => WithInput( () =>
	{
		IToolsDll.Current = null;
		keyboardFocus = 30;
		WindowInput.UpdateApplicationState();
		Assert.IsTrue( WindowInput.IsAppActive() );
		WindowInput.SetRelativeMouseMode( true );
		Assert.IsFalse( WindowInput.GetRelativeMouseMode() );
		WindowInput.SetRelativeMouseMode( false );
		Assert.IsFalse( WindowInput.GetRelativeMouseMode() );
		Assert.AreEqual( 0, overrideCalls );
	} );

	[TestMethod]
	public void SdlFocusDoesNotOverrideEditorActivation() => WithInput( () =>
	{
		WindowInput.OnEditorGameFocusChange( 20, true );
		keyboardFocus = IntPtr.Zero;
		WindowInput.UpdateApplicationState();
		Assert.IsTrue( WindowInput.IsAppActive() );
		Assert.IsTrue( WindowInput.HasMouseFocus() );
		SetActivation( false );
		keyboardFocus = 20;
		WindowInput.OnEditorGameFocusChange( 20, true );
		WindowInput.UpdateApplicationState();
		Assert.IsFalse( WindowInput.IsAppActive() );
		Assert.IsFalse( WindowInput.HasMouseFocus() );
		SetActivation( true );
		Assert.IsTrue( WindowInput.HasMouseFocus() );
	} );

	[TestMethod]
	public void FailedCaptureDoesNotClaimTheMouse() => WithInput( () =>
	{
		SetActivation( true );
		WindowInput.OnEditorGameFocusChange( 20, true );
		failCapture = true;
		WindowInput.SetRelativeMouseMode( true );
		Assert.IsFalse( WindowInput.GetRelativeMouseMode() );
		Assert.AreEqual( 0, overrideCalls );
	} );

	[TestMethod]
	public void PanelCaptureTracksFailuresAndTransfers() => WithInput( () =>
	{
		var first = new FakePanelWindow { Handle = 30 };
		var second = new FakePanelWindow { Handle = 40 };
		failCapture = true;
		PanelWindowInput.SetRelativeMouse( first, true );
		Assert.IsNull( PanelWindows.CaptureWindow );
		Assert.IsFalse( PanelWindows.SkipNextCaptureDelta );
		failCapture = false;
		PanelWindowInput.SetRelativeMouse( first, true );
		Assert.AreSame( first, PanelWindows.CaptureWindow );
		Assert.IsTrue( PanelWindows.SkipNextCaptureDelta );
		PanelWindows.CaptureDelta = new Vector2( 3, 4 );
		failCapture = true;
		PanelWindowInput.SetRelativeMouse( second, true );
		Assert.AreSame( first, PanelWindows.CaptureWindow, "A failed release must not transfer capture." );
		Assert.AreEqual( new Vector2( 3, 4 ), PanelWindows.CaptureDelta );
		failCapture = false;
		PanelWindowInput.SetRelativeMouse( second, true );
		Assert.AreSame( second, PanelWindows.CaptureWindow );
		Assert.AreEqual( Vector2.Zero, PanelWindows.CaptureDelta );
		PanelWindowInput.SetRelativeMouse( second, false );
		Assert.IsNull( PanelWindows.CaptureWindow );
		Assert.IsFalse( PanelWindows.SkipNextCaptureDelta );
		Assert.AreEqual( (40, 12f, 34f), warp );
		PanelWindowInput.SetRelativeMouse( first, true );
		failCapture = true;
		PanelWindows.Unregister( first );
		Assert.IsNull( PanelWindows.CaptureWindow, "A closed window must not retain capture when SDL rejects release." );
		Assert.IsFalse( PanelWindows.SkipNextCaptureDelta );
	} );

	[TestMethod]
	public void NamedCursorSelectionIsSharedAcrossWindows() => WithInput( () =>
	{
		SdlCursors.SetCursor( "text" );
		Assert.AreEqual( (IntPtr)101, selected );
		var before = shows;
		SdlCursors.SetCursor( "IBEAM" );
		Assert.AreEqual( before, shows, "Aliases should reuse the selected cursor." );
		SdlCursors.SetCursor( "pointer", allowCustom: false );
		SdlCursors.SetCursor( "text" );
		Assert.AreEqual( (IntPtr)101, selected, "Returning from a panel must restore the game cursor." );
		SdlCursors.ShowArrow();
		SdlCursors.SetCursor( "text" );
		Assert.AreEqual( (IntPtr)101, selected );
		Assert.IsTrue( SdlCursors.CreateCursor( "ibeam", new byte[16], 2, 2, 0, 0 ) );
		SdlCursors.SetCursor( "text" );
		Assert.AreEqual( (IntPtr)600, selected, "Custom cursors take precedence after alias resolution." );
		SdlCursors.SetCursor( "text", allowCustom: false );
		Assert.AreEqual( (IntPtr)101, selected );
		SdlCursors.SetCursor( "unknown" );
		Assert.AreEqual( (IntPtr)100, selected );
	} );

	[TestMethod]
	public void TextInputAndCaretUseTheEditorWindow() => WithInput( () =>
	{
		WindowInput.OnEditorGameFocusChange( 20, true );
		WindowInput.SetIMEAllowed( false );
		WindowInput.SetIMEAllowed( true );
		WindowInput.SetIMETextLocation( 5, 6, 7, 8 );
		Assert.AreEqual( 1, stops );
		Assert.AreEqual( 1, starts );
		Assert.AreEqual( (IntPtr)10, textWindow );
		Assert.AreEqual( 35, textArea.X );
		Assert.AreEqual( 46, textArea.Y );
		Assert.AreEqual( 7, textArea.Width );
		Assert.AreEqual( 8, textArea.Height );
		WindowInput.OnWindowDestroyed( 20 );
		Assert.IsFalse( WindowInput.HasMouseFocus() );
		WindowInput.OnWindowDestroyed( 10 );
		WindowInput.SetIMEAllowed( false );
		Assert.AreEqual( 1, stops, "A destroyed window must not receive SDL calls." );
	} );

	[TestMethod]
	public void CursorCacheDisposesOnceAndFocusRestoresSelection() => WithInput( () =>
	{
		SdlCursors.SetCursor( "ibeam" );
		SdlCursors.SetCursor( "ibeam" );
		Assert.AreEqual( 2, creates, "Arrow and I-beam should each be created once." );
		WindowInput.OnEditorGameFocusChange( 20, false );
		Assert.AreEqual( (IntPtr)100, selected );
		WindowInput.OnEditorGameFocusChange( 20, true );
		Assert.AreEqual( (IntPtr)101, selected );
		SdlCursors.SetCursor( "none" );
		Assert.IsTrue( hides > 0 );
		SdlCursors.Shutdown();
		SdlCursors.Shutdown();
		Assert.AreEqual( creates, destroys );
	} );

	[TestMethod]
	public void CustomCursorsReleaseSurfacesAndClampHotspots() => WithInput( () =>
	{
		var pixels = new byte[16];
		Assert.IsTrue( SdlCursors.CreateCursor( "test", pixels, 2, 2, -10, 20 ) );
		Assert.AreEqual( (0, 1), hotspot );
		Assert.AreEqual( 1, surfacesDestroyed );
		Assert.IsTrue( SdlCursors.CreateCursor( "TEST", pixels, 2, 2, 0, 0 ) );
		Assert.AreEqual( 1, surfacesDestroyed, "Names share one cursor regardless of case." );
		SdlCursors.SetCursor( "test" );
		SdlCursors.ShutdownUserCursors();
		Assert.IsFalse( SdlCursors.HasUserCursor( "test" ) );
		Assert.AreEqual( (IntPtr)100, selected );
		Assert.AreEqual( 1, destroys );
		failColor = true;
		Assert.IsFalse( SdlCursors.CreateCursor( "failed", pixels, 2, 2, 0, 0 ) );
		Assert.AreEqual( 2, surfacesDestroyed );
		Assert.IsFalse( SdlCursors.HasUserCursor( "failed" ) );
	} );

	[TestMethod]
	public void ScreensaverIsRestoredOnceOnShutdown() => WithInput( () =>
	{
		Assert.AreEqual( 1, screensaverDisables );
		Assert.AreEqual( 0, screensaverEnables );
		WindowInput.Shutdown();
		WindowInput.Shutdown();
		Assert.AreEqual( 1, screensaverEnables );
	} );

	[TestMethod]
	public void HeadlessInputSkipsNativeInputAndScreensaver() => WithInput( () =>
	{
		WindowInput.Shutdown();
		typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, true );
		nativeFocus = 42;
		WindowInput.Initialize();
		WindowInput.Shutdown();
		Assert.AreEqual( 42, nativeFocus );
		Assert.AreEqual( 1, screensaverDisables );
		Assert.AreEqual( 1, screensaverEnables );
	} );

	static void WithInput( Action test )
	{
		var disableScreensaver = Sdl.__N.globalSdl_DisableScreenSaver;
		var enableScreensaver = Sdl.__N.globalSdl_EnableScreenSaver;
		var tools = IToolsDll.Current;
		var applicationActive = Native.QApp.__N.qApp_IsApplicationActive;
		var keyboard = Sdl.__N.globalSdl_GetKeyboardFocus;
		IToolsDll.Current = (IToolsDll)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject( typeof( ToolsDll ) );
		var saved0 = InputSystem.__N.g_pInputSystem_SetGameFocus;
		var saved1 = g_pToolFramework2.__N.g_pTlFrmwrk2_SetOverrideCursor;
		var saved2 = InputSystem.__N.g_pInputSystem_SetEditorMainWindow;
		var saved3 = GameWindowNative.__N.GameWindowGlue_FromNativeHandle;
		var saved4 = Sdl.__N.globalSdl_CreateSystemCursor;
		var saved5 = Sdl.__N.globalSdl_DestroyCursor;
		var saved6 = Sdl.__N.globalSdl_SetCursor;
		var saved7 = Sdl.__N.globalSdl_ShowCursor;
		var saved8 = Sdl.__N.globalSdl_HideCursor;
		var saved9 = Sdl.__N.globalSdl_GetWindowRelativeMouseMode;
		var saved10 = Sdl.__N.globalSdl_SetWindowRelativeMouseMode;
		var saved11 = Sdl.__N.globalSdl_StartTextInput;
		var saved12 = Sdl.__N.globalSdl_StopTextInput;
		var saved13 = Sdl.__N.globalSdl_SetTextInputArea;
		var saved14 = Sdl.__N.globalSdl_GetWindowPosition;
		var createSurface = Sdl.__N.globalSdl_CreateSurfaceFrom;
		var destroySurface = Sdl.__N.globalSdl_DestroySurface;
		var createColor = Sdl.__N.globalSdl_CreateColorCursor;
		var mouseState = Sdl.__N.globalSdl_GetMouseState;
		var warpMouse = Sdl.__N.globalSdl_WarpMouseInWindow;
		var unitTest = Application.IsUnitTest;
		var wasMainThread = ThreadSafe.IsMainThread;
		var headless = Application.IsHeadless;
		ThreadSafe.MarkMainThread();
		typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, false );
		Application.IsUnitTest = false;
		try
		{
			Sdl.__N.globalSdl_DisableScreenSaver = &DisableScreensaver;
			Sdl.__N.globalSdl_EnableScreenSaver = &EnableScreensaver;
			screensaverDisables = screensaverEnables = 0;
			Native.QApp.__N.qApp_IsApplicationActive = &ApplicationActive;
			Sdl.__N.globalSdl_GetKeyboardFocus = &KeyboardFocus;
			keyboardFocus = IntPtr.Zero;
			appActive = true;
			InputSystem.__N.g_pInputSystem_SetGameFocus = &State;
			g_pToolFramework2.__N.g_pTlFrmwrk2_SetOverrideCursor = &Override;
			InputSystem.__N.g_pInputSystem_SetEditorMainWindow = &SetEditor;
			GameWindowNative.__N.GameWindowGlue_FromNativeHandle = &Identity;
			Sdl.__N.globalSdl_CreateSystemCursor = &CreateCursor;
			Sdl.__N.globalSdl_DestroyCursor = &DestroyCursor;
			Sdl.__N.globalSdl_SetCursor = &SetCursor;
			Sdl.__N.globalSdl_ShowCursor = &Show;
			Sdl.__N.globalSdl_HideCursor = &Hide;
			Sdl.__N.globalSdl_GetWindowRelativeMouseMode = &GetRelative;
			Sdl.__N.globalSdl_SetWindowRelativeMouseMode = &SetRelative;
			Sdl.__N.globalSdl_StartTextInput = &StartText;
			Sdl.__N.globalSdl_StopTextInput = &StopText;
			Sdl.__N.globalSdl_SetTextInputArea = (delegate* unmanaged< IntPtr, ref Sdl.Rect, int, int >)(delegate* unmanaged< IntPtr, Sdl.Rect*, int, int >)&SetArea;
			Sdl.__N.globalSdl_GetWindowPosition = (delegate* unmanaged< IntPtr, out int, out int, int >)(delegate* unmanaged< IntPtr, int*, int*, int >)&Position;
			Sdl.__N.globalSdl_CreateSurfaceFrom = &CreateSurface;
			Sdl.__N.globalSdl_DestroySurface = &DestroySurface;
			Sdl.__N.globalSdl_CreateColorCursor = &CreateColor;
			Sdl.__N.globalSdl_GetMouseState = (delegate* unmanaged< out float, out float, uint >)(delegate* unmanaged< float*, float*, uint >)&MouseState;
			Sdl.__N.globalSdl_WarpMouseInWindow = &WarpMouse;
			PanelWindows.CaptureWindow = null;
			PanelWindows.SkipNextCaptureDelta = false;
			surfacesDestroyed = 0;
			failColor = false;
			relative = failCapture = false;
			creates = destroys = shows = hides = overrideCalls = stops = starts = 0;
			WindowInput.Initialize();
			WindowInput.SetEditorMainWindow( 10 );
			WindowInput.UpdateApplicationState();
			test();
		}
		finally
		{
			WindowInput.Shutdown();
			Sdl.__N.globalSdl_DisableScreenSaver = disableScreensaver;
			Sdl.__N.globalSdl_EnableScreenSaver = enableScreensaver;
			InputSystem.__N.g_pInputSystem_SetGameFocus = saved0;
			g_pToolFramework2.__N.g_pTlFrmwrk2_SetOverrideCursor = saved1;
			InputSystem.__N.g_pInputSystem_SetEditorMainWindow = saved2;
			GameWindowNative.__N.GameWindowGlue_FromNativeHandle = saved3;
			Sdl.__N.globalSdl_CreateSystemCursor = saved4;
			Sdl.__N.globalSdl_DestroyCursor = saved5;
			Sdl.__N.globalSdl_SetCursor = saved6;
			Sdl.__N.globalSdl_ShowCursor = saved7;
			Sdl.__N.globalSdl_HideCursor = saved8;
			Sdl.__N.globalSdl_GetWindowRelativeMouseMode = saved9;
			Sdl.__N.globalSdl_SetWindowRelativeMouseMode = saved10;
			Sdl.__N.globalSdl_StartTextInput = saved11;
			Sdl.__N.globalSdl_StopTextInput = saved12;
			Sdl.__N.globalSdl_SetTextInputArea = saved13;
			Sdl.__N.globalSdl_GetWindowPosition = saved14;
			Sdl.__N.globalSdl_CreateSurfaceFrom = createSurface;
			Sdl.__N.globalSdl_DestroySurface = destroySurface;
			Sdl.__N.globalSdl_CreateColorCursor = createColor;
			Sdl.__N.globalSdl_GetMouseState = mouseState;
			Sdl.__N.globalSdl_WarpMouseInWindow = warpMouse;
			PanelWindows.CaptureWindow = null;
			PanelWindows.CaptureDelta = 0;
			PanelWindows.SkipNextCaptureDelta = false;
			IToolsDll.Current = tools;
			Native.QApp.__N.qApp_IsApplicationActive = applicationActive;
			Sdl.__N.globalSdl_GetKeyboardFocus = keyboard;
			Application.IsUnitTest = unitTest;
			typeof( Application ).GetProperty( nameof( Application.IsHeadless ) ).SetValue( null, headless );
			typeof( ThreadSafe ).GetField( "isMainThread", BindingFlags.NonPublic | BindingFlags.Static ).SetValue( null, wasMainThread );
		}
	}

	[UnmanagedCallersOnly] static uint MouseState( float* x, float* y ) { *x = 12; *y = 34; return 0; }
	[UnmanagedCallersOnly] static void WarpMouse( IntPtr window, float x, float y ) => warp = ((int)window, x, y);
	[UnmanagedCallersOnly] static int DisableScreensaver() { screensaverDisables++; return 1; }
	[UnmanagedCallersOnly] static int EnableScreensaver() { screensaverEnables++; return 1; }

	[UnmanagedCallersOnly] static IntPtr CreateSurface( int width, int height, uint format, IntPtr pixels, int pitch ) => (IntPtr)500;
	[UnmanagedCallersOnly] static void DestroySurface( IntPtr surface ) => surfacesDestroyed++;
	[UnmanagedCallersOnly] static IntPtr CreateColor( IntPtr surface, int x, int y ) { hotspot = (x, y); return failColor ? IntPtr.Zero : (IntPtr)600; }

	[UnmanagedCallersOnly] static void State( int focused ) => nativeFocus = focused;
	[UnmanagedCallersOnly] static int ApplicationActive() => appActive ? 1 : 0;
	[UnmanagedCallersOnly] static IntPtr KeyboardFocus() => keyboardFocus;

	static void SetActivation( bool value )
	{
		appActive = value;
		WindowInput.UpdateApplicationState();
	}
	[UnmanagedCallersOnly] static void Override( int value ) => overrideCalls++;
	[UnmanagedCallersOnly] static void SetEditor( IntPtr hwnd ) { }
	[UnmanagedCallersOnly] static IntPtr Identity( IntPtr hwnd ) => hwnd;
	[UnmanagedCallersOnly] static IntPtr CreateCursor( long type ) { creates++; return (IntPtr)(100 + type); }
	[UnmanagedCallersOnly] static void DestroyCursor( IntPtr cursor ) => destroys++;
	[UnmanagedCallersOnly] static int SetCursor( IntPtr cursor ) { selected = cursor; return 1; }
	[UnmanagedCallersOnly] static int Show() { shows++; return 1; }
	[UnmanagedCallersOnly] static int Hide() { hides++; return 1; }
	[UnmanagedCallersOnly] static int GetRelative( IntPtr window ) => relative && relativeWindow == window ? 1 : 0;
	[UnmanagedCallersOnly] static int SetRelative( IntPtr window, int value ) { if ( failCapture ) return 0; relativeWindow = window; relative = value != 0; return 1; }
	[UnmanagedCallersOnly] static int StartText( IntPtr window ) { starts++; textWindow = window; return 1; }
	[UnmanagedCallersOnly] static int StopText( IntPtr window ) { stops++; textWindow = window; return 1; }
	[UnmanagedCallersOnly] static int SetArea( IntPtr window, Sdl.Rect* area, int cursor ) { textWindow = window; textArea = *area; return 1; }
	[UnmanagedCallersOnly] static int Position( IntPtr window, int* x, int* y ) { *x = window == (IntPtr)20 ? 40 : 10; *y = window == (IntPtr)20 ? 60 : 20; return 1; }
}
