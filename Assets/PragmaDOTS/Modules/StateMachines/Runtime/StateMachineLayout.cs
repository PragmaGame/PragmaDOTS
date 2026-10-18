using System;
using System.Reflection;

namespace Pragma.Dots.StateMachines
{
    /// <summary>
    /// The rule a machine component follows: a <see cref="StateMachine"/> and nothing else, so that the transitions can
    /// read every machine component as one without a generic job per machine.
    /// </summary>
    internal static class StateMachineLayout
    {
        public static void Validate(Type type)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length != 1 || fields[0].FieldType != typeof(StateMachine))
            {
                throw new InvalidOperationException(
                    $"The state machine component {type.FullName} must have one field, a {nameof(StateMachine)}, and nothing else: " +
                    "keep the data of the states in components of their own.");
            }
        }
    }
}
