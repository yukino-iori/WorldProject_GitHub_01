using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// ラジオのイコライザー表示
/// MusicSourceの音を周波数ごとに分けて、列ごとのブロックを下から光らせる。
/// ・columns：列の親オブジェクト（左から＝低音→高音の順）
/// ・各列の子ブロックは、下から上に自動で並べ替えて使う
/// ・光ってるブロックは litMaterial、消えてるブロックは unlitMaterial に切り替える
/// eq（列の親）など、どこに付けてもOK。
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class RadioEqualizer : UdonSharpBehaviour
{
    [Header("Audio")]
    [Tooltip("RetroRadioと同じMusicSourceを入れる")]
    [SerializeField] private AudioSource musicSource;

    [Header("Blocks")]
    [Tooltip("列ごとの親オブジェクト（左から順＝低音→高音）")]
    [SerializeField] private Transform[] columns;
    [Tooltip("光ってるときのマテリアル（Emission ON）")]
    [SerializeField] private Material litMaterial;
    [Tooltip("消えてるときのマテリアル")]
    [SerializeField] private Material unlitMaterial;
    [Tooltip("上の段が光ったときのマテリアル（任意・赤などにするとそれっぽい）")]
    [SerializeField] private Material peakMaterial;
    [Tooltip("上から何段をpeakMaterialにするか")]
    [SerializeField] private int peakRows = 1;

    [Header("Frequency（Hz）")]
    [Tooltip("一番左の列の周波数")]
    [SerializeField] private float minFrequency = 300f;
    [Tooltip("一番右の列の周波数")]
    [SerializeField] private float maxFrequency = 5000f;
    [SerializeField] private float sampleRate = 48000f;

    [Header("Sensitivity")]
    [Tooltip("これより小さい音は光らない（小さくすると反応が敏感に）")]
    [SerializeField] private float minDb = -60f;
    [Tooltip("これ以上の音で全段光る（小さくすると光りやすく）")]
    [SerializeField] private float maxDb = -15f;
    [Tooltip("列ごとの感度（任意・空なら全部1）")]
    [SerializeField] private float[] columnGains;
    [Tooltip("光が下がる速さ（大きいほどキビキビ下がる）")]
    [SerializeField] private float fallSpeed = 2.5f;

    private const int SPECTRUM_SIZE = 256;

    private float[] spectrum;
    private Renderer[] blocks;   // 列 * maxRows + 段 で並べた一覧
    private int[] rowCounts;
    private int maxRows = 0;
    private int[] binStart;
    private int[] binEnd;
    private float[] levels;
    private int[] litCounts;
    private bool ready = false;

    private void Start()
    {
        if (columns == null || columns.Length == 0) return;

        int colCount = columns.Length;
        spectrum = new float[SPECTRUM_SIZE];
        rowCounts = new int[colCount];
        levels = new float[colCount];
        litCounts = new int[colCount];
        binStart = new int[colCount];
        binEnd = new int[colCount];

        // 1周目：各列のブロック数を数える
        for (int c = 0; c < colCount; c++)
        {
            litCounts[c] = -1;
            if (columns[c] == null) continue;
            Renderer[] found = columns[c].GetComponentsInChildren<Renderer>();
            int count = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].transform != columns[c]) count++;
            }
            rowCounts[c] = count;
            if (count > maxRows) maxRows = count;
        }
        if (maxRows == 0) return;

        // 2周目：下から上の順に並べて登録
        blocks = new Renderer[colCount * maxRows];
        for (int c = 0; c < colCount; c++)
        {
            if (columns[c] == null || rowCounts[c] == 0) continue;
            Renderer[] found = columns[c].GetComponentsInChildren<Renderer>();

            Renderer[] list = new Renderer[rowCounts[c]];
            float[] heights = new float[rowCounts[c]];
            int n = 0;
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i].transform == columns[c]) continue;
                list[n] = found[i];
                heights[n] = columns[c].InverseTransformPoint(found[i].transform.position).y;
                n++;
            }

            // 高さ順に並べ替え（下が0番）
            for (int i = 1; i < n; i++)
            {
                Renderer r = list[i];
                float h = heights[i];
                int j = i - 1;
                while (j >= 0 && heights[j] > h)
                {
                    list[j + 1] = list[j];
                    heights[j + 1] = heights[j];
                    j--;
                }
                list[j + 1] = r;
                heights[j + 1] = h;
            }

            for (int i = 0; i < n; i++)
            {
                blocks[c * maxRows + i] = list[i];
            }
        }

        // 列ごとの周波数範囲を決める（低音→高音を対数で割り振り）
        float binHz = (sampleRate * 0.5f) / SPECTRUM_SIZE;
        float fMin = Mathf.Max(minFrequency, binHz);
        float fMax = Mathf.Max(maxFrequency, fMin + binHz);
        for (int c = 0; c < colCount; c++)
        {
            float f0 = fMin * Mathf.Pow(fMax / fMin, (float)c / colCount);
            float f1 = fMin * Mathf.Pow(fMax / fMin, (float)(c + 1) / colCount);
            int b0 = Mathf.Clamp(Mathf.FloorToInt(f0 / binHz), 0, SPECTRUM_SIZE - 1);
            int b1 = Mathf.Clamp(Mathf.FloorToInt(f1 / binHz), b0, SPECTRUM_SIZE - 1);
            binStart[c] = b0;
            binEnd[c] = b1;
        }

        ready = true;
    }

    private void Update()
    {
        if (!ready || musicSource == null) return;

        bool playing = musicSource.isPlaying;
        if (playing)
        {
            musicSource.GetSpectrumData(spectrum, 0, FFTWindow.BlackmanHarris);
        }

        int colCount = columns.Length;
        float dt = Time.deltaTime;

        for (int c = 0; c < colCount; c++)
        {
            if (rowCounts[c] == 0) continue;

            float target = 0f;
            if (playing)
            {
                float amp = 0f;
                for (int b = binStart[c]; b <= binEnd[c]; b++)
                {
                    amp += spectrum[b];
                }
                float db = 20f * Mathf.Log10(amp + 0.0000001f);
                target = Mathf.Clamp01((db - minDb) / Mathf.Max(maxDb - minDb, 0.01f));

                if (columnGains != null && c < columnGains.Length)
                {
                    target = Mathf.Clamp01(target * columnGains[c]);
                }
            }

            // 上がるときは一瞬、下がるときはゆっくり
            if (target > levels[c])
            {
                levels[c] = target;
            }
            else
            {
                levels[c] = Mathf.MoveTowards(levels[c], target, fallSpeed * dt);
            }

            int lit = Mathf.RoundToInt(levels[c] * rowCounts[c]);
            if (lit != litCounts[c])
            {
                ApplyColumn(c, lit);
                litCounts[c] = lit;
            }
        }
    }

    private void ApplyColumn(int c, int lit)
    {
        int rows = rowCounts[c];
        for (int r = 0; r < rows; r++)
        {
            Renderer block = blocks[c * maxRows + r];
            if (block == null) continue;

            Material mat = unlitMaterial;
            if (r < lit)
            {
                bool isPeak = peakMaterial != null && r >= rows - peakRows;
                mat = isPeak ? peakMaterial : litMaterial;
            }
            block.sharedMaterial = mat;
        }
    }
}