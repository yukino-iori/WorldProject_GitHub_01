using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// アンテナ（同期対応）：クリックでZ軸45度まで回転、もう一回クリックで0度に戻る
/// Collider付きのantennaオブジェクトに付けてください。
/// ※Blender側でアンテナの原点（ピボット）を根元に置いておくこと
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RadioAntenna : UdonSharpBehaviour
{
    [Header("Sync")]
    [Tooltip("ON：アンテナの向きを全員に同期／OFF：クリックした本人だけ")]
    [SerializeField] private bool syncToEveryone = true;

    [Header("Rotation")]
    [SerializeField] private Vector3 rotateAxis = new Vector3(0f, 0f, 1f);
    [SerializeField] private float targetAngle = 45f;
    [Tooltip("回転にかかる時間（秒）")]
    [SerializeField] private float duration = 0.5f;

    [UdonSynced] private bool syncRaised = false;

    private Quaternion baseRotation;
    private bool isRaised = false;
    private float progress = 0f; // 0 = 0度, 1 = targetAngle
    private bool hasReceived = false;
    private bool started = false;

    private void Start()
    {
        baseRotation = transform.localRotation;
        started = true;
        ApplyAngle();
    }

    public override void Interact()
    {
        isRaised = !isRaised;

        if (syncToEveryone)
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            if (local != null && !Networking.IsOwner(gameObject))
            {
                Networking.SetOwner(local, gameObject);
            }
            syncRaised = isRaised;
            RequestSerialization();
        }
    }

    public override void OnDeserialization()
    {
        if (!syncToEveryone) return;
        if (Networking.IsOwner(gameObject)) return;

        bool first = !hasReceived;
        hasReceived = true;
        isRaised = syncRaised;

        // あとから入った人は、アニメなしで今の向きにそろえる
        if (first)
        {
            progress = isRaised ? 1f : 0f;
            if (started) ApplyAngle();
        }
    }

    private void Update()
    {
        if (!started) return;

        float target = isRaised ? 1f : 0f;
        if (Mathf.Approximately(progress, target)) return;

        float speed = 1f / Mathf.Max(duration, 0.01f);
        progress = Mathf.MoveTowards(progress, target, speed * Time.deltaTime);
        ApplyAngle();
    }

    private void ApplyAngle()
    {
        float angle = Mathf.SmoothStep(0f, targetAngle, progress);
        transform.localRotation = baseRotation * Quaternion.AngleAxis(angle, rotateAxis);
    }
}