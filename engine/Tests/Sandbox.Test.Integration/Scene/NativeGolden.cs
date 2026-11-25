using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace SceneTests;

/// <summary>
/// Expected scene object state and light binner records, as native's scene system gave them: its objects' flags, visibility,
/// tint, layer, LOD, mesh groups, tags and draws (<c>RenderTools::GetSceneObjectDraw</c>), and its light and envmap packing
/// (<c>RenderTools::PackSceneLight</c>, <c>PackSceneEnvMap</c>). They were recorded once, from the native objects managed
/// code made for each scene object before those were removed, into <c>Data/NativeGoldens/scene.txt</c>; the tests check what
/// managed code and Borealis's packers give against them.
/// </summary>
internal static class NativeGolden
{
	/// <summary>The value for <paramref name="section"/>: recorded from native once, given by managed code now.</summary>
	public static string Native( SceneObject so, string section, Func<string> managed ) => managed();

	static Dictionary<string, string> expected;

	static Dictionary<string, string> Load()
	{
		var path = Path.Combine( AppContext.BaseDirectory, "Data", "NativeGoldens", "scene.txt" );
		var values = new Dictionary<string, string>();
		foreach ( var line in File.ReadAllLines( path ) )
		{
			var tab = line.IndexOf( '\t' );
			if ( tab > 0 ) values[line[..tab]] = line[(tab + 1)..];
		}
		return values;
	}

	/// <summary>
	/// <paramref name="actual"/> against the value recorded under <paramref name="key"/>: numbers within
	/// <paramref name="tolerance"/> (relative above 1, unless <paramref name="absolute"/>), everything else exactly.
	/// </summary>
	public static void Check( string key, string actual, float tolerance = 1e-5f, bool absolute = false )
	{
		expected ??= Load();
		Assert.IsTrue( expected.TryGetValue( key, out var want ), $"{key}: nothing recorded" );
		var problem = Differ( want, actual, tolerance, absolute );
		Assert.IsNull( problem, $"{key}: {problem}\nexpected {want}\nactual   {actual}" );
	}

	/// <summary>
	/// An object's draws against those recorded under <paramref name="key"/>. A draw without draw data of its own takes its
	/// translucency and shadow fast path from the model's shared mesh, which native worked out again in place whenever any
	/// object drawing it changed material (the native objects made for managed ones did, in the recording): those two are
	/// compared only for draws whose object works them out itself.
	/// </summary>
	public static void CheckDraws( string key, SceneObject so )
	{
		var draws = new List<SceneObjectDraw>();
		so.GetDraws( draws );
		var own = so.OwnDrawData;
		expected ??= Load();
		Assert.IsTrue( expected.TryGetValue( key, out var want ), $"{key}: nothing recorded" );
		var wanted = want.Split( " | " );
		var given = Draws( draws ).Split( " | " );
		Assert.AreEqual( wanted.Length, given.Length, $"{key}: draws" );
		for ( int i = 0; i < given.Length; i++ )
		{
			var a = wanted[i];
			var b = given[i];
			if ( own is null || i >= own.Length || !own[i] )
			{
				a = SharedMeshBits.Replace( a, "" );
				b = SharedMeshBits.Replace( b, "" );
			}
			Assert.AreEqual( a, b, $"{key}: draw {i}" );
		}
	}

	static readonly System.Text.RegularExpressions.Regex SharedMeshBits = new( @" translucent \w+ fastpath \w+" );

	static readonly char[] Separators = [' ', ',', ';', '=', '(', ')', '[', ']', '|'];

