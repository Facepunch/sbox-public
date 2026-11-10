using System;
using System.Runtime.InteropServices;
using static Sandbox.Mounting.Source1.BspWorldLightType;

namespace Sandbox.Mounting.Source1;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
internal struct PropWorldLight
{
	public Vector3 Origin, Intensity, Normal;
	public int Cluster, Type, Style;
	public float InnerCone, OuterCone, Exponent, Radius, Constant, Linear, Quadratic;
	public int Flags, TexInfo, Owner;
	public readonly float Falloff( Vector3 position )
	{
		float squared = (Origin - position).LengthSquared;
		if ( Type == SkyLight )
			return 1;
		if ( Type == BspWorldLightType.Surface )
			return 1 / Math.Max( 1, squared );
		if ( Type == QuakeLight )
			return Math.Max( 0, Linear - MathF.Sqrt( squared ) );
		return 1 / Math.Max( float.Epsilon, Constant + Linear * MathF.Sqrt( squared ) + Quadratic * squared );
	}

	public readonly float Angular( Vector3 direction )
	{
		float dot = -Vector3.Dot( direction, Normal );
		if ( Type == BspWorldLightType.Surface )
			return dot > 0.01f ? dot : 0;
		if ( Type != Spotlight )
			return 1;
		if ( dot <= OuterCone )
			return 0;
		if ( dot >= InnerCone )
			return 1;
		return MathF.Pow( (dot - OuterCone) / (InnerCone - OuterCone), Exponent );
	}

	public readonly Vector3 Illuminate( Vector3 position, Vector3 normal, float? directional = null )
	{
		if ( Type is QuakeLight or SkyAmbient )
			return Vector3.Zero;
		var direction = Type == SkyLight ? -Normal : (Origin - position).Normal;
		float amount = directional ?? Math.Max( 0, Vector3.Dot( normal, direction ) );
		if ( amount <= 0 )
			return Vector3.Zero;
		float falloff = Type == BspWorldLightType.Surface ? 1 / Math.Max( float.Epsilon, (Origin - position).LengthSquared ) : Falloff( position );
		float angular = Type == BspWorldLightType.Surface ? Vector3.Dot( direction, Normal ) < 0 ? 1 : 0 : Angular( direction );
		return Intensity * (amount * falloff * angular);
	}
}

internal sealed class PropLightingState
{
	internal static readonly Vector3[] Axes = [new( 1, 0, 0 ), new( -1, 0, 0 ), new( 0, 1, 0 ), new( 0, -1, 0 ), new( 0, 0, 1 ), new( 0, 0, -1 )];
	internal static readonly Vector3[] AmbientDirections = CreateAmbientDirections();
	public Vector3[] Ambient { get; } = new Vector3[6];
	public List<PropWorldLight> Lights { get; } = [];
	public float StyleZero { get; init; } = 256.0f * (1.0f / 264.0f);

	public Vector3 Illuminate( Vector3 position, Vector3 normal, float? directional = null )
	{
		var squared = normal * normal;
		var color = Ambient[normal.x < 0 ? 1 : 0] * squared.x + Ambient[normal.y < 0 ? 3 : 2] * squared.y + Ambient[normal.z < 0 ? 5 : 4] * squared.z;
		foreach ( var light in Lights )
			color += light.Illuminate( position, normal, directional );
		return color;
	}

	public Vector3 VertexColor( Vector3 position, Vector3 normal, float? directional = null )
	{
		var color = Illuminate( position, normal, directional ) * StyleZero;
		return new Vector3( Quantize( color.x ), Quantize( color.y ), Quantize( color.z ) );
	}

	internal static float Quantize( float linear )
	{
		float gamma = LightmapCodec.VertexLight( linear );
		byte channel = (byte)(BitConverter.SingleToInt32Bits( gamma * 255 + 8388608f ) & 255);
		return VhvFile.DecodeChannel( channel );
	}

	internal void AddMaterialBindings( SourceMaterialOverrides bindings )
	{
		for ( int side = 0; side < Ambient.Length; side++ )
			bindings.Vectors[$"g_vSourcePropAmbient{side}"] = Ambient[side];
		for ( int index = 0; index < 4; index++ )
		{
			bool valid = index < Lights.Count && Lights[index].Type is not (QuakeLight or SkyAmbient);
			var light = valid ? Lights[index] : default;
			bindings.Values[$"g_vSourcePropLightPosition{index}"] = new Vector4( light.Origin, valid ? light.Type : -1 );
			bindings.Values[$"g_vSourcePropLightColor{index}"] = new Vector4( light.Intensity, light.Exponent );
			bindings.Values[$"g_vSourcePropLightDirection{index}"] = new Vector4( light.Normal, light.InnerCone );
			bindings.Values[$"g_vSourcePropLightAttenuation{index}"] = new Vector4( light.Constant, light.Linear, light.Quadratic, light.OuterCone );
		}
	}

