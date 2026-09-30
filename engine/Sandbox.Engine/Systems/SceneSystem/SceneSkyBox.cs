using NativeEngine;
using System;

namespace Sandbox;

/// <summary>
/// Renders a skybox within a <see cref="SceneWorld"/>.
/// </summary>
public class SceneSkyBox : SceneObject
{
	CSceneSkyBoxObject skyboxNative;

	public enum FogType
	{
		None,
		Distance,
		Angular
	}

	public struct FogParamInfo
	{
		public FogType FogType;
		public float FogMinStart;
		public float FogMinEnd;
		public float FogMaxStart;
		public float FogMaxEnd;
	}

	public struct SkyLightInfo
	{
		public Vector3 LightColor;
		public Vector3 LightDirection;
	}

	internal SceneSkyBox() { }
	internal SceneSkyBox( HandleCreationData _ ) { }
	public SceneSkyBox( SceneWorld world, Material skyMaterial )
	{
		Assert.NotNull( skyMaterial );
		Assert.IsValid( world );

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CSceneSystem.CreateSkyBox( skyMaterial.native, world );
		}

		Material = skyMaterial;
	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );

		skyboxNative = (CSceneSkyBoxObject)ptr;
	}

	internal override void OnNativeDestroy()
	{
		skyboxNative = default;
		base.OnNativeDestroy();
	}

	/// <summary>
	/// The skybox material. Typically it should use the "Sky" shader.
	/// </summary>
	public Material SkyMaterial
	{
		set
		{
			skyboxNative.SetMaterial( value?.native ?? default );
			Material = value;
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	/// <summary>
	/// The sky material last set, for the managed scene renderer's mirror - <see cref="SkyMaterial"/> has no getter.
	/// </summary>
	internal Material Material { get; private set; }

	/// <summary>
	/// Skybox color tint.
	/// </summary>
	public Color SkyTint
	{
		get => skyboxNative.GetSkyTint();
		set { skyboxNative.SetSkyTint( value ); NotifyChanged( Rendering.SceneObjectChange.Settings ); }
	}

	/// <summary>
	/// Controls the skybox specific fog.
	/// </summary>
	public FogParamInfo FogParams
	{
		get => new()
		{
			FogType = (FogType)skyboxNative.GetFogType(),
			FogMinStart = skyboxNative.GetFogMinStart(),
			FogMinEnd = skyboxNative.GetFogMinEnd(),
			FogMaxStart = skyboxNative.GetFogMaxStart(),
			FogMaxEnd = skyboxNative.GetFogMaxEnd()
		};

		set
		{
			skyboxNative.SetFogType( (int)value.FogType );
			skyboxNative.SetAngularFogParams( value.FogMinStart, value.FogMinEnd, value.FogMaxStart, value.FogMaxEnd );
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	[Obsolete]
	public void SetSkyLighting( Vector3 ConstantSkyLight )
	{
	}
}