	static string Differ( string want, string actual, float tolerance, bool absolute )
	{
		var a = want.Split( Separators, StringSplitOptions.RemoveEmptyEntries );
		var b = actual.Split( Separators, StringSplitOptions.RemoveEmptyEntries );
		if ( a.Length != b.Length ) return $"{a.Length} values expected, {b.Length} given";

		for ( int i = 0; i < a.Length; i++ )
		{
			if ( a[i] == b[i] ) continue;
			var name = Label( a, i );
			if ( !float.TryParse( a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var x ) || !float.TryParse( b[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var y ) )
				return $"{name}: {a[i]} expected, {b[i]} given";
			if ( MathF.Abs( x - y ) > (absolute ? tolerance : tolerance * MathF.Max( 1, MathF.Abs( x ) )) )
				return $"{name}: {a[i]} expected, {b[i]} given";
		}

		return null;
	}

	// The nearest name before a value, for the message
	static string Label( string[] tokens, int index )
	{
		for ( int i = index; i >= 0; i-- )
		{
			if ( tokens[i].Length > 0 && char.IsLetter( tokens[i][0] ) && !float.TryParse( tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out _ ) )
				return $"{tokens[i]} (value {index})";
		}
		return $"value {index}";
	}

	static string Read( SceneObject so, string section, Func<string> managed ) => managed();

	public static string N( float f ) => f.ToString( "R", CultureInfo.InvariantCulture );
	public static string V( Vector3 v ) => $"{N( v.x )},{N( v.y )},{N( v.z )}";
	public static string V( Vector4 v ) => $"{N( v.x )},{N( v.y )},{N( v.z )},{N( v.w )}";
	public static string C( Color c ) => $"{N( c.r )},{N( c.g )},{N( c.b )},{N( c.a )}";
	public static string B( BBox b ) => $"{V( b.Mins )} {V( b.Maxs )}";
	public static string T( Transform t ) => $"{V( t.Position )} {N( t.Rotation.x )},{N( t.Rotation.y )},{N( t.Rotation.z )},{N( t.Rotation.w )} {V( t.Scale )}";

	public static string Draws( IEnumerable<SceneObjectDraw> draws ) => string.Join( " | ", draws.Select( d =>
		$"mesh {d.Mesh} draw {d.DrawCall} {d.Material?.Name?.ToLowerInvariant() ?? "null"} visible {d.Visible} translucent {d.Translucent} fastpath {d.ShadowFastPath} lightmapped {d.Lightmapped}" ) );

	/// <summary>Every public field of a struct: numbers and vectors as numbers, the rest as written.</summary>
	public static string Fields( object value, params string[] skip )
	{
		var text = new StringBuilder();
		foreach ( var field in value.GetType().GetFields( BindingFlags.Instance | BindingFlags.Public ) )
		{
			if ( Array.IndexOf( skip, field.Name ) >= 0 ) continue;
			var v = field.GetValue( value );
			var s = v switch
			{
				float f => N( f ),
				Vector2 v2 => $"{N( v2.x )},{N( v2.y )}",
				Vector3 v3 => V( v3 ),
				Vector4 v4 => V( v4 ),
				Color c => C( c ),
				IFormattable f => f.ToString( null, CultureInfo.InvariantCulture ),
				_ => v?.ToString() ?? "null",
			};
			text.Append( $"{field.Name}={s} " );
		}
		return text.ToString().TrimEnd();
	}

	/// <summary>What every scene object has.</summary>
	public static string Object( SceneObject so )
	{
		var draws = new List<SceneObjectDraw>();
		var text = new StringBuilder();
		text.Append( $"flags={Read( so, "flags", () => ((ulong)so.CurrentFlags).ToString( "X" ) )}" );
		text.Append( $"; visible={Read( so, "visible", () => so.RenderingEnabled.ToString() )}" );
		text.Append( $"; tint={Read( so, "tint", () => C( so.ColorTint ) )}" );
		text.Append( $"; layer={Read( so, "layer", () => so.LayerMatchToken.ToString( CultureInfo.InvariantCulture ) )}" );
		text.Append( $"; lod={Read( so, "lod", () => so.LodOverride.ToString( CultureInfo.InvariantCulture ) )}" );
		text.Append( $"; groups={Read( so, "groups", () => so.MeshGroupMask.ToString( "X" ) )}" );
		text.Append( $"; batchable={Read( so, "batchable", () => so.Batchable.ToString() )}" );
		text.Append( $"; model={so.Model?.Name?.ToLowerInvariant() ?? "null"}" );
		text.Append( $"; bounds={B( so.WorldBounds )}" );
		text.Append( $"; transform={T( so.Transform )}" );
		text.Append( $"; tags={Read( so, "tags", () => string.Join( ",", so.TagTokens ) )}" );
		text.Append( $"; draws={Read( so, "draws", () => { so.GetDraws( draws ); return Draws( draws ); } )}" );
		return text.ToString();
	}

	/// <summary>A light: its object, description, and its record for the light binner (<c>LightPacking</c>).</summary>
	public static string Light( SceneLight light )
	{
		return $"{Object( light )}; description {Fields( light.Description )}; packed {Read( light, "pack", () =>
		{
			Sandbox.Rendering.LightPacking.Pack( light, out var packed, out var flags, out var bakeIndex );
			return $"type flags={flags} bake={bakeIndex} {Fields( packed, "LightCookieTextureIndex" )}";
		} )}";
	}

	/// <summary>A cubemap: its object, settings, description, and its record for the light binner (as its render object packs).</summary>
	public static string Cubemap( SceneCubemap cubemap, bool priority = true )
	{
		var text = $"{Object( cubemap )}; projection={cubemap.Projection} box={B( cubemap.ProjectionBounds )} tint={C( cubemap.TintColor )} feathering={N( cubemap.Feathering )} texture={(cubemap.Texture is null ? "none" : "set")}; description {Fields( cubemap.Description )}";
		if ( !priority ) return text; // native leaves a new cubemap's priority unset
		return $"{text}; priority={cubemap.Priority}";
	}

	/// <summary>A cubemap's record for the light binner, as its render object packs it.</summary>
	public static string EnvMapRecord( SceneCubemap cubemap )
	{
		return Read( cubemap, "envpack", () =>
		{
			var obj = new Borealis.EnvMapObject( null, cubemap.ProjectionBounds, cubemap.Transform )
			{
				Tint = cubemap.TintColor,
				Feathering = cubemap.Feathering,
				Priority = cubemap.Priority,
			};
			return EnvMap( Borealis.Features.LightBinnerFeature.Pack( obj ) );
		} );
	}

	public static string EnvMap( Borealis.Features.LightBinnerFeature.GpuEnvMap e ) =>
		$"row0={V( e.Row0 )} row1={V( e.Row1 )} row2={V( e.Row2 )} mins={V( e.BoxMins )} maxs={V( e.BoxMaxs )} color={V( e.Color )} priority={e.Priority}";

	/// <summary>A light probe volume: its object and placement.</summary>
	public static string Probe( SceneLightProbe probe ) =>
		$"{Object( probe )}; mins={V( probe.BoxMins )} maxs={V( probe.BoxMaxs )} handshake={probe.Handshake} priority={probe.RenderPriority} groups={string.Join( ",", probe.LightGroups )}";
}
