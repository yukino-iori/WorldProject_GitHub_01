
using System;
using System.Linq;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Data;
using VRC.SDK3.Rendering;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace Rasta.RenderTextureSyncerV2
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class RTS : UdonSharpBehaviour
    {
        [SerializeField] public RenderTexture TargetRenderTexture;
        [Space(10)]
        [SerializeField] public UdonBehaviour EventTriggerTarget;
        [Space(10)]
        [SerializeField] private CustomRenderTexture encoder;
        [SerializeField] private CustomRenderTexture decoder;
        [SerializeField] private Texture2D TempTexture;

        [UdonSynced] private int syncedSyncedDataCount = 0;

        private int segmentSize = 500;

        [HideInInspector] public RTSManager Manager;
        [HideInInspector] public int RTSId = -1;

        private int rtId = -1;
        private Color[] localPixels = new Color[0];
        private Color[][] segments = new Color[0][];
        private int sendingSegmentIndex = -1;
        private DataList timeoutSegmentIndexes = new DataList();
        private float downloadProgress = 0;

        private bool hasFirstData = false;
        private DataList joinedPlayers = new DataList();
        private bool isSyncing = false;


        private const float MIN_UPLOAD_SPEED = 100.0f;


        void Start()
        {
        }

        public void StoreAllPlayers()
        {
            rtId++;
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(Store), rtId);
        }

        [NetworkCallable]
        public void Store(int rtId)
        {
            this.rtId = rtId;
            addEncodeQueue();
        }

        private void addEncodeQueue()
        {
            if (Manager.EncodeQueue.Contains(this)) return;

            Manager.EncodeQueue.Add(this);
            RTS rts = (RTS)Manager.EncodeQueue[0].Reference;
            if (rts == this)
            {
                rts.Encode();
            }
        }

        public void Encode()
        {
            encoder.material.mainTexture = TargetRenderTexture;
            encoder.Update();
            SendCustomEventDelayedFrames(nameof(GetRenderTexturePixels), 3);
        }

        public void GetRenderTexturePixels()
        {
            VRCAsyncGPUReadback.Request(encoder, 0, (IUdonEventReceiver)this);
        }

        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
        {
            if (!request.hasError)
            {
                Color[] pixels = new Color[encoder.width * encoder.height];
                if (request.TryGetData(pixels))
                {
                    localPixels = pixels;
                    hasFirstData = true;

                    if (Networking.IsOwner(gameObject))
                    {
                        syncedSyncedDataCount = localPixels.Length;
                        RequestSerialization();
                    }

                    addSyncList();
                }
            }

            if ((RTS)Manager.EncodeQueue[0].Reference == this)
            {
                Manager.EncodeQueue.RemoveAt(0);

                if (Manager.EncodeQueue.Count > 0)
                {
                    ((RTS)Manager.EncodeQueue[0].Reference).Encode();
                }
            }
        }

        public void Clear()
        {
            Manager.SyncList.Remove(this);
            Manager.SyncQueue.RemoveAll(this);
            localPixels = new Color[0];

            if (Networking.IsOwner(gameObject))
            {
                syncedSyncedDataCount = 0;
                RequestSerialization();
            }
        }

        private void addSyncList()
        {
            Manager.SyncList.Remove(this);
            Manager.SyncList.Add(this);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            joinedPlayers.Add(player.playerId);
            SendCustomEventDelayedSeconds(nameof(LateOnPlayerJoined), 1.0f);
        }

        public void LateOnPlayerJoined()
        {
            VRCPlayerApi player = VRCPlayerApi.GetPlayerById(joinedPlayers[0].Int);
            joinedPlayers.RemoveAt(0);
            if (!Utilities.IsValid(player) || player.isLocal) return;

            if (
                Networking.IsOwner(gameObject) &&
                Manager.SyncList.Contains(this)
                )
            {
                if ((RTS)Manager.SyncList[0].Reference == this)
                {
                    bool isAlreadySyncing = Manager.SyncQueue.Count > 0;

                    addSyncQueue();

                    if (!isAlreadySyncing)
                    {
                        Sync();
                        return;
                    }
                }

                SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(HandleWaitStartDownload), player.playerId);
            }
        }

        private void addSyncQueue()
        {
            if (Manager.SyncQueue.Count == 0)
            {
                Manager.SyncQueue.AddRange(Manager.SyncList.DeepClone());
            }
            else
            {
                DataList compareSyncQueue = Manager.SyncQueue.GetRange(1, Manager.SyncQueue.Count - 1);

                if (compareSyncQueue.Count == 0)
                {
                    Manager.SyncQueue.AddRange(Manager.SyncList.DeepClone());
                }
                else
                {
                    for (int i = 0; i < Manager.SyncList.Count; i++)
                    {
                        RTS rts = (RTS)Manager.SyncList[i].Reference;

                        if (!compareSyncQueue.Contains(rts))
                        {
                            Manager.SyncQueue.Add(rts);
                        }
                    }
                }
            }

            //string syncQueueLog = "SyncQueue[" + Manager.SyncQueue.Count + "] : ";
            //for (int i = 0; i < Manager.SyncQueue.Count; i++)
            //{
            //    RTS rts = (RTS)Manager.SyncQueue[i].Reference;
            //    syncQueueLog += rts.transform.parent.parent.name + ",";
            //}
            //debugLog(syncQueueLog);
        }

        [NetworkCallable]
        public void HandleWaitStartDownload(int playerId)
        {
            if (Networking.LocalPlayer.playerId == playerId)
            {
                RTSHandleWaitStartDownload();
            }
        }

        public void Sync()
        {
            if (VRCPlayerApi.GetPlayerCount() >= 2)
            {
                int totalSegmentCount = Mathf.CeilToInt(localPixels.Length / (float)segmentSize);
                segments = new Color[totalSegmentCount][];
                for (int i = 0; i < totalSegmentCount; i++)
                {
                    int start = i * segmentSize;
                    int end = Mathf.Min(start + segmentSize, localPixels.Length);
                    int length = end - start;
                    segments[i] = new Color[length];
                    Array.Copy(localPixels, start, segments[i], 0, length);
                }

                sendingSegmentIndex = -1;
                sendNextSegment();
            }
            else
            {
                if (Manager.SyncQueue.Count >= 1 && (RTS)Manager.SyncQueue[0].Reference == this)
                {
                    Manager.SyncQueue.RemoveAt(0);
                }
                if (Manager.SyncQueue.Count >= 1)
                {
                    RTS rts = ((RTS)Manager.SyncQueue[0].Reference);
                    if (!Networking.IsOwner(rts.gameObject))
                    {
                        Networking.SetOwner(Networking.LocalPlayer, rts.gameObject);
                    }
                    rts.Sync();
                }
            }
        }

        private void sendNextSegment()
        {
            sendingSegmentIndex++;

            bool endSyncLoop = sendingSegmentIndex >= segments.Length || VRCPlayerApi.GetPlayerCount() == 1 || localPixels.Length == 0;

            if (endSyncLoop)
            {
                if (localPixels.Length == 0)
                {
                    SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(GetSegment), -1, -1, -1, new Color[0]);
                }

                if (Manager.SyncQueue.Count >= 1 && (RTS)Manager.SyncQueue[0].Reference == this)
                {
                    Manager.SyncQueue.RemoveAt(0);
                }
                if (Manager.SyncQueue.Count >= 1)
                {
                    RTS rts = ((RTS)Manager.SyncQueue[0].Reference);
                    if (!Networking.IsOwner(rts.gameObject))
                    {
                        Networking.SetOwner(Networking.LocalPlayer, rts.gameObject);
                    }
                    rts.Sync();
                }
            }
            else
            {
                SendCustomNetworkEvent(
                    NetworkEventTarget.Others, nameof(GetSegment),
                    rtId,
                    sendingSegmentIndex,
                    segments.Length,
                    segments[sendingSegmentIndex]
                );

                timeoutSegmentIndexes.Add(sendingSegmentIndex);
                float timeoutTime = segments[sendingSegmentIndex].Length / MIN_UPLOAD_SPEED;
                SendCustomEventDelayedSeconds(nameof(Timeout), timeoutTime);
            }
        }

        public void Timeout()
        {
            int segmentIndex = timeoutSegmentIndexes[0].Int;
            timeoutSegmentIndexes.RemoveAt(0);
            GetACK(segmentIndex);
        }


        // Remote Player
        [NetworkCallable]
        public void GetSegment(int rtId, int id, int totalSegmentCount, Color[] segment)
        {
            if (segment.Length == 0)
            {
                isSyncing = false;
                return;
            }
            else
            {
                isSyncing = id < totalSegmentCount - 1;
            }

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(GetACK), id);

            // 送られてきたデータは表示する必要があるか
            if (rtId <= this.rtId) return;

            if (!hasFirstData)
            {
                if (id == 0)
                {
                    hasFirstData = true;
                }
                else
                {
                    return;
                }
            }

            if (localPixels.Length < id * segmentSize + segment.Length)
            {
                Color[] newLocalPixels = new Color[id * segmentSize + segment.Length];
                Array.Copy(localPixels, newLocalPixels, localPixels.Length);
                localPixels = newLocalPixels;
            }
            Array.Copy(segment, 0, localPixels, id * segmentSize, segment.Length);
            downloadProgress = (float)(id + 1) / totalSegmentCount;
            RTSHandleDownloading();

            if (id == totalSegmentCount - 1)
            {
                this.rtId = rtId;
                addSyncList();
                addDecodeQueue();
                RTSHandleFinishDownload();
            }
        }

        [NetworkCallable]
        public void GetACK(int segmentIndex)
        {
            //debugLog("SendedSegment sendingSegmentIndex:" + sendingSegmentIndex + " segmentIndex:" + segmentIndex + " forceStop:" + forceStop);
            if (sendingSegmentIndex == segmentIndex)
            {
                sendNextSegment();
            }
        }



        private void addDecodeQueue()
        {
            if (Manager.DecodeQueue.Contains(this)) return;
            Manager.DecodeQueue.Add(this);
            RTS rts = (RTS)Manager.DecodeQueue[0].Reference;
            if (rts == this)
            {
                rts.Decode();
            }
        }

        public void Decode()
        {
            TempTexture.SetPixels(localPixels);
            TempTexture.Apply();
            decoder.Update();
            SendCustomEventDelayedFrames(nameof(TransferPixel), 3);
        }

        public void TransferPixel()
        {
            if (TargetRenderTexture)
                VRCGraphics.Blit(decoder, TargetRenderTexture);

            if ((RTS)Manager.DecodeQueue[0].Reference == this)
            {
                Manager.DecodeQueue.RemoveAt(0);
                if (Manager.DecodeQueue.Count > 0)
                {
                    ((RTS)Manager.DecodeQueue[0].Reference).Decode();
                }
            }
        }

        public float GetDownloadProgress()
        {
            return downloadProgress;
        }


        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (player.isLocal)
            {
                VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
                VRCPlayerApi.GetPlayers(players);
                if (Networking.LocalPlayer.playerId != players[0].playerId)
                {
                    Networking.SetOwner(players[0], gameObject);
                    return;
                }

                if (syncedSyncedDataCount > 0)
                {
                    if (localPixels.Length == syncedSyncedDataCount)
                    {
                        if (!Manager.SyncList.Contains(this))
                        {
                            Manager.SyncList.Add(this);
                        }

                        if ((RTS)Manager.SyncList[0].Reference == this)
                        {
                            bool isAlreadySyncing = Manager.SyncQueue.Count > 0;

                            addSyncQueue();

                            if (!isAlreadySyncing)
                            {
                                Sync();
                            }
                            else
                            {
                                SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(HandleWaitStartDownload), player.playerId);
                            }
                        }
                        else
                        {
                            SendCustomNetworkEvent(NetworkEventTarget.Others, nameof(HandleWaitStartDownload), player.playerId);
                        }

                    }
                    else
                    {
                        // error
                        syncedSyncedDataCount = -1;
                        RequestSerialization();

                        RTSHandleDownloadError();
                    }
                }
            }
        }

        public override void OnDeserialization()
        {
            if (syncedSyncedDataCount == -1 && localPixels.Length == 0)
            {
                RTSHandleDownloadError();
            }
        }



        // global
        // 1.a
        public void RTSHandleWaitStartDownload()
        {
            if (Utilities.IsValid(EventTriggerTarget))
            {
                EventTriggerTarget.SendCustomEvent("OnRTSWaitStartDownload");
            }
        }

        // local
        // 1.b
        public void RTSHandleDownloading()
        {
            if (Utilities.IsValid(EventTriggerTarget))
            {
                EventTriggerTarget.SendCustomEvent("OnRTSDownloading");
            }
        }

        // local
        // 2.a
        public void RTSHandleFinishDownload()
        {
            if (Utilities.IsValid(EventTriggerTarget))
            {
                EventTriggerTarget.SendCustomEvent("OnRTSFinishDownload");
            }
        }

        // local?
        // 2.b
        public void RTSHandleDownloadError()
        {
            if (Utilities.IsValid(EventTriggerTarget))
            {
                EventTriggerTarget.SendCustomEvent("OnRTSDownloadError");
            }
        }

        // local
        // 3
        public void RTSHandleClearData()
        {
            if (Utilities.IsValid(EventTriggerTarget))
            {
                EventTriggerTarget.SendCustomEvent("OnRTSClearData");
            }
        }
    }
}
