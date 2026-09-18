using Editor.NodeEditor;
using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace Editor.ActionGraphs;

public class Properties : Widget
{

	private static HashSet<Type> DefaultNumericTypes { get; } = new()
	{
		typeof(int),
		typeof(float),
		typeof(Vector2),
		typeof(Vector3),
		typeof(Vector4)
	};

	private static HashSet<Type> DefaultTypes { get; } = new()
	{
		typeof(int),
		typeof(float),
		typeof(Vector2),
		typeof(Vector3),
		typeof(Vector4),
		typeof(Color),
		typeof(string),
		typeof(bool),
		typeof(object)
	};

	public static Type DefaultType { get; } = Either.CreateType( DefaultTypes );

	public static Type DefaultNumericType { get; } = Either.CreateType( DefaultNumericTypes );

	public static Type PredictBestType( Node.IParameter target )
	{
		if ( target is not Node.Input )
		{
			return DefaultType;
		}

		if ( !target.Node.Definition.IsOperator() )
		{
			return DefaultType;
		}

		var otherInput = target.Name switch
		{
			"a" => target.Node.Inputs.TryGetValue( "b", out var b ) ? b : null,
			"b" => target.Node.Inputs.TryGetValue( "a", out var a ) ? a : null,
			_ => null
		};

		switch ( target.Node.Definition.Identifier )
		{
			case "op.conditional":
			case "op.coalesce":
			case "op.equal":
			case "op.notequal":
				return otherInput?.SourceType ?? DefaultType;

			default:
				return otherInput?.SourceType is { } otherType ? typeof( Either<,> ).MakeGenericType( otherType, DefaultNumericType ) : DefaultNumericType;
		}
	}

	private record PropertyRow( SerializedProperty Property, ControlWidget Widget, Node.IParameter Parameter = null, int? Index = null );

	private SerializedProperty _lastActive;
	private object _target;
	private readonly List<PropertyRow> _rows = new();
	Layout Content;

	private ControlWidget _startEditingTarget;

	public MainWindow MainWindow { get; }

	public object Target
	{
		get => _target;
		set
		{
			if ( _target == value )
			{
				return;
			}

			_target = value;
			_contentInvalid = true;
		}
	}

	private bool _contentInvalid;

	public Properties( MainWindow mainWindow ) : base( null )
	{
		MainWindow = mainWindow;

		Name = "Inspector";
		WindowTitle = "Inspector";
		SetWindowIcon( "manage_search" );

		Layout = Layout.Column();
		Content = Layout.AddColumn();

		SetSizeMode( SizeMode.Default, SizeMode.CanShrink );

		MainWindow.SelectionChanged += OnSelectionChanged;
		MainWindow.FocusedOnInput += FocusOnInput;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		MainWindow.SelectionChanged -= OnSelectionChanged;
		MainWindow.FocusedOnInput -= FocusOnInput;
	}

	private void OnSelectionChanged( object target )
	{
		Target = target;
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		var target = Target;

		if ( target is NodeUI nodeUi )
		{
			target = nodeUi.Node;
		}

		if ( target is EditorNode node )
		{
			if ( !node.Node.IsValid )
			{
				Target = null;
			}
			else
			{
				node.MarkDirty();
			}
		}

		if ( _contentInvalid )
		{
			_contentInvalid = false;
			RebuildContent();
		}

		if ( _startEditingTarget.IsValid() )
		{
			_startEditingTarget.StartEditing();
			_startEditingTarget = null;
		}

		SerializedProperty active = null;

		foreach ( var row in _rows )
		{
			if ( row.Widget.IsControlActive )
			{
				active = row.Property;
				break;
			}
		}

		if ( _lastActive != active )
		{
			_lastActive = active;

			if ( active != null )
			{
				MainWindow.FocusedView?.PushUndo( $"Set Property ({active?.Name})" );
			}
		}
	}

	protected override void OnFocus( FocusChangeReason reason )
	{
		MainWindow.FocusedView?.PushUndo( "Edit Properties" );
	}

	public void FocusOnInput( Node.Input input, int? index )
	{
		if ( _contentInvalid )
		{
			_contentInvalid = false;
			RebuildContent();
		}

		var match = _rows.FirstOrDefault( x => x.Parameter == input && x.Index == index )
			?? _rows.FirstOrDefault( x => x.Parameter == input && x.Index == null );

		_startEditingTarget = match?.Widget;
	}