	private static Vector3[] CreateAmbientDirections()
	{
		return [new( -0.525731f, 0, 0.850651f ), new( -0.442863f, 0.238856f, 0.864188f ), new( -0.295242f, 0, 0.955423f ), new( -0.309017f, 0.5f, 0.809017f ), new( -0.162460f, 0.262866f, 0.951056f ), new( 0, 0, 1 ), new( 0, 0.850651f, 0.525731f ), new( -0.147621f, 0.716567f, 0.681718f ), new( 0.147621f, 0.716567f, 0.681718f ), new( 0, 0.525731f, 0.850651f ), new( 0.309017f, 0.5f, 0.809017f ), new( 0.525731f, 0, 0.850651f ), new( 0.295242f, 0, 0.955423f ), new( 0.442863f, 0.238856f, 0.864188f ), new( 0.162460f, 0.262866f, 0.951056f ), new( -0.681718f, 0.147621f, 0.716567f ), new( -0.809017f, 0.309017f, 0.5f ), new( -0.587785f, 0.425325f, 0.688191f ), new( -0.850651f, 0.525731f, 0 ), new( -0.864188f, 0.442863f, 0.238856f ), new( -0.716567f, 0.681718f, 0.147621f ), new( -0.688191f, 0.587785f, 0.425325f ), new( -0.5f, 0.809017f, 0.309017f ), new( -0.238856f, 0.864188f, 0.442863f ), new( -0.425325f, 0.688191f, 0.587785f ), new( -0.716567f, 0.681718f, -0.147621f ), new( -0.5f, 0.809017f, -0.309017f ), new( -0.525731f, 0.850651f, 0 ), new( 0, 0.850651f, -0.525731f ), new( -0.238856f, 0.864188f, -0.442863f ), new( 0, 0.955423f, -0.295242f ), new( -0.262866f, 0.951056f, -0.162460f ), new( 0, 1, 0 ), new( 0, 0.955423f, 0.295242f ), new( -0.262866f, 0.951056f, 0.162460f ), new( 0.238856f, 0.864188f, 0.442863f ), new( 0.262866f, 0.951056f, 0.162460f ), new( 0.5f, 0.809017f, 0.309017f ), new( 0.238856f, 0.864188f, -0.442863f ), new( 0.262866f, 0.951056f, -0.162460f ), new( 0.5f, 0.809017f, -0.309017f ), new( 0.850651f, 0.525731f, 0 ), new( 0.716567f, 0.681718f, 0.147621f ), new( 0.716567f, 0.681718f, -0.147621f ), new( 0.525731f, 0.850651f, 0 ), new( 0.425325f, 0.688191f, 0.587785f ), new( 0.864188f, 0.442863f, 0.238856f ), new( 0.688191f, 0.587785f, 0.425325f ), new( 0.809017f, 0.309017f, 0.5f ), new( 0.681718f, 0.147621f, 0.716567f ), new( 0.587785f, 0.425325f, 0.688191f ), new( 0.955423f, 0.295242f, 0 ), new( 1, 0, 0 ), new( 0.951056f, 0.162460f, 0.262866f ), new( 0.850651f, -0.525731f, 0 ), new( 0.955423f, -0.295242f, 0 ), new( 0.864188f, -0.442863f, 0.238856f ), new( 0.951056f, -0.162460f, 0.262866f ), new( 0.809017f, -0.309017f, 0.5f ), new( 0.681718f, -0.147621f, 0.716567f ), new( 0.850651f, 0, 0.525731f ), new( 0.864188f, 0.442863f, -0.238856f ), new( 0.809017f, 0.309017f, -0.5f ), new( 0.951056f, 0.162460f, -0.262866f ), new( 0.525731f, 0, -0.850651f ), new( 0.681718f, 0.147621f, -0.716567f ), new( 0.681718f, -0.147621f, -0.716567f ), new( 0.850651f, 0, -0.525731f ), new( 0.809017f, -0.309017f, -0.5f ), new( 0.864188f, -0.442863f, -0.238856f ), new( 0.951056f, -0.162460f, -0.262866f ), new( 0.147621f, 0.716567f, -0.681718f ), new( 0.309017f, 0.5f, -0.809017f ), new( 0.425325f, 0.688191f, -0.587785f ), new( 0.442863f, 0.238856f, -0.864188f ), new( 0.587785f, 0.425325f, -0.688191f ), new( 0.688191f, 0.587785f, -0.425325f ), new( -0.147621f, 0.716567f, -0.681718f ), new( -0.309017f, 0.5f, -0.809017f ), new( 0, 0.525731f, -0.850651f ), new( -0.525731f, 0, -0.850651f ), new( -0.442863f, 0.238856f, -0.864188f ), new( -0.295242f, 0, -0.955423f ), new( -0.162460f, 0.262866f, -0.951056f ), new( 0, 0, -1 ), new( 0.295242f, 0, -0.955423f ), new( 0.162460f, 0.262866f, -0.951056f ), new( -0.442863f, -0.238856f, -0.864188f ), new( -0.309017f, -0.5f, -0.809017f ), new( -0.162460f, -0.262866f, -0.951056f ), new( 0, -0.850651f, -0.525731f ), new( -0.147621f, -0.716567f, -0.681718f ), new( 0.147621f, -0.716567f, -0.681718f ), new( 0, -0.525731f, -0.850651f ), new( 0.309017f, -0.5f, -0.809017f ), new( 0.442863f, -0.238856f, -0.864188f ), new( 0.162460f, -0.262866f, -0.951056f ), new( 0.238856f, -0.864188f, -0.442863f ), new( 0.5f, -0.809017f, -0.309017f ), new( 0.425325f, -0.688191f, -0.587785f ), new( 0.716567f, -0.681718f, -0.147621f ), new( 0.688191f, -0.587785f, -0.425325f ), new( 0.587785f, -0.425325f, -0.688191f ), new( 0, -0.955423f, -0.295242f ), new( 0, -1, 0 ), new( 0.262866f, -0.951056f, -0.162460f ), new( 0, -0.850651f, 0.525731f ), new( 0, -0.955423f, 0.295242f ), new( 0.238856f, -0.864188f, 0.442863f ), new( 0.262866f, -0.951056f, 0.162460f ), new( 0.5f, -0.809017f, 0.309017f ), new( 0.716567f, -0.681718f, 0.147621f ), new( 0.525731f, -0.850651f, 0 ), new( -0.238856f, -0.864188f, -0.442863f ), new( -0.5f, -0.809017f, -0.309017f ), new( -0.262866f, -0.951056f, -0.162460f ), new( -0.850651f, -0.525731f, 0 ), new( -0.716567f, -0.681718f, -0.147621f ), new( -0.716567f, -0.681718f, 0.147621f ), new( -0.525731f, -0.850651f, 0 ), new( -0.5f, -0.809017f, 0.309017f ), new( -0.238856f, -0.864188f, 0.442863f ), new( -0.262866f, -0.951056f, 0.162460f ), new( -0.864188f, -0.442863f, 0.238856f ), new( -0.809017f, -0.309017f, 0.5f ), new( -0.688191f, -0.587785f, 0.425325f ), new( -0.681718f, -0.147621f, 0.716567f ), new( -0.442863f, -0.238856f, 0.864188f ), new( -0.587785f, -0.425325f, 0.688191f ), new( -0.309017f, -0.5f, 0.809017f ), new( -0.147621f, -0.716567f, 0.681718f ), new( -0.425325f, -0.688191f, 0.587785f ), new( -0.162460f, -0.262866f, 0.951056f ), new( 0.442863f, -0.238856f, 0.864188f ), new( 0.162460f, -0.262866f, 0.951056f ), new( 0.309017f, -0.5f, 0.809017f ), new( 0.147621f, -0.716567f, 0.681718f ), new( 0, -0.525731f, 0.850651f ), new( 0.425325f, -0.688191f, 0.587785f ), new( 0.587785f, -0.425325f, 0.688191f ), new( 0.688191f, -0.587785f, 0.425325f ), new( -0.955423f, 0.295242f, 0 ), new( -0.951056f, 0.162460f, 0.262866f ), new( -1, 0, 0 ), new( -0.850651f, 0, 0.525731f ), new( -0.955423f, -0.295242f, 0 ), new( -0.951056f, -0.162460f, 0.262866f ), new( -0.864188f, 0.442863f, -0.238856f ), new( -0.951056f, 0.162460f, -0.262866f ), new( -0.809017f, 0.309017f, -0.5f ), new( -0.864188f, -0.442863f, -0.238856f ), new( -0.951056f, -0.162460f, -0.262866f ), new( -0.809017f, -0.309017f, -0.5f ), new( -0.681718f, 0.147621f, -0.716567f ), new( -0.681718f, -0.147621f, -0.716567f ), new( -0.850651f, 0, -0.525731f ), new( -0.688191f, 0.587785f, -0.425325f ), new( -0.587785f, 0.425325f, -0.688191f ), new( -0.425325f, 0.688191f, -0.587785f ), new( -0.425325f, -0.688191f, -0.587785f ), new( -0.587785f, -0.425325f, -0.688191f ), new( -0.688191f, -0.587785f, -0.425325f )];
	}
}
