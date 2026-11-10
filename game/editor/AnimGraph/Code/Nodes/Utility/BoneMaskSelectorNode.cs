using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Selects the first bone mask whose option ID matches the input, using <b>Default Mask</b> when no option matches.
/// <br/><br/><b>Use when:</b>&#160;Choosing masks from an ID such as stance, equipment type, or body region.
/// <br/><b>Important:</b>&#160;Selection occurs on initialization. Enable <b>Switch Dynamically</b> to react to later ID changes, optionally blending over time.
/// </summary>
[Title( "Bone Mask Selector" ), Group( "Bone Mask" ), Icon( "rule" )]
public class BoneMaskSelectorNode : Node
{
	public class Option
	{
		/// <summary>
		/// ID that selects this mask.
		/// </summary>
		[Property, KeyProperty]
		public string Id { get; set; } = "";

		[Hide]
		public NodeInput Mask { get; set; }
	}

	[Input( typeof( IDLink ) )]
	public NodeInput Parameter { get; set; }

	[Input( typeof( BoneMaskLink ) ), Title( "Default Mask" )]
	public NodeInput DefaultMask { get; set; }

	[Property]
	public List<Option> Options { get; set; } = [new()];

	/// <summary>
	/// Blend to the new mask when the parameter changes while active.
	/// </summary>
	[Property]
	public bool SwitchDynamically { get; set; } = false;

	[Property, ShowIf( nameof( SwitchDynamically ), true )]
	public float BlendTime { get; set; } = 0.1f;

	[Output( typeof( BoneMaskLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#d19a66" ).Value;

	private readonly List<IPlugIn> _optionPlugs = new();

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			while ( _optionPlugs.Count > Options.Count )
				_optionPlugs.RemoveAt( _optionPlugs.Count - 1 );

			while ( _optionPlugs.Count < Options.Count )
			{
				var i = _optionPlugs.Count;
				var info = new AnimPlugInfo( $"Option{i}", () => OptionTitle( i ),
					_ => i < Options.Count ? Options[i].Mask : default,
					( _, value ) => { if ( i < Options.Count ) Options[i].Mask = value; } );

				_optionPlugs.Add( new AnimPlugIn( this, info, typeof( BoneMaskLink ) ) );
			}

			return _optionPlugs;
		}
	}

	private string OptionTitle( int i ) =>
		i < Options.Count && !string.IsNullOrWhiteSpace( Options[i].Id ) ? Options[i].Id : $"Mask {i + 1}";

	public override Menu CreateContextMenu( NodeUI node )
	{
		var menu = new Menu( "Bone Mask Selector" );
		menu.AddOption( "Add Mask", "add", () => { Options.Add( new Option() ); Update(); } );

		if ( Options.Count > 0 )
			menu.AddOption( "Remove Last Mask", "remove", () => { Options.RemoveAt( Options.Count - 1 ); Update(); } );

		return menu;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var parameter = compiler.EmitValue( this, Parameter );
		if ( parameter is null )
		{
			compiler.Warn( "Bone Mask Selector's parameter input isn't connected" );
			parameter = compiler.ConstID();
		}

		var options = Options
			.Select( option => (option.Id, compiler.EmitMask( this, option.Mask, "Bone Mask Selector" )) )
			.ToArray();

		return compiler.Builder.BoneMaskSelector(
			parameter,
			compiler.EmitValue( this, DefaultMask ),
			options,
			new GraphBuilder.BoneMaskSelectionSettings
			{
				SwitchDynamically = SwitchDynamically,
				BlendTime = BlendTime,
			} );
	}

}
