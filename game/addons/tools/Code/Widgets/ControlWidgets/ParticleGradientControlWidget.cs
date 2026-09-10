using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;

namespace Editor;

[CustomEditor( typeof( ParticleGradient ) )]
public class ParticleGradientControlWidget : ControlWidget
{
	public override bool SupportsMultiEdit => true;

	Layout ControlArea;

	SerializedObject Target;

	public ParticleGradientControlWidget( SerializedProperty property ) : base( property )
	{
		SetSizeMode( SizeMode.Ignore, SizeMode.Default );

		if ( !property.TryGetAsObject( out Target ) )
			return;

		Layout = Layout.Row();
		Layout.Spacing = 3;
		//Layout.Margin = new Sandbox.UI.Margin( 0, 0 );

		ControlArea = Layout.AddRow( 1 );
		ControlArea.Spacing = 2;
		//ControlArea.Margin = new Sandbox.UI.Margin( 0, 0 );

		Layout.AddStretchCell();

		var type = Target.GetProperty( "Type" );
		var dropDown = Create( type );
		dropDown.FixedWidth = 100;
		Layout.Add( dropDown );

		var evaluate = Target.GetProperty( "Evaluation" );
		var evalDropDown = Create( evaluate );
		evalDropDown.FixedWidth = 100;
		Layout.Add( evalDropDown );

		Target.OnPropertyChanged += ( p ) =>
		{
			if ( p != type ) return;
			RebuildForType( p.GetValue<ParticleGradient.ValueType>() );
		};

		RebuildForType( type.GetValue<ParticleGradient.ValueType>() );

	}

	void RebuildForType( ParticleGradient.ValueType type )
	{
		ControlArea.Clear( true );

		if ( type == ParticleGradient.ValueType.Constant )
		{
			var control = Create( Target.GetProperty( "ConstantValue" ) );
			ControlArea.Add( control );
		}

		if ( type == ParticleGradient.ValueType.Range )
		{
			var controlA = Create( Target.GetProperty( "ConstantA" ) );
			ControlArea.Add( controlA );

			var controlB = Create( Target.GetProperty( "ConstantB" ) );
			ControlArea.Add( controlB );
		}

		if ( type == ParticleGradient.ValueType.Gradient )
		{
			var controlA = Create( Target.GetProperty( "GradientA" ) );
			ControlArea.Add( controlA );
		}
	}

	protected override void OnPaint()
	{

	}

}