	void RebuildContent()
	{
		_lastActive = null;

		Content.Clear( true );
		_rows.Clear();

		if ( _target == null )
		{
			return;
		}

		var ps = new ControlSheet();
		var target = Target;

		Content.Add( ps );
		Content.AddStretchCell();

		if ( target is NodeUI nodeUi )
		{
			target = nodeUi.Node;
		}
		else
		{
			nodeUi = null;
		}

		if ( target is CommentEditorNode commentNode )
		{
			var obj = AddObject( ps, EditorTypeLibrary.GetSerializedObject( commentNode ) );
			obj.OnPropertyChanged += _ => commentNode.MarkDirty();

		}
		else if ( target is EditorActionGraph actionGraph )
		{
			var obj = AddObject( ps, EditorTypeLibrary.GetSerializedObject( actionGraph ) );
			var inputNode = actionGraph.FindNode( actionGraph.Graph.InputNode );

			obj.OnPropertyChanged += _ => inputNode?.MarkDirty();
		}
		else if ( target is EditorNode node )
		{
			var obj = AddObject( ps, EditorTypeLibrary.GetSerializedObject( node ) );
			obj.OnPropertyChanged += _ => nodeUi!.Position = node.Position;

			obj = AddObject( ps, new SerializedActionNode( this, node ) );
			obj.OnPropertyChanged += _ => node.MarkDirty();
		}
	}

	private SerializedObject AddObject( ControlSheet ps, SerializedObject obj )
	{
		ps.AddObject( obj );

		return obj;
	}
}

internal class SerializedActionNode : SerializedObject
{
	private static bool CanEditInputType( Type type )
	{
		if ( type is null ) return false;
		if ( Either.IsEitherType( type ) ) return true;

		if ( type.IsAbstract || type.IsInterface || type.IsArray || type.ContainsGenericParameters )
		{
			return false;
		}

		if ( type == typeof( Task ) || type == typeof( Task[] ) ) return false;
		if ( type == typeof( Variable ) ) return false;

		// Should use scene ref / resource ref nodes instead
		if ( type == typeof( GameObject ) ) return false;
		if ( type.IsAssignableTo( typeof( Component ) ) ) return false;
		if ( type.IsAssignableTo( typeof( Resource ) ) ) return false;

		return true;
	}

	private static HashSet<string> HiddenProperties { get; } = new()
	{
		"graph/graph",
		"input.value/_name",
		"var/_var",
		"property/_name", "property/_type",
		"call/_name", "call/_type", "call/_isStatic",
		"new/_type",
		"switch/cases"
	};

	private static HashSet<string> HiddenInputs { get; } = new()
	{

	};

	public SerializedActionNode( Properties properties, EditorNode node )
	{
		PropertyList = new();

		foreach ( var (name, property) in node.Node.Properties )
		{
			if ( property.Display.Hidden is true )
			{
				continue;
			}

			if ( HiddenProperties.Contains( $"{node.Definition.Identifier}/{name}" ) )
			{
				continue;
			}

			PropertyList.Add( new SerializedNodeProperty( properties, node, property ) );
		}

		if ( node.Definition.Identifier == "switch" )
		{
			AddSwitchNodeProperties( properties, node );
		}

		foreach ( var (name, input) in node.Node.Inputs )
		{
			if ( HiddenInputs.Contains( $"{node.Definition.Identifier}/{name}" ) )
			{
				continue;
			}

			if ( !input.IsArray )
			{
				if ( input.IsLinked && input.Link?.TryGetConstant( out _ ) is not true )
				{
					continue;
				}

				if ( !CanEditInputType( input.Type ) )
				{
					continue;
				}

				PropertyList.Add( new SerializedNodeInput( properties, node, input ) );
			}
			else
			{
				if ( input.Link is not null )
				{
					continue;
				}

				if ( !CanEditInputType( input.ElementType ) )
				{
					continue;
				}

				var count = input.LinkArray?.Count ?? 0;

				for ( var i = 0; i <= count; ++i )
				{
					if ( i < count && input.LinkArray?[i].TryGetConstant( out _ ) is not true )
					{
						continue;
					}

					PropertyList.Add( new SerializedNodeInputElement( properties, node, input, i ) );
				}
			}
		}
	}

