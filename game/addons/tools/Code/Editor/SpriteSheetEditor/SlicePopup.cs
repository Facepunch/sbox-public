using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.SpriteSheetEditor;

/// <summary>
/// The slicing controls: how to cut, where the pivots go, and what happens to what is already there.
/// </summary>
public class SlicePopup : PopupWidget
{
	readonly Window _editor;

	ControlSheet _sheet;
	Label _methodExplanation;
	Label _warning;
	Button _sliceButton;

	public SlicePopup( Window editor ) : base( editor )
	{
		_editor = editor;

		IsPopup = true;
		WindowTitle = "Slice";

		Layout = Layout.Column();
		Layout.Margin = 16;
		Layout.Spacing = 8;

		MinimumWidth = 320;
		MaximumWidth = 320;

		Rebuild();
		Show();
		ConstrainToScreen();
	}

	void Rebuild()
	{
		Layout.Clear( true );

		var serialized = _editor.SliceSettings.GetSerialized();

		_sheet = new ControlSheet();
		_sheet.AddObject( serialized );
		Layout.Add( _sheet );

		// Explaining what each method does, where the choice is made, rather than leaving three
		// similar-sounding words to be guessed at.
		_methodExplanation = new Label( "" ) { WordWrap = true };
		_methodExplanation.SetStyles( "color: #999; font-size: 11px;" );
		Layout.Add( _methodExplanation );

		_warning = new Label( "" ) { WordWrap = true, Color = Theme.Yellow };
		Layout.Add( _warning );

		_sliceButton = new Button.Primary( "Slice", "grid_view" );
		_sliceButton.Clicked = ApplySlicing;
		Layout.Add( _sliceButton );

		serialized.OnPropertyChanged += prop =>
		{
			// Unity resets the method whenever the type changes, on the grounds that a fresh cut is
			// the common case. Keeping the user's choice is the less surprising behaviour, so we
			// just re-describe it.
			UpdateExplanation();
			Rebuild();
		};

		UpdateExplanation();
	}

	void UpdateExplanation()
	{
		if ( _methodExplanation is null ) return;

		_methodExplanation.Text = _editor.SliceSettings.Method switch
		{
			SpriteSheet.SliceMethod.Smart =>
				"Smart reshapes slices that clearly match the new cut and adds the rest. Names and " +
				"references survive, and pivots you placed by hand are left alone.",

			SpriteSheet.SliceMethod.Safe =>
				"Safe only adds slices where nothing already sits. Nothing existing is changed or removed.",

			_ =>
				"Delete Existing throws away every slice and starts over. Names, pivots and any " +
				"sprites built from them are lost."
		};

		var destructive = _editor.SliceSettings.Method == SpriteSheet.SliceMethod.DeleteExisting;

		_warning.Text = destructive && _editor.HasNamedSlices()
			? "This sheet has renamed slices. Use Smart to keep them."
			: "";
		_warning.Visible = !string.IsNullOrEmpty( _warning.Text );

		if ( _sliceButton is not null )
		{
			_sliceButton.Text = _editor.SliceSettings.Type == SpriteSheet.SliceType.Automatic
				? "Slice Automatically"
				: "Slice";
		}
	}

	void ApplySlicing()
	{
		// Only nag when there is actually something to lose. A sheet nobody has named yet can be
		// re-cut freely, and asking every time trains people to click through the dialog.
		if ( _editor.SliceSettings.Method == SpriteSheet.SliceMethod.DeleteExisting && _editor.HasNamedSlices() )
		{
			var confirm = new PopupWindow(
				"Potential loss of slice data",
				"Delete Existing recreates every slice with a default name. Renamed slices will lose " +
				"their names and pivots, and anything referencing them will break.\n\n" +
				"Slice with Smart instead to keep them.",
				"Cancel",
				new Dictionary<string, Action>
				{
					{ "Use Smart", () => { _editor.SliceSettings.Method = SpriteSheet.SliceMethod.Smart; DoSlice(); } },
					{ "Delete Existing", DoSlice }
				} );

			confirm.Show();
			return;
		}

		DoSlice();
	}

	void DoSlice()
	{
		_editor.ApplySlicing();
		Close();
	}
}
