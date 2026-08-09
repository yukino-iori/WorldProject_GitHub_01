
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;
using VRC.Udon;

namespace Rasta.RenderTextureSyncerV2
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class RTSManager : UdonSharpBehaviour
    {
        [HideInInspector] public DataList SyncList = new DataList();
        [HideInInspector] public DataList SyncQueue = new DataList();
        [HideInInspector] public DataList EncodeQueue = new DataList();
        [HideInInspector] public DataList DecodeQueue = new DataList();
    }
}