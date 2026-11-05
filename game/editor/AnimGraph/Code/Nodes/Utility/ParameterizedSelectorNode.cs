using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Chooses one child pose when initialized using a deterministic index derived from the absolute floored input value. Option weights control each pose's share of the selection cycle.
/// <br/><br/><b>Use when:</b>&#160;Deterministically distributing entries across pose variations, optionally with relative weighting.
/// <br/><b>Does not continuously update:</b>&#160;the pose is picked on entry and held, so changing the parameter later will not switch it. To switch while playing, use a State Machine with a transition on that parameter.
/// <br/><b>Important:</b>&#160;The parameter is not normalized to 0-1, and all-zero effective weights select nothing.
/// </summary>
[Title( "Parameterized Selector" ), Group( "Flow" ), Icon( "shuffle" )]
public class ParameterizedSelectorNode : VariationDataNode<ParameterizedSelectorNode.Data>
{
	public override void OnPaint( Rect rect ) => EntrySelectionHint.Draw( rect );

	/// <summary>
	/// The parts of a parameterized selector that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		/// <summary>
		/// Relative weight of each option within the selection range, one per option.
		/// </summary>
		[Property, Range( 0, 255 )]
		public List<int> OptionWeights { get; set; } = [];
	}

	public class Option
	{
		/// <summary>
		/// Name shown on this option's input pin.
		/// </summary>
		[Property, KeyProperty]
		public string Label { get; set; } = "";

		[Hide]
		public NodeInput Pose { get; set; }
	}

	[Input( typeof( FloatLink ) )]
	public NodeInput Parameter { get; set; }

	[Property]
	public List<Option> Options { get; set; } =
	[
		new(),
		new(),
	];

	/// <summary>
	/// Skip options whose pose failed to compile instead of failing the whole node.
	/// </summary>
	[Property]
	public bool IgnoreInvalidOptions { get; set; } = false;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#e5c07b" ).Value;

	private readonly List<IPlugIn> _optionPlugs = new();

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			SyncOptionWeights();

			while ( _optionPlugs.Count > Options.Count )
				_optionPlugs.RemoveAt( _optionPlugs.Count - 1 );

			while ( _optionPlugs.Count < Options.Count )
			{
				var i = _optionPlugs.Count;
				var info = new AnimPlugInfo( $"Option{i}", () => OptionLabel( i ),
					_ => i < Options.Count ? Options[i].Pose : default,
					( _, value ) => { if ( i < Options.Count ) Options[i].Pose = value; } );

				_optionPlugs.Add( new AnimPlugIn( this, info, typeof( PoseLink ) ) );
			}

			return _optionPlugs;
		}
	}

	private string OptionLabel( int index )
	{
		if ( index >= Options.Count )
			return $"Option {index + 1}";

		return string.IsNullOrWhiteSpace( Options[index].Label ) ? $"Option {index + 1}" : Options[index].Label;
	}

	/// <summary>
	/// Keep every variation's weight list the same length as the option list. Esoterica does this
	/// from its pin creation and destruction hooks, we have no equivalent so we reconcile lazily.
	/// </summary>
	private void SyncOptionWeights()
	{
		foreach ( var data in AllVariationData )
		{
			data.OptionWeights ??= [];

			while ( data.OptionWeights.Count > Options.Count )
				data.OptionWeights.RemoveAt( data.OptionWeights.Count - 1 );

			while ( data.OptionWeights.Count < Options.Count )
				data.OptionWeights.Add( 0 );
		}
	}

	public override Menu CreateContextMenu( NodeUI node )
	{
		var menu = new Menu( "Parameterized Selector" );
		menu.AddOption( "Add Option", "add", () => { Options.Add( new Option() ); Update(); } );

		if ( Options.Count > 0 )
			menu.AddOption( "Remove Last Option", "remove", () => { Options.RemoveAt( Options.Count - 1 ); Update(); } );

		return menu;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var title = this is ParameterizedAnimationClipSelectorNode ? "Parameterized Clip Selector" : "Parameterized Selector";

		if ( Options.Count == 0 )
		{
			compiler.Warn( $"{title} has no options" );
			return compiler.ReferencePose();
		}

		var parameter = compiler.EmitValue( this, Parameter );
		if ( parameter is null )
		{
			compiler.Warn( $"{title}'s parameter input isn't connected" );
			parameter = compiler.ConstFloat( 0.0f );
		}

		var isClip = this is ParameterizedAnimationClipSelectorNode;
		var options = new List<GraphBuilder.Node>();
		foreach ( var option in Options )
		{
			var index = compiler.EmitPose( this, option.Pose, title );
			options.Add( index );
			isClip &= index.IsClip();
		}

		if ( this is ParameterizedAnimationClipSelectorNode && !isClip )
			compiler.Warn( "Parameterized Clip Selector options must be animation clips" );

		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );
		var weights = Enumerable.Range( 0, Options.Count )
			.Select( index => Math.Clamp( data.OptionWeights.ElementAtOrDefault( index ), 0, 255 ) ).ToArray();
		var hasWeights = weights.Distinct().Skip( 1 ).Any();

		return compiler.Builder.ParameterizedSelector(
			parameter,
			options,
			new GraphBuilder.ParameterizedSelectorSettings
			{
				Weights = weights,
				IgnoreInvalidOptions = IgnoreInvalidOptions,
				HasWeights = hasWeights,
				ClipsOnly = isClip,
			} );
	}

}
