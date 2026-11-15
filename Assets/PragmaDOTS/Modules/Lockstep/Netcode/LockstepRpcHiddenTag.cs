using Pragma.Dots.Utils;
using Unity.Burst;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// The editor's hidden tag for the request entities of the lockstep RPCs, so that the server and client worlds do not
    /// flicker in the Hierarchy window with every packet.
    /// </summary>
    /// <remarks>
    /// Netcode sends a request in the frame it is made but destroys it through the next frame's begin command buffer, and
    /// makes a received request through the begin command buffer of the next frame while the transport system destroys it
    /// later in that frame; the window takes its snapshot inside the world update and may see either. The tag is looked up
    /// by name (Entities declares it internal) and kept in a <see cref="SharedStatic{T}"/> for the Burst function pointers
    /// that make received requests, where it cannot be looked up; it is Null outside the editor.
    /// </remarks>
    internal static class LockstepRpcHiddenTag
    {
        private struct Key
        {
        }

        private static readonly SharedStatic<TypeIndex> Index = SharedStatic<TypeIndex>.GetOrCreate<Key>();

        /// <summary>Looks the tag up for received requests; the transport systems call it when they are created.</summary>
        public static void Initialize()
        {
            Index.Data = EntityHierarchy.TryGetHiddenTag(out var hidden) ? hidden.TypeIndex : TypeIndex.Null;
        }

        /// <summary>Hides a received request made through the command buffer of the RPC system.</summary>
        public static void HideReceived(ref RpcExecutor.Parameters parameters, Entity request)
        {
            var hidden = Index.Data;
            if (hidden != TypeIndex.Null)
            {
                parameters.CommandBuffer.AddComponent(parameters.JobIndex, request, ComponentType.FromTypeIndex(hidden));
            }
        }

        /// <summary>The archetype of a send request of <typeparamref name="T"/>, hidden in the editor.</summary>
        public static EntityArchetype CreateRequestArchetype<T>(EntityManager entityManager) where T : unmanaged, IComponentData
        {
            var packet = ComponentType.ReadWrite<T>();
            var request = ComponentType.ReadWrite<SendRpcCommandRequest>();
            if (EntityHierarchy.TryGetHiddenTag(out var hidden))
            {
                return entityManager.CreateArchetype(packet, request, hidden);
            }
            return entityManager.CreateArchetype(packet, request);
        }
    }
}
