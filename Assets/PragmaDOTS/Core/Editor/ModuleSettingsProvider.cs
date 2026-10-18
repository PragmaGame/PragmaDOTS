using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Pragma.Dots.Editor
{
    /// <summary>
    /// Project Settings > Pragma > DOTS: turns the optional modules on and off. A module compiles only with its scripting
    /// define symbol, which the page writes to every build target at once: a server and clients built with different
    /// modules run different simulation systems and reject each other's simulation hash.
    /// </summary>
    internal static class ModuleSettingsProvider
    {
        private const string LOCKSTEP = "PRAGMA_DOTS_LOCKSTEP";

        // A module that requires another one is listed after it.
        private static readonly (string Name, string Define, string Requires, string Description)[] Modules =
        {
            ("Lockstep", LOCKSTEP, null, "Deterministic lockstep: the server, the clients, the simulation world, transforms, " +
                "replays; its Netcode for Entities transport compiles when com.unity.netcode is installed."),
            ("Navigation", "PRAGMA_DOTS_NAVIGATION", LOCKSTEP, "Navigation grid, obstacles, agents and the pathfinder."),
            ("Vision", "PRAGMA_DOTS_VISION", LOCKSTEP, "Fog of war: one vision plane per player slot, vision sources, queries."),
            ("Stats", "PRAGMA_DOTS_STATS", LOCKSTEP, "Attributes and resources with modifiers, changes and grants."),
            ("Views", "PRAGMA_DOTS_VIEWS", LOCKSTEP, "Pooled GameObject views of simulated entities."),
            ("State Machines", "PRAGMA_DOTS_STATE_MACHINES", null, "Per-entity state machines whose states tick in parallel jobs.")
        };

        private static NamedBuildTarget[] _buildTargets;

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Pragma/DOTS", SettingsScope.Project)
            {
                label = "DOTS",
                guiHandler = _ => OnGUI(),
                keywords = new HashSet<string>(Modules.Select(module => module.Name)) { "DOTS", "Modules", "Define" }
            };
        }

        /// <summary>
        /// Turns the module with <paramref name="define"/> on or off on every build target: on together with the module it
        /// requires, off together with the modules that require it.
        /// </summary>
        internal static void SetModule(string define, bool enabled)
        {
            var changed = new HashSet<string> { define };
            foreach (var module in Modules)
            {
                if (enabled && module.Define == define && module.Requires != null)
                {
                    changed.Add(module.Requires);
                }
                if (!enabled && module.Requires == define)
                {
                    changed.Add(module.Define);
                }
            }
            SetDefines(changed, enabled);
        }

        /// <summary>Adds or removes <paramref name="defines"/> on every build target.</summary>
        internal static void SetDefines(IEnumerable<string> defines, bool enabled)
        {
            foreach (var target in GetBuildTargets())
            {
                var current = GetDefines(target);
                var changed = false;
                foreach (var define in defines)
                {
                    changed |= enabled ? current.Add(define) : current.Remove(define);
                }
                if (changed)
                {
                    PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", current));
                }
            }
        }

        private static void OnGUI()
        {
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Mathematics, actors and utilities are always there. A module compiles only with its " +
                "scripting define symbol; the toggles write it to every build target, so that servers and clients run the " +
                "same simulation systems.", MessageType.None);

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

                    EditorGUI.indentLevel = module.Requires != null ? 1 : 0;
                    EditorGUI.showMixedValue = count > 0 && count < targets.Length;
                    EditorGUI.BeginChangeCheck();
                    var tooltip = module.Requires != null ? $"{module.Description} Turns {module.Requires} on with it." : module.Description;
                    var label = new GUIContent($"{module.Name}  ({module.Define})", tooltip);
                    var enabled = EditorGUILayout.ToggleLeft(label, count == targets.Length);
                    EditorGUI.showMixedValue = false;
                    if (EditorGUI.EndChangeCheck())
                    {
                        SetModule(module.Define, enabled);
                    }
                }
                EditorGUI.indentLevel = 0;
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