	private void AddSwitchNodeProperties( Properties properties, EditorNode node )
	{
		var valueType = GetSwitchValueType( node.Node ) ?? typeof( string );
		var sPropType = typeof( SerializedSwitchCasesProperty<> ).MakeGenericType( valueType );

		PropertyList.Add( (SerializedProperty)Activator.CreateInstance( sPropType, properties, node ) );
	}

	private Type GetSwitchValueType( Node node )
	{
		if ( node.Inputs["value"].SourceType is { } valueType )
		{
			return valueType;
		}

		if ( node.Properties["cases"].Value is not { } casesObj )
		{
			return null;
		}

		foreach ( var iFace in casesObj.GetType().GetInterfaces() )
		{
			if ( !iFace.IsConstructedGenericType ) continue;
			if ( iFace.GetGenericTypeDefinition() != typeof( IReadOnlyList<> ) ) continue;

			return iFace.GetGenericArguments()[0];
		}

		return null;
	}
}

public interface ISerializedNodeParameter
{
	Node.IParameter Parameter { get; }
}

internal abstract class SerializedNodeParameter<T> : SerializedProperty, ISerializedNodeParameter
	where T : Node.IParameter
{
	public Properties Properties { get; }
	public EditorNode Node { get; }
	public T Target { get; }

	public override string Name => Target.Name;
	public override string DisplayName => Target.Display.Title;
	public override string Description => Target.Display.Description;
	public override Type PropertyType => Target.Type == typeof( object )
		? Properties.PredictBestType( Target )
		: Nullable.GetUnderlyingType( Target.Type ) ?? Target.Type;

	public abstract object Value { get; set; }
	public abstract object DefaultValue { get; }
	public abstract bool IsRequired { get; }
	public abstract bool HasValue { get; }

	Node.IParameter ISerializedNodeParameter.Parameter => Target;

	public override IEnumerable<Attribute> GetAttributes() => Target.Attributes.Union( new[] { new AllowNullAttribute() } );

	protected SerializedNodeParameter( Properties properties, EditorNode node, T target )
	{
		Properties = properties;
		Node = node;
		Target = target;
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		obj = null;

		var description = EditorTypeLibrary.GetType( PropertyType );

		if ( description == null )
		{
			return false;
		}

		try
		{
			if ( !PropertyType.IsValueType )
			{
				var curVal = GetValue<object>( Target.Definition.IsRequired ? description.Create<object>() : null );

				if ( curVal == null )
				{
					return false;
				}

				obj = EditorTypeLibrary.GetSerializedObject( () => curVal, description, this );
				return true;
			}

			obj = EditorTypeLibrary.GetSerializedObject( () => HasValue && Value is not null ? Value : IsRequired || DefaultValue is null
					? Activator.CreateInstance( PropertyType )
					: DefaultValue,
				description, this );
			return true;
		}
		catch ( Exception e )
		{
			Log.Warning( e );
			obj = null;
			return false;
		}
	}

	private static object ConvertTo( object value, Type sourceType, Type targetType )
	{
		if ( sourceType == targetType || sourceType.IsAssignableTo( targetType ) )
		{
			return value;
		}

		if ( Nullable.GetUnderlyingType( targetType ) is { } underlyingTargetType && Nullable.GetUnderlyingType( sourceType ) is null )
		{
			if ( value is null )
			{
				return Activator.CreateInstance( targetType, null );
			}

			return Activator.CreateInstance( targetType, ConvertTo( value, sourceType, underlyingTargetType ) );
		}

		if ( sourceType == typeof( long ) && targetType.IsEnum )
		{
			// Special case for EnumControlWidget :S

			return Enum.ToObject( targetType, (long)value );
		}

		if ( sourceType.IsEnum && targetType == typeof( long ) )
		{
			// Special case for EnumControlWidget :S

			return Convert.ChangeType(
				Convert.ChangeType(
					value,
					Enum.GetUnderlyingType( sourceType ) ),
				targetType );
		}

		return Convert.ChangeType( value, targetType );
	}

	public override void SetValue<TVal>( TVal value )
	{
		if ( Either.IsEitherType( PropertyType ) )
		{
			Value = value;
		}
		else
		{
			try
			{
				Value = ConvertTo( value, typeof( TVal ), PropertyType );
			}
			catch
			{
				Value = value;
			}
		}

		Node.MarkDirty();
	}

	public override TVal GetValue<TVal>( TVal defaultValue = default )
	{
		var rawValue = HasValue ? Value : Target.Definition.IsRequired ? defaultValue : DefaultValue;

		if ( rawValue is TVal value )
		{
			return value;
		}

		if ( rawValue is null )
		{
			return defaultValue;
		}

		try
		{
			return (TVal)ConvertTo( rawValue, rawValue.GetType(), typeof( TVal ) );
		}
		catch
		{
			return defaultValue;
		}
	}
}

