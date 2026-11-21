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

	// The settings, once managed code owns them: a sky it made, or one native code made (a tool's: Hammer's sky helper, a tool
	// scene's sky) that it has changed since. Only a sky native code made is a native sky object; the native twin of one
	// managed code made is a plain object, and its settings live here only
	bool managed;
	Material material;
	Vector3 skyTint;
	FogParamInfo fogParams;

	internal SceneSkyBox() { }
	internal SceneSkyBox( HandleCreationData _ ) { }
	public SceneSkyBox( SceneWorld world, Material skyMaterial )
	{
		Assert.NotNull( skyMaterial );
		Assert.IsValid( world );

		managed = true;
		material = skyMaterial;
		skyTint = Vector3.One;

		// Native's defaults (CSceneSkyBoxObject)
		fogParams = new FogParamInfo { FogType = FogType.Distance, FogMinStart = -25, FogMinEnd = -35, FogMaxStart = 25, FogMaxEnd = 35 };

		using ( var h = IHandle.MakeNextHandle( this ) )
		{
			CManagedSceneObject.Create( world );
		}

		ChangeFlags( Rendering.SceneObjectFlags.None, ~InternalFlags );
	}

	internal override void OnNativeInit( CSceneObject ptr )
	{
		base.OnNativeInit( ptr );

		// A sky managed code made has a plain twin (managed is set before it's made)
		skyboxNative = managed ? default : (CSceneSkyBoxObject)ptr;

		lock ( World.Skies )
		{
			World.Skies.Add( this );
		}
	}

	internal override void OnNativeDestroy()
	{
		lock ( World.Skies )
		{
			World.Skies.Remove( this );
		}

		skyboxNative = default;
		base.OnNativeDestroy();
	}

	/// <summary>
	/// Managed code owns the settings from here: take what the native twin has (native's defaults, or what native code set
	/// after making it) once.
	/// </summary>
	void TakeOver()
	{
		if ( managed || skyboxNative.IsNull ) return;
		managed = true;
		material = Sandbox.Material.FromNative( skyboxNative.GetMaterial() );
		skyTint = skyboxNative.GetSkyTint();
		fogParams = NativeFogParams;
	}

	FogParamInfo NativeFogParams => new()
	{
		FogType = (FogType)skyboxNative.GetFogType(),
		FogMinStart = skyboxNative.GetFogMinStart(),
		FogMinEnd = skyboxNative.GetFogMinEnd(),
		FogMaxStart = skyboxNative.GetFogMaxStart(),
		FogMaxEnd = skyboxNative.GetFogMaxEnd()
	};

	/// <summary>
	/// The skybox material. Typically it should use the "Sky" shader.
	/// </summary>
	public Material SkyMaterial
	{
		set
		{
			TakeOver();
			material = value;
			if ( WritesNative && !skyboxNative.IsNull ) skyboxNative.SetMaterial( value?.native ?? default );
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	/// <summary>
	/// The sky material, including that of a sky native code made without a managed constructor.
	/// </summary>
	internal Material Material => !this.IsValid() ? null : managed ? material : Sandbox.Material.FromNative( skyboxNative.GetMaterial() );

	/// <summary>
	/// Skybox color tint.
	/// </summary>
	public Color SkyTint
	{
		get => managed || skyboxNative.IsNull ? skyTint : skyboxNative.GetSkyTint();
		set
		{
			TakeOver();
			skyTint = value;
			if ( WritesNative && !skyboxNative.IsNull ) skyboxNative.SetSkyTint( skyTint );
			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	/// <summary>
	/// Controls the skybox specific fog.
	/// </summary>
	public FogParamInfo FogParams
	{
		get => managed || skyboxNative.IsNull ? fogParams : NativeFogParams;
		set
		{
			TakeOver();
			fogParams = value;
			if ( WritesNative && !skyboxNative.IsNull )
			{
				skyboxNative.SetFogType( (int)value.FogType );
				skyboxNative.SetAngularFogParams( value.FogMinStart, value.FogMinEnd, value.FogMaxStart, value.FogMaxEnd );
			}

			NotifyChanged( Rendering.SceneObjectChange.Settings );
		}
	}

	[Obsolete]
	public void SetSkyLighting( Vector3 ConstantSkyLight )
	{
	}
}
