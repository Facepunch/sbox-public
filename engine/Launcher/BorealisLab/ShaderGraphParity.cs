using Borealis;
using Borealis.Backend;
using Borealis.Gpu;
using Sandbox.ShaderCompiler;
using System.IO;
using System.Text.RegularExpressions;
using GpuDevice = Borealis.Gpu.Device;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-shadergraph-parity cases [-out folder] [-validation] [-tolerance N]</c>: ShaderGraph's output before and after it
/// emitted surfaces, drawn side by side. Each case in the folder is a pair: <c>name.shader</c>, what the graph compiled to
/// as a VFX shader, and <c>name.slang</c>, the surface it compiles to now. The VFX shader is drawn the way a local
/// <c>.shader</c> without vfx is, as a standalone module made by <see cref="PassConverter"/>; the surface through the
/// surface passes. Every texture both name is the rope's colour texture. Writes each picture with a diff and a line per
/// case, and exits 1 when a case doesn't compile or its mean difference is over <c>-tolerance</c> (default 0.5).
/// </summary>
internal static class ShaderGraphParity
{
	public static bool Requested => Args.String( "-shadergraph-parity" ) is not null;

	public static int Run()
	{
		var cases = Path.GetFullPath( Args.String( "-shadergraph-parity" ) );
		var output = Path.GetFullPath( Args.String( "-out" ) ?? Path.Combine( cases, "results" ) );
		var tolerance = double.TryParse( Args.String( "-tolerance" ), System.Globalization.CultureInfo.InvariantCulture, out var t ) ? t : 0.5;
		Directory.CreateDirectory( output );

		// The references as modules, in a content root of their own beside the results
		var content = Path.Combine( output, "content" );
		var modules = Path.Combine( content, "shaders", "shadergraph_reference" );
		Directory.CreateDirectory( modules );

		using var gpu = GpuDevice.Create( new DeviceOptions
		{
			ApplicationName = "Borealis Lab ShaderGraph parity",
			Validation = Args.Has( "-validation" ),
			Presentation = false,
			Message = m => Console.WriteLine( $"Borealis.Gpu: {m.Severity} {m.Text}" ),
		} );

		using var device = new BackendDevice( gpu );
		device.Content.Add( content );
		device.Surfaces.ConvertShaders = false;
		device.Surfaces.AddFolder( cases );
		device.Surfaces.Error = m => Console.WriteLine( $"Shader: {m}" );

		var lines = new List<string>();
		var failed = 0;
		var previous = RenderHost.Current;
		RenderHost.Current = new BackendRenderHost( device );
		try
		{
			var rope = SurfaceParity.Rope( device );
			var color = string.IsNullOrEmpty( rope.Color ) ? null : (BackendTexture)device.LoadTexture( rope.Color );

			foreach ( var file in Directory.GetFiles( cases, "*.slang" ).Order( StringComparer.Ordinal ) )
			{
				var name = Path.GetFileNameWithoutExtension( file );
				var vfx = Path.ChangeExtension( file, ".shader" );
				var surfaceText = File.ReadAllText( file );

				// The surface: every material texture the rope's colour
				var textures = new Dictionary<string, string>();
				foreach ( Match field in Regex.Matches( surfaceText, @"\]\s*Texture2D\s+(\w+)\s*;" ) )
					if ( color is not null ) textures["g_t" + field.Groups[1].Value] = rope.Color;
				var surface = device.Surfaces.CreateMaterial( file, textures, null, name + " (surface)" );
				if ( surface is null )
				{
					failed++;
					lines.Add( $"{name}: the surface didn't compile FAIL" );
					continue;
				}

				var alphaTest = surfaceText.Contains( "[Material, AlphaTest", StringComparison.Ordinal ) || surfaceText.Contains( ", AlphaTest,", StringComparison.Ordinal );
				var after = SurfaceParity.Render( device, surface, alphaTest );
				SurfaceParity.Save( after, Path.Combine( output, name + "-surface.png" ) );

				if ( !File.Exists( vfx ) )
				{
					lines.Add( $"{name}: surface compiled and drew; no .shader to compare with" );
					continue;
				}

				// The reference: the VFX text as a standalone module
				var text = File.ReadAllText( vfx );
				if ( PassConverter.Refuse( text ) is { } why )
				{
					lines.Add( $"{name}: reference not converted: {why}" );
					continue;
				}

				File.WriteAllText( Path.Combine( modules, name + ".slang" ), PassConverter.ConvertStandalone( text, name ) );
				var reference = (BackendMaterial)((Borealis.GpuDevice)device).CreateMaterial( name + " (vfx)", $"shaders/shadergraph_reference/{name}.shader" );
				if ( reference.Shader is null )
				{
					failed++;
					lines.Add( $"{name}: the reference module didn't load FAIL" );
					continue;
				}

				foreach ( Match texture in Regex.Matches( text, @"Texture2D\s+(g_t\w+)\s*<\s*Channel" ) )
					if ( color is not null ) reference.SetTexture( texture.Groups[1].Value, color );

				var before = SurfaceParity.Render( device, reference, alphaTest );
				SurfaceParity.Save( before, Path.Combine( output, name + "-vfx.png" ) );
				var (mean, max, over, diff) = SurfaceParity.Compare( before, after );
				SurfaceParity.Save( diff, Path.Combine( output, name + "-diff.png" ) );
				var ok = mean <= tolerance;
				if ( !ok ) failed++;
				lines.Add( $"{name}: surface against the VFX output: mean difference {mean:F3}, max {max}, {over} pixels over 8{(ok ? "" : " FAIL")}" );
			}
		}
		finally
		{
			gpu.WaitIdle();
			RenderHost.Current = previous;
		}

		lines.Add( $"ShaderGraph parity: {(failed == 0 ? "pass" : $"{failed} failed")} ({output}); Borealis.Gpu validation errors {gpu.ValidationErrors}" );
		foreach ( var line in lines ) Console.WriteLine( line );
		File.WriteAllLines( Path.Combine( output, "results.txt" ), lines );
		return failed == 0 && gpu.ValidationErrors == 0 ? 0 : 1;
	}
}
