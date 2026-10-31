using System;
using Unity.Entities;

namespace Pragma.Dots.Utils
{
    /// <summary>
    /// Keeping an entity out of the Hierarchy window. Entities has a tag for it (the window's <i>Show Hidden Entities</i>
    /// setting shows such entities again), but declares it internal and only in the editor, hence the lookup by name.
    /// </summary>
    public static class EntityHierarchy
    {
        /// <summary>Full name of the editor-only tag of Entities that hides an entity in the Hierarchy window.</summary>
        public const string HIDDEN_TAG_NAME = "Unity.Entities.HideInHierarchy";

#if UNITY_EDITOR
        private static readonly Type HiddenTagType = typeof(Entity).Assembly.GetType(HIDDEN_TAG_NAME);
#endif

        /// <summary>
        /// The tag that hides an entity in the Hierarchy window: there is one in the editor while Entities has it. Outside
        /// the editor, or after Entities renames it, there is none, and an entity simply stays visible.
        /// </summary>
        public static bool TryGetHiddenTag(out ComponentType tag)
        {
#if UNITY_EDITOR
            if (HiddenTagType != null)
            {
                tag = ComponentType.FromTypeIndex(TypeManager.GetTypeIndex(HiddenTagType));
                return true;
            }
#endif
            tag = default;
            return false;
        }
    }
}
