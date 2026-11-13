using Sandbox.Internal;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Ordinary hotload updates the component's callback overrides and preserves its state.
	/// </summary>
	[TestMethod]
	public void HotloadAddsAndRemovesComponentCallbacks()
	{
		const string declarations = "[Property] public int Updates { get; set; } [Property] public int Fixed { get; set; } [Property] public int Render { get; set; }";
		const string source = "class ChangingCallbacks : Component { " + declarations + " }";
		LoadSource( "callbacks.csx", source );
		scene = new Scene();
		using var scope = scene.Push();
		var type = Game.TypeLibrary.GetType( "ChangingCallbacks" );
		var component = scene.CreateObject().Components.Create( type );
		reloadRoots = [component];

		Assert.IsFalse( scene.updateComponents.Contains( component ) );
		Assert.IsFalse( scene.fixedUpdateComponents.Contains( component ) );
		Assert.IsFalse( scene.preRenderComponents.Contains( component ) );

		LoadSource( "callbacks.csx", "class ChangingCallbacks : Component { " + declarations + """
			protected override void OnUpdate() { Updates++; }
			protected override void OnFixedUpdate() { Fixed++; }
			protected override void OnPreRender() { Render++; }
			}
			""" );

		Assert.AreNotSame( component, reloadRoots[0] );
		component = (Component)reloadRoots[0];
		type = Game.TypeLibrary.GetType( "ChangingCallbacks" );
		Assert.AreSame( type.TargetType, component.GetType() );
		Assert.IsTrue( scene.updateComponents.Contains( component ) );
		Assert.IsTrue( scene.fixedUpdateComponents.Contains( component ) );
		Assert.IsTrue( scene.preRenderComponents.Contains( component ) );
		component.InternalUpdate();
		component.InternalFixedUpdate();
		component.OnPreRenderInternal();
		Assert.AreEqual( 1, type.GetProperty( "Updates" ).GetValue( component ) );
		Assert.AreEqual( 1, type.GetProperty( "Fixed" ).GetValue( component ) );
		Assert.AreEqual( 1, type.GetProperty( "Render" ).GetValue( component ) );

		LoadSource( "callbacks.csx", source );
		Assert.AreNotSame( component, reloadRoots[0] );
		component = (Component)reloadRoots[0];
		type = Game.TypeLibrary.GetType( "ChangingCallbacks" );
		// Scene hotload retains callback sets for manually registered callbacks. The replaced
		// class loses its markers, and invoking the inherited callbacks must leave state unchanged.
		Assert.IsFalse( component is IUpdateSubscriber );
		Assert.IsFalse( component is IFixedUpdateSubscriber );
		Assert.IsFalse( component is IPreRenderSubscriber );
		component.InternalUpdate();
		component.InternalFixedUpdate();
		component.OnPreRenderInternal();
		Assert.AreEqual( 1, type.GetProperty( "Updates" ).GetValue( component ) );
		Assert.AreEqual( 1, type.GetProperty( "Fixed" ).GetValue( component ) );
		Assert.AreEqual( 1, type.GetProperty( "Render" ).GetValue( component ) );
	}
}
