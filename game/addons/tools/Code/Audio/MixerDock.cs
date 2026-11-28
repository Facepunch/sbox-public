using Sandbox.Audio;

namespace Editor.Audio;

public static class Helper
{
	// Convert from linear scale (0-1) to decibels
	public static float LinearToDecibels( float linear )
	{
		if ( linear == 0 ) return -float.PositiveInfinity; // To avoid taking the log of 0

		return 20 * MathF.Log10( linear );
	}

	// Convert from decibels to linear scale (0-1)
	public static float DecibelsToLinear( float decibels )
	{
		return MathF.Pow( 10.0f, decibels / 20.0f );
	}

}

[Dock( "Editor", "Mixer", "settings_input_component" )]
public class MixerDock : Widget
{
	Layout Body;
	bool _isDirty = false;

	public MixerDock( Widget parent ) : base( parent )
	{
		Layout = Layout.Row();
		Layout.Margin = 4;
		Layout.Spacing = 4;

		FocusMode = FocusMode.TabOrClickOrWheel;
	}

	void Rebuild()
	{
		Layout.Clear( true );

		var tree = new MixerTree( this );

		Layout.Add( tree );
		Body = Layout.AddColumn( 1 );

		var detail = new MixerDetail( this, Mixer.Master );

		tree.Selection.OnItemAdded += ( item ) => SelectMixer( item as Mixer );

		tree.Selection.Add( Mixer.Master );
	}

	private void SelectMixer( Mixer mixer )
	{
		Body.Clear( true );
		Body.Add( new MixerDetail( this, mixer ) );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( SetContentHash( contentHash ??= ContentHash, 0.5f ) )
		{
			Rebuild();
		}
	}

	// Made once: passing the method itself would make a delegate each call
	Func<int> contentHash;

	int ContentHash() => HashCode.Combine( Mixer.Master );

	[Shortcut( "editor.save", "CTRL+S" )]
	void SaveMixer()
	{
		var ms = new MixerSettings();
		ms.Mixers = Mixer.Master.Serialize();

		EditorUtility.SaveProjectSettings( ms, "Mixer.config" );
		_isDirty = false;
		UpdateWindowTitle();
	}

	void Restore()
	{
		var ms = EditorUtility.LoadProjectSettings<MixerSettings>( "Mixer.config" );
		Mixer.Master.Deserialize( ms.Mixers, TypeLibrary );
		_isDirty = false;
		UpdateWindowTitle();
	}

	internal void SetDirty()
	{
		_isDirty = true;
		UpdateWindowTitle();
	}

	void UpdateWindowTitle()
	{
		WindowTitle = $"Mixer{(_isDirty ? "*" : "")}";
		var window = GetWindow();
		if ( window.WindowTitle.StartsWith( "Mixer" ) && window.WindowTitle.Length < 7 )
		{
			window.WindowTitle = WindowTitle;
		}
	}

	protected override bool OnClose()
	{
		if ( _isDirty )
		{
			var confirm = new PopupWindow(
				"Save Mixer", "The mixer has unsaved changes. Would you like to save now?", "Cancel",
				new Dictionary<string, System.Action>()
				{
					{ "No", () => { Restore(); Destroy(); } },
					{ "Yes", () => { SaveMixer(); Destroy(); } }
				}
			);

			confirm.Show();

			return false;
		}

		return true;
	}
}

