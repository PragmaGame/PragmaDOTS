using Unity.Entities;

namespace Pragma.Dots.StateMachines
{
    /// <summary>
    /// A state machine component: a struct of the game whose only field is a <see cref="StateMachine"/>, one type per
    /// machine (a unit's brain, a spell's cooldown), so an entity may run several. A
    /// <see cref="StateMachineSystemGroup{TMachine}"/> updates it.
    /// </summary>
    /// <example>
    /// <code>
    /// public struct SpellMachine : IStateMachine
    /// {
    ///     public StateMachine machine;
    /// }
    /// </code>
    /// </example>
    public interface IStateMachine : IComponentData
    {
    }
}
