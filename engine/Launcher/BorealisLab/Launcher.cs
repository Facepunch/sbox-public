global using Sandbox;
global using System;
global using System.Collections.Generic;
global using System.Linq;
global using static Sandbox.Internal.GlobalSystemNamespace;

namespace Sandbox;

/// <summary>
/// Called by the shared launcher startup once the native dll paths are set up.
/// </summary>
public static class Launcher
{
	public static int Main()
	{
		// Before the engine starts, so validation messages from creating the device count too
		if ( Lab.GoldenTruthRun.Enabled ) Lab.GoldenTruthRun.WatchValidation();

		// -shader-route: the core shaders' materials as their .shader, not the surfaces standing in for them (for A/B)
		if ( Lab.Args.Has( "-shader-route" ) ) Borealis.Backend.SurfaceShaders.ConvertByDefault = false;

		// -no-surface a,b: these core shaders' materials as their .shader (or pass module), the other surfaces as usual (for A/B of one surface)
		if ( Lab.Args.String( "-no-surface" ) is { } shaders )
			foreach ( var shader in shaders.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) ) Borealis.Backend.SurfaceShaders.NotConvertedByDefault.Add( shader );

		// -no-specialize: surface pipelines not specialised for their materials' features (for A/B)
		if ( Lab.Args.Has( "-no-specialize" ) ) Borealis.Backend.SurfaceShaders.SpecializeByDefault = false;

		// Sun cascades, for A/B runs in any mode: -far-cache, -no-far-cache, -no-cascade-tiles, -no-texel-cascades
		if ( Lab.Args.Has( "-far-cache" ) ) Borealis.Shadows.ShadowMapper.FarCascadeCache = true;
		if ( Lab.Args.Has( "-no-far-cache" ) ) Borealis.Shadows.ShadowMapper.FarCascadeCache = false;
		if ( Lab.Args.Has( "-no-cascade-tiles" ) ) Borealis.Shadows.ShadowMapper.CascadeTiles = false;
		if ( Lab.Args.Has( "-no-texel-cascades" ) ) Borealis.Shadows.ShadowMapper.TexelSizedCascades = false;

		return BorealisLab.BorealisLabApp.Run();
	}
}
