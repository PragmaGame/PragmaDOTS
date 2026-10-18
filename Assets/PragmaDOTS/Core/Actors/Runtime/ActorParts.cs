using System.Collections.Generic;

namespace Pragma.Dots.Actors
{
    /// <summary>The rule that combines parts: a part replaces the part of its type, a part of a new type is added at the end.</summary>
    public static class ActorParts
    {
        /// <summary>
        /// Applies <paramref name="overrides"/> to <paramref name="parts"/>: each one replaces the part of its type in place,
        /// or is added at the end. Empty entries (a part whose class no longer exists) are skipped.
        /// </summary>
        public static void Override(List<ActorPart> parts, IReadOnlyList<ActorPart> overrides)
        {
            if (overrides == null)
            {
                return;
            }
            for (var i = 0; i < overrides.Count; i++)
            {
                var part = overrides[i];
                if (part == null)
                {
                    continue;
                }
                var index = IndexOf(parts, part.GetType());
                if (index >= 0)
                {
                    parts[index] = part;
                }
                else
                {
                    parts.Add(part);
                }
            }
        }

        public static int IndexOf(IReadOnlyList<ActorPart> parts, System.Type type)
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null && parts[i].GetType() == type)
                {
                    return i;
                }
            }
            return -1;
        }

        public static bool TryGet<TPart>(IReadOnlyList<ActorPart> parts, out TPart part) where TPart : ActorPart
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i] is TPart match)
                {
                    part = match;
                    return true;
                }
            }
            part = null;
            return false;
        }
    }
}
