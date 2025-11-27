using Facepunch.XR;
using NativeEngine;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Sandbox.VR;

/// <summary>
/// Native helpers for VR
/// </summary>
internal static unsafe partial class VRNative
{
	/// <summary>
	/// Private store for distance between user's pupils, in inches.
	/// Accessed and converted through <see cref="IPDMillimetres"/> and <see cref="IPDInches"/>
	/// </summary>
	private static float IPD { get; set; } = 0f;

	/// <summary>
	/// Distance between user's pupils, in millimetres
	/// </summary>
	public static float IPDMillimetres => IPD.InchToMillimeter();

	/// <summary>
	/// Distance between user's pupils, in inches
	/// </summary>
	public static float IPDInches => IPD;

	/// <summary>
	/// Headset refresh rate, in Hz
	/// </summary>
	public static float RefreshRate { get; private set; }

	//
	// Data
	//
	public static List<TrackedDevice> TrackedDevices { get; private set; }

	//
	// Common properties
	//

	/// <summary>
	/// Is the SteamVR dashboard currently visible?
	/// </summary>
	public static bool IsDashboardVisible => SessionState != SessionState.Focused;

	/// <summary>
	/// Has the user selected that they're left hand dominant in SteamVR?
	/// </summary>
	public static bool IsLeftHandDominant => false;

	/// <summary>
	/// Spans both eyes - equivalent to (eye width * 2, eye height)
	/// </summary>
	internal static Vector2 FullRenderTargetSize { get; private set; }

	/// <summary>
	/// Spans one eye
	/// </summary>
	internal static Vector2 EyeRenderTargetSize { get; private set; }

	/// <summary>
	/// Scales the relative position between the two eyes
	/// </summary>
	public static float WorldScale { get; set; } = 1.0f;

	//
	// Performance timings
	//
	internal static uint _totalFrames;
	internal static uint _totalDroppedFrames;
	internal static uint _totalReprojectedFrames;

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

	public delegate void DebugUtilsMessengerCallback( string message, DebugCallbackType type );
	public delegate void DebugUtilsErrorCallback( string message );

	private static Logger Log = new( "OpenXR" );

