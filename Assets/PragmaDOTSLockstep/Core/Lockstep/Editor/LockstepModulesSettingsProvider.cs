using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Pragma.Lockstep.Editor
{
    /// <summary>
    /// Project Settings > Pragma > Lockstep: turns the optional modules on and off. A module compiles only with its
    /// scripting define symbol, which the page writes to every build target at once: a server and clients built with
    /// different modules run different simulation systems and reject each other's simulation hash.
    /// </summary>
    internal static class LockstepModulesSettingsProvider
    {
        private static readonly (string Name, string Define, string Description)[] Modules =
        {
            ("Navigation", "PRAGMA_LOCKSTEP_NAVIGATION", "Navigation grid, obstacles, agents and the pathfinder."),
            ("Vision", "PRAGMA_LOCKSTEP_VISION", "Fog of war: one vision plane per player slot, vision sources, queries."),
            ("Stats", "PRAGMA_LOCKSTEP_STATS", "Attributes and resources with modifiers, changes and grants."),
            ("Views", "PRAGMA_LOCKSTEP_VIEWS", "Pooled GameObject views of simulated entities.")
        };

        private static NamedBuildTarget[] _buildTargets;

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Pragma/Lockstep", SettingsScope.Project)
            {
                label = "Lockstep",
                guiHandler = _ => OnGUI(),
                keywords = new HashSet<string>(Modules.Select(module => module.Name)) { "Lockstep", "Modules", "Define" }
            };
        }

        /// <summary>Adds or removes <paramref name="define"/> on every build target.</summary>
        internal static void SetDefine(string define, bool enabled)
        {
            foreach (var target in GetBuildTargets())
            {
                var defines = GetDefines(target);
                var changed = enabled ? defines.Add(define) : defines.Remove(define);
                if (changed)
                {
                    PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
                }
            }
        }

        private static void OnGUI()
        {
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("A module compiles only with its scripting define symbol. The toggles write it to " +
                "every build target, so that servers and clients run the same simulation systems.", MessageType.None);

            var targets = GetBuildTargets();
            var defines = targets.Select(GetDefines).ToArray();
            var partial = new List<string>();
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
            {
                foreach (var module in Modules)
                {
                    var count = defines.Count(set => set.Contains(module.Define));
                    if (count > 0 && count < targets.Length)
                    {
                        partial.Add(module.Name);
                    }

                    EditorGUI.showMixedValue = count > 0 && count < targets.Length;
                    EditorGUI.BeginChangeCheck();
                    var label = new GUIContent($"{module.Name}  ({module.Define})", module.Description);
                    var enabled = EditorGUILayout.ToggleLeft(label, count == targets.Length);
                    EditorGUI.showMixedValue = false;
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetDefine(module.Define, enabled);
                    }
                }
            }

            if (partial.Count > 0)
            {
                EditorGUILayout.HelpBox($"{string.Join(", ", partial)}: on for some build targets only. Toggle to set " +
                    "every target the same way.", MessageType.Warning);
            }
        }

        private static HashSet<string> GetDefines(NamedBuildTarget target)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(target);
            return new HashSet<string>(defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(define => define.Trim()));
        }

        // Every target the editor knows, installed or not: a target left without the defines would build without the modules.
        private static NamedBuildTarget[] GetBuildTargets()
        {
            if (_buildTargets != null)
            {
                return _buildTargets;
            }

            var targets = new Dictionary<string, NamedBuildTarget> { [NamedBuildTarget.Server.TargetName] = NamedBuildTarget.Server };
            foreach (var field in typeof(BuildTargetGroup).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var group = (BuildTargetGroup)field.GetValue(null);
                if (group == BuildTargetGroup.Unknown || field.IsDefined(typeof(ObsoleteAttribute), false))
                {
                    continue;
                }
                try
                {
                    var target = NamedBuildTarget.FromBuildTargetGroup(group);
                    targets[target.TargetName] = target;
                }
                catch (ArgumentException)
                {
                    // Groups without a named build target of their own.
                }
            }
            _buildTargets = targets.Values.OrderBy(target => target.TargetName, StringComparer.Ordinal).ToArray();
            return _buildTargets;
        }
    }
}
