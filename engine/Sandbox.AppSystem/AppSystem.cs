using Sandbox.Diagnostics;
using Sandbox.Engine;
using Sandbox.Internal;
using Sandbox.Network;
using Sandbox.Rendering;
using System;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace Sandbox;

public class AppSystem
{
	protected Logger log = new Logger("AppSystem");
	internal CMaterialSystem2AppSystemDict _appSystem { get; set; }

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

	/// <summary>
	/// We should check all the system requirements here as early as possible.
	/// </summary>
	public void TestSystemRequirements()
	{
		if (!OperatingSystem.IsWindows())
			return;

		// AVX is on any sane CPU since 2011
		if (!Avx.IsSupported)
		{
			MessageBox(IntPtr.Zero, "Your CPU needs to support AVX instructions to run this game.", "Unsupported CPU", 0x10);
			Environment.Exit(1);
		}

		// check core count, ram, os?
		// rendersystemvulkan ends up checking gpu, driver, vram later on

		MissingDependancyDiagnosis.Run();
	}

	public virtual void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		NetCore.InitializeInterop(Environment.CurrentDirectory);
	}

	void SetupEnvironment()
	{
		CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();

		//
		// force GregorianCalendar, because that's how we're going to be parsing dates etc
		//
		if (culture.DateTimeFormat.Calendar is not GregorianCalendar)
		{
			culture.DateTimeFormat.Calendar = new GregorianCalendar();
		}

		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
	}

	/// <summary>
	/// Create the Menu instance.
	/// </summary>
	protected void CreateMenu()
	{
		MenuDll.Create();
	}

	/// <summary>
	/// Create the Game (Sandbox.GameInstance)
	/// </summary>
	protected void CreateGame()
	{
		GameInstanceDll.Create();
	}

	/// <summary>
	/// Create the editor (Sandbox.Tools)
	/// </summary>
	protected void CreateEditor()
	{
		Editor.AssemblyInitialize.Initialize();
	}

	public void Run()
	{
		try
		{
			SetupEnvironment();

			Application.TryLoadVersionInfo(Environment.CurrentDirectory);

			//
			// Putting ErrorReporter.Initialize(); before Init here causes engine2.dll 
			// to be unable to load. I dont know wtf and I spent too much time looking into it.
			// It's finding the assemblies still, The last dll it loads is tier0.dll.
			//

			Init();

			NativeEngine.EngineGlobal.Plat_SetCurrentFrame(0);

			while (RunFrame())
			{
				BlockingLoopPumper.Run(() => RunFrame());
			}

			Shutdown();
		}
		catch (System.Exception e)
		{
			ErrorReporter.Initialize();
			ErrorReporter.ReportException(e);
			ErrorReporter.Flush();

			Console.WriteLine($"Error: ({e.GetType()}) {e.Message}");

			Environment.Exit(1);
		}
	}

	protected virtual bool RunFrame()
	{
		EngineLoop.RunFrame(_appSystem, out bool wantsToQuit);

		return !wantsToQuit;
	}

	public virtual void Shutdown()
	{
		// Make sure game instance is closed
		IGameInstanceDll.Current?.CloseGame();

		// Send shutdown event, should allow us to track successful shutdown vs crash
		{
			var analytic = new Api.Events.EventRecord("Exit");
			analytic.SetValue("uptime", RealTime.Now);
			// We could record a bunch of stats during the session and
			// submit them here. I'm thinking things like num games played
			// menus visited, time in menus, time in game, files downloaded.
			// Things to give us a whole session picture.
			analytic.Submit();
		}

		ConVarSystem.SaveAll();

		IToolsDll.Current?.Exiting();
		IMenuDll.Current?.Exiting();
		IGameInstanceDll.Current?.Exiting();

		SoundFile.Shutdown();
		SoundHandle.Shutdown();
		DedicatedServer.Shutdown();

		// Flush API
		Api.Shutdown();

		ConVarSystem.ClearNativeCommands();

		// Whatever package still exists needs to fuck off
		PackageManager.UnmountAll();

		// Clear static resources
		Texture.DisposeStatic();
		Model.DisposeStatic();
		Material.UI.DisposeStatic();
		Gizmo.GizmoDraw.DisposeStatic();
		CubemapRendering.DisposeStatic();
		Graphics.DisposeStatic();

		TextRendering.ClearCache();

		NativeResourceCache.Clear();

		// Renderpipeline may hold onto native resources, clear them out
		RenderPipeline.ClearPool();

		// Run GC and finalizers to clear any resources held by managed
		GC.Collect();
		GC.WaitForPendingFinalizers();

		// Run the queue one more time, since some finalizers queue tasks
		MainThread.RunQueues();

		// print each scene that is leaked
		foreach (var leakedScene in Scene.All)
		{
			log.Warning($"Leaked scene {leakedScene.Id} during shutdown.");
		}

		// Shut the engine down (close window etc)
		NativeEngine.EngineGlobal.SourceEngineShutdown(_appSystem, false);

		if (_appSystem.IsValid)
		{
			_appSystem.Destroy();
			_appSystem = default;
		}

		if (steamApiDll != IntPtr.Zero)
		{
			NativeLibrary.Free(steamApiDll);
			steamApiDll = default;
		}
		// Unload native dlls:
		// At this point we should no longer need them.
		// If we still hold references to native resources, we want it to crash here rather than on application exit.
		Managed.SandboxEngine.NativeInterop.Free();

		// No-ops if editor isn't loaded
		Managed.SourceTools.NativeInterop.Free();
		Managed.SourceAssetSytem.NativeInterop.Free();
		Managed.SourceHammer.NativeInterop.Free();
		Managed.SourceModelDoc.NativeInterop.Free();
		Managed.SourceAnimgraph.NativeInterop.Free();

		EngineFileSystem.Shutdown();
		Application.Shutdown();
	}

	protected void InitGame(AppSystemCreateInfo createInfo, string commandLine = null)
	{
		commandLine ??= System.Environment.CommandLine;
		commandLine = commandLine.Replace(".dll", ".exe"); // uck

<<<<<<< HEAD
		if (OperatingSystem.IsWindows()) _appSystem = CMaterialSystem2AppSystemDict.Create(createInfo.ToMaterialSystem2AppSystemDictCreateInfo());
		else _appSystem = CreateAppSystemWithInteropWorkaround(createInfo);
=======
		_appSystem = OperatingSystem.IsWindows()
			? CMaterialSystem2AppSystemDict.Create( createInfo.ToMaterialSystem2AppSystemDictCreateInfo() )
			: CreateAppSystemWithInteropWorkaround( createInfo );
>>>>>>> 73728b8cc77f9bcef4ce8d4d456ac637b7507b94

		if (createInfo.Flags.HasFlag(AppSystemFlags.IsEditor))
		{
			_appSystem.SetInToolsMode();
		}

		if (createInfo.Flags.HasFlag(AppSystemFlags.IsUnitTest))
		{
			_appSystem.SetInTestMode();
		}

		if (createInfo.Flags.HasFlag(AppSystemFlags.IsStandaloneGame))
		{
			_appSystem.SetInStandaloneApp();
		}

		if (createInfo.Flags.HasFlag(AppSystemFlags.IsDedicatedServer))
		{
			_appSystem.SetDedicatedServer(true);
		}

		_appSystem.SetSteamAppId((uint)Application.AppId);

		if (!NativeEngine.EngineGlobal.SourceEnginePreInit(commandLine, _appSystem))
		{
			throw new System.Exception("SourceEnginePreInit failed");
		}

		Bootstrap.PreInit(_appSystem);

		if (createInfo.Flags.HasFlag(AppSystemFlags.IsStandaloneGame))
		{
			Standalone.Init();
		}

		if (!NativeEngine.EngineGlobal.SourceEngineInit(_appSystem))
		{
			throw new System.Exception("SourceEngineInit returned false");
		}

		Bootstrap.Init();
	}

	protected void SetWindowTitle(string title)
	{
		_appSystem.SetAppWindowTitle(title);
	}

	IntPtr steamApiDll = IntPtr.Zero;

	/// <summary>
	/// Explicitly load the Steam Api dll from our bin folder, so that it doesn't accidentally
	/// load one from c:\system32\ or something. This is a problem when people have installed
	/// pirate versions of Steam in the past and have the assembly hanging around still. By loading
	/// it here we're saying use this version, and it won't try to load another one.
	/// </summary>
	protected void LoadSteamDll()
	{
		if (!OperatingSystem.IsWindows())
			return;

		var dllName = $"{Environment.CurrentDirectory}\\bin\\win64\\steam_api64.dll";
		if (!NativeLibrary.TryLoad(dllName, out steamApiDll))
		{
			throw new System.Exception("Couldn't load bin/win64/steam_api64.dll");
		}
	}

	/// <summary>
	/// Creates a <see cref="CMaterialSystem2AppSystemDict"/> using a manual interop lifetime workaround.
	/// </summary>
	/// <remarks>
	/// On some Linux configurations, the native material system creation path can retain and
	/// dereference a pointer to <see cref="NativeEngine.MaterialSystem2AppSystemDictCreateInfo"/>
	/// after the managed marshalling layer would normally release or move the underlying data.
	/// This can lead to a native use-after-free when the structure is passed using the default
	/// P/Invoke marshalling behavior.
	///
	/// This helper allocates the create-info structure in unmanaged memory, copies the managed
	/// data into that buffer, and only frees it after the native
	/// <c>CMtrlSystm2ppSys_Create</c> call returns. This effectively extends the lifetime of the
	/// data across the interop boundary and avoids the use-after-free on Linux, while remaining
	/// safe on other platforms.
	///
	/// Use this method when constructing a <see cref="CMaterialSystem2AppSystemDict"/> in code
	/// paths that call into the native material system on Linux or when the exact lifetime
	/// expectations of the native code are unknown. Prefer other, simpler creation helpers only
	/// when you are certain the native side does not retain the provided pointer beyond the call.
	/// </remarks>
	private static CMaterialSystem2AppSystemDict CreateAppSystemWithInteropWorkaround(AppSystemCreateInfo createInfo)
	{
		var ci = createInfo.ToMaterialSystem2AppSystem2AppSystemDictCreateInfo();
		var size = Marshal.SizeOf<NativeEngine.MaterialSystem2AppSystemDictCreateInfo>();

		IntPtr pCI = Marshal.AllocHGlobal(size);

		try
		{
			Marshal.StructureToPtr(ci, pCI, false);

			unsafe
			{
				return new CMaterialSystem2AppSystemDict(
					CMaterialSystem2AppSystemDict.__N.CMtrlSystm2ppSys_Create(
						(NativeEngine.MaterialSystem2AppSystemDictCreateInfo*)pCI
					)
				);
			}
		}
		finally
		{
			Marshal.FreeHGlobal(pCI);
		}
	}
}
