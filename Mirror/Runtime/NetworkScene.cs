using System.Collections.Generic;
using UnityEngine;

namespace Mirror
{
    // This is an internal class to allow the client and server to share scene-related functionality.
    // This code (mostly) used to be in ClientScene.
    internal class NetworkScene
    {
        // localObjects is NOT static. For the Host, even though there is one scene and gameObjects are
        // shared with the localClient, the set of active objects for each must be separate to prevent
        // out-of-order object initialization problems.
        readonly Dictionary<NetworkInstanceId, NetworkIdentity> m_LocalObjects = new Dictionary<NetworkInstanceId, NetworkIdentity>();

        static Dictionary<NetworkHash128, GameObject> s_GuidToPrefab = new Dictionary<NetworkHash128, GameObject>();
        static Dictionary<NetworkHash128, SpawnDelegate> s_SpawnHandlers = new Dictionary<NetworkHash128, SpawnDelegate>();
        static Dictionary<NetworkHash128, UnSpawnDelegate> s_UnspawnHandlers = new Dictionary<NetworkHash128, UnSpawnDelegate>();

        internal Dictionary<NetworkInstanceId, NetworkIdentity> localObjects { get { return m_LocalObjects; }}

        internal static Dictionary<NetworkHash128, GameObject> guidToPrefab { get { return s_GuidToPrefab; }}
        internal static Dictionary<NetworkHash128, SpawnDelegate> spawnHandlers { get { return s_SpawnHandlers; }}
        internal static Dictionary<NetworkHash128, UnSpawnDelegate> unspawnHandlers { get { return s_UnspawnHandlers; }}

        internal void Shutdown()
        {
            ClearLocalObjects();
            ClearSpawners();
        }

        internal void SetLocalObject(NetworkInstanceId netId, GameObject obj, bool isClient, bool isServer)
        {
            if (LogFilter.logDev) { Debug.Log("SetLocalObject " + netId + " " + obj); }

            if (obj == null)
            {
                m_LocalObjects[netId] = null;
                return;
            }

            NetworkIdentity foundNetworkIdentity = null;
            if (m_LocalObjects.ContainsKey(netId))
            {
                foundNetworkIdentity = m_LocalObjects[netId];
            }

            if (foundNetworkIdentity == null)
            {
                foundNetworkIdentity = obj.GetComponent<NetworkIdentity>();
                m_LocalObjects[netId] = foundNetworkIdentity;
            }

            foundNetworkIdentity.UpdateClientServer(isClient, isServer);
        }

        // this lets the client take an instance ID from the server and find
        // the local object that it corresponds too. This is temporary until
        // object references can be serialized transparently.
        internal GameObject FindLocalObject(NetworkInstanceId netId)
        {
            NetworkIdentity identity;
            if (GetNetworkIdentity(netId, out identity))
            {
                return identity.gameObject;
            }
            return null;
        }

        internal bool GetNetworkIdentity(NetworkInstanceId netId, out NetworkIdentity uv)
        {
            return m_LocalObjects.TryGetValue(netId, out uv) && uv != null;
        }

        internal bool RemoveLocalObject(NetworkInstanceId netId)
        {
            return m_LocalObjects.Remove(netId);
        }

        internal bool RemoveLocalObjectAndDestroy(NetworkInstanceId netId)
        {
            if (m_LocalObjects.ContainsKey(netId))
            {
                NetworkIdentity localObject = m_LocalObjects[netId];
                Object.Destroy(localObject.gameObject);
                return m_LocalObjects.Remove(netId);
            }
            return false;
        }

        internal void ClearLocalObjects()
        {
            m_LocalObjects.Clear();
        }

        internal static void RegisterPrefab(GameObject prefab, NetworkHash128 newAssetId)
        {
            NetworkIdentity view = prefab.GetComponent<NetworkIdentity>();
            if (view)
            {
                view.SetDynamicAssetId(newAssetId);

                if (LogFilter.logDebug) { Debug.Log("Registering prefab '" + prefab.name + "' as asset:" + view.assetId); }
                s_GuidToPrefab[view.assetId] = prefab;
            }
            else
            {
                if (LogFilter.logError) { Debug.LogError("Could not register '" + prefab.name + "' since it contains no NetworkIdentity component"); }
            }
        }

        internal static void RegisterPrefab(GameObject prefab)
        {
            NetworkIdentity view = prefab.GetComponent<NetworkIdentity>();
            if (view)
            {
                if (LogFilter.logDebug) { Debug.Log("Registering prefab '" + prefab.name + "' as asset:" + view.assetId); }
                s_GuidToPrefab[view.assetId] = prefab;

                var uvs = prefab.GetComponentsInChildren<NetworkIdentity>();
                if (uvs.Length > 1)
                {
                    if (LogFilter.logWarn)
                    {
                        Debug.LogWarning("The prefab '" + prefab.name +
                            "' has multiple NetworkIdentity components. There can only be one NetworkIdentity on a prefab, and it must be on the root object.");
                    }
                }
            }
            else
            {
                if (LogFilter.logError) { Debug.LogError("Could not register '" + prefab.name + "' since it contains no NetworkIdentity component"); }
            }
        }

