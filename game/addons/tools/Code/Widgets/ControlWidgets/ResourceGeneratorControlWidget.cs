using Sandbox.Resources;

namespace Editor;

/// <summary>
/// A control widget that shows a ResourceGenerator. This will either be a dropdown which
/// will pop up the config editor for the generator, or will be an inline editor with the
/// key properties, with an edit button on the side.
/// </summary>
public class ResourceGeneratorControlWidget : ControlWidget
{
	GenericControlWidget Editor { get; set; }
	protected ResourceGenerator Generator { get; set; }

	bool _isDirty = false;
	bool _isGenerating = false;

	public ResourceGeneratorControlWidget( ResourceGenerator generator, SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		Layout.Spacing = 2;

		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		var generatorObject = generator.GetSerialized();
		Generator = generator;

		var loadedFromProperty = LoadFromResource( property.GetValue<Resource>() );

		var prop = EditorTypeLibrary.CreateProperty<ResourceGenerator>( "Generator", generatorObject );

		Editor = new GenericControlWidget( prop );
		generatorObject.OnPropertyChanged = x => MakeDirty();

		BuildContent();

		_ = UpdateGenerator( writeBack: !loadedFromProperty );
	}

	protected virtual void BuildContent()
	{
		var col = Layout.AddColumn();
		col.Add( Editor );
		col.AddStretchCell();
		Layout.AddStretchCell();
	}

	/// <summary>
	/// We might want existing data from an embedded resource - in that case, deserialize it
	/// </summary>
	/// <param name="resource"></param>
	/// <returns>True if our config came from the resource the property is already holding.</returns>
	bool LoadFromResource( Resource resource )
	{
		var embeddedResource = resource?.EmbeddedResource;

		if ( embeddedResource is null )
		{
			return false;
		}

		// Not a matching compiler
		if ( embeddedResource.Value.ResourceGenerator != EditorTypeLibrary.GetType( Generator.GetType() ).ClassName )
		{
			return false;
		}

		Generator.Deserialize( embeddedResource.Value.Data );
		return true;
	}

	void MakeDirty()
	{
		_isDirty = true;
	}

	/// <param name="writeBack">
	/// Whether the generated resource should be written back to the property. False when we only
	/// materialised what the property was already describing.
	/// </param>
	async Task UpdateGenerator( bool writeBack = true )
	{
		_isGenerating = true;

		try
		{
			var resource = await Generator.FindOrCreateObjectAsync( ResourceGenerator.Options.Default, default );

			//
			// Only write when this is actually a change. On a multi-selection SerializedProperty.SetValue
			// writes to every selected object, so writing back a resource we just loaded out of the
			// property would copy the first selection's resource on to all of the others - just from
			// opening the inspector, with nothing edited.
			//
			if ( writeBack )
			{
				SerializedProperty.SetValue( resource );
			}

			OnResourceChanged( resource );
			Update();
		}
		finally
		{
			_isGenerating = false;
		}
	}

	protected virtual void OnResourceChanged( Resource resource )
	{

	}

	[EditorEvent.Frame]
	public void FrameUpdate()
	{
		if ( _isGenerating ) return;
		if ( !_isDirty ) return;

		if ( Generator is not null )
		{
			_isDirty = false;
			_ = UpdateGenerator();
		}
	}

	protected override void PaintUnder()
	{
		// nothing
	}

	/// <summary>
	/// Create a ResourceGeneratorControlWidget for a specific generator
	/// </summary>
	internal static Widget Create( ResourceGenerator generator, SerializedProperty serializedProperty )
	{
		var allTypes = EditorTypeLibrary.GetTypes<ResourceGeneratorControlWidget>();
		var editor = allTypes.OrderByDescending( x => Score( generator, x ) ).FirstOrDefault();

		return editor.Create<Widget>( [generator, serializedProperty] );
	}

	/// <summary>
	/// score a ResourceGeneratorControlWidget based on how well it matches the target ResourceGenerator
	/// </summary>
	private static int Score( ResourceGenerator target, TypeDescription targetEditor )
	{
		var editorTarget = targetEditor.GetAttribute<CanEditAttribute>()?.Type;

		//
		// Target editor doesn't have a CanEdit
		//
		if ( editorTarget == null ) return 100;

		int score = 1000;
		var targetType = target.GetType();

		while ( targetType != null )
		{
			if ( editorTarget.IsAssignableFrom( targetType ) )
			{
				return score;
			}

			score -= 100;
			targetType = targetType.BaseType;
		}

		return 0;
	}
}
