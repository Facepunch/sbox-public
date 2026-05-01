namespace Sandbox;

public abstract partial class Component
{
	bool _dirty;

	/// <summary>
	/// Called when a property is set, which will run a callback
	/// </summary>
	[Obsolete]
	protected void OnPropertyDirty<T>( in WrappedPropertySet<T> p )
	{
		p.Setter( p.Value );
		OnPropertyDirty();
	}

	[Obsolete]
	protected void OnPropertyDirty()
	{
		if ( _dirty ) return;
		if ( !IsValid ) return;

		_dirty = true;

		using ( CallbackBatch.Batch() )
		{
			CallbackBatch.Add( CommonCallback.Dirty, this, "OnDirty" );
		}
	}

	void OnDirtyInternal()
	{
		if ( !_dirty ) return;

#pragma warning disable CS0612 // Type or member is obsolete
		try { OnDirty(); }
#pragma warning restore CS0612 // Type or member is obsolete
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'OnDirty' on {this}" ); }

		_dirty = false;
	}

	/// <summary>
	/// Called when the component has become dirty
	/// </summary>
	[Obsolete]
	protected virtual void OnDirty()
	{

	}
}


[Obsolete]
[AttributeUsage( AttributeTargets.Property )]
[CodeGenerator( CodeGeneratorFlags.WrapPropertySet | CodeGeneratorFlags.Instance, "OnPropertyDirty" )]
public class MakeDirtyAttribute : Attribute
{

}
