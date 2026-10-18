using System;
using Unity.Collections.LowLevel.Unsafe;

namespace Pragma.Dots.StateMachines
{
    /// <summary>
    /// One state machine of an entity: the state it is in, the one it left last and the one requested next. States are
    /// numbers the game picks, usually the values of an enum; <see cref="NONE"/> (0) is no state.
    /// </summary>
    /// <remarks>
    /// It is the only field of a machine component of the game (<see cref="IStateMachine"/>), which a
    /// <see cref="StateMachineSystemGroup{TMachine}"/> updates: the group applies the requested transition, then its
    /// systems, the states, run. A state's system ticks the entities in its state (<see cref="Is(int)"/>), runs its enter
    /// logic on the update of the transition (<see cref="Entered(int)"/>) and its exit logic for the entities that just
    /// left it (<see cref="Exited(int)"/>), and asks for the next state (<see cref="Request(int)"/>).
    /// </remarks>
    public struct StateMachine
    {
        public const int NONE = 0;

        /// <summary>The state the machine is in; <see cref="NONE"/> until the first update after it was created.</summary>
        public int current;

        /// <summary>The state it left on its last transition; <see cref="NONE"/> before the first one.</summary>
        public int previous;

        /// <summary>The state requested for the next update; <see cref="NONE"/> when nothing is requested.</summary>
        public int next;

        /// <summary>Updates of the machine since it entered <see cref="current"/>: 0 on the update of the transition.</summary>
        public int ticksInState;

        /// <summary>A machine that enters <paramref name="initial"/> on its first update.</summary>
        public static StateMachine Create(int initial) => new StateMachine { next = initial };

        /// <inheritdoc cref="Create(int)"/>
        public static StateMachine Create<TState>(TState initial) where TState : unmanaged, Enum => Create(Id(initial));

        /// <summary>True when a transition is requested for the next update.</summary>
        public bool HasRequest => next != NONE;

        public bool Is(int state) => current == state;

        public bool Is<TState>(TState state) where TState : unmanaged, Enum => Is(Id(state));

        /// <summary>True on the update the machine entered <paramref name="state"/>: the time for its enter logic.</summary>
        public bool Entered(int state) => ticksInState == 0 && current == state;

        /// <inheritdoc cref="Entered(int)"/>
        public bool Entered<TState>(TState state) where TState : unmanaged, Enum => Entered(Id(state));

        /// <summary>True on the update the machine left <paramref name="state"/>: the time for its exit logic.</summary>
        public bool Exited(int state) => ticksInState == 0 && previous == state && current != state;

        /// <inheritdoc cref="Exited(int)"/>
        public bool Exited<TState>(TState state) where TState : unmanaged, Enum => Exited(Id(state));

        /// <summary>
        /// Asks for <paramref name="state"/> on the next update of the machine; a later request replaces it, and a request
        /// for the current state (or <see cref="NONE"/>) changes nothing.
        /// </summary>
        public void Request(int state) => next = state;

        /// <inheritdoc cref="Request(int)"/>
        public void Request<TState>(TState state) where TState : unmanaged, Enum => Request(Id(state));

        /// <summary>The number a state of an enum is stored as: its value.</summary>
        public static int Id<TState>(TState state) where TState : unmanaged, Enum
        {
            // An enum is its underlying integer, and its size is a constant to Burst.
            switch (UnsafeUtility.SizeOf<TState>())
            {
                case 1:
                    return UnsafeUtility.As<TState, byte>(ref state);
                case 2:
                    return UnsafeUtility.As<TState, ushort>(ref state);
                case 8:
                    return (int)UnsafeUtility.As<TState, long>(ref state);
                default:
                    return UnsafeUtility.As<TState, int>(ref state);
            }
        }

        public override string ToString()
        {
            var text = $"{current} (from {previous}, {ticksInState} ticks)";
            return next != NONE ? $"{text}, next {next}" : text;
        }

        /// <summary>One update: the requested transition, or one more tick in the current state.</summary>
        internal void Advance()
        {
            if (next != NONE && next != current)
            {
                previous = current;
                current = next;
                ticksInState = 0;
            }
            else if (ticksInState < int.MaxValue)
            {
                ticksInState++;
            }
            next = NONE;
        }
    }
}