internal class SerializedNodeProperty : SerializedNodeParameter<Node.Property>
{
	public override string GroupName => "Properties";

	public SerializedNodeProperty( Properties properties, EditorNode node, Node.Property target ) : base( properties, node, target ) { }

	public override object Value
	{
		get => Target.Value;
		set => Target.Value = value;
	}

	public override Type PropertyType => Target.Type;
	public override object DefaultValue => Target.Definition.IsRequired ? null : Target.Definition.Default;

	public override bool HasValue => Target.Value is not null;
	public override bool IsRequired => Target.Definition.IsRequired;
}

internal class SerializedSwitchCasesProperty<T> : SerializedNodeProperty
{
	public override Type PropertyType { get; }

	private List<T> _value;

	public override object Value
	{
		get
		{
			if ( _value is not null ) return _value;

			if ( base.Value is not IReadOnlyList<T> baseValue )
			{
				return _value = new List<T>( (IReadOnlyList<T>)base.DefaultValue );
			}

			if ( baseValue is List<T> list )
			{
				return _value = list;
			}

			return _value = new List<T>( baseValue );
		}
		set
		{
			_value = (List<T>)value;
			base.Value = _value.ToArray();
		}
	}

	public override bool HasValue => true;

	public SerializedSwitchCasesProperty( Properties properties, EditorNode node )
		: base( properties, node, node.Node.Properties["cases"] )
	{
		PropertyType = typeof( List<> ).MakeGenericType( typeof( T ) );
	}

	public override bool TryGetAsObject( out SerializedObject obj )
	{
		obj = EditorTypeLibrary.GetSerializedObject( Value );
		obj.ParentProperty = this;
		obj.OnPropertyChanged += prop =>
		{
			Node.Node.Properties["cases"].Value = null;
			Node.Node.Properties["cases"].Value = ((List<T>)Value).ToArray();
			Node.MarkDirty();
		};

		return true;
	}
}

internal class SerializedNodeInput : SerializedNodeParameter<Node.Input>
{
	public override string GroupName => "Inputs";

	public SerializedNodeInput( Properties properties, EditorNode node, Node.Input target ) : base( properties, node, target ) { }

	public override object Value
	{
		get => Target.Value;
		set => Target.Value = value;
	}

	public override object DefaultValue => Target.Definition.IsRequired ? null : Target.Definition.Default;

	public override bool HasValue => Target.Link?.TryGetConstant( out _ ) is true;
	public override bool IsRequired => Target.Definition.IsRequired;
}

internal class SerializedNodeInputElement : SerializedNodeParameter<Node.Input>
{
	public int Index { get; }

	public override string DisplayName => $"{Target.Display.Title}[{Index}]";
	public override string GroupName => "Inputs";

	public SerializedNodeInputElement( Properties properties, EditorNode node, Node.Input target, int index ) : base( properties, node, target )
	{
		Index = index;
	}

	public override object Value
	{
		get => Target.LinkArray is { } linkArray && Index >= 0 && Index < linkArray.Count
			&& linkArray[Index].TryGetConstant( out var value ) ? value : null;
		set
		{
			if ( Target.LinkArray is { } linkArray && Index >= 0 && Index < linkArray.Count )
			{
				Target.SetLink( new Constant( value ), Index );
			}
			else
			{
				Target.InsertLink( new Constant( value ), Index );
			}
		}
	}

	public override Type PropertyType => Target.ElementType == typeof( object ) ? Properties.PredictBestType( Target ) : Target.ElementType;
	public override object DefaultValue => Target.ElementType.IsValueType
		? Activator.CreateInstance( Target.ElementType )
		: null;

	public override bool HasValue => Target.LinkArray is { } linkArray && Index >= 0 && Index < linkArray.Count
		&& linkArray[Index].TryGetConstant( out _ );
	public override bool IsRequired => false;
}
