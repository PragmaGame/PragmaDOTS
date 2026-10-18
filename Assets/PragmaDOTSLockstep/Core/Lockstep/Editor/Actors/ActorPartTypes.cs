using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEditor;

namespace Pragma.Lockstep.Editor
{
    /// <summary>The part types the part menu offers: <see cref="ActorPart"/> classes and the types with <see cref="ActorComponentAttribute"/>.</summary>
    internal static class ActorPartTypes
    {
        private static List<(Type Type, string Menu)> _all;

        public static IReadOnlyList<(Type Type, string Menu)> All => _all ??= Collect();

        public static string GetMenu(Type type)
        {
            foreach (var entry in All)
            {
                if (entry.Type == type)
                {
                    return entry.Menu;
                }
            }
            // A generic part of a type without the attribute, made in code.
            if (type.IsGenericType)
            {
                return ObjectNames.NicifyVariableName(type.GetGenericArguments()[0].Name);
            }
            return Nicify(type.Name);
        }

        /// <summary>The last segment of the menu path: the title of the part in the inspector.</summary>
        public static string GetTitle(Type type)
        {
            var menu = GetMenu(type);
            var slash = menu.LastIndexOf('/');
            return slash >= 0 ? menu.Substring(slash + 1) : menu;
        }

        private static List<(Type, string)> Collect()
        {
            var result = new List<(Type, string)>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<ActorPart>())
            {
                if (type.IsAbstract || type.ContainsGenericParameters || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }
                var attribute = type.GetCustomAttribute<ActorPartMenuAttribute>();
                result.Add((type, attribute != null ? attribute.Menu : Nicify(type.Name)));
            }
            foreach (var type in TypeCache.GetTypesWithAttribute<ActorComponentAttribute>())
            {
                if (!type.IsValueType || !UnsafeUtility.IsUnmanaged(type))
                {
                    continue;
                }
                var menu = type.GetCustomAttribute<ActorComponentAttribute>().Menu;
                if (typeof(IComponentData).IsAssignableFrom(type))
                {
                    result.Add((typeof(ActorComponentPart<>).MakeGenericType(type), string.IsNullOrEmpty(menu) ? "Components/" + ObjectNames.NicifyVariableName(type.Name) : menu));
                }
                else if (typeof(IBufferElementData).IsAssignableFrom(type))
                {
                    result.Add((typeof(ActorBufferPart<>).MakeGenericType(type), string.IsNullOrEmpty(menu) ? "Buffers/" + ObjectNames.NicifyVariableName(type.Name) : menu));
                }
            }
            return result.OrderBy(entry => entry.Item2, StringComparer.Ordinal).ToList();
        }

        private static string Nicify(string typeName)
        {
            if (typeName.EndsWith("Part", StringComparison.Ordinal) && typeName.Length > 4)
            {
                typeName = typeName.Substring(0, typeName.Length - 4);
            }
            return ObjectNames.NicifyVariableName(typeName);
        }
    }
}
