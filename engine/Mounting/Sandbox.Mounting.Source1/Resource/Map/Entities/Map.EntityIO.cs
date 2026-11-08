using System;
using System.Collections.Generic;
using Door = Sandbox.Mapping.Door;
using Button = Sandbox.Mapping.Button;

namespace Sandbox.Mounting.Source1;

partial class MapLoader
{
	private sealed partial class EntityBuilder
	{
		private readonly Dictionary<MapEntity, Component> ioEntities = [];

		public void RegisterEntityIO( MapEntity entity, Component component ) => ioEntities.Add( entity, component );

		private void WireEntityIO()
		{
			foreach ( var (entity, component) in ioEntities )
			{
				switch ( component )
				{
					case Door door:
						door.OnOpen = BuildOutput( entity, "OnOpen", true );
						door.OnClose = BuildOutput( entity, "OnClose", false );
						break;
					case Button button:
						button.OnPressed = BuildOutput( entity, "OnPressed", true );
						break;
				}
			}
			foreach ( var entity in bsp.Entities )
				foreach ( var (key, value) in entity.KeyValues.Where( entry => entry.Key.StartsWith( "On", StringComparison.OrdinalIgnoreCase )
					|| entry.Key.StartsWith( "Out", StringComparison.OrdinalIgnoreCase ) ) )
				{
					if ( postProcessing.HandledOutputs.Contains( (entity, key, value) ) )
						continue;
					var supported = ioEntities.TryGetValue( entity, out var component ) && (component is Door
						&& (key.Equals( "OnOpen", StringComparison.OrdinalIgnoreCase ) || key.Equals( "OnClose", StringComparison.OrdinalIgnoreCase ))
						|| component is Button && key.Equals( "OnPressed", StringComparison.OrdinalIgnoreCase ));
					if ( !supported )
						warnings.Add( $"Entity '{entity.Get( "classname" )}' output '{key}' has no native component/Doo event; omitted." );
				}
		}

		private Doo BuildOutput( MapEntity source, string output, bool hasUser )
		{
			var actions = new List<(float Delay, Doo.Block Block)>();
			foreach ( var (_, value) in source.KeyValues.Where( entry => entry.Key.Equals( output, StringComparison.OrdinalIgnoreCase ) ) )
			{
				var connection = BspEntityConnection.Read( output, value );
				if ( connection.FireCount > 0 || connection.Parameter.Length > 0 )
				{
					warnings.Add( $"Output '{output}' to '{connection.Target}.{connection.Input}' requires a parameter or finite fire count not supported by native Doo events; omitted." );
					continue;
				}
				var targets = ResolveTargets( source, connection.Target ).ToArray();
				if ( targets.Length == 0 )
					warnings.Add( $"Output '{output}' target '{connection.Target}' has no imported native receiver; omitted." );
				foreach ( var target in targets )
				{
					var action = BuildInput( target, connection.Input, hasUser );
					if ( action is not null ) actions.Add( (connection.Delay, action) );
					else warnings.Add( $"Input '{connection.Input}' on '{target.GetType().Name}' is not supported by the engine's public Doo methods; omitted." );
				}
			}
			if ( actions.Count == 0 ) return null;
			var body = new List<Doo.Block>();
			var elapsed = 0f;
			foreach ( var (delay, block) in actions.OrderBy( action => action.Delay ) )
			{
				if ( delay > elapsed )
					body.Add( new Doo.DelayBlock { Seconds = new Doo.LiteralExpression { LiteralValue = delay - elapsed } } );
				elapsed = delay;
				body.Add( block );
			}
			return new Doo { Body = body };
		}

		private IEnumerable<Component> ResolveTargets( MapEntity source, string name )
		{
			return BspEntityConnection.ResolveTargets( source, name, ioEntities.Keys ).Select( entity => ioEntities[entity] );
		}

		private static Doo.Block BuildInput( Component target, string input, bool hasUser )
		{
			if ( target is not Door ) return null;
			var method = input.ToLowerInvariant() switch
			{
				"open" => nameof( Door.Open ),
				"close" => nameof( Door.Close ),
				"toggle" => nameof( Door.Toggle ),
				_ => null
			};
			if ( method is null ) return null;
			return new Doo.InvokeBlock
			{
				InvokeType = Doo.InvokeType.Member,
				TargetComponent = new Doo.TargetComponent { Type = Doo.TargetComponent.TargetType.Direct, ComponentValue = target },
				Member = $"{typeof( Door ).FullName}.{method}",
				Arguments = method == nameof( Door.Close ) ? null : [hasUser
					? new Doo.VariableExpression { VariableName = "user" }
					: new Doo.LiteralExpression { LiteralValue = default }]
			};
		}
	}
}
