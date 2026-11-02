using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Reads the input ID when initialized, selects the first matching option, and keeps that pose for the node's active lifetime. An unmatched ID uses Fallback when connected.
/// <br/><br/><b>Use when:</b>&#160;Selecting a pose once from a named state, item, stance, or action ID.
/// <br/><b>Does not continuously update:</b>&#160;the pose is picked on entry and held, so changing the ID later will not switch it. To switch while playing, use a State Machine with a transition on that parameter.
/// <br/><b>Important:</b>&#160;Invalid selected options can use Fallback.
/// </summary>
[Title( "ID Based Selector" ), Group( "Flow" ), Icon( "call_split" )]
public class IDBasedSelectorNode : Node, IParameterReference
{
	IParameter IParameterReference.ResolveParameter()
		=> Root is { } root ? ParameterReferences.Resolve( root.Parameters, ParameterIdentifier, ParameterName, AnimParameterType.ID ) : null;

	public override void OnPaint( Rect rect ) => EntrySelectionHint.Draw( rect );

	public class Option
	{
		/// <summary>
		/// The ID this option is selected by.
		/// </summary>
		[Property, KeyProperty]
		public string Id { get; set; } = "";

		[Hide]
		public NodeInput Pose { get; set; }
	}

	[Hide]
	public string ParameterIdentifier { get; set; } = "";

	[Property, Title( "Parameter" ), Editor( "AnimGraphParameterName" )]
	public string ParameterName { get; set; } = "";

	[Input( typeof( PoseLink ) )]
	public NodeInput Fallback { get; set; }

	[Hide]
	public List<Option> Options { get; set; } = [];

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
			while ( _optionPlugs.Count > Options.Count )
				_optionPlugs.RemoveAt( _optionPlugs.Count - 1 );

			while ( _optionPlugs.Count < Options.Count )
			{
				var i = _optionPlugs.Count;
				var info = new AnimPlugInfo( $"Option{i}",
					() => i < Options.Count && !string.IsNullOrWhiteSpace( Options[i].Id ) ? Options[i].Id : $"Option {i + 1}",
					_ => i < Options.Count ? Options[i].Pose : default,
					( _, value ) => { if ( i < Options.Count ) Options[i].Pose = value; } );

				_optionPlugs.Add( new AnimPlugIn( this, info, typeof( PoseLink ) ) );
			}

			return _optionPlugs;
		}
	}

	internal void SelectParameter( Parameter parameter )
	{
		var changed = ParameterIdentifier != parameter.Identifier;
		ParameterIdentifier = parameter.Identifier;
		ParameterName = parameter.Name;

		if ( changed )
		{
			ResetOptionsFromParameter();
		}
		else if ( SynchronizeOptionsFromParameter() )
		{
			Update();
		}
	}

	internal void ClearParameter()
	{
		ParameterIdentifier = "";
		ParameterName = "";
		Options = [];
		Update();
	}

	internal void ClearParameterIfMissing()
	{
		var exists = Root?.Parameters.Any( parameter =>
			parameter.Type == AnimParameterType.ID
			&& ((!string.IsNullOrWhiteSpace( ParameterIdentifier ) && parameter.Identifier == ParameterIdentifier)
				|| string.Equals( parameter.Name, ParameterName, StringComparison.OrdinalIgnoreCase )) ) is true;

		if ( exists
			|| (string.IsNullOrWhiteSpace( ParameterIdentifier )
				&& string.IsNullOrWhiteSpace( ParameterName )
				&& Options.Count == 0) )
		{
			return;
		}

		ClearParameter();
	}

	internal bool SynchronizeOptionsFromParameter()
	{
		if ( Root is not { } root )
			return false;

		var parameter = !string.IsNullOrWhiteSpace( ParameterIdentifier )
			? root.Parameters.FirstOrDefault( candidate => candidate.Identifier == ParameterIdentifier )
			: null;
		parameter ??= root.Parameters.FirstOrDefault( candidate =>
			candidate.Type == AnimParameterType.ID
			&& string.Equals( candidate.Name, ParameterName, StringComparison.OrdinalIgnoreCase ) );

		if ( parameter is not { Type: AnimParameterType.ID } )
		{
			if ( Options.Count == 0 )
				return false;

			Options = [];
			return true;
		}

		ParameterIdentifier = parameter.Identifier;
		ParameterName = parameter.Name;

		var existing = Options
			.Where( option => !string.IsNullOrWhiteSpace( option.Id ) )
			.GroupBy( option => option.Id, StringComparer.Ordinal )
			.ToDictionary( group => group.Key, group => group.First(), StringComparer.Ordinal );
		var synchronized = parameter.Options
			.Select( id => existing.GetValueOrDefault( id ) ?? new Option { Id = id } )
			.ToList();

		if ( Options.Count == synchronized.Count && Options.SequenceEqual( synchronized ) )
			return false;

		Options = synchronized;
		return true;
	}

	internal void ResetOptionsFromParameter()
	{
		if ( Options.Count > 0 )
		{
			Options = [];
			Update();
		}

		if ( SynchronizeOptionsFromParameter() )
			Update();
	}

	internal IReadOnlyList<Parameter> GetAvailableParameters()
	{
		if ( Root is not { } root )
			return [];

		return root.Parameters
			.Where( parameter => parameter.Type == AnimParameterType.ID )
			.ToArray();
	}



	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var title = this is IDBasedAnimationClipSelectorNode ? "ID Based Clip Selector" : "ID Based Selector";
		var fallback = compiler.EmitValue( this, Fallback );

		if ( Options.Count == 0 )
		{
			compiler.Warn( $"{title} has no ID parameter with expected values" );
			return fallback ?? compiler.ReferencePose();
		}

		var parameter = compiler.EmitControlParameterReference( this, ParameterIdentifier, ParameterName, AnimParameterType.ID );
		if ( parameter is null )
			parameter = compiler.ConstID();

		var isClip = this is IDBasedAnimationClipSelectorNode;
		var options = new List<(string Id, GraphBuilder.Node Pose)>();
		foreach ( var option in Options.Where( option => option.Pose.IsValid ) )
		{
			var pose = compiler.EmitPose( this, option.Pose, title );
			options.Add( (option.Id, pose) );
			isClip &= pose.IsClip();
		}

		if ( options.Count == 0 )
		{
			compiler.Warn( $"{title} has no options connected" );
			return fallback ?? compiler.ReferencePose();
		}

		isClip &= fallback is null || fallback.IsClip();

		if ( this is IDBasedAnimationClipSelectorNode && !isClip )
			compiler.Warn( "ID Based Clip Selector options must be animation clips" );

		return compiler.Builder.IdBasedSelector(
			parameter,
			options,
			new GraphBuilder.IdBasedSelectorSettings
			{
				Fallback = fallback,
				IgnoreInvalidOptions = IgnoreInvalidOptions,
				ClipsOnly = isClip,
			} );
	}

}
