using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// クリックされたら RetroRadio のイベントを呼ぶボタン
/// knob_on_off → Event Name: TogglePower
/// knob_L      → Event Name: ToggleMode
/// Collider付きのつまみオブジェクトに付けてください。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RadioButton : UdonSharpBehaviour
{
    [SerializeField] private UdonSharpBehaviour radio;
    [SerializeField] private string eventName = "TogglePower";

    public override void Interact()
    {
        if (radio == null) return;
        radio.SendCustomEvent(eventName);
    }
}