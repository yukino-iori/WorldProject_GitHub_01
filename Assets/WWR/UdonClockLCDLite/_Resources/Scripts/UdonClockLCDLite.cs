using System;
using System.Globalization;
using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace WWR.Udon
{
    public enum LCD_LITE_DATEFORMAT
    {
        [InspectorName("03-14 WED")] MM_DD_DDD,
        [InspectorName("WED 03-14")] DDD_MM_DD,
        [InspectorName("WED 14-03")] DDD_DD_MM,
        [InspectorName("MAR 14 WED")] MMM_DD_DDD,
        [InspectorName("WED MAR 14")] DDD_MMM_DD,
        [InspectorName("WED 14 MAR")] DDD_DD_MMM,
        [InspectorName("2012-03-14")] YYYY_MM_DD,
        [InspectorName("03-14-2012")] MM_DD_YYYY,
        [InspectorName("14-03-2012")] DD_MM_YYYY,
        [InspectorName("MAR 14")] MMM_DD,
        [InspectorName("14 MAR")] DD_MMM,
        [InspectorName("WEDNESDAY")] DDDD,
    }
    public class UdonClockLCDLite : UdonSharpBehaviour
    {
        [Header("Time")]
        [SerializeField][Tooltip("Use UTC time instead of local time")] private bool UseUTCTime;
        [SerializeField][Tooltip("Time difference from UTC (Unit: hours)\n(only when Use UTC Time is enabled)")] private float UTCDifference;

        [Header("Format")]
        [SerializeField][Tooltip("Use 24-hour format")] private bool UseMilitaryTime;
        [Tooltip("Date display format\n(only when the date display is available)")] public LCD_LITE_DATEFORMAT DateFormat;

        [Space(50)]

        [Header("Objects")]
        [SerializeField] private TextMeshProUGUI LCDTimeText;

        [SerializeField] private GameObject LCDTimeAMPM;
        [SerializeField] private TextMeshProUGUI LCDTimeAMText, LCDTimePMText;
        [SerializeField] private TextMeshProUGUI LCDSecondText, LCDDateText, LCDPlayerText;

        private int LastSecond, LastMinute, LastHour, LastDayOfYear, LastPlayer;
        private bool ForceUpdate, EnableTime, EnableSecond, EnableDate, EnablePlayer;
        private DateTime Now;
        private CultureInfo Culture;


        private void SetLCDSecond(DateTime date)
        {
            // 秒更新
            LCDSecondText.text = date.Second.ToString("00");
        }
        private void SetLCDTime(DateTime date)
        {
            // 時分更新
            if (UseMilitaryTime)
            {
                LCDTimeText.text = date.ToString("H:mm");
            }
            else
            {
                LCDTimeText.text = date.ToString("h:mm");
            }
        }
        private void SetLCDAMPM(DateTime date)
        {
            // AM・PM表示
            if (UseMilitaryTime)
            {
                // 表示なし
                LCDTimeAMPM.SetActive(false);
                LCDTimeAMText.gameObject.SetActive(false);
                LCDTimePMText.gameObject.SetActive(false);
            }
            else
            {
                LCDTimeAMPM.SetActive(true);
                if (date.Hour < 12)
                {
                    // AM
                    LCDTimeAMText.gameObject.SetActive(true);
                    LCDTimePMText.gameObject.SetActive(false);
                }
                else
                {
                    // PM
                    LCDTimeAMText.gameObject.SetActive(false);
                    LCDTimePMText.gameObject.SetActive(true);
                }
            }
        }
        private void SetLCDDate(DateTime date)
        {
            String text;

            // 日付更新
            switch (DateFormat)
            {
                case LCD_LITE_DATEFORMAT.MM_DD_DDD:
                    text = date.Date.ToString("MM-dd!ddd", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.DDD_MM_DD:
                    text = date.Date.ToString("ddd!MM-dd", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.DDD_DD_MM:
                    text = date.Date.ToString("ddd!dd-MM", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.MMM_DD_DDD:
                    text = date.Date.ToString("MMM!dd!ddd", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.DDD_MMM_DD:
                    text = date.Date.ToString("ddd!MMM!dd", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.DDD_DD_MMM:
                    text = date.Date.ToString("ddd!dd!MMM", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.YYYY_MM_DD:
                    text = date.Date.ToString("yyyy-MM-dd", Culture);
                    break;
                case LCD_LITE_DATEFORMAT.MM_DD_YYYY:
                    text = date.Date.ToString("MM-dd-yyyy", Culture);
                    break;
                case LCD_LITE_DATEFORMAT.DD_MM_YYYY:
                    text = date.Date.ToString("dd-MM-yyyy", Culture);
                    break;
                case LCD_LITE_DATEFORMAT.MMM_DD:
                    text = date.Date.ToString("MMM!dd", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.DD_MMM:
                    text = date.Date.ToString("dd!MMM", Culture).ToUpper();
                    break;
                case LCD_LITE_DATEFORMAT.DDDD:
                    text = date.Date.ToString("dddd", Culture).ToUpper();
                    break;
                default:
                    text = date.Date.ToString("MM-dd!ddd", Culture).ToUpper();
                    break;
            }

            LCDDateText.text = text;
        }

        private void SetLCDPlayer(int pc)
        {
            // 100以上なら99として表示
            LCDPlayerText.text = (pc < 100) ? pc.ToString("00") : "99";
        }

        public void CheckPlayer()
        {
            int pc = VRCPlayerApi.GetPlayerCount();
            if (pc != LastPlayer)
            {
                SetLCDPlayer(pc);
                LastPlayer = pc;
            }
        }


        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if (EnablePlayer) SendCustomEventDelayedSeconds("CheckPlayer", 1);
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (EnablePlayer) SendCustomEventDelayedSeconds("CheckPlayer", 1);
        }

        void Start()
        {
            // 日付フォーマット用
            Culture = CultureInfo.GetCultureInfo("en-US");

            if (Utilities.IsValid(LCDTimeText) && Utilities.IsValid(LCDTimeAMPM) && Utilities.IsValid(LCDTimeAMText) && Utilities.IsValid(LCDTimePMText)) EnableTime = true;
            if (Utilities.IsValid(LCDDateText)) EnableDate = true;
            if (Utilities.IsValid(LCDSecondText)) EnableSecond = true;
            if (Utilities.IsValid(LCDPlayerText)) EnablePlayer = true;

            // 強制更新開始
            ForceUpdate = true;
        }

        void Update()
        {
            Now = UseUTCTime ? DateTime.UtcNow + TimeSpan.FromHours(UTCDifference) : DateTime.Now;

            // 1. 毎秒実行
            if (Now.Second != LastSecond)
            {
                LastSecond = Now.Second;
                if (EnableSecond) SetLCDSecond(Now);

                // 2. 毎分実行
                if (Now.Minute != LastMinute)
                {
                    LastMinute = Now.Minute;
                    if (EnableTime) SetLCDTime(Now);

                    // プレイヤー数チェック（保険）
                    if (EnablePlayer) CheckPlayer();

                    // 3. 毎時実行
                    if (Now.Hour != LastHour)
                    {
                        LastHour = Now.Hour;
                        SetLCDAMPM(Now);

                        // 4. 毎日実行
                        if (Now.DayOfYear != LastDayOfYear)
                        {
                            LastDayOfYear = Now.DayOfYear;
                            if (EnableDate) SetLCDDate(Now);
                        }
                    }
                }
            }

            // 強制更新
            if (ForceUpdate)
            {
                if (EnableTime) SetLCDTime(Now);
                if (EnableTime) SetLCDAMPM(Now);
                if (EnableSecond) SetLCDSecond(Now);
                if (EnableDate) SetLCDDate(Now);
                if (EnablePlayer) CheckPlayer();


                // 強制更新終了
                ForceUpdate = false;
            }
        }

    }
}
