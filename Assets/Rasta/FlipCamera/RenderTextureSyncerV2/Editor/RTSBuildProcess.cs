using Rasta.RenderTextureSyncerV2;
using System.Collections;
using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rasta.RenderTextureSyncerV2
{

    public class RTSBuildProcess : IProcessSceneWithReport
    {
        public int callbackOrder => 0;


        public void OnProcessScene(Scene scene, BuildReport report)
        {
            //GameObject managerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Rasta/FlipCamera/RenderTextureSyncerV2/Prefab/RTSManager.prefab");
            //GameObject manager = PrefabUtility.InstantiatePrefab(managerPrefab) as GameObject;

            RTSManager rtsManager = new GameObject("RTSManager").AddUdonSharpComponent<RTSManager>();

            RTS[] rtss = Object.FindObjectsByType<RTS>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < rtss.Length; i++)
            {
                rtss[i].Manager = rtsManager;
                rtss[i].RTSId = i;
            }
        }
    }
}