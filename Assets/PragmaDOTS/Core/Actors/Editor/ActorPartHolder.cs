using UnityEngine;

namespace Pragma.Dots.Actors.Editor
{
    // Holds a part that lives in no asset (the merge of a lineage), so that the inspector can draw it as a property.
    internal sealed class ActorPartHolder : ScriptableObject
    {
        [SerializeReference] public ActorPart part;
    }
}