        internal static bool GetPrefab(NetworkHash128 assetId, out GameObject prefab)
        {
            prefab = null;
            if (assetId.IsValid() && guidToPrefab.ContainsKey(assetId) && guidToPrefab[assetId] != null)
            {
                prefab = guidToPrefab[assetId];
                return true;
            }
            return false;
        }

        internal static void ClearSpawners()
        {
            s_GuidToPrefab.Clear();
            s_SpawnHandlers.Clear();
            s_UnspawnHandlers.Clear();
        }

        public static void UnregisterSpawnHandler(NetworkHash128 assetId)
        {
            s_SpawnHandlers.Remove(assetId);
            s_UnspawnHandlers.Remove(assetId);
        }

        internal static void RegisterSpawnHandler(NetworkHash128 assetId, SpawnDelegate spawnHandler, UnSpawnDelegate unspawnHandler)
        {
            if (spawnHandler == null || unspawnHandler == null)
            {
                if (LogFilter.logError) { Debug.LogError("RegisterSpawnHandler custom spawn function null for " + assetId); }
                return;
            }

            if (LogFilter.logDebug) { Debug.Log("RegisterSpawnHandler asset '" + assetId + "' " + spawnHandler.GetMethodName() + "/" + unspawnHandler.GetMethodName()); }

            s_SpawnHandlers[assetId] = spawnHandler;
            s_UnspawnHandlers[assetId] = unspawnHandler;
        }

        internal static void UnregisterPrefab(GameObject prefab)
        {
            NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                if (LogFilter.logError) { Debug.LogError("Could not unregister '" + prefab.name + "' since it contains no NetworkIdentity component"); }
                return;
            }
            s_SpawnHandlers.Remove(identity.assetId);
            s_UnspawnHandlers.Remove(identity.assetId);
        }

        internal static void RegisterPrefab(GameObject prefab, SpawnDelegate spawnHandler, UnSpawnDelegate unspawnHandler)
        {
            NetworkIdentity identity = prefab.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                if (LogFilter.logError) { Debug.LogError("Could not register '" + prefab.name + "' since it contains no NetworkIdentity component"); }
                return;
            }

            if (spawnHandler == null || unspawnHandler == null)
            {
                if (LogFilter.logError) { Debug.LogError("RegisterPrefab custom spawn function null for " + identity.assetId); }
                return;
            }

            if (!identity.assetId.IsValid())
            {
                if (LogFilter.logError) { Debug.LogError("RegisterPrefab game object " + prefab.name + " has no prefab. Use RegisterSpawnHandler() instead?"); }
                return;
            }

            if (LogFilter.logDebug) { Debug.Log("Registering custom prefab '" + prefab.name + "' as asset:" + identity.assetId + " " + spawnHandler.GetMethodName() + "/" + unspawnHandler.GetMethodName()); }

            s_SpawnHandlers[identity.assetId] = spawnHandler;
            s_UnspawnHandlers[identity.assetId] = unspawnHandler;
        }

        internal static bool GetSpawnHandler(NetworkHash128 assetId, out SpawnDelegate handler)
        {
            if (s_SpawnHandlers.ContainsKey(assetId))
            {
                handler = s_SpawnHandlers[assetId];
                return true;
            }
            handler = null;
            return false;
        }

        internal static bool InvokeUnSpawnHandler(NetworkHash128 assetId, GameObject obj)
        {
            if (s_UnspawnHandlers.ContainsKey(assetId) && s_UnspawnHandlers[assetId] != null)
            {
                UnSpawnDelegate handler = s_UnspawnHandlers[assetId];
                handler(obj);
                return true;
            }
            return false;
        }

        internal void DestroyAllClientObjects()
        {
            foreach (var netId in m_LocalObjects.Keys)
            {
                NetworkIdentity uv = m_LocalObjects[netId];

                if (uv != null && uv.gameObject != null)
                {
                    if (!InvokeUnSpawnHandler(uv.assetId, uv.gameObject))
                    {
                        if (uv.sceneId.IsEmpty())
                        {
                            Object.Destroy(uv.gameObject);
                        }
                        else
                        {
                            uv.MarkForReset();
                            uv.gameObject.SetActive(false);
                        }
                    }
                }
            }
            ClearLocalObjects();
        }

        internal void DumpAllClientObjects()
        {
            foreach (var netId in m_LocalObjects.Keys)
            {
                NetworkIdentity uv = m_LocalObjects[netId];
                if (uv != null)
                    Debug.Log("ID:" + netId + " OBJ:" + uv.gameObject + " AS:" + uv.assetId);
                else
                    Debug.Log("ID:" + netId + " OBJ: null");
            }
        }
    }
}
