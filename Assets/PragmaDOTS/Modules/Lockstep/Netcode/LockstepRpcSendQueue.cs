using Unity.Collections;
using Unity.Entities;
using Unity.Netcode;

namespace Pragma.Dots.Lockstep.Netcode
{
    /// <summary>
    /// Whether the lockstep packets a world sent as reliable RPCs have left it. Netcode serializes a send request into the
    /// outgoing RPC queue of its connection at the end of the simulation group (and destroys the request one frame later),
    /// and its RPC system hands the queue to the transport, which resends it while the connection lives.
    /// </summary>
    internal static class LockstepRpcSendQueue
    {
        /// <summary>
        /// No send request of <paramref name="requests"/> is left, and the outgoing RPC queue of every connection of
        /// <paramref name="connections"/> is empty.
        /// </summary>
        public static bool IsEmpty(EntityManager entityManager, EntityQuery requests, EntityQuery connections)
        {
            if (!requests.IsEmptyIgnoreFilter)
            {
                return false;
            }
            using (var entities = connections.ToEntityArray(Allocator.Temp))
            {
                foreach (var connection in entities)
                {
                    if (entityManager.HasBuffer<OutgoingRpcDataStreamBuffer>(connection) &&
                        entityManager.GetBuffer<OutgoingRpcDataStreamBuffer>(connection, true).Length > 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }
    }
}
