using System.Runtime.InteropServices;
using System.Text;
using Facepunch.XR;
using NativeEngine;
using Sandbox.Utility;

namespace Sandbox.VR;

internal static unsafe partial class VRSystem
{
	public enum States
	{
		Inactive,
		Standby,
		Active
	}

	public static States State { get; private set; }
	public static bool IsActive => State == States.Active;
	internal static bool InternalIsActive() => IsActive; // For native side

	private static float IPD { get; set; } = 0f;
	public static float IPDInches => IPD;
	public static float RefreshRate { get; private set; }
	public static List<TrackedDevice> TrackedDevices { get; private set; }

	internal static Vector2 FullRenderTargetSize { get; private set; }
	internal static Vector2 EyeRenderTargetSize { get; private set; }

	public static float WorldScale { get; set; } = 1.0f;
	public static bool HasHeadset { get; set; } = false;

	internal static bool IsRendering = false;

	public static void Init()
	{
		//
		// If we've already inited, don't do it again
		//
		if ( IsActive )
			return;

		//
		// Check if we actually want to init VR
		//
		if ( CommandLine.HasSwitch( "-novr" ) )
			return;

		if ( Application.IsHeadless )
			return;

		if ( Application.IsStandalone && !Standalone.Manifest.IsVRProject )
			return;

		//
		// Check if we have a headset...
		//
		HasHeadset = Instance.HasHeadset();

		if ( !HasHeadset )
			return;

		// All good, init
		{
			State = States.Active;

			CreateInstance();
			Reset();
		}
	}

	[ConCmd( "vr_info", ConVarFlags.Protected )]
	public static void VrInfoConCommand()
	{
		if ( !IsActive )
		{
			Log.Info( "VR not initialized." );
			return;
		}

		Log.Info( $"Refresh rate: {RefreshRate}Hz" );
		Log.Info( $"Distance between eyes (IPD): {IPD} inches ({IPD.InchToMillimeter()}mm)" );
		Log.Info( $"Render target size: {EyeRenderTargetSize}" );
		Log.Info( $"System name: '{GetSystemName()}'" );
		Log.Info( $"Session state: {SessionState}" );

		Log.Info( $"------------------------------------------------------------------------------------------------------------------------" );

		Log.Info( $"Tracked objects:" );

		int i = 0;
		foreach ( var trackedObject in Sandbox.Input.VR.TrackedObjects )
		{
			var device = trackedObject._trackedDevice;

			if ( device.IsActive )
			{
				Log.Info( $"Device {i}:" );
				Log.Info( $"\tType: {device.DeviceType}" );
				Log.Info( $"\tRole: {device.DeviceRole}" );
				Log.Info( $"\tIndex: {device.DeviceIndex}" );

				Log.Info( $"\tPosition: {device.Transform.Position}" );
				Log.Info( $"\tRotation: {device.Transform.Rotation.Angles()}" );

				Log.Info( $"\tInput source: \"{device.InputSource}\"" );
				Log.Info( $"\tInput source handle: {device.InputSourceHandle}" );

				Log.Info( $"------------------------------------------------------------------------------------------------------------------------" );
			}

			i++;
		}
	}

	internal static void FrameStart()
	{
		if ( !IsActive )
			return;

		if ( Game.IsPlaying || Game.IsMainMenuVisible )
		{
			OnPlay();

			Update();

			// TODO: More than just controllers.. e.g. trackers
			// Not sure how easy this is to do nor how many people actually use them right now
			// since the biggest platform (by far) is Quest, which doesn't support trackers
			var leftHandDevice = new TrackedDevice( InputSource.LeftHand );
			var rightHandDevice = new TrackedDevice( InputSource.RightHand );

			VRInput.Current ??= new VRInput( leftHandDevice, rightHandDevice );
			VRInput.Current.Update();
		}
		else
		{
			OnStop();
		}
	}

	internal static void FrameEnd()
	{
	}

	public static void Enable()
	{
		if ( IsActive )
			return;

		State = States.Active;
	}

	public static void Disable()
	{
		if ( !IsActive )
			return;

		State = States.Standby;
	}

	internal static void OnPlay()
	{
		Reset();
		CreateCompositor();
	}

	internal static void OnStop()
	{
		DestroyCompositor();
		Reset();
	}

	internal static void BeginFrame() => FrameStartInternal();
	internal static void EndFrame() => FrameEndInternal();

	internal static bool Submit( IntPtr pColorTexture, IntPtr pDepthTexture )
	{
		var colorTexture = new ITexture( pColorTexture );
		var depthTexture = new ITexture( pDepthTexture );

		return SubmitInternal( colorTexture, depthTexture );
	}

	internal static string GetVulkanInstanceExtensionsRequired()
	{
		return GetRequiredVulkanInstanceExtensions();
	}

	internal static string GetVulkanDeviceExtensionsRequired()
	{
		return GetRequiredVulkanDeviceExtensions();
	}

	public delegate void DebugUtilsMessengerCallback( string message, DebugCallbackType type );
	public delegate void DebugUtilsErrorCallback( string message );

	private static Logger Log = new( "OpenXR" );

	private static void XrErrorCallback( string message )
	{
		Log.Error( $"Unhandled OpenXR exception: {message}" );
		EngineGlobal.Plat_MessageBox( "Unhandled OpenXR exception", $"{message}" );
		Application.Exit();
	}

	private static void XrDebugCallback( string message, DebugCallbackType type )
	{
		switch ( type )
		{
			case DebugCallbackType.Verbose:
				Log.Trace( $"{message}" );
				break;
			case DebugCallbackType.Warning:
				Log.Warning( $"{message}" );
				break;
			case DebugCallbackType.Error:
				Log.Error( $"{message}" );
				break;
			case DebugCallbackType.Info:
			default:
				Log.Info( $"{message}" );
				break;
		}
	}