	private static void XrErrorCallback( string message )
	{
		Log.Error( $"{message}" );
		Application.Exit(); // For now
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

	private static void FpxrCheck( XRResult result )
	{
		if ( result < XRResult.Success )
		{
			Log.Warning( $"Facepunch.XR: {result}" );
		}
	}

	private static InstanceProperties InstanceProperties;

	internal static void CreateInstance()
	{
		if ( !VRSystem.HasHeadset )
			return;

		// Initialize app config callbacks
		{
			if ( VRSystem.WantsDebug )
			{
				// Set up a debug callback for logging Facepunch.XR messages
				var pDebugCallback = Marshal.GetFunctionPointerForDelegate<DebugUtilsMessengerCallback>( XrDebugCallback );
				ApplicationConfig.SetDebugCallback( pDebugCallback );
			}

			var pErrorCallback = Marshal.GetFunctionPointerForDelegate<DebugUtilsErrorCallback>( XrErrorCallback );
			ApplicationConfig.SetErrorCallback( pErrorCallback );
		}

		// Create the OpenXR instance
		var instanceInfo = new InstanceInfo()
		{
			graphicsApi = GraphicsAPI.Vulkan,
			useDebugMessenger = true
		};

		CopyStringToBuffer( "s&box", instanceInfo.appName, Constants.MaxAppNameSize );
		var manifestPath = "core/cfg/fpxr/actions.json"; // this isn't great but EngineFileSystem.CoreContent isn't initialised when we call this
		CopyStringToBuffer( manifestPath, instanceInfo.actionManifestPath, Constants.MaxPathSize );

		Instance = Instance.Create( instanceInfo );
		InstanceProperties = Instance.GetProperties();
	}

	internal static unsafe void CreateCompositor()
	{
		// Give FPXR all the info it needs about our vulkan device, instance, etc
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

		// Save off display info so we can use it for rendering
		RefreshRate = Compositor.GetDisplayRefreshRate();
		EyeRenderTargetSize = new Vector2( Compositor.GetEyeWidth(), Compositor.GetEyeHeight() );
		FullRenderTargetSize = new Vector2( Compositor.GetRenderTargetWidth(), Compositor.GetRenderTargetHeight() );

		Log.Trace( $"Full render target dims: {FullRenderTargetSize}" );
		Log.Trace( $"Eye render target dims: {EyeRenderTargetSize}" );
		Log.Trace( $"Display refresh rate: {RefreshRate}Hz" );
	}

	public static SessionState SessionState { get; private set; } = SessionState.Unknown;

	internal static ViewInfo LeftEyeInfo = ViewInfo.Zero;
	internal static ViewInfo RightEyeInfo = ViewInfo.Zero;

	internal static void Update()
	{
		if ( VRSystem.IsRendering )
		{
			FpxrCheck( Compositor.GetViewInfo( 0, out LeftEyeInfo ) );
			FpxrCheck( Compositor.GetViewInfo( 1, out RightEyeInfo ) );

			UpdateIPD();
		}

		//
		// Poll the event loop
		//
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

	internal static void TriggerHapticVibration( float duration, float frequency, float amplitude, InputSource source )
	{
		FpxrCheck( Input.TriggerHapticVibration( duration, frequency, amplitude, source ) );
	}

	internal static void Reset()
	{
		WorldScale = 1.0f;
	}

	internal static bool IsHMDInStandby()
	{
		return SessionState != SessionState.Focused;
	}

	internal static Matrix CreateProjection( float tanL, float tanR, float tanU, float tanD, float near, float far )
	{
		var result = new Matrix4x4(
			2f / (tanR - tanL), 0f, 0f, 0f,
			0f, 2f / (tanU - tanD), 0f, 0f,
			(tanR + tanL) / (tanR - tanL), (tanU + tanD) / (tanU - tanD), -far / (far - near), -(far * near) / (far - near),
			0f, 0f, -1f, 0f
		);
		return result;
	}

	internal static Matrix GetProjectionMatrix( float znear, float zfar, VREye eye )
	{
		var viewInfo = eye == VREye.Left ? LeftEyeInfo : RightEyeInfo;

		float left = MathF.Tan( viewInfo.fovLeft );
		float right = MathF.Tan( viewInfo.fovRight );
		float up = MathF.Tan( viewInfo.fovUp );
		float down = MathF.Tan( viewInfo.fovDown );

		return CreateProjection( left, right, up, down, znear, zfar ).Transpose();
	}

	internal static Vector4 GetClipForEye( VREye eye )
	{
		var eyeInfo = eye == VREye.Left ? LeftEyeInfo : RightEyeInfo;

		return new Vector4(
			MathF.Tan( eyeInfo.fovLeft ),
			MathF.Tan( eyeInfo.fovDown ),
			MathF.Tan( eyeInfo.fovRight ),
			MathF.Tan( eyeInfo.fovUp )
		);
	}

	internal static Transform GetTransformForEye( Vector3 cameraPosition, Rotation cameraRotation, VREye eye )
	{
		var transform = new Transform();

		//
		// Calculate transform based on user IPD
		//
		var positionOffset = (eye == VREye.Left ? cameraRotation.Left : cameraRotation.Right) * IPD;
		transform.Position = cameraPosition + positionOffset;
		transform.Rotation = cameraRotation;
		transform.Scale = 1.0f;

		//
		// Save off poses for later submit
		//

		if ( eye == VREye.Left )
			LeftEyeRenderPose = LeftEyeInfo.pose;
		else
			RightEyeRenderPose = RightEyeInfo.pose;

		return transform;
	}

	internal static Transform GetHeadTransform()
	{
		// Take the center of the two eye poses
		var headPos = (LeftEyeInfo.pose.GetTransform().Position + RightEyeInfo.pose.GetTransform().Position) / 2.0f;
		var headRot = Rotation.Slerp( LeftEyeInfo.pose.GetTransform().Rotation, RightEyeInfo.pose.GetTransform().Rotation, 0.5f );

		return new Transform( headPos, headRot );
	}

	internal static void UpdateIPD()
	{
		// IPD is distance between two views.
		// We can calculate it by taking the distance between the two eye poses.
		IPD = (LeftEyeInfo.pose.GetTransform().Position - RightEyeInfo.pose.GetTransform().Position).Length / WorldScale;
	}

	internal static string GetSystemName()
	{
		var instanceProperties = Instance.GetProperties();
		return BufferToString( instanceProperties.systemName, Constants.MaxSystemNameSize );
	}

	internal static InputPoseHandState GetHandPoseState( InputSource source, MotionRange motionRange )
	{
		FpxrCheck( Input.GetHandPoseState( source, motionRange, out var state ) );
		return state;
	}

	internal static float GetFingerCurl( InputSource source, FingerValue finger )
	{
		return Input.GetFingerCurl( source, finger );
	}

	internal static bool HandTrackingSupported()
	{
		return InstanceProperties.supportsHandTracking;
	}

	internal static Transform GetOffsetForDeviceRole( TrackedDeviceRole role )
	{
		//
		// alex: Offset controllers to match original SteamVR pose
		// We do this because /input/grip/pose is the palm, but we want the controller
		// to match the SteamVR pose (which is the controller itself)
		//
		// This should be uniform across all controllers because the palm is the same
		// for all of them.
		//
		return role switch
		{
			TrackedDeviceRole.LeftHand => new Transform( new Vector3( 5f, -2f, -3f ), Rotation.From( 10, -10, 90 ) ),
			TrackedDeviceRole.RightHand => new Transform( new Vector3( 5f, 2f, -3f ), Rotation.From( 10, 10, -90 ) ),
			_ => Transform.Zero
		};
	}
}
