namespace Pragma.Dots.Actors
{
    /// <summary>
    /// A part whose override merges with the inherited part of its type instead of replacing it: a part of entries by key
    /// (stats by stat, grants by what they change), where a derived definition or a placement lists only the entries it
    /// adds or changes, and the inherited ones it does not list stay as the parent has them.
    /// </summary>
    public interface IMergeableActorPart
    {
        /// <summary>
        /// This part laid over <paramref name="inherited"/>, a part of the same type: a new part, whose entries are the
        /// inherited ones with this part's entries replacing those with the same key and the others added after them, and
        /// whose other settings are this part's. Neither part changes.
        /// </summary>
        ActorPart MergeOnto(ActorPart inherited);

        /// <summary>An override of this part that changes nothing yet: its settings, no entries. The inspector starts overrides with it.</summary>
        ActorPart CreateOverride();
    }
}
