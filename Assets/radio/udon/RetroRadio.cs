using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// レトロラジオ本体（同期対応）
///
/// ・TogglePower：電源ON/OFF
/// ・ToggleMode ：通常再生（順番）⇔ ランダム再生
/// ・noise_A：電源ON中、曲にずっと薄く重ねるノイズ
/// ・noise_B：曲のつなぎ目でフェードイン/アウトするノイズ
/// ・knob_on_off：OFF=-24度 / ON=24度 にカチッと切り替え
/// ・knob_L：クリック時・曲の切り替え時に左右に回してチューニング演出
/// ・Sync To Everyone：ONでその場の全員に同じ曲が流れる
///
/// 【初回起動】
/// 最初にワールドへ入ったプレイヤーがラジオを自動的にONにします。
///
/// 【途中参加】
/// 後からワールドへ入ったプレイヤーは、現在の電源状態・
/// 再生中の曲・曲の再生位置に合わせて同期されます。
///
/// radio（ルート）に付けてください。
/// クリック処理は RadioButton から呼ばれます。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class RetroRadio : UdonSharpBehaviour
{
    [Header("Sync")]
    [Tooltip("ON：その場の全員に同じ曲が流れる／OFF：クリックした本人だけに流れる")]
    [SerializeField] private bool syncToEveryone = true;


    [Header("Audio Sources")]
    [Tooltip("音楽用（Loopオフ）")]
    [SerializeField] private AudioSource musicSource;

    [Tooltip("noise_A用（常時うっすら）")]
    [SerializeField] private AudioSource noiseASource;

    [Tooltip("noise_B用（曲のつなぎ目）")]
    [SerializeField] private AudioSource noiseBSource;

    [Tooltip("つまみのクリック音用（任意）")]
    [SerializeField] private AudioSource sfxSource;


    [Header("Music（全10曲・上から順に通常再生）")]
    [Tooltip("好きな音源をドラッグ＆ドロップで差し替えできます")]
    [SerializeField] private AudioClip[] musicClips = new AudioClip[10];


    [Header("Noise / SE")]
    [SerializeField] private AudioClip noiseAClip;
    [SerializeField] private AudioClip noiseBClip;
    [SerializeField] private AudioClip knobClickClip;

    [Tooltip("電源つまみでクリック音を鳴らす")]
    [SerializeField] private bool clickOnPower = true;

    [Tooltip("チューニングつまみでクリック音を鳴らす")]
    [SerializeField] private bool clickOnTune = false;


    [Header("Volume")]
    [Range(0f, 1f)]
    [SerializeField] private float musicVolume = 0.8f;

    [Tooltip("曲に常に重ねるnoise_Aの音量")]
    [Range(0f, 1f)]
    [SerializeField] private float noiseAVolume = 0.03f;

    [Tooltip("つなぎ目のnoise_Bの最大音量")]
    [Range(0f, 1f)]
    [SerializeField] private float noiseBVolume = 0.2f;


    [Header("Timing（秒）")]
    [Tooltip("曲間でnoise_Bだけが鳴る時間（最短）")]
    [SerializeField] private float gapMin = 1.0f;

    [Tooltip("曲間でnoise_Bだけが鳴る時間（最長）")]
    [SerializeField] private float gapMax = 2.0f;

    [Tooltip("次の曲のフェードイン＆noise_Bフェードアウトの時間")]
    [SerializeField] private float fadeInTime = 1.0f;

    [Tooltip("曲の終わり何秒前からnoise_Bを重ねるか")]
    [SerializeField] private float outroTime = 1.5f;

    [Tooltip("モード切替時に今の曲を消す時間")]
    [SerializeField] private float switchFadeTime = 0.4f;

    [Tooltip("電源ON/OFF時のフェード時間")]
    [SerializeField] private float powerFadeTime = 0.6f;


    [Header("Power Knob（knob_on_off）")]
    [SerializeField] private Transform powerKnob;
    [SerializeField] private Vector3 powerKnobAxis = new Vector3(0f, 0f, 1f);
    [SerializeField] private float powerOffAngle = -24f;
    [SerializeField] private float powerOnAngle = 24f;

    [Tooltip("カチッと切り替わる時間（短いほどキビキビ）")]
    [SerializeField] private float powerSnapTime = 0.08f;


    [Header("Tune Knob（knob_L）")]
    [SerializeField] private Transform tuneKnob;
    [SerializeField] private Vector3 tuneKnobAxis = new Vector3(0f, 0f, 1f);

    [Tooltip("左右に振る角度")]
    [SerializeField] private float tuneAngle = 40f;

    [Tooltip("左右に振る回数")]
    [SerializeField] private int tuneCycles = 3;

    [Tooltip("チューニング演出の全体時間")]
    [SerializeField] private float tuneDuration = 2.7f;

    [Tooltip("曲が自動で切り替わるときもチューニング演出をする")]
    [SerializeField] private bool tuneOnSongChange = true;

    [Tooltip("電源ONのときもチューニング演出をする")]
    [SerializeField] private bool tuneOnPowerOn = false;


    // ============================================================
    // 同期データ
    // ============================================================

    [UdonSynced] private bool syncPowerOn = false;
    [UdonSynced] private bool syncRandom = false;
    [UdonSynced] private int syncIndex = -1;
    [UdonSynced] private int syncSongStartMs = 0;
    [UdonSynced] private int syncSongSeq = 0;
    [UdonSynced] private int syncModeCount = 0;

    // 初回の自動ONがすでに行われたか
    [UdonSynced] private bool syncInitialized = false;


    // ============================================================
    // 再生フェーズ
    // ============================================================

    private const int PHASE_IDLE = 0;
    private const int PHASE_GAP = 1;
    private const int PHASE_MUSIC_IN = 2;
    private const int PHASE_PLAYING = 3;
    private const int PHASE_OUTRO = 4;
    private const int PHASE_STOPPING = 5;


    private bool isPowerOn = false;
    private bool isRandom = false;

    private int phase = PHASE_IDLE;
    private float phaseTimer = 0f;
    private float gapDuration = 0f;

    private float musicFrom = 0f;
    private float noiseBFrom = 0f;

    private int currentIndex = -1;

    private int lastSongSeq = 0;
    private int lastModeCount = 0;

    private bool hasReceived = false;
    private bool started = false;
    private bool pendingDeserialization = false;


    // ============================================================
    // つまみアニメーション
    // ============================================================

    private Quaternion powerKnobBase;
    private Quaternion tuneKnobBase;

    private bool powerAnimating = false;
    private float powerAnimT = 0f;
    private float powerFromAngle = 0f;
    private float powerToAngle = 0f;
    private float powerCurrentAngle = 0f;

    private bool tuneAnimating = false;
    private float tuneAnimT = 0f;


    // ============================================================
    // Start
    // ============================================================

    private void Start()
    {
        InitSource(musicSource, false);
        InitSource(noiseASource, true);
        InitSource(noiseBSource, true);


        // 電源つまみをOFF位置にする
        if (powerKnob != null)
        {
            powerKnobBase = powerKnob.localRotation;

            powerCurrentAngle = powerOffAngle;

            SetKnobAngle(
                powerKnob,
                powerKnobBase,
                powerKnobAxis,
                powerOffAngle
            );
        }


        // チューニングつまみの初期回転を保存
        if (tuneKnob != null)
        {
            tuneKnobBase = tuneKnob.localRotation;
        }


        started = true;


        // ========================================================
        // 初回起動
        //
        // まだ一度も初期化されておらず、
        // 自分がこのオブジェクトのOwnerなら、
        // ラジオを自動的にONにする。
        // ========================================================

        if (syncToEveryone &&
            Networking.IsOwner(gameObject) &&
            !syncInitialized)
        {
            syncInitialized = true;

            // ラジオをON
            ApplyPower(true, false);

            // 同期情報を設定
            syncPowerOn = true;
            syncRandom = false;

            // 初期化状態を全員に送信
            RequestSerialization();
        }


        // Startより先に同期データが届いていた場合
        if (pendingDeserialization)
        {
            pendingDeserialization = false;
            OnDeserialization();
        }
    }


    // ============================================================
    // Audio Source 初期化
    // ============================================================

    private void InitSource(AudioSource src, bool loop)
    {
        if (src == null) return;

        src.playOnAwake = false;
        src.loop = loop;
        src.volume = 0f;
        src.Stop();
    }


    private bool SourcesReady()
    {
        return musicSource != null &&
               noiseASource != null &&
               noiseBSource != null;
    }


    // ============================================================
    // 電源ボタン
    // ============================================================

    public void TogglePower()
    {
        if (!SourcesReady()) return;


        TakeOwnership();


        ApplyPower(!isPowerOn, true);


        if (syncToEveryone)
        {
            syncPowerOn = isPowerOn;
            syncRandom = isRandom;
            syncIndex = -1;

            lastModeCount = syncModeCount;
            lastSongSeq = syncSongSeq;

            RequestSerialization();
        }
    }


    // ============================================================
    // モード切替ボタン
    // ============================================================

    public void ToggleMode()
    {
        if (!isPowerOn) return;


        TakeOwnership();


        ApplyModeSwitch(!isRandom, true);


        if (syncToEveryone)
        {
            syncRandom = isRandom;

            syncModeCount++;

            lastModeCount = syncModeCount;

            syncIndex = -1;

            RequestSerialization();
        }
    }


    // ============================================================
    // Ownership
    // ============================================================

    private void TakeOwnership()
    {
        if (!syncToEveryone) return;

        VRCPlayerApi local = Networking.LocalPlayer;

        if (local == null) return;

        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(local, gameObject);
        }
    }


    private bool CanDrive()
    {
        if (!syncToEveryone)
            return true;

        if (Networking.LocalPlayer == null)
            return true;

        return Networking.IsOwner(gameObject);
    }


    // ============================================================
    // 同期受信
    // ============================================================

    public override void OnDeserialization()
    {
        if (!syncToEveryone || !SourcesReady())
            return;


        // Startより前に同期データが届いた場合
        if (!started)
        {
            pendingDeserialization = true;
            return;
        }


        // Owner本人は同期データを自分に適用しない
        if (Networking.IsOwner(gameObject))
            return;


        bool first = !hasReceived;

        hasReceived = true;


        // --------------------------------------------------------
        // 電源状態
        // --------------------------------------------------------

        if (syncPowerOn != isPowerOn)
        {
            ApplyPower(syncPowerOn, !first);
        }


        // 電源OFFならここで終了
        if (!isPowerOn)
        {
            lastModeCount = syncModeCount;
            lastSongSeq = syncSongSeq;

            return;
        }


        // --------------------------------------------------------
        // モード変更
        // --------------------------------------------------------

        if (syncModeCount != lastModeCount)
        {
            lastModeCount = syncModeCount;

            if (!first)
            {
                ApplyModeSwitch(syncRandom, true);
            }
        }

        isRandom = syncRandom;


        // --------------------------------------------------------
        // 新しい曲が始まった
        // --------------------------------------------------------

        if (syncSongSeq != lastSongSeq)
        {
            lastSongSeq = syncSongSeq;


            if (syncIndex >= 0 &&
                musicClips != null &&
                syncIndex < musicClips.Length)
            {
                currentIndex = syncIndex;


                // サーバー時刻から現在の曲の経過時間を計算
                float offset =
                    (Networking.GetServerTimeInMilliseconds()
                    - syncSongStartMs) / 1000f;


                PlaySongAt(syncIndex, offset);
            }
        }
    }


    // ============================================================
    // 電源状態変更
    // ============================================================

    private void ApplyPower(bool on, bool effects)
    {
        isPowerOn = on;


        if (effects && clickOnPower)
        {
            PlayClick();
        }


        if (effects)
        {
            StartPowerKnobAnim();
        }
        else
        {
            SnapPowerKnob();
        }


        // --------------------------------------------------------
        // ON
        // --------------------------------------------------------

        if (on)
        {
            isRandom = false;

            currentIndex = -1;


            // 常時ノイズ
            StartLoop(
                noiseASource,
                noiseAClip
            );


            if (effects && tuneOnPowerOn)
            {
                StartTuneKnobAnim();
            }


            // まずnoise_Bを流してから曲開始
            BeginGap();
        }


        // --------------------------------------------------------
        // OFF
        // --------------------------------------------------------

        else
        {
            SetPhase(PHASE_STOPPING);
        }
    }


    // ============================================================
    // 再生モード変更
    // ============================================================

    private void ApplyModeSwitch(bool random, bool effects)
    {
        if (effects && clickOnTune)
        {
            PlayClick();
        }


        if (effects)
        {
            StartTuneKnobAnim();
        }


        isRandom = random;


        // 今の曲を切り上げてnoise_Bを挟む
        BeginGap();
    }


    // ============================================================
    // 曲間
    // ============================================================

    private void BeginGap()
    {
        StartLoop(
            noiseBSource,
            noiseBClip
        );


        gapDuration = Random.Range(
            gapMin,
            Mathf.Max(gapMin, gapMax)
        );


        SetPhase(PHASE_GAP);
    }


    // ============================================================
    // Update
    // ============================================================

    private void Update()
    {
        UpdateKnobAnims();

        UpdateNoiseA();


        if (phase == PHASE_IDLE)
            return;


        phaseTimer += Time.deltaTime;


        // --------------------------------------------------------
        // 曲間
        // --------------------------------------------------------

        if (phase == PHASE_GAP)
        {
            float tn =
                Ratio(phaseTimer, switchFadeTime);


            noiseBSource.volume =
                Mathf.Lerp(
                    noiseBFrom,
                    noiseBVolume,
                    tn
                );


            // モード切替時など、
            // 現在の曲をフェードアウト
            if (musicSource.isPlaying)
            {
                float tm =
                    Ratio(
                        phaseTimer,
                        switchFadeTime
                    );


                musicSource.volume =
                    Mathf.Lerp(
                        musicFrom,
                        0f,
                        tm
                    );


                if (tm >= 1f)
                {
                    musicSource.Stop();
                }
            }


            // 曲を決めるのはOwnerだけ
            if (CanDrive() &&
                phaseTimer >= gapDuration)
            {
                StartNextSong();
            }
        }


        // --------------------------------------------------------
        // 曲フェードイン
        // --------------------------------------------------------

        else if (phase == PHASE_MUSIC_IN)
        {
            float t =
                Ratio(
                    phaseTimer,
                    fadeInTime
                );


            musicSource.volume =
                Mathf.Lerp(
                    0f,
                    musicVolume,
                    t
                );


            noiseBSource.volume =
                Mathf.Lerp(
                    noiseBFrom,
                    0f,
                    t
                );


            if (t >= 1f)
            {
                noiseBSource.Stop();

                SetPhase(PHASE_PLAYING);
            }
        }


        // --------------------------------------------------------
        // 曲再生中
        // --------------------------------------------------------

        else if (phase == PHASE_PLAYING)
        {
            AudioClip clip =
                musicSource.clip;


            bool ended =
                (clip == null) ||
                !musicSource.isPlaying;


            if (ended ||
                clip.length - musicSource.time <= outroTime)
            {
                StartLoop(
                    noiseBSource,
                    noiseBClip
                );


                if (tuneOnSongChange)
                {
                    StartTuneKnobAnim();
                }


                SetPhase(PHASE_OUTRO);
            }
        }


        // --------------------------------------------------------
        // 曲終了
        // --------------------------------------------------------

        else if (phase == PHASE_OUTRO)
        {
            float t =
                Ratio(
                    phaseTimer,
                    outroTime
                );


            musicSource.volume =
                Mathf.Lerp(
                    musicFrom,
                    0f,
                    t
                );


            noiseBSource.volume =
                Mathf.Lerp(
                    noiseBFrom,
                    noiseBVolume,
                    t
                );


            if (t >= 1f ||
                !musicSource.isPlaying)
            {
                musicSource.Stop();


                gapDuration =
                    Random.Range(
                        gapMin,
                        Mathf.Max(
                            gapMin,
                            gapMax
                        )
                    );


                SetPhase(PHASE_GAP);
            }
        }


        // --------------------------------------------------------
        // 電源OFF
        // --------------------------------------------------------

        else if (phase == PHASE_STOPPING)
        {
            float t =
                Ratio(
                    phaseTimer,
                    powerFadeTime
                );


            musicSource.volume =
                Mathf.Lerp(
                    musicFrom,
                    0f,
                    t
                );


            noiseBSource.volume =
                Mathf.Lerp(
                    noiseBFrom,
                    0f,
                    t
                );


            if (t >= 1f)
            {
                musicSource.Stop();
                noiseBSource.Stop();

                currentIndex = -1;

                phase = PHASE_IDLE;
            }
        }
    }


    // ============================================================
    // noise_A
    // ============================================================

    private void UpdateNoiseA()
    {
        if (noiseASource == null ||
            !noiseASource.isPlaying)
        {
            return;
        }


        float target =
            isPowerOn
            ? noiseAVolume
            : 0f;


        float rate =
            Mathf.Max(
                noiseAVolume,
                0.01f
            )
            /
            Mathf.Max(
                powerFadeTime,
                0.01f
            );


        noiseASource.volume =
            Mathf.MoveTowards(
                noiseASource.volume,
                target,
                rate * Time.deltaTime
            );


        if (!isPowerOn &&
            noiseASource.volume <= 0f)
        {
            noiseASource.Stop();
        }
    }


    // ============================================================
    // 次の曲
    // ============================================================

    private void StartNextSong()
    {
        int index =
            PickIndex();


        if (index < 0)
        {
            // 曲が未登録ならnoise_Bだけ流す
            gapDuration = 9999f;

            SetPhase(PHASE_GAP);

            return;
        }


        currentIndex = index;


        PlaySongAt(
            index,
            0f
        );


        // --------------------------------------------------------
        // 同期
        // --------------------------------------------------------

        if (syncToEveryone &&
            CanDrive())
        {
            syncIndex = index;


            // この曲を再生開始したサーバー時刻
            syncSongStartMs =
                Networking.GetServerTimeInMilliseconds();


            syncSongSeq++;


            lastSongSeq =
                syncSongSeq;


            RequestSerialization();
        }
    }


    // ============================================================
    // 曲再生
    // ============================================================

    private void PlaySongAt(
        int index,
        float offset
    )
    {
        AudioClip clip =
            musicClips[index];


        if (clip == null)
            return;


        if (offset < 0f)
            offset = 0f;


        if (offset >= clip.length - outroTime)
            return;


        musicSource.clip =
            clip;


        musicSource.volume =
            0f;


        musicSource.Play();


        musicSource.time =
            offset;


        SetPhase(PHASE_MUSIC_IN);
    }


    // ============================================================
    // 曲選択
    // ============================================================

    private int PickIndex()
    {
        if (musicClips == null ||
            musicClips.Length == 0)
        {
            return -1;
        }


        int len =
            musicClips.Length;


        // --------------------------------------------------------
        // 通常再生
        // --------------------------------------------------------

        if (!isRandom)
        {
            for (int i = 1; i <= len; i++)
            {
                int idx =
                    (currentIndex + i + len)
                    % len;


                if (musicClips[idx] != null)
                {
                    return idx;
                }
            }


            return -1;
        }


        // --------------------------------------------------------
        // ランダム再生
        // --------------------------------------------------------

        int validCount = 0;
        int lastValid = -1;


        for (int i = 0; i < len; i++)
        {
            if (musicClips[i] != null)
            {
                validCount++;

                lastValid = i;
            }
        }


        if (validCount == 0)
            return -1;


        if (validCount == 1)
            return lastValid;


        for (int tries = 0; tries < 20; tries++)
        {
            int r =
                Random.Range(0, len);


            if (musicClips[r] != null &&
                r != currentIndex)
            {
                return r;
            }
        }


        for (int i = 0; i < len; i++)
        {
            if (musicClips[i] != null &&
                i != currentIndex)
            {
                return i;
            }
        }


        return -1;
    }


    // ============================================================
    // 電源つまみアニメーション
    // ============================================================

    private void StartPowerKnobAnim()
    {
        if (powerKnob == null)
            return;


        powerFromAngle =
            powerCurrentAngle;


        powerToAngle =
            isPowerOn
            ? powerOnAngle
            : powerOffAngle;


        powerAnimT = 0f;

        powerAnimating = true;
    }


    private void SnapPowerKnob()
    {
        if (powerKnob == null)
            return;


        powerAnimating = false;


        powerCurrentAngle =
            isPowerOn
            ? powerOnAngle
            : powerOffAngle;


        SetKnobAngle(
            powerKnob,
            powerKnobBase,
            powerKnobAxis,
            powerCurrentAngle
        );
    }


    // ============================================================
    // チューニングつまみアニメーション
    // ============================================================

    private void StartTuneKnobAnim()
    {
        if (tuneKnob == null)
            return;


        tuneAnimT = 0f;

        tuneAnimating = true;
    }


    private void UpdateKnobAnims()
    {
        // --------------------------------------------------------
        // 電源つまみ
        // --------------------------------------------------------

        if (powerAnimating)
        {
            powerAnimT +=
                Time.deltaTime /
                Mathf.Max(
                    powerSnapTime,
                    0.01f
                );


            float t =
                Mathf.Clamp01(
                    powerAnimT
                );


            float eased =
                1f -
                (1f - t) *
                (1f - t) *
                (1f - t);


            powerCurrentAngle =
                Mathf.Lerp(
                    powerFromAngle,
                    powerToAngle,
                    eased
                );


            SetKnobAngle(
                powerKnob,
                powerKnobBase,
                powerKnobAxis,
                powerCurrentAngle
            );


            if (t >= 1f)
            {
                powerAnimating = false;
            }
        }


        // --------------------------------------------------------
        // チューニングつまみ
        // --------------------------------------------------------

        if (tuneAnimating)
        {
            tuneAnimT +=
                Time.deltaTime /
                Mathf.Max(
                    tuneDuration,
                    0.01f
                );


            float p =
                Mathf.Clamp01(
                    tuneAnimT
                );


            float wave =
                Mathf.Sin(
                    p *
                    tuneCycles *
                    2f *
                    Mathf.PI
                );


            float damping =
                1f -
                0.4f *
                p;


            float angle =
                tuneAngle *
                wave *
                damping;


            SetKnobAngle(
                tuneKnob,
                tuneKnobBase,
                tuneKnobAxis,
                angle
            );


            if (p >= 1f)
            {
                SetKnobAngle(
                    tuneKnob,
                    tuneKnobBase,
                    tuneKnobAxis,
                    0f
                );


                tuneAnimating = false;
            }
        }
    }


    // ============================================================
    // つまみ回転
    // ============================================================

    private void SetKnobAngle(
        Transform knob,
        Quaternion baseRot,
        Vector3 axis,
        float angle
    )
    {
        if (knob == null)
            return;


        knob.localRotation =
            baseRot *
            Quaternion.AngleAxis(
                angle,
                axis
            );
    }


    // ============================================================
    // AudioSource再生
    // ============================================================

    private void StartLoop(
        AudioSource src,
        AudioClip clip
    )
    {
        if (src == null ||
            src.isPlaying)
        {
            return;
        }


        src.clip =
            clip;


        src.loop =
            true;


        src.volume =
            0f;


        src.Play();


        // 毎回同じノイズに聞こえないよう
        // 開始位置をランダムにずらす
        if (clip != null &&
            clip.length > 0.1f)
        {
            src.time =
                Random.Range(
                    0f,
                    clip.length * 0.9f
                );
        }
    }


    // ============================================================
    // フェーズ変更
    // ============================================================

    private void SetPhase(int newPhase)
    {
        phase =
            newPhase;


        phaseTimer =
            0f;


        musicFrom =
            musicSource.volume;


        noiseBFrom =
            noiseBSource.volume;
    }


    // ============================================================
    // クリック音
    // ============================================================

    private void PlayClick()
    {
        if (sfxSource != null &&
            knobClickClip != null)
        {
            sfxSource.PlayOneShot(
                knobClickClip
            );
        }
    }


    // ============================================================
    // 比率計算
    // ============================================================

    private float Ratio(
        float time,
        float duration
    )
    {
        if (duration <= 0.0001f)
            return 1f;


        return Mathf.Clamp01(
            time / duration
        );
    }
}
