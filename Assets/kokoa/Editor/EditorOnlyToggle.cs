using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System;

public class EditorOnlyToggle
{
    public const string VERSION = "0.1.0";
    // アイコンのGUIContent
    private static GUIContent iconContent;
    // Enable/DisableアイコンのためのGUIContent
    private static GUIContent enableIconContent;
    private static GUIContent disableIconContent;

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.hierarchyWindowItemOnGUI += HierarchyWindowItemOnGUI;
        // 可視性アイコンを使用
        iconContent = EditorGUIUtility.IconContent("d_CustomTool");
        // Enable/Disableアイコンを使用
        enableIconContent = EditorGUIUtility.IconContent("d_VisibilityOn");
        disableIconContent = EditorGUIUtility.IconContent("d_VisibilityOff");
    }

    private static void HierarchyWindowItemOnGUI(int instanceID, Rect selectionRect)
    {
        // 設定でトグルボタンが無効になっている場合は何も表示しない
        if (!KokoaToggleSettings.IsEnabled) return;
        
        var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
        if (obj == null) return;
        
        // UntaggedかEditorOnlyの場合のみボタンを表示
        if (obj.tag != "Untagged" && obj.tag != "EditorOnly") return;
        
        // Ctrlキーが押されている場合のみボタンを表示
        // if (!Event.current.control) return;
        
        // 表示するボタンの数を計算してボタン位置を調整
        int buttonCount = 0;
        if (KokoaToggleSettings.ShowEditorOnlyToggle) buttonCount++;
        if (KokoaToggleSettings.ShowActiveToggle) buttonCount++;
        
        if (buttonCount == 0) return; // ボタンが何も表示されない場合は終了
        
        var baseOffset = KokoaToggleSettings.ButtonOffsetFromRight;
        var buttonWidth = 20;
        var buttonSpacing = 20; // ボタン間の間隔

        var editorOnly = obj.tag == "EditorOnly";
        var isEnabled = obj.activeSelf;
        var inactiveAlpha = KokoaToggleSettings.InactiveButtonAlpha;
        var originalColor = GUI.color;
        
        // ボタンの位置を右から左へ計算（EditorOnly → Active の順）
        int currentButtonIndex = 0;
        
        // EditorOnlyボタンの表示（右端）
        if (KokoaToggleSettings.ShowEditorOnlyToggle)
        {
            var editorOnlyRect = new Rect(selectionRect);
            editorOnlyRect.x = selectionRect.xMax - baseOffset - (currentButtonIndex * buttonSpacing);
            editorOnlyRect.width = buttonWidth;
            
            GUI.color = editorOnly ? Color.white : new Color(0.5f, 0.5f, 0.5f, inactiveAlpha);
            
            var newEditorOnly = GUI.Toggle(editorOnlyRect, editorOnly, iconContent, GUI.skin.button);
            
            if (newEditorOnly != editorOnly)
            {
                ApplyTagToSelection(obj, newEditorOnly ? "EditorOnly" : "Untagged");
            }
            
            currentButtonIndex++;
        }
        
        // Activeボタンの表示（左側）
        if (KokoaToggleSettings.ShowActiveToggle)
        {
            var activeRect = new Rect(selectionRect);
            activeRect.x = selectionRect.xMax - baseOffset - (currentButtonIndex * buttonSpacing);
            activeRect.width = buttonWidth;
            
            var iconToUse = isEnabled ? enableIconContent : disableIconContent;
            GUI.color = isEnabled ? Color.white : new Color(0.5f, 0.5f, 0.5f, inactiveAlpha);
            
            var newIsEnabled = GUI.Toggle(activeRect, isEnabled, iconToUse, GUI.skin.button);
            
            if (newIsEnabled != isEnabled)
            {
                ApplyActiveStateToSelection(obj, newIsEnabled);
            }
        }
        
        // 色を元に戻す
        GUI.color = originalColor;
    }

    // 選択されたオブジェクトに対してタグを一括適用
    private static void ApplyTagToSelection(GameObject clickedObject, string newTag)
    {
        var selectedObjects = Selection.gameObjects;
        
        // 設定に応じてShiftキーの判定を行い、かつクリックしたオブジェクトが選択範囲に含まれている場合は、選択されたオブジェクト全てに適用
        bool shouldApplyToSelection = KokoaToggleSettings.RequireShiftForBulkOperation 
            ? (Event.current.shift && selectedObjects.Contains(clickedObject))
            : selectedObjects.Contains(clickedObject);
            
        if (shouldApplyToSelection)
        {
            Undo.RecordObjects(selectedObjects, "Change Tag");
            foreach (var obj in selectedObjects)
            {
                if (obj.tag == "Untagged" || obj.tag == "EditorOnly")
                {
                    obj.tag = newTag;
                }
            }
        }
        else
        {
            // 一括適用の条件を満たしていない場合は、そのオブジェクトのみに適用
            Undo.RecordObject(clickedObject, "Change Tag");
            clickedObject.tag = newTag;
        }
    }

    // 選択されたオブジェクトに対してアクティブ状態を一括適用
    private static void ApplyActiveStateToSelection(GameObject clickedObject, bool newActiveState)
    {
        var selectedObjects = Selection.gameObjects;
        
        // 設定に応じてShiftキーの判定を行い、かつクリックしたオブジェクトが選択範囲に含まれている場合は、選択されたオブジェクト全てに適用
        bool shouldApplyToSelection = KokoaToggleSettings.RequireShiftForBulkOperation 
            ? (Event.current.shift && selectedObjects.Contains(clickedObject))
            : selectedObjects.Contains(clickedObject);
            
        if (shouldApplyToSelection)
        {
            Undo.RecordObjects(selectedObjects, "Change Active State");
            foreach (var obj in selectedObjects)
            {
                if (obj.tag == "Untagged" || obj.tag == "EditorOnly")
                {
                    obj.SetActive(newActiveState);
                }
            }
        }
        else
        {
            // 一括適用の条件を満たしていない場合は、そのオブジェクトのみに適用
            Undo.RecordObject(clickedObject, "Change Active State");
            clickedObject.SetActive(newActiveState);
        }
    }
}
