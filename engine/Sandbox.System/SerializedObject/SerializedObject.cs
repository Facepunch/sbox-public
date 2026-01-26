using System.Collections;


namespace Sandbox;

/// <summary>
/// An object (or data) that can be accessed as an object
/// </summary>
public abstract class SerializedObject : IEnumerable<SerializedProperty>, IValid
{
	public SerializedProperty ParentProperty { get; set; }

	public virtual string TypeIcon { get => ""; }
	public virtual string TypeName { get => ""; }
	public virtual string TypeTitle { get => ""; }

	/// <summary>
	/// Does the target object still exist?
	/// </summary>
	public virtual bool IsValid
	{
		// If parent isn't valid, we're not valid.
		// If we have no parent, we're the root and always valid.

		get => ParentProperty is null || ParentProperty.IsValid;
	}

	protected List<SerializedProperty> PropertyList;

	public delegate void PropertyChangedDelegate( SerializedProperty property );

	public delegate void PropertyPreChangeDelegate( SerializedProperty property );

	public delegate void PropertyStartEditDelegate( SerializedProperty property );

	public delegate void PropertyFinishEditDelegate( SerializedProperty property );

	public PropertyPreChangeDelegate OnPropertyPreChange { get; set; }

	public PropertyChangedDelegate OnPropertyChanged { get; set; }

	public PropertyStartEditDelegate OnPropertyStartEdit { get; set; }

	public PropertyFinishEditDelegate OnPropertyFinishEdit { get; set; }

	public virtual SerializedProperty GetProperty( string v )
	{
		return PropertyList?.FirstOrDefault( x => x.Name == v );
	}

	public virtual bool TryGetProperty( string v, out SerializedProperty prop )
	{
		prop = GetProperty( v );
		return prop != null;
	}

	public IEnumerator<SerializedProperty> GetEnumerator()
	{
		PrepareEnumerator();
		return PropertyList.GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		PrepareEnumerator();
		return ((IEnumerable)PropertyList).GetEnumerator();
	}


	/// <summary>
	/// It's good manners for a changed SerializedProperty to tell its parent
	/// on set. That way the parent can cascade changes up the tree. This is 
	/// particularly important if the tree includes struct types - because those
	/// values will need to be re-set on any ParentProperty's.
	/// </summary>
	public virtual void NoteChanged( SerializedProperty childProperty )
	{
		OnPropertyChanged?.Invoke( childProperty );

		if ( ParentProperty is not null )
		{
			ParentProperty.NoteChanged( childProperty );
		}
	}

	internal void NoteChanged()
	{
		OnPropertyChanged?.Invoke( null );

		if ( ParentProperty is not null )
		{
			ParentProperty.NoteChanged( ParentProperty );
		}
	}

	public virtual void NotePreChange( SerializedProperty childProperty )
	{
		OnPropertyPreChange?.Invoke( childProperty );

		if ( ParentProperty is not null )
		{
			ParentProperty.NotePreChange( childProperty );
		}
	}

	internal void NotePreChange()
	{
		OnPropertyPreChange?.Invoke( null );

		if ( ParentProperty is not null )
		{
			ParentProperty.NotePreChange( ParentProperty );
		}
	}

	public virtual void NoteStartEdit( SerializedProperty childProperty )
	{
		OnPropertyStartEdit?.Invoke( childProperty );
		if ( ParentProperty is not null )
		{
			ParentProperty.NoteStartEdit( childProperty );
		}
	}

	internal void NoteStartEdit()
	{
		OnPropertyStartEdit?.Invoke( null );
		if ( ParentProperty is not null )
		{
			ParentProperty.NoteStartEdit( ParentProperty );
		}
	}

	public virtual void NoteFinishEdit( SerializedProperty childProperty )
	{
		OnPropertyFinishEdit?.Invoke( childProperty );
		if ( ParentProperty is not null )
		{
			ParentProperty.NoteFinishEdit( childProperty );
		}
	}

	internal void NoteFinishEdit()
	{
		OnPropertyFinishEdit?.Invoke( null );
		if ( ParentProperty is not null )
		{
			ParentProperty.NoteFinishEdit( ParentProperty );
		}
	}

	/// <summary>
	/// Called right before enumeration, to allow derivitives react to changes
	/// </summary>
	protected virtual void PrepareEnumerator() { }

	/// <summary>
	/// True if the target is multiple objects
	/// </summary>
	public virtual bool IsMultipleTargets => false;

	/// <summary>
	/// A list of actual target objects - if applicable
	/// </summary>
	public virtual IEnumerable<object> Targets => Enumerable.Empty<object>();
}
