using System.Numerics;
using System.Runtime.InteropServices;
using Facepunch.XR;
using NativeEngine;

namespace Sandbox.VR;

internal static unsafe partial class VRSystem
{
	internal struct VRClipPlanes
	{
		public float ZNear;
		public float ZFar;

		public VRClipPlanes()
		{
			ZNear = 1.0f;
			ZFar = 1000.0f;
		}
	}

	internal static VRClipPlanes ClipPlanes;
	internal static TrackedDevicePose LeftEyeRenderPose;
	internal static TrackedDevicePose RightEyeRenderPose;
	internal static ViewInfo LeftEyeInfo = ViewInfo.Zero;
	internal static ViewInfo RightEyeInfo = ViewInfo.Zero;

	[ConVar( "vr_enable_depth_submit", ConVarFlags.Protected, Help = "Enable submitting depth texture to compositor" )]
	public static bool EnableDepthSubmit { get; set; } = false;

	private static TextureSubmitInfo GetTextureSubmitInfoVulkan( ITexture hColorTexture, ITexture hDepthTexture )
	{
		var pVkColorTexture = g_pRenderDevice.GetDeviceSpecificTexture( hColorTexture );
		var vkColorTexture = Marshal.PtrToStructure<VulkanDeviceSpecificTexture_t>( pVkColorTexture );
		var sampleCount = (uint)Graphics.RenderMultiSampleToNum( g_pRenderDevice.GetTextureMultisampleType( hColorTexture ) );

		var vulkanTextureData = new TextureSubmitInfo()
		{
			image = vkColorTexture.m_pImage,
			format = vkColorTexture.m_nFormat,
			depthImage = 0,
			depthFormat = 0,
			sampleCount = sampleCount,
			poseLeft = LeftEyeRenderPose,
			poseRight = RightEyeRenderPose
		};

		return vulkanTextureData;
	}

	private static bool IsSessionReady()
	{
		var sessionState = EventManager.GetSessionState();

		return sessionState >= SessionState.Ready && sessionState <= SessionState.Focused;
	}

	internal static bool FrameSubmitted = false;

	/// <summary>
	/// Tell the compositor that we're starting a frame, and reset frame state
	/// </summary>
	internal static void FrameStartInternal()
	{
		if ( Compositor == IntPtr.Zero )
			return;

		if ( !IsSessionReady() )
			return;

		FpxrCheck( Compositor.BeginFrame() );
		FrameSubmitted = false;
	}

	/// <summary>
	/// Tell the compositor that we're ending a frame
	/// </summary>
	internal static void FrameEndInternal()
	{
		if ( Compositor == IntPtr.Zero )
			return;

		if ( !IsSessionReady() )
			return;

		if ( FrameSubmitted )
		{
			FpxrCheck( Compositor.EndFrame() );
		}
	}

	internal static bool SubmitInternal( ITexture colorTexture, ITexture depthTexture )
	{
		if ( !IsSessionReady() )
			return false;

		var textureSubmitInfo = GetTextureSubmitInfoVulkan( colorTexture, depthTexture );
		FpxrCheck( Compositor.Submit( textureSubmitInfo ) );

		FrameSubmitted = true;
		return true;
	}

	internal static Matrix CreateProjection( float tanL, float tanR, float tanU, float tanD, float near, float far )
	{
		var result = new Matrix4x4(
			2f / (tanR - tanL), 0f, 0f, 0f,
			0f, 2f / (tanU - tanD), 0f, 0f,
			(tanR + tanL) / (tanR - tanL), (tanU + tanD) / (tanU - tanD), -far / (far - near), -far / (far - near),
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

		var positionOffset = (eye == VREye.Left ? cameraRotation.Left : cameraRotation.Right) * IPD;
		transform.Position = cameraPosition + positionOffset;
		transform.Rotation = cameraRotation;
		transform.Scale = 1.0f;

		if ( eye == VREye.Left )
			LeftEyeRenderPose = LeftEyeInfo.pose;
		else
			RightEyeRenderPose = RightEyeInfo.pose;

		return transform;
	}

	internal static Transform GetHeadTransform()
	{
		var headPos = (LeftEyeInfo.pose.GetTransform().Position + RightEyeInfo.pose.GetTransform().Position) / 2.0f;
		var headRot = Rotation.Slerp( LeftEyeInfo.pose.GetTransform().Rotation, RightEyeInfo.pose.GetTransform().Rotation, 0.5f );

		return new Transform( headPos, headRot );
	}

	internal static void UpdateIPD()
	{
		IPD = (LeftEyeInfo.pose.GetTransform().Position - RightEyeInfo.pose.GetTransform().Position).Length / WorldScale;
	}

	internal static string GetRequiredVulkanInstanceExtensions()
	{
		if ( !IsActive )
			Init();

		if ( !HasHeadset )
			return "";

		return Instance.GetRequiredInstanceExtensions();
	}

	internal static string GetRequiredVulkanDeviceExtensions()
	{
		if ( !IsActive )
			Init();

		if ( !HasHeadset )
			return "";

		return Instance.GetRequiredDeviceExtensions();
	}
}
