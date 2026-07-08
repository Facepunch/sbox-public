using System.Reflection;

namespace Sandbox;

public abstract partial class Component
{
	/// <summary>
	/// Called in the editor to draw things like bounding boxes etc
	/// </summary>
	protected virtual void DrawGizmos() { }

	static readonly ReflectionCache<Type, bool> _typeOverridesDrawGizmos = new(
		t => t.GetMethod( nameof( DrawGizmos ), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null )?.DeclaringType != typeof( Component ) );

	bool? _overridesDrawGizmos;

	internal bool OverridesDrawGizmos => _overridesDrawGizmos ??= _typeOverridesDrawGizmos[GetType()];

	internal void DrawGizmosInternal()
	{
		try { DrawGizmos(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'DrawGizmos' on {this}" ); }
	}
}
