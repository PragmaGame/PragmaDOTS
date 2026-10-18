using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pragma.Lockstep.Views.Editor
{
    /// <summary>
    /// Shows placed actors (<see cref="ActorAuthoring"/>, in scenes and in prefab mode) with the view their
    /// <see cref="ViewPart"/> binds in the <see cref="EntityViewConfig"/> assets of the project. The actor is built from its
    /// parts in a scratch world, and its components are handed to the view's parts, so the preview runs the view code of
    /// the game.
    /// </summary>
    /// <remarks>
    /// The preview is never saved. It is a child of the placed actor, except in an open subscene: baking would take the
    /// child for content of the map, so the preview lives hidden in a scene that is not baked and follows the actor there.
    /// Picking the preview selects the actor. Turn previews off with <i>Tools > Pragma > Lockstep > Actor View Previews</i>.
    /// </remarks>
    [InitializeOnLoad]
    internal static class ActorViewPreview
    {
        private const string ENABLED_KEY = "Pragma.Lockstep.ActorViewPreviews";
        private const string MENU = "Tools/Pragma/Lockstep/Actor View Previews";
        private const HideFlags PREVIEW_FLAGS = HideFlags.DontSave | HideFlags.NotEditable;
        private const HideFlags HOSTED_PREVIEW_FLAGS = HideFlags.HideAndDontSave;
        private const string PREVIEW_NAME = "View Preview";

        private static readonly MethodInfo GetComponentDataMethod = typeof(EntityManager).GetMethods()
            .First(method => method.Name == nameof(EntityManager.GetComponentData) && method.IsGenericMethodDefinition &&
                             method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(Entity));

        private static readonly MethodInfo GetBufferMethod = typeof(EntityManager).GetMethods()
            .First(method => method.Name == nameof(EntityManager.GetBuffer) && method.IsGenericMethodDefinition && method.GetParameters().Length == 2);

        private static readonly MethodInfo UpdateBufferMethod = typeof(EntityView).GetMethod(nameof(EntityView.UpdateBuffer));

        private static readonly Dictionary<ActorAuthoring, (EntityView Prefab, EntityView Instance)> Previews =
            new Dictionary<ActorAuthoring, (EntityView, EntityView)>();

        private static readonly HashSet<ActorAuthoring> Pending = new HashSet<ActorAuthoring>();
        private static readonly HashSet<EntityView> WarnedViews = new HashSet<EntityView>();
        private static World _world;

        static ActorViewPreview()
        {
            ActorAuthoring.EditorChanged += Schedule;
            ActorAuthoring.EditorDisabled += Remove;
            ActorDefinition.EditorChanged += _ => ScheduleAll();
            EditorApplication.projectChanged += ScheduleAll;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += FollowActors;
            Selection.selectionChanged += SelectActorOfPreview;
            AssemblyReloadEvents.beforeAssemblyReload += RemoveAll;
        }

        private static bool IsEnabled => EditorPrefs.GetBool(ENABLED_KEY, true);

        [MenuItem(MENU)]
        private static void Toggle()
        {
            EditorPrefs.SetBool(ENABLED_KEY, !IsEnabled);
            if (IsEnabled)
            {
                ScheduleAll();
            }
            else
            {
                RemoveAll();
            }
        }

        [MenuItem(MENU, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MENU, IsEnabled);
            return true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                RemoveAll();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                ScheduleAll();
            }
        }

        private static void Schedule(ActorAuthoring authoring)
        {
            Pending.Add(authoring);
            // Not from OnValidate or OnEnable themselves: objects cannot be created there.
            EditorApplication.delayCall -= Flush;
            EditorApplication.delayCall += Flush;
        }

        private static void ScheduleAll()
        {
            foreach (var authoring in Object.FindObjectsByType<ActorAuthoring>(FindObjectsInactive.Exclude))
            {
                Schedule(authoring);
            }
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                foreach (var authoring in stage.prefabContentsRoot.GetComponentsInChildren<ActorAuthoring>())
                {
                    Schedule(authoring);
                }
            }
        }

        private static void Flush()
        {
            var pending = Pending.ToArray();
            Pending.Clear();
            List<EntityViewConfig> configs = null;
            foreach (var authoring in pending)
            {
                Refresh(authoring, ref configs);
            }
        }

        private static void Refresh(ActorAuthoring authoring, ref List<EntityViewConfig> configs)
        {
            if (authoring == null || !authoring.isActiveAndEnabled || Application.isPlaying || !IsEnabled ||
                EditorUtility.IsPersistent(authoring) || !TryGetView(authoring, ref configs, out var prefab))
            {
                Remove(authoring);
                return;
            }

            if (!Previews.TryGetValue(authoring, out var preview) || preview.Instance == null || preview.Prefab != prefab)
            {
                Remove(authoring);
                RemoveStrays(authoring);
                var isHosted = authoring.gameObject.scene.isSubScene;
                var host = default(UnityEngine.SceneManagement.Scene);
                if (isHosted && !TryGetHostScene(out host))
                {
                    return;
                }
                var instance = isHosted ? Object.Instantiate(prefab) : Object.Instantiate(prefab, authoring.transform, false);
                instance.name = PREVIEW_NAME;
                if (isHosted)
                {
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.gameObject, host);
                    Follow(authoring, instance);
                }
                foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                {
                    transform.gameObject.hideFlags = isHosted ? HOSTED_PREVIEW_FLAGS : PREVIEW_FLAGS;
                }
                preview = (prefab, instance);
                Previews[authoring] = preview;
            }
            Push(authoring, preview.Instance);
        }

        // The view bound to the key of the actor's ViewPart in the view configs of the project.
        private static bool TryGetView(ActorAuthoring authoring, ref List<EntityViewConfig> configs, out EntityView prefab)
        {
            prefab = null;
            var definition = authoring.Definition;
            if (definition == null || !definition.TryGetKey(out var actorKey))
            {
                return false;
            }
            var parts = new List<ActorPart>();
            authoring.GetParts(parts);
            if (!ActorParts.TryGet(parts, out ViewPart viewPart))
            {
                return false;
            }
            var key = viewPart.GetKey(actorKey).ToString();

            configs ??= AssetDatabase.FindAssets("t:" + nameof(EntityViewConfig))
                .Select(guid => AssetDatabase.LoadAssetAtPath<EntityViewConfig>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(config => config != null)
                .ToList();
            foreach (var config in configs)
            {
                foreach (var binder in config.Binders)
                {
                    if (binder.Key == key && binder.View != null)
                    {
                        prefab = binder.View;
                        return true;
                    }
                }
            }
            return false;
        }

        // Builds the actor in the scratch world as baking would, and hands its components to the view's parts.
        private static void Push(ActorAuthoring authoring, EntityView view)
        {
            _world ??= new World("Actor View Preview", WorldFlags.Editor);
            var entityManager = _world.EntityManager;
            Entity entity;
            try
            {
                entity = LockstepActorUtility.CreatePlaced(entityManager, authoring, 0);
            }
            catch (Exception)
            {
                return;
            }

            using (var types = entityManager.GetComponentTypes(entity, Allocator.Temp))
            {
                foreach (var componentType in types)
                {
                    var type = TypeManager.GetType(componentType.TypeIndex);
                    if (type == null || componentType.IsZeroSized || !view.IsHasHandler(type))
                    {
                        continue;
                    }
                    try
                    {
                        if (componentType.IsBuffer)
                        {
                            var buffer = GetBufferMethod.MakeGenericMethod(type).Invoke(entityManager, new object[] { entity, true });
                            UpdateBufferMethod.MakeGenericMethod(type).Invoke(view, new[] { buffer });
                        }
                        else if (typeof(IComponentData).IsAssignableFrom(type))
                        {
                            view.UpdateData((IComponentData)GetComponentDataMethod.MakeGenericMethod(type).Invoke(entityManager, new object[] { entity }));
                        }
                    }
                    catch (Exception exception)
                    {
                        // A part written for bound views (one that reads View.Client, say) may not run in edit mode.
                        if (WarnedViews.Add(view))
                        {
                            Debug.LogWarning($"[Lockstep] The preview of '{authoring.name}' could not show {type.Name}: {exception.InnerException?.Message ?? exception.Message}", authoring);
                        }
                    }
                }
            }
            entityManager.DestroyEntity(entity);
        }

        private static void Remove(ActorAuthoring authoring)
        {
            if (authoring is null || !Previews.TryGetValue(authoring, out var preview))
            {
                return;
            }
            Previews.Remove(authoring);
            if (preview.Instance != null)
            {
                Object.DestroyImmediate(preview.Instance.gameObject);
            }
        }

        // A loaded scene that is not baked, to hold the previews of the actors of open subscenes.
        private static bool TryGetHostScene(out UnityEngine.SceneManagement.Scene host)
        {
            for (var i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                host = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (host.IsValid() && host.isLoaded && !host.isSubScene)
                {
                    return true;
                }
            }
            host = default;
            return false;
        }

        // A preview outside its actor's hierarchy takes the actor's pose on every editor update.
        private static void FollowActors()
        {
            foreach (var pair in Previews)
            {
                if (pair.Key != null && pair.Value.Instance != null && pair.Value.Instance.transform.parent == null)
                {
                    Follow(pair.Key, pair.Value.Instance);
                }
            }
        }

        private static void Follow(ActorAuthoring authoring, EntityView preview)
        {
            var source = authoring.transform;
            var target = preview.transform;
            target.SetPositionAndRotation(source.position, source.rotation);
            target.localScale = source.lossyScale;
        }

        // Picking the preview in the scene view selects the actor it shows.
        private static void SelectActorOfPreview()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                return;
            }
            var view = selected.GetComponentInParent<EntityView>(true);
            if (view == null)
            {
                return;
            }
            foreach (var pair in Previews)
            {
                if (pair.Value.Instance == view && pair.Key != null)
                {
                    Selection.activeGameObject = pair.Key.gameObject;
                    return;
                }
            }
        }

        // A duplicated actor copies the preview of the original as a plain child; it must not be saved with the copy.
        private static void RemoveStrays(ActorAuthoring authoring)
        {
            for (var i = authoring.transform.childCount - 1; i >= 0; i--)
            {
                var child = authoring.transform.GetChild(i);
                if (child.name == PREVIEW_NAME && child.GetComponent<EntityView>() != null)
                {
                    Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static void RemoveAll()
        {
            foreach (var authoring in Previews.Keys.ToArray())
            {
                Remove(authoring);
            }
            Previews.Clear();
            if (_world != null && _world.IsCreated)
            {
                _world.Dispose();
            }
            _world = null;
        }
    }
}
