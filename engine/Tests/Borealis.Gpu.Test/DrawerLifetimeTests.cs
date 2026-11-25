using Borealis.Backend;
using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Reflection;

namespace BorealisGpuTests;

/// <summary>
/// Pipelines a drawer makes for itself go with it, and a context's drawers (culls, skinning) go with the context: each new
/// game's views made contexts whose cull pipelines stayed on the device for good, about 18 a session.
/// </summary>
[TestClass]
public class DrawerLifetimeTests : GpuTest
{
	static ShaderFile CullShader( BackendDevice backend )
	{
		var shader = backend.Shaders.Load( "shaders/clutter_cull_cs.shader" );
		if ( shader is null ) Assert.Inconclusive( "clutter_cull_cs isn't compiled" );
		return shader;
	}

	[TestMethod]
	public void ADisposedDrawersPipelinesGo()
	{
		using var backend = new BackendDevice( Device );
		var drawer = new ShaderDrawer( backend );
		var variant = drawer.Variant( CullShader( backend ), new ShaderAttributes() );
		var before = Device.Pipelines.Count;
		var pipeline = drawer.ReflectedComputePipeline( variant );
		Assert.IsTrue( pipeline.IsValid );
		Assert.AreEqual( before + 1, Device.Pipelines.Count );

		drawer.Dispose();
		Assert.AreEqual( before, Device.Pipelines.Count, "the drawer's pipeline is destroyed" );
		Device.WaitIdle();
	}

	[TestMethod]
	public void ADisposedContextsDrawersGo()
	{
		using var backend = new BackendDevice( Device );
		var context = backend.CreateContext();
		var field = context.GetType().GetField( "cullDrawer", BindingFlags.NonPublic | BindingFlags.Instance );
		Assert.IsNotNull( field );
		// Disposed by the context (a second dispose does nothing)
		using var drawer = new ShaderDrawer( backend );
		field.SetValue( context, drawer );
		var before = Device.Pipelines.Count;
		drawer.ReflectedComputePipeline( drawer.Variant( CullShader( backend ), new ShaderAttributes() ) );
		Assert.AreEqual( before + 1, Device.Pipelines.Count );

		context.Dispose();
		Assert.AreEqual( before, Device.Pipelines.Count, "the context's cull drawer and its pipeline are gone" );
		Assert.IsNull( field.GetValue( context ) );
		Device.WaitIdle();
	}
}
