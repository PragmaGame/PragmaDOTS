using System;
using Pragma.Dots.Actors;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace Pragma.Dots.StateMachines
{
    /// <summary>
    /// Gives an actor the machine <typeparamref name="TMachine"/>, which enters <see cref="Initial"/> on its first update.
    /// Declare one subclass per machine:
    /// <c>[Serializable, ActorPartMenu("Spell Machine")] public sealed class SpellMachinePart : StateMachinePart&lt;SpellMachine, SpellState&gt; { }</c>.
    /// </summary>
    [Serializable]
    public abstract class StateMachinePart<TMachine, TState> : ActorPart where TMachine : unmanaged, IStateMachine where TState : unmanaged, Enum
    {
        [SerializeField] private TState _initial;

        public TState Initial
        {
            get => _initial;
            set => _initial = value;
        }

        public override void Build(IActorBuilder builder)
        {
            StateMachineLayout.Validate(typeof(TMachine));
            var machine = StateMachine.Create(_initial);
            builder.AddComponent(UnsafeUtility.As<StateMachine, TMachine>(ref machine));
        }
    }
}
