namespace Sandbox;

/// <summary>
/// An object (or data) that can be accessed as an object
/// </summary>
public class MultiSerializedObject : SerializedObject
{
	List<SerializedObject> children = new List<SerializedObject>();

	private static TResult SelectDistincSingleOrFallback<TSource, TResult>( IEnumerable<TSource> source, Func<TSource, TResult> selector, TResult fallback )
	{
		// Last 2 elements is all we need to check if there is more than 2 we want to return fallback
		var distinct = source.Select( selector ).Distinct().Take( 2 );
		var isSingle = distinct.Count() == 1;
		return isSingle ? distinct.FirstOrDefault() : fallback;
	}

	public override string TypeIcon => SelectDistincSingleOrFallback( children, x => x.TypeIcon, "multiple-icon" );
	public override string TypeName => SelectDistincSingleOrFallback( children, x => x.TypeName, "MultipleTypes" );
	public override string TypeTitle => SelectDistincSingleOrFallback( children, x => x.TypeTitle, "Multiple Objects" );

	public override bool IsValid => children.Any( x => x.IsValid() );

	public MultiSerializedObject()
	{
		PropertyList = new List<SerializedProperty>();
	}

	/// <summary>
	/// Add an object. Don't forget to rebuild after editing!
	/// </summary>
	public void Add( SerializedObject obj )
	{
		obj.OnPropertyChanged += p => OnPropertyChanged?.Invoke( p );
		obj.OnPropertyPreChange += p => OnPropertyPreChange?.Invoke( p );
		obj.OnPropertyStartEdit += p => OnPropertyStartEdit?.Invoke( p );
		obj.OnPropertyFinishEdit += p => OnPropertyFinishEdit?.Invoke( p );
		children.Add( obj );
	}

	/// <summary>
	/// Rebuild the object after modifying. This updates PropertyList.
	/// </summary>
	public void Rebuild()
	{
		PropertyList.Clear();

		SerializedProperty[] childProperties = children.SelectMany( x => x ).ToArray();
		var groupedProperties = childProperties.GroupBy( x => x.Name ).ToArray();

		foreach ( var group in groupedProperties )
		{
			if ( group.Count() == 1 )
			{
				PropertyList.Add( group.First() );
				continue;
			}

			var msp = new MultiSerializedProperty( this, group.Select( x => x ) );
			PropertyList.Add( msp );
		}

	}

	/// <summary>
	/// True if the target is multiple objects
	/// </summary>
	public override bool IsMultipleTargets => children.Count > 1;

	/// <summary>
	/// A list of actual target objects - if applicable
	/// </summary>
	public override IEnumerable<object> Targets => children.Where( x => x.IsValid() ).SelectMany( x => x.Targets );
}


class MultiSerializedProperty : SerializedProperty
{
	readonly List<SerializedProperty> properties;
	readonly MultiSerializedObject parent;

	public override SerializedObject Parent => parent;

	public override string Name => properties.First().Name;
	public override string DisplayName => properties.First().DisplayName;
	public override string Description => properties.First().Description;
	public override string GroupName => properties.First().GroupName;
	public override int Order => properties.First().Order;
	public override bool IsEditable => properties.First().IsEditable;
	public override bool IsPublic => properties.First().IsPublic;
	public override Type PropertyType => properties.First().PropertyType;
	public override string SourceFile => properties.First().SourceFile;
	public override int SourceLine => properties.First().SourceLine;
	public override bool HasChanges => properties.First().HasChanges;
	public override bool IsProperty => properties.First().IsProperty;
	public override bool IsField => properties.First().IsField;
	public override bool IsMethod => properties.First().IsMethod;

	public override IEnumerable<Attribute> GetAttributes()
	{
		// TODO: maybe this should be attributes that all targets share?
		return properties?.FirstOrDefault()?.GetAttributes() ?? base.GetAttributes();
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		var mso = new MultiSerializedObject();
		int count = 0;

		foreach ( var e in properties )
		{
			if ( e.TryGetAsObject( out var o ) )
			{
				mso.Add( o );
				count++;
			}
		}

		mso.Rebuild();
		obj = mso;

		return count > 0;
	}

	public MultiSerializedProperty( MultiSerializedObject parent, IEnumerable<SerializedProperty> enumerable )
	{
		this.parent = parent;
		properties = enumerable.ToList();
	}

	public override T GetValue<T>( T defaultValue = default )
	{
		return properties.First().GetValue<T>( defaultValue );
	}

	public override void SetValue<T>( T value )
	{
		NotePreChange();

		foreach ( var prop in properties )
		{
			prop.SetValue( value );
		}

		NoteChanged();
	}

	protected override void NoteStartEdit()
	{
		foreach ( var prop in properties )
		{
			prop.NoteStartEdit( prop );
		}
	}

	internal override void NoteStartEdit( SerializedProperty childProperty )
	{
		foreach ( var prop in properties )
		{
			prop.NoteStartEdit( prop );
		}
	}

	protected override void NoteFinishEdit()
	{
		foreach ( var prop in properties )
		{
			prop.NoteFinishEdit( prop );
		}
	}

	internal override void NoteFinishEdit( SerializedProperty childProperty )
	{
		foreach ( var prop in properties )
		{
			prop.NoteFinishEdit( prop );
		}
	}

	public override bool IsMultipleValues => properties.Count > 1;

	public override bool IsMultipleDifferentValues => properties.Select( x => x.GetValue<object>() ).Distinct().Count() > 1;

	public override IEnumerable<SerializedProperty> MultipleProperties => properties;
}
