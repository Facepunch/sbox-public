using System.Runtime.InteropServices;

namespace Borealis.Backend;

/// <summary>
/// The 2D sky (<see cref="DrawSky"/>), as native's <c>RenderTools.DrawSkyBox</c> draws it: a unit box with the sky
/// material's mode, its rotation both ways in <c>SkyConstantBuffer_t</c>, <c>SkyTint</c> and the fog combos over the
/// view's attributes. Those go in as object attributes, so the material binds as any draw's does.
/// </summary>
internal sealed partial class BackendContext
{
	[StructLayout( LayoutKind.Sequential )]
	struct SkyConstants
	{
		public Vector4 OsToWs0, OsToWs1, OsToWs2;
		public Vector4 WsToOs0, WsToOs1, WsToOs2;
	}

	static readonly StringToken SkyConstantBuffer = new( "SkyConstantBuffer_t" );
	static readonly StringToken SkyTint = new( "SkyTint" );
	static readonly StringToken FogType = new( "D_FOG_TYPE" );
	static readonly StringToken MinAngularFog = new( "D_MIN_ANGULAR_FOG" );
	static readonly StringToken FogMinStart = new( "FogMinStart" ), FogMinEnd = new( "FogMinEnd" ), FogMaxStart = new( "FogMaxStart" ), FogMaxEnd = new( "FogMaxEnd" );

	BackendParameters skyAttributes;

	public override bool DrawSky( SceneLighting lighting, StringToken shaderMode )
	{
		if ( lighting.SkyMaterial is not BackendMaterial { Shader: { } shader } material || shader.Modes.Count == 0 ) return false;

		// The material's mode, else its first, as native's GetMode() falls back
		var mode = ModeName( shader, shaderMode ) is null ? Token( shader.Modes[0].Name ) : shaderMode;

		var rotation = lighting.SkyRotation;
		Vector3 forward = rotation.Forward, left = rotation.Left, up = rotation.Up;
		var constants = new SkyConstants
		{
			// matrix3x4_t rows: the basis vectors are its columns; the transpose has them as rows
			OsToWs0 = new Vector4( forward.x, left.x, up.x, 0 ),
			OsToWs1 = new Vector4( forward.y, left.y, up.y, 0 ),
			OsToWs2 = new Vector4( forward.z, left.z, up.z, 0 ),
			WsToOs0 = new Vector4( forward.x, forward.y, forward.z, 0 ),
			WsToOs1 = new Vector4( left.x, left.y, left.z, 0 ),
			WsToOs2 = new Vector4( up.x, up.y, up.z, 0 ),
		};

		var sky = skyAttributes ??= new BackendParameters();
		sky.Clear();
		sky.SetData( SkyConstantBuffer, constants );
		var tint = lighting.SkyTint;
		sky.Set( SkyTint, new Vector3( tint.r, tint.g, tint.b ) );

		var fog = lighting.SkyFog;
		sky.SetCombo( FogType, (int)fog.Type );
		if ( fog.Type == SkyFogType.Angular )
		{
			if ( fog.MinStart > -90 && fog.MinEnd > -90 )
			{
				sky.SetCombo( MinAngularFog, 1 );
				sky.Set( FogMinStart, MathF.Sin( fog.MinStart.DegreeToRadian() ) );
				sky.Set( FogMinEnd, MathF.Sin( fog.MinEnd.DegreeToRadian() ) );
			}

			sky.Set( FogMaxStart, MathF.Sin( fog.MaxStart.DegreeToRadian() ) );
			sky.Set( FogMaxEnd, MathF.Sin( fog.MaxEnd.DegreeToRadian() ) );
		}

		var box = device.SkyBox;
		SetObjectAttributes( sky );
		if ( Census ) CensusNote = "2D sky: one draw a view";
		var drawn = DrawModel( material, mode, box, box.DrawsForLod( 0 )[0], 0, 1 );
		SetObjectAttributes( null );
		return drawn;
	}
}
