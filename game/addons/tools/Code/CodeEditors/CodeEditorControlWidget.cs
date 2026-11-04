using Editor.CodeEditors;

namespace Editor;

[CustomEditor( typeof( ICodeEditor ) )]
public class CodeEditorControlWidget : ControlWidget
{
	/// <summary>
	/// Raised whenever the selected code editor changes, passing whether the newly selected editor is
	/// a <see cref="CustomCodeEditor"/>.
	/// </summary>
	public event Action<bool> CustomEditorSelected;

	public bool IsCustomSelected { get; private set; }

	public CodeEditorControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();

		IsCustomSelected = CodeEditor.Current is CustomCodeEditor;

		var comboBox = new ComboBox( this );

		var codeEditors = EditorTypeLibrary.GetTypes<ICodeEditor>()
			.Where( x => !x.IsInterface )
			.OrderBy( x => x.Order )
			.ThenByDescending( x => x.Create<ICodeEditor>()?.IsInstalled() )
			.ThenBy( x => x.Name );

		// If we have no code editors, the combobox will end up defaulting to a code editor we don't have installed.
		if ( !codeEditors.Any() )
		{
			comboBox.AddItem( "None - install one!", "error" );
		}

		foreach ( var codeEditor in codeEditors )
		{
			if ( codeEditor.TargetType == typeof( ICodeEditor ) ) continue;

			var instance = codeEditor.Create<ICodeEditor>();
			var isCustom = instance is CustomCodeEditor;

			comboBox.AddItem(
				codeEditor.Title,
				codeEditor.Icon,
				() =>
				{
					property.SetValue( codeEditor.Create<ICodeEditor>() );
					IsCustomSelected = isCustom;
					CustomEditorSelected?.Invoke( isCustom );
				},
				codeEditor.Description,
				false,
				instance.IsInstalled()
			);
		}

		if ( CodeEditor.Current is not null )
		{
			comboBox.TrySelectNamed( DisplayInfo.ForType( CodeEditor.Current.GetType() ).Name );
		}

		Layout.Add( comboBox );
	}
}