	private static Instance Instance = new();
	private static EventManager EventManager = new();
	private static Facepunch.XR.Input Input = new();
	private static Compositor Compositor = new();
	private static InstanceProperties InstanceProperties;

	private static void FpxrCheck( XRResult result )
	{
		if ( result < XRResult.Success )
		{
			Log.Warning( $"Facepunch.XR: {result}" );
		}
	}

	private static void CopyStringToBuffer( string input, byte* buffer, uint maxLength )
	{
		byte[] inputBytes = Encoding.ASCII.GetBytes( input );

		for ( int i = 0; i < maxLength; i++ )
		{
			if ( i < inputBytes.Length )
				buffer[i] = inputBytes[i];
			else
				buffer[i] = 0;
		}
	}

	private static string BufferToString( byte* buffer, uint maxLength )
	{
		var str = new byte[maxLength];
		for ( int i = 0; i < maxLength; i++ )
		{
			str[i] = buffer[i];
		}

		return Encoding.ASCII.GetString( str ).TrimEnd( '\0' );
	}

	internal static void CreateInstance()
	{
		if ( !HasHeadset )
			return;

		if ( CommandLine.HasSwitch( "-vrdebug" ) )
		{
			var pDebugCallback = Marshal.GetFunctionPointerForDelegate<DebugUtilsMessengerCallback>( XrDebugCallback );
			ApplicationConfig.SetDebugCallback( pDebugCallback );
		}

		var pErrorCallback = Marshal.GetFunctionPointerForDelegate<DebugUtilsErrorCallback>( XrErrorCallback );
		ApplicationConfig.SetErrorCallback( pErrorCallback );

		var instanceInfo = new InstanceInfo()
		{
			graphicsApi = GraphicsAPI.Vulkan,
			useDebugMessenger = true
		};

		CopyStringToBuffer( "s&box", instanceInfo.appName, Constants.MaxAppNameSize );
		var manifestPath = "core/cfg/fpxr/actions.json";
		CopyStringToBuffer( manifestPath, instanceInfo.actionManifestPath, Constants.MaxPathSize );

		Instance = Instance.Create( instanceInfo );
		InstanceProperties = Instance.GetProperties();
	}

	private static uint ReadUInt32( IntPtr ptr )
	{
		var signedValue = Marshal.ReadInt32( ptr );
		return BitConverter.ToUInt32( BitConverter.GetBytes( signedValue ) );
	}

	internal static unsafe void CreateCompositor()
	{
		if ( Compositor.IsValid )
			return;

		var vulkanInfo = new VulkanInfo()
		{
			vkDevice = g_pRenderDevice.GetDeviceSpecificInfo( DeviceSpecificInfo_t.DSI_VULKAN_DEVICE ),
			vkPhysicalDevice = g_pRenderDevice.GetDeviceSpecificInfo( DeviceSpecificInfo_t.DSI_VULKAN_PHYSICAL_DEVICE ),
			vkInstance = g_pRenderDevice.GetDeviceSpecificInfo( DeviceSpecificInfo_t.DSI_VULKAN_INSTANCE ),
			vkQueueIndex = 0,
			vkQueueFamilyIndex = ReadUInt32( g_pRenderDevice.GetDeviceSpecificInfo( DeviceSpecificInfo_t.DSI_VULKAN_QUEUE_FAMILY_INDEX ) ),
		};

		Compositor = Instance.Compositor( vulkanInfo );
		Input = Instance.Input();
		EventManager = Compositor.EventManager();

		RefreshRate = Compositor.GetDisplayRefreshRate();
		EyeRenderTargetSize = new Vector2( Compositor.GetEyeWidth(), Compositor.GetEyeHeight() );
		FullRenderTargetSize = new Vector2( Compositor.GetRenderTargetWidth(), Compositor.GetRenderTargetHeight() );

		Log.Trace( $"Full render target dims: {FullRenderTargetSize}" );
		Log.Trace( $"Eye render target dims: {EyeRenderTargetSize}" );
		Log.Trace( $"Display refresh rate: {RefreshRate}Hz" );
	}

	internal static unsafe void DestroyCompositor()
	{
		if ( !Compositor.IsValid )
			return;

		Compositor.Shutdown();
		Compositor.self = IntPtr.Zero;
	}

	public static SessionState SessionState { get; private set; } = SessionState.Unknown;

	internal static void Update()
	{
		if ( !Compositor.IsValid )
			return;

		if ( !EventManager.IsValid )
			return;

		if ( IsRendering )
		{
			FpxrCheck( Compositor.GetViewInfo( 0, out LeftEyeInfo ) );
			FpxrCheck( Compositor.GetViewInfo( 1, out RightEyeInfo ) );

			UpdateIPD();
		}

		while ( EventManager.PumpEvent( out var e ) != XRResult.NoEventsPending )
		{
			if ( e.type == EventType.SessionStateChanged )
			{
				var sessionStateChangedEventData = e.GetData<SessionStateChangedEventData>();
				Log.Trace( $"Session state changed to: {sessionStateChangedEventData.state}" );

				SessionState = sessionStateChangedEventData.state;
			}
		}
	}

	internal static void Reset()
	{
		WorldScale = 1.0f;
	}

	internal static string GetSystemName()
	{
		var instanceProperties = Instance.GetProperties();
		return BufferToString( instanceProperties.systemName, Constants.MaxSystemNameSize );
	}
}
