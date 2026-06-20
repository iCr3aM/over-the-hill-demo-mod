using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using UnityEngine;
using VehiclePhysics;
using Il2CppInterop.Runtime.Injection;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace OverTheHillMod
{
    [BepInPlugin("com.cr3am.overthehill.firstmod", "越过山丘小助手", "1.8.0")]
    public class MyPlugin : BasePlugin
    {
        internal static ManualLogSource LogSource;
        
        // Vehicle tuning configuration.
        public static ConfigEntry<bool> EnableMod;
        public static ConfigEntry<float> TorqueMult;
        public static ConfigEntry<float> PowerClampMult;
        public static ConfigEntry<float> GearRatioMult;
        public static ConfigEntry<float> GripMult;
        public static ConfigEntry<float> RpmMult;
        public static ConfigEntry<bool> LowGravity;
        public static ConfigEntry<bool> SafeSpeedLimiter;
        public static ConfigEntry<float> MaxSafeSpeedKph;
        public static ConfigEntry<float> MaxSafeAngularVelocity;
        public static ConfigEntry<float> BrakeTorqueMult;
        public static ConfigEntry<float> LinearDragMult;
        public static ConfigEntry<float> AngularDragMult;
        public static ConfigEntry<float> DamageTakenMult;
        public static ConfigEntry<float> SteeringAssistRatio;
        public static ConfigEntry<bool> DrivingAidsOverride;
        public static ConfigEntry<bool> AbsEnabled;
        public static ConfigEntry<bool> TractionControlEnabled;
        public static ConfigEntry<bool> StabilityControlEnabled;
        public static ConfigEntry<string> UILanguage;

        // Stability-related tuning.
        public static ConfigEntry<float> MagicCoMY;
        public static ConfigEntry<float> DownforceStrength;

        public override void Load()
        {
            LogSource = Log;
            EnableMod = Config.Bind("Settings", "EnableMod", true, "F10车辆调校总开关");
            TorqueMult = Config.Bind("Settings", "TorqueMultiplier", 1.0f, "引擎扭矩倍数");
            PowerClampMult = Config.Bind("Settings", "PowerClampMultiplier", 1.0f, "动力限制上限倍数");
            GearRatioMult = Config.Bind("Settings", "GearRatioMultiplier", 1.0f, "齿轮传动比倍数");
            GripMult = Config.Bind("Settings", "GripMultiplier", 1.0f, "轮胎抓地力倍数");
            RpmMult = Config.Bind("Settings", "RpmMultiplier", 1.0f, "红线转速上限倍数");
            LowGravity = Config.Bind("SuperPowers", "LowGravity", false, "低重力模式");
            SafeSpeedLimiter = Config.Bind("Safety", "SafeSpeedLimiter", true, "限制车辆速度和角速度");
            MaxSafeSpeedKph = Config.Bind("Safety", "MaxSafeSpeedKph", 120.0f, "最高速度(km/h)");
            MaxSafeAngularVelocity = Config.Bind("Safety", "MaxSafeAngularVelocity", 10.0f, "最大角速度(rad/s)");
            BrakeTorqueMult = Config.Bind("Handling", "BrakeTorqueMultiplier", 1.0f, "刹车倍率");
            LinearDragMult = Config.Bind("Handling", "LinearDragMultiplier", 1.0f, "线性阻力倍率");
            AngularDragMult = Config.Bind("Handling", "AngularDragMultiplier", 1.0f, "刚体角阻尼倍数");
            DamageTakenMult = Config.Bind("Handling", "DamageTakenMultiplier", 1.0f, "车辆受伤倍率：0.75为低伤，1为原版");
            SteeringAssistRatio = Config.Bind("Handling", "SteeringAssistRatio", -1.0f, "原生转向辅助强度：-1为不覆盖，0-1为指定强度");
            DrivingAidsOverride = Config.Bind("Handling", "DrivingAidsOverride", false, "开启后覆盖ABS/TCS/ESC驾驶辅助开关");
            AbsEnabled = Config.Bind("Handling", "AbsEnabled", true, "ABS 防抱死开关");
            TractionControlEnabled = Config.Bind("Handling", "TractionControlEnabled", true, "TCS 牵引力控制开关");
            StabilityControlEnabled = Config.Bind("Handling", "StabilityControlEnabled", true, "ESC 稳定控制开关");
            UILanguage = Config.Bind("Interface", "UILanguage", "zh", "UI language: zh or en");

            MagicCoMY = Config.Bind("AntiRoll", "MagicCenterOfMassY", 0.0f, "重心Y轴偏移：0为原版，负数降低重心");
            DownforceStrength = Config.Bind("AntiRoll", "DownforceStrength", 0.0f, "车身下压力强度：0为关闭");
            NormalizeUnsafeOldDefaults();
            Config.Save();

            ClassInjector.RegisterTypeInIl2Cpp<ModUI>();
            ClassInjector.RegisterTypeInIl2Cpp<DelayRunner>();

            var uiObj = new GameObject("ModUI");
            GameObject.DontDestroyOnLoad(uiObj);
            uiObj.AddComponent<ModUI>();

            var harmony = new Harmony("com.cr3am.overthehill.firstmod");
            harmony.PatchAll();
            
            LogSource.LogInfo("车辆与环境控制模块已载入。F10 打开车辆面板。");
            WeatherManager.Initialize();
        }

        public static bool IsEnglishUI => UILanguage != null && string.Equals(UILanguage.Value, "en", StringComparison.OrdinalIgnoreCase);
        public static string L(string zh, string en) => IsEnglishUI ? en : zh;

        public static void ToggleUILanguage()
        {
            if (UILanguage == null) return;
            UILanguage.Value = IsEnglishUI ? "zh" : "en";
        }

        private static void NormalizeUnsafeOldDefaults()
        {
            if (Mathf.Approximately(TorqueMult.Value, 6.0f)) TorqueMult.Value = 1.0f;
            if (Mathf.Approximately(PowerClampMult.Value, 12.0f)) PowerClampMult.Value = 1.0f;
            if (Mathf.Approximately(GearRatioMult.Value, 0.6f)) GearRatioMult.Value = 1.0f;
            if (Mathf.Approximately(GripMult.Value, 3.0f)) GripMult.Value = 1.0f;
            if (Mathf.Approximately(RpmMult.Value, 1.6f)) RpmMult.Value = 1.0f;
            if (Mathf.Approximately(MagicCoMY.Value, -1.5f)) MagicCoMY.Value = 0.0f;
            if (Mathf.Approximately(DownforceStrength.Value, 6.0f)) DownforceStrength.Value = 0.0f;

            TorqueMult.Value = Mathf.Clamp(TorqueMult.Value, 1.0f, 1.4f);
            PowerClampMult.Value = Mathf.Clamp(PowerClampMult.Value, 1.0f, 1.5f);
            GearRatioMult.Value = Mathf.Clamp(GearRatioMult.Value, 0.95f, 1.15f);
            GripMult.Value = Mathf.Clamp(GripMult.Value, 0.95f, 1.5f);
            RpmMult.Value = Mathf.Clamp(RpmMult.Value, 1.0f, 1.2f);
            MagicCoMY.Value = Mathf.Clamp(MagicCoMY.Value, -0.6f, 0.2f);
            DownforceStrength.Value = Mathf.Clamp(DownforceStrength.Value, 0.0f, 1.5f);
            MaxSafeSpeedKph.Value = Mathf.Clamp(MaxSafeSpeedKph.Value, 60.0f, 140.0f);
            MaxSafeAngularVelocity.Value = Mathf.Clamp(MaxSafeAngularVelocity.Value, 4.0f, 10.0f);
            BrakeTorqueMult.Value = Mathf.Clamp(BrakeTorqueMult.Value, 0.8f, 1.5f);
            LinearDragMult.Value = Mathf.Clamp(LinearDragMult.Value, 0.9f, 1.5f);
            AngularDragMult.Value = Mathf.Clamp(AngularDragMult.Value, 0.9f, 1.5f);
            DamageTakenMult.Value = Mathf.Clamp(DamageTakenMult.Value, 0.75f, 1.5f);
            SteeringAssistRatio.Value = Mathf.Clamp(SteeringAssistRatio.Value, -1.0f, 1.0f);
        }

        public static void ResetVehicleTuningToVanilla()
        {
            TorqueMult.Value = 1.0f;
            PowerClampMult.Value = 1.0f;
            GearRatioMult.Value = 1.0f;
            GripMult.Value = 1.0f;
            RpmMult.Value = 1.0f;
            MagicCoMY.Value = 0.0f;
            DownforceStrength.Value = 0.0f;
            SafeSpeedLimiter.Value = true;
            MaxSafeSpeedKph.Value = 120.0f;
            MaxSafeAngularVelocity.Value = 10.0f;
            BrakeTorqueMult.Value = 1.0f;
            LinearDragMult.Value = 1.0f;
            AngularDragMult.Value = 1.0f;
            DamageTakenMult.Value = 1.0f;
            SteeringAssistRatio.Value = -1.0f;
            DrivingAidsOverride.Value = false;
            LowGravity.Value = false;
        }
    }

    // Runtime patches for vehicle powertrain and tire-force multipliers.
    [HarmonyPatch(typeof(Engine), "CalculateTorque")]
    public class TorquePatch 
    { 
        [HarmonyPostfix] 
        public static void Postfix(ref float __result) 
        {
            if (MyPlugin.EnableMod.Value) __result *= Mathf.Clamp(MyPlugin.TorqueMult.Value, 1.0f, 1.4f); 
        }
    }

    [HarmonyPatch(typeof(Engine), "ClampPowerTorque")]
    public class ClampPatch 
    { 
        [HarmonyPostfix] 
        public static void Postfix(ref float __result) 
        {
            if (MyPlugin.EnableMod.Value) __result *= Mathf.Clamp(MyPlugin.PowerClampMult.Value, 1.0f, 1.5f); 
        }
    }

    [HarmonyPatch(typeof(Gearbox), "GetGearRatio")]
    public class GearboxPatch 
    { 
        [HarmonyPostfix] 
        public static void Postfix(ref float __result) 
        {
            if (MyPlugin.EnableMod.Value) __result *= Mathf.Clamp(MyPlugin.GearRatioMult.Value, 0.95f, 1.15f); 
        }
    }

    [HarmonyPatch(typeof(TireFrictionStandard), "GetTireForce")]
    public class GripPatch 
    { 
        [HarmonyPostfix] 
        public static void Postfix(ref Vector2 __result) 
        {
            if (MyPlugin.EnableMod.Value) __result *= Mathf.Clamp(MyPlugin.GripMult.Value, 0.95f, 1.5f); 
        }
    }

    public class DefenderParam
    {
        public object TargetObject;
        public PropertyInfo Prop;
        public List<object> TargetObjects = new List<object>();
        public List<PropertyInfo> Props = new List<PropertyInfo>();
        public string NameInChinese;
        public float OriginalValue;
        public float CurrentValue;
        public float MinLimit;
        public float MaxLimit;
        public float StepSize;
    }

    // F10 vehicle control panel and runtime application.
    public class ModUI : MonoBehaviour
    {
        public ModUI(IntPtr ptr) : base(ptr) { }
        private bool showMenu = false;
        private Rect windowRect = new Rect(20, 20, 840, 820);
        private Vector2 menuScrollPosition = Vector2.zero;
        private string utilityStatus = string.Empty;
        private static GUIStyle wrapLabelStyle;

        public static MonoBehaviour currentVehicle = null;
        public static List<DefenderParam> defenderParams = new List<DefenderParam>();
        private static readonly Dictionary<int, float> originalMass = new Dictionary<int, float>();
        private static readonly Dictionary<int, Vector3> originalCenterOfMass = new Dictionary<int, Vector3>();
        private static readonly Dictionary<string, float> originalFloatValues = new Dictionary<string, float>();
        private static readonly Dictionary<string, object> originalObjectValues = new Dictionary<string, object>();
        private static readonly Dictionary<int, float> originalDrag = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> originalAngularDrag = new Dictionary<int, float>();
        private static int runtimeApplyFrame = 0;

        void Update() 
        { 
            if (Input.GetKeyDown(KeyCode.F10)) showMenu = !showMenu; 
            if (Input.GetKeyDown(KeyCode.R)) FlipCurrentVehicle();
            Physics.gravity = (MyPlugin.EnableMod.Value && MyPlugin.LowGravity.Value) ? new Vector3(0, -2.5f, 0) : new Vector3(0, -9.81f, 0);
        }

        // Applies runtime-only vehicle adjustments.
        void FixedUpdate()
        {
            if (currentVehicle == null) return;

            if (!MyPlugin.EnableMod.Value)
            {
                RestoreRuntimePhysics(currentVehicle);
                return;
            }

            if (MyPlugin.EnableMod.Value && currentVehicle != null)
            {
                try
                {
                    var rb = currentVehicle.GetComponent<Rigidbody>();
                    if (rb == null) rb = currentVehicle.GetType().GetProperty("cachedRigidbody")?.GetValue(currentVehicle, null) as Rigidbody;
                    
                    if (rb != null)
                    {
                        CacheRigidbodyOriginals(rb);
                        int rbId = rb.GetInstanceID();

                        float centerOffsetY = Mathf.Clamp(MyPlugin.MagicCoMY.Value, -0.6f, 0.2f);
                        float downforceStrength = Mathf.Clamp(MyPlugin.DownforceStrength.Value, 0.0f, 1.5f);

                        if (Mathf.Abs(centerOffsetY) > 0.001f)
                        {
                            rb.centerOfMass = originalCenterOfMass[rbId] + Vector3.up * centerOffsetY;
                        }
                        else
                        {
                            rb.centerOfMass = originalCenterOfMass[rbId];
                        }

                        float speed = rb.velocity.magnitude;
                        if (speed > 2f && downforceStrength > 0.001f)
                        {
                            Vector3 downForceVector = -currentVehicle.transform.up * (speed * downforceStrength * rb.mass * 0.01f);
                            rb.AddForce(downForceVector, ForceMode.Force);
                        }

                        ApplyRigidbodyDrag(rb);
                        ApplySafeVelocityLimits(rb);
                    }

                    runtimeApplyFrame++;
                    if (runtimeApplyFrame % 60 == 0)
                    {
                        ApplyEngineRpmMultiplier(currentVehicle);
                        ApplyAdvancedHandling(currentVehicle);
                    }
                }
                catch {}
            }
        }

        void OnGUI()
        {
            if (!showMenu) return;
            windowRect = ClampWindowToScreen(windowRect, 800f, 700f);
            windowRect = GUI.Window(0, windowRect, (GUI.WindowFunction)DrawWindow, MyPlugin.L("车辆控制面板 (F10)", "Vehicle Control Panel (F10)"));
        }

        void DrawWindow(int windowID)
        {
            GUILayout.BeginVertical();
            menuScrollPosition = GUILayout.BeginScrollView(menuScrollPosition, GUILayout.Height(Mathf.Max(240f, windowRect.height - 45f)));

            GUILayout.BeginHorizontal();
            string status = currentVehicle != null
                ? "<color=green>" + MyPlugin.L("当前车辆: ", "Current vehicle: ") + currentVehicle.gameObject.name + "</color>"
                : "<color=yellow>" + MyPlugin.L("未检测到当前车辆", "No vehicle detected") + "</color>";
            GUILayout.Label(status);
            if (GUILayout.Button(MyPlugin.L("语言: 中文", "Language: English"), GUILayout.Width(140)))
            {
                MyPlugin.ToggleUILanguage();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            DrawUtilityActions();

            float columnWidth = Mathf.Max(370f, (windowRect.width - 44f) * 0.5f);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(columnWidth));
            MyPlugin.EnableMod.Value = GUILayout.Toggle(MyPlugin.EnableMod.Value, MyPlugin.L("启用车辆调校", "Enable vehicle tuning"));
            GUILayout.Space(8);

            GUILayout.Label("<color=lime><b>" + MyPlugin.L("1. 车身稳定与保护", "1. Stability and Protection") + "</b></color>");
            
            GUILayout.Space(4);

            if (GUILayout.Button(MyPlugin.L("恢复全部默认", "Reset all to default"), GUILayout.Height(26)))
            {
                MyPlugin.ResetVehicleTuningToVanilla();
                if (currentVehicle != null) SafeRecoverVehicle(currentVehicle);
            }

            DrawConfigSlider(MyPlugin.L("重心Y偏移", "Center of mass Y"), MyPlugin.MagicCoMY, -0.6f, 0.2f, "F2", MyPlugin.L("米", " m"), MyPlugin.L("0为原版", "0 is vanilla"));

            DrawConfigSlider(MyPlugin.L("下压力", "Downforce"), MyPlugin.DownforceStrength, 0f, 1.5f, "F2", "x", MyPlugin.L("0为关闭", "0 disables it"));

            GUILayout.Space(10);
            MyPlugin.LowGravity.Value = GUILayout.Toggle(MyPlugin.LowGravity.Value, MyPlugin.L("低重力", "Low gravity"));
            MyPlugin.SafeSpeedLimiter.Value = GUILayout.Toggle(MyPlugin.SafeSpeedLimiter.Value, MyPlugin.L("限制速度和角速度", "Limit speed and angular velocity"));
            if (MyPlugin.SafeSpeedLimiter.Value)
            {
                DrawConfigSlider(MyPlugin.L("最高速度", "Max speed"), MyPlugin.MaxSafeSpeedKph, 60f, 140f, "F0", " km/h", MyPlugin.L("超过后限速", "Clamps above this speed"));
                DrawConfigSlider(MyPlugin.L("最大角速度", "Max angular velocity"), MyPlugin.MaxSafeAngularVelocity, 4f, 10f, "F1", " rad/s", MyPlugin.L("限制翻滚速度", "Limits roll speed"));
            }

            if (GUILayout.Button(MyPlugin.L("翻正车辆 (R)", "Upright vehicle (R)"), GUILayout.Height(28)))
            {
                FlipCurrentVehicle();
            }
            if (GUILayout.Button(MyPlugin.L("恢复车辆状态", "Recover vehicle state"), GUILayout.Height(28)))
            {
                if (currentVehicle != null)
                {
                    MyPlugin.ResetVehicleTuningToVanilla();
                    SafeRecoverVehicle(currentVehicle);
                    utilityStatus = MyPlugin.L("车辆状态已恢复。", "Vehicle state recovered.");
                }
                else
                {
                    utilityStatus = MyPlugin.L("请先进入车辆。", "Enter a vehicle first.");
                }
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(columnWidth));

            GUILayout.Label("<color=orange><b>" + MyPlugin.L("2. 动力与传动", "2. Powertrain") + "</b></color>");
            
            DrawVehiclePresetButtons();
            GUILayout.Space(4);

            DrawConfigSlider(MyPlugin.L("扭矩倍率", "Torque multiplier"), MyPlugin.TorqueMult, 1.0f, 1.4f, "F2", "x", MyPlugin.L("不建议超过预设值", "Presets are recommended"));

            DrawConfigSlider(MyPlugin.L("功率上限", "Power limit"), MyPlugin.PowerClampMult, 1.0f, 1.5f, "F2", "x", null);

            DrawConfigSlider(MyPlugin.L("齿比倍率", "Gear ratio"), MyPlugin.GearRatioMult, 0.95f, 1.15f, "F2", "x", null);

            DrawConfigSlider(MyPlugin.L("抓地倍率", "Grip multiplier"), MyPlugin.GripMult, 0.95f, 1.5f, "F2", "x", null);

            GUILayout.BeginHorizontal();
            GUILayout.Label(MyPlugin.L("转速上限", "RPM limit") + ": " + MyPlugin.RpmMult.Value.ToString("F2") + "x", GetWrapLabelStyle(), GUILayout.Width(170), GUILayout.MinHeight(24));
            float newRpmMult = GUILayout.HorizontalSlider(MyPlugin.RpmMult.Value, 1.0f, 1.2f);
            if (Mathf.Abs(newRpmMult - MyPlugin.RpmMult.Value) > 0.001f)
            {
                MyPlugin.RpmMult.Value = newRpmMult;
                if (currentVehicle != null && MyPlugin.EnableMod.Value) ApplyEngineRpmMultiplier(currentVehicle);
            }
            if (GUILayout.Button(MyPlugin.L("重置", "Reset"), GUILayout.Width(48))) MyPlugin.RpmMult.Value = 1.0f;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            DrawAdvancedHandlingControls();
            GUILayout.Space(10);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<color=cyan><b>" + MyPlugin.L("3. 原生底盘", "3. Native Chassis") + "</b></color>");

            if (defenderParams.Count > 0)
            {
                for (int i = 0; i < defenderParams.Count; i++)
                {
                    var p = defenderParams[i];
                    GUILayout.BeginHorizontal();
                    
                    GUILayout.Label($"{GetDefenderDisplayName(p.NameInChinese)}\n<color=yellow>{MyPlugin.L("当前值", "Value")}: {FormatDefenderValue(p.CurrentValue, p.StepSize)} | {MyPlugin.L("步进", "Step")}: {FormatDefenderValue(p.StepSize, p.StepSize)}</color>", GetWrapLabelStyle(), GUILayout.Width(280), GUILayout.MinHeight(38));
                    
                    float newVal = GUILayout.HorizontalSlider(p.CurrentValue, p.MinLimit, p.MaxLimit);
                    newVal = QuantizeDefenderValue(newVal, p.StepSize);
                    if (Mathf.Abs(newVal - p.CurrentValue) >= Mathf.Max(0.0001f, p.StepSize * 0.5f))
                    {
                        p.CurrentValue = newVal;
                        ApplyDefenderParamValue(p, newVal);
                    }

                    if (GUILayout.Button(MyPlugin.L("重置", "Reset"), GUILayout.Width(45)))
                    {
                        p.CurrentValue = QuantizeDefenderValue(p.OriginalValue, p.StepSize);
                        ApplyDefenderParamValue(p, p.OriginalValue);
                    }
                    
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
            }
            else
            {
                GUILayout.Label("<color=yellow>" + MyPlugin.L("等待车辆初始化...", "Waiting for vehicle initialization...") + "</color>");
            }
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, windowRect.width, 24));
        }

        private static Rect ClampWindowToScreen(Rect rect, float minWidth, float minHeight)
        {
            rect.width = Mathf.Clamp(rect.width, minWidth, Mathf.Max(minWidth, Screen.width - 40f));
            rect.height = Mathf.Clamp(rect.height, minHeight, Mathf.Max(minHeight, Screen.height - 40f));
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
            return rect;
        }

        private static GUIStyle GetWrapLabelStyle()
        {
            if (wrapLabelStyle == null)
            {
                wrapLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    wordWrap = true,
                    richText = true
                };
            }
            return wrapLabelStyle;
        }

        private void DrawVehiclePresetButtons()
        {
            GUILayout.Label("<color=white><b>" + MyPlugin.L("预设", "Presets") + "</b></color>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(MyPlugin.L("原版", "Vanilla"), GUILayout.Height(28))) ApplyVehiclePreset("vanilla");
            if (GUILayout.Button(MyPlugin.L("稳定越野", "Trail"), GUILayout.Height(28))) ApplyVehiclePreset("trail");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(MyPlugin.L("公路轻快", "Road"), GUILayout.Height(28))) ApplyVehiclePreset("road");
            if (GUILayout.Button(MyPlugin.L("牵引爬坡", "Climb"), GUILayout.Height(28))) ApplyVehiclePreset("climb");
            GUILayout.EndHorizontal();
        }

        private void ApplyVehiclePreset(string preset)
        {
            MyPlugin.ResetVehicleTuningToVanilla();

            switch (preset)
            {
                case "trail":
                    MyPlugin.TorqueMult.Value = 1.12f;
                    MyPlugin.PowerClampMult.Value = 1.15f;
                    MyPlugin.GearRatioMult.Value = 1.04f;
                    MyPlugin.GripMult.Value = 1.18f;
                    MyPlugin.RpmMult.Value = 1.05f;
                    MyPlugin.MagicCoMY.Value = -0.12f;
                    MyPlugin.DownforceStrength.Value = 0.25f;
                    MyPlugin.BrakeTorqueMult.Value = 1.08f;
                    MyPlugin.AngularDragMult.Value = 1.10f;
                    MyPlugin.MaxSafeSpeedKph.Value = 110f;
                    MyPlugin.MaxSafeAngularVelocity.Value = 8.0f;
                    utilityStatus = MyPlugin.L("已应用预设：稳定越野。", "Preset applied: Trail.");
                    break;
                case "road":
                    MyPlugin.TorqueMult.Value = 1.20f;
                    MyPlugin.PowerClampMult.Value = 1.30f;
                    MyPlugin.GearRatioMult.Value = 1.02f;
                    MyPlugin.GripMult.Value = 1.12f;
                    MyPlugin.RpmMult.Value = 1.08f;
                    MyPlugin.MagicCoMY.Value = -0.08f;
                    MyPlugin.DownforceStrength.Value = 0.35f;
                    MyPlugin.BrakeTorqueMult.Value = 1.12f;
                    MyPlugin.LinearDragMult.Value = 0.95f;
                    MyPlugin.AngularDragMult.Value = 1.08f;
                    MyPlugin.MaxSafeSpeedKph.Value = 130f;
                    MyPlugin.MaxSafeAngularVelocity.Value = 8.5f;
                    utilityStatus = MyPlugin.L("已应用预设：公路轻快。", "Preset applied: Road.");
                    break;
                case "climb":
                    MyPlugin.TorqueMult.Value = 1.30f;
                    MyPlugin.PowerClampMult.Value = 1.25f;
                    MyPlugin.GearRatioMult.Value = 0.98f;
                    MyPlugin.GripMult.Value = 1.35f;
                    MyPlugin.RpmMult.Value = 1.03f;
                    MyPlugin.MagicCoMY.Value = -0.18f;
                    MyPlugin.DownforceStrength.Value = 0.15f;
                    MyPlugin.BrakeTorqueMult.Value = 1.15f;
                    MyPlugin.AngularDragMult.Value = 1.15f;
                    MyPlugin.MaxSafeSpeedKph.Value = 95f;
                    MyPlugin.MaxSafeAngularVelocity.Value = 7.0f;
                    utilityStatus = MyPlugin.L("已应用预设：牵引爬坡。", "Preset applied: Climb.");
                    break;
                default:
                    utilityStatus = MyPlugin.L("已恢复原版预设。", "Vanilla preset restored.");
                    break;
            }

            if (currentVehicle != null && MyPlugin.EnableMod.Value)
            {
                ApplyDefenderPreset(preset);
                ApplyEngineRpmMultiplier(currentVehicle);
                ApplyAdvancedHandling(currentVehicle);
                if (defenderParams.Count > 0 && preset != "vanilla")
                {
                    utilityStatus += MyPlugin.L($" 已同步底盘 {defenderParams.Count} 项。", $" Chassis values synced: {defenderParams.Count}.");
                }
            }
        }

        private static void ApplyDefenderPreset(string preset)
        {
            if (currentVehicle == null) return;
            if (defenderParams.Count == 0) TargetDefenderHandling(currentVehicle);

            foreach (var param in defenderParams)
            {
                float multiplier = GetDefenderPresetMultiplier(param.NameInChinese, preset);
                float targetValue = preset == "vanilla" ? param.OriginalValue : param.OriginalValue * multiplier;
                targetValue = Mathf.Clamp(QuantizeDefenderValue(targetValue, param.StepSize), param.MinLimit, param.MaxLimit);
                param.CurrentValue = targetValue;
                ApplyDefenderParamValue(param, targetValue);
            }
        }

        private static float GetDefenderPresetMultiplier(string name, string preset)
        {
            if (string.IsNullOrEmpty(name) || preset == "vanilla") return 1.0f;

            bool steering = name.Contains("[转向]");
            bool travel = name.Contains("避震") && (name.Contains("行程") || name.Contains("极限"));
            bool spring = name.Contains("弹簧");
            bool damper = name.Contains("阻尼");
            bool rolling = name.Contains("滚动阻力");
            bool penetration = name.Contains("穿透容差");

            switch (preset)
            {
                case "trail":
                    if (steering) return 1.10f;
                    if (travel) return 1.15f;
                    if (spring) return 1.15f;
                    if (damper) return 1.20f;
                    if (rolling) return 0.90f;
                    if (penetration) return 1.10f;
                    break;
                case "road":
                    if (steering) return 1.05f;
                    if (travel) return 0.95f;
                    if (spring) return 1.25f;
                    if (damper) return 1.25f;
                    if (rolling) return 0.85f;
                    if (penetration) return 1.00f;
                    break;
                case "climb":
                    if (steering) return 1.12f;
                    if (travel) return 1.20f;
                    if (spring) return 1.10f;
                    if (damper) return 1.15f;
                    if (rolling) return 0.80f;
                    if (penetration) return 1.15f;
                    break;
            }

            return 1.0f;
        }

        private static string GetDefenderDisplayName(string name)
        {
            if (!MyPlugin.IsEnglishUI || string.IsNullOrEmpty(name)) return name;
            if (name.Contains("方向盘传动角极限")) return "[Steering] Max steering angle";
            if (name.Contains("避震极限行程")) return "[Suspension] Max suspension travel";
            if (name.Contains("弹簧刚度承载上限")) return "[Suspension] Max spring rate";
            if (name.Contains("减震阻尼吸能上限")) return "[Suspension] Max damper rate";
            if (name.Contains("自由滑行滚动阻力")) return "[Tire] Rolling resistance";
            if (name.Contains("避震行程")) return "[Suspension] Wheel travel";
            if (name.Contains("弹簧刚度")) return "[Suspension] Wheel spring rate";
            if (name.Contains("减震阻尼")) return "[Suspension] Wheel damper rate";
            if (name.Contains("地面穿透容差")) return "[Tire] Ground penetration tolerance";
            return name;
        }

        private void DrawUtilityActions()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<color=white><b>" + MyPlugin.L("常用操作", "Actions") + "</b></color>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(MyPlugin.L("修复车辆", "Repair vehicle"), GUILayout.Height(30)))
            {
                RepairCurrentVehicle();
            }
            if (GUILayout.Button(MyPlugin.L("补充装置", "Refill tools"), GUILayout.Height(30)))
            {
                RefillCurrentVehicleTools();
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(MyPlugin.L("货币清零", "Clear money"), GUILayout.Height(30)))
            {
                ClearPlayerMoney();
            }
            if (GUILayout.Button(MyPlugin.L("添加 10 货币", "Add 10 money"), GUILayout.Height(30)))
            {
                AddMoneyToPlayer(10);
            }
            GUILayout.EndHorizontal();
            string statusText = string.IsNullOrEmpty(utilityStatus) ? MyPlugin.L("等待操作", "Ready") : utilityStatus;
            GUILayout.Label("<color=cyan>" + statusText + "</color>");
            GUILayout.EndVertical();
        }

        private static void DrawConfigSlider(string label, ConfigEntry<float> entry, float min, float max, string format, string unit, string hint)
        {
            GUILayout.BeginHorizontal();
            string hintText = string.IsNullOrEmpty(hint) ? string.Empty : $"\n<color=cyan>({hint})</color>";
            GUILayout.Label($"{label}: {entry.Value.ToString(format)}{unit}{hintText}", GetWrapLabelStyle(), GUILayout.Width(170), GUILayout.MinHeight(string.IsNullOrEmpty(hint) ? 24 : 38));
            float newValue = GUILayout.HorizontalSlider(entry.Value, min, max);
            if (Mathf.Abs(newValue - entry.Value) > 0.001f)
            {
                entry.Value = newValue;
            }
            if (GUILayout.Button(MyPlugin.L("重置", "Reset"), GUILayout.Width(48)))
            {
                entry.Value = Mathf.Clamp(1.0f, min, max);
                if (entry == MyPlugin.MagicCoMY || entry == MyPlugin.DownforceStrength) entry.Value = 0.0f;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawAdvancedHandlingControls()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<color=magenta><b>" + MyPlugin.L("4. 原生操控", "4. Native Handling") + "</b></color>");
            bool changed = false;
            changed |= DrawConfigSliderRealtime(MyPlugin.L("刹车倍率", "Brake multiplier"), MyPlugin.BrakeTorqueMult, 0.8f, 1.5f, "F2", "x", MyPlugin.L("可到1.5，通常稳定", "Up to 1.5 is usually stable"));
            changed |= DrawConfigSliderRealtime(MyPlugin.L("线性阻力", "Linear drag"), MyPlugin.LinearDragMult, 0.9f, 1.5f, "F2", "x", MyPlugin.L("影响滑行减速", "Affects coasting deceleration"));
            changed |= DrawConfigSliderRealtime(MyPlugin.L("角阻尼", "Angular drag"), MyPlugin.AngularDragMult, 0.9f, 1.5f, "F2", "x", MyPlugin.L("影响车身旋转速度", "Affects body rotation speed"));
            changed |= DrawConfigSliderRealtime(MyPlugin.L("受伤倍率", "Damage multiplier"), MyPlugin.DamageTakenMult, 0.75f, 1.5f, "F2", "x", MyPlugin.L("低于1更耐撞", "Below 1 reduces damage taken"));
            changed |= DrawConfigSliderRealtime(MyPlugin.L("转向辅助", "Steering assist"), MyPlugin.SteeringAssistRatio, -1.0f, 1.0f, "F2", "", MyPlugin.L("-1保持原版", "-1 keeps vanilla"));

            bool oldOverride = MyPlugin.DrivingAidsOverride.Value;
            MyPlugin.DrivingAidsOverride.Value = GUILayout.Toggle(MyPlugin.DrivingAidsOverride.Value, MyPlugin.L("覆盖驾驶辅助", "Override driving assists"));
            changed |= oldOverride != MyPlugin.DrivingAidsOverride.Value;
            if (MyPlugin.DrivingAidsOverride.Value)
            {
                GUILayout.BeginHorizontal();
                bool oldAbs = MyPlugin.AbsEnabled.Value;
                bool oldTcs = MyPlugin.TractionControlEnabled.Value;
                bool oldEsc = MyPlugin.StabilityControlEnabled.Value;
                MyPlugin.AbsEnabled.Value = GUILayout.Toggle(MyPlugin.AbsEnabled.Value, "ABS");
                MyPlugin.TractionControlEnabled.Value = GUILayout.Toggle(MyPlugin.TractionControlEnabled.Value, "TCS");
                MyPlugin.StabilityControlEnabled.Value = GUILayout.Toggle(MyPlugin.StabilityControlEnabled.Value, "ESC");
                changed |= oldAbs != MyPlugin.AbsEnabled.Value || oldTcs != MyPlugin.TractionControlEnabled.Value || oldEsc != MyPlugin.StabilityControlEnabled.Value;
                GUILayout.EndHorizontal();
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(MyPlugin.L("恢复本组默认", "Reset this group"), GUILayout.Height(28)))
            {
                MyPlugin.BrakeTorqueMult.Value = 1.0f;
                MyPlugin.LinearDragMult.Value = 1.0f;
                MyPlugin.AngularDragMult.Value = 1.0f;
                MyPlugin.DamageTakenMult.Value = 1.0f;
                MyPlugin.SteeringAssistRatio.Value = -1.0f;
                MyPlugin.DrivingAidsOverride.Value = false;
                if (currentVehicle != null)
                {
                    SafeRecoverVehicle(currentVehicle);
                    utilityStatus = MyPlugin.L("本组参数已恢复。", "This group has been restored.");
                }
                else
                {
                    utilityStatus = MyPlugin.L("本组参数已重置。", "This group has been reset.");
                }
            }
            GUILayout.EndHorizontal();

            if (changed && currentVehicle != null)
            {
                ApplyAdvancedHandling(currentVehicle);
            }
            GUILayout.EndVertical();
        }

        private static bool DrawConfigSliderRealtime(string label, ConfigEntry<float> entry, float min, float max, string format, string unit, string hint)
        {
            GUILayout.BeginHorizontal();
            string hintText = string.IsNullOrEmpty(hint) ? string.Empty : $"\n<color=cyan>({hint})</color>";
            GUILayout.Label($"{label}: {entry.Value.ToString(format)}{unit}{hintText}", GetWrapLabelStyle(), GUILayout.Width(170), GUILayout.MinHeight(string.IsNullOrEmpty(hint) ? 24 : 38));
            float oldValue = entry.Value;
            float newValue = GUILayout.HorizontalSlider(oldValue, min, max);
            bool changed = false;
            if (Mathf.Abs(newValue - oldValue) > 0.001f)
            {
                entry.Value = Mathf.Clamp(newValue, min, max);
                changed = true;
            }
            if (GUILayout.Button(MyPlugin.L("重置", "Reset"), GUILayout.Width(48)))
            {
                entry.Value = entry == MyPlugin.SteeringAssistRatio ? -1.0f : 1.0f;
                changed = true;
            }
            GUILayout.EndHorizontal();
            return changed;
        }

        private void RepairCurrentVehicle()
        {
            try
            {
                var damageSystem = FindRuntimeComponent<CarDamageSystem>();
                if (damageSystem == null)
                {
                    utilityStatus = MyPlugin.L("未找到当前车辆伤害系统，请进入车辆后再试。", "Vehicle damage system not found. Enter a vehicle and try again.");
                    return;
                }

                damageSystem.Cheat_SetDamage(0);
                damageSystem.DamageAmount = 0f;
                utilityStatus = MyPlugin.L("已将当前车辆伤害清零。", "Current vehicle damage cleared.");
            }
            catch (Exception ex)
            {
                utilityStatus = MyPlugin.L("恢复车辆血量失败：", "Vehicle repair failed: ") + ex.Message;
                MyPlugin.LogSource.LogWarning("[Utility] Restore vehicle health failed: " + ex);
            }
        }

        private void RefillCurrentVehicleTools()
        {
            try
            {
                var inventory = FindRuntimeComponent<CarInventory>();
                if (inventory == null)
                {
                    utilityStatus = MyPlugin.L("未找到当前车辆装置背包，请进入车辆后再试。", "Vehicle tool inventory not found. Enter a vehicle and try again.");
                    return;
                }

                inventory.AskForItemRefill();
                inventory.RPC_RefillItems();
                utilityStatus = MyPlugin.L("已请求补满当前车辆已装备装置。", "Equipped vehicle tools refill requested.");
            }
            catch (Exception ex)
            {
                utilityStatus = MyPlugin.L("补满装置失败：", "Tool refill failed: ") + ex.Message;
                MyPlugin.LogSource.LogWarning("[Utility] Refill vehicle tools failed: " + ex);
            }
        }

        private void AddMoneyToPlayer(int amount)
        {
            try
            {
                if (!TryGetMoneyInventory(out var manager, out uint moneyItemId)) return;

                manager.Inventory.AddItem(moneyItemId, Mathf.Clamp(amount, 1, 100000), false);
                utilityStatus = MyPlugin.L($"已增加 {amount} 货币 (ItemId {moneyItemId})。", $"Added {amount} money (ItemId {moneyItemId}).");
            }
            catch (Exception ex)
            {
                utilityStatus = MyPlugin.L("增加货币失败：", "Add money failed: ") + ex.Message;
                MyPlugin.LogSource.LogWarning("[Utility] Add money failed: " + ex);
            }
        }

        private void ClearPlayerMoney()
        {
            try
            {
                if (!TryGetMoneyInventory(out var manager, out uint moneyItemId)) return;

                int currentCount = manager.Inventory.GetItemsCount(moneyItemId);
                if (currentCount <= 0)
                {
                    utilityStatus = MyPlugin.L($"当前货币已经是 0 (ItemId {moneyItemId})。", $"Money is already 0 (ItemId {moneyItemId}).");
                    return;
                }

                bool removed = manager.Inventory.TryRemoveItem(moneyItemId, currentCount);
                utilityStatus = removed
                    ? MyPlugin.L($"已清零货币：移除 {currentCount} (ItemId {moneyItemId})。", $"Money cleared: removed {currentCount} (ItemId {moneyItemId}).")
                    : MyPlugin.L($"货币清零失败，当前数量 {currentCount}。", $"Clear money failed. Current amount: {currentCount}.");
            }
            catch (Exception ex)
            {
                utilityStatus = MyPlugin.L("货币清零失败：", "Clear money failed: ") + ex.Message;
                MyPlugin.LogSource.LogWarning("[Utility] Clear money failed: " + ex);
            }
        }

        private bool TryGetMoneyInventory(out PlayerInventoryManager manager, out uint moneyItemId)
        {
            manager = PlayerInventoryManager.Instance;
            if (manager == null)
            {
                var managers = Resources.FindObjectsOfTypeAll<PlayerInventoryManager>();
                if (managers != null && managers.Length > 0) manager = managers[0];
            }

            moneyItemId = manager != null ? manager.MoneyItemId : 0;
            if (manager == null || manager.Inventory == null)
            {
                utilityStatus = MyPlugin.L("未找到玩家背包管理器，请进入车辆后再试。", "Player inventory manager not found. Enter a vehicle and try again.");
                return false;
            }

            if (moneyItemId == 0)
            {
                utilityStatus = MyPlugin.L("货币ID尚未初始化，暂不写入背包。", "Money item id is not initialized.");
                return false;
            }

            return true;
        }

        private static T FindRuntimeComponent<T>() where T : Component
        {
            if (currentVehicle != null)
            {
                var component = currentVehicle.GetComponentInChildren<T>(true);
                if (component != null) return component;

                var parent = currentVehicle.GetComponentInParent<T>();
                if (parent != null) return parent;
            }

            var all = Resources.FindObjectsOfTypeAll<T>();
            return all != null && all.Length > 0 ? all[0] : null;
        }

        public static void TargetDefenderHandling(MonoBehaviour vehicle)
        {
            defenderParams.Clear();
            if (vehicle == null) return;
            ScanDefenderRecursive(vehicle, 0);
            ScanWheelComponents(vehicle);
        }

        private static void FlipCurrentVehicle()
        {
            if (currentVehicle == null) return;

            try
            {
                var rb = currentVehicle.GetComponent<Rigidbody>();
                if (rb == null) rb = currentVehicle.GetType().GetProperty("cachedRigidbody")?.GetValue(currentVehicle, null) as Rigidbody;
                if (rb == null) return;

                rb.transform.rotation = Quaternion.Euler(0, rb.transform.rotation.eulerAngles.y, 0);
                rb.transform.position += Vector3.up * 1.5f;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            catch {}
        }

        private static void SafeRecoverVehicle(MonoBehaviour vehicle)
        {
            if (vehicle == null) return;

            try
            {
                RestoreRuntimePhysics(vehicle);
                originalFloatValues.Clear();
                originalObjectValues.Clear();
                defenderParams.Clear();
                TargetDefenderHandling(vehicle);

                var rb = vehicle.GetComponent<Rigidbody>();
                if (rb == null) rb = vehicle.GetType().GetProperty("cachedRigidbody")?.GetValue(vehicle, null) as Rigidbody;
                if (rb != null)
                {
                    rb.velocity = Vector3.ClampMagnitude(rb.velocity, 10f);
                    rb.angularVelocity = Vector3.ClampMagnitude(rb.angularVelocity, 4f);
                    rb.transform.rotation = Quaternion.Euler(0f, rb.transform.rotation.eulerAngles.y, 0f);
                    rb.transform.position += Vector3.up * 0.8f;
                }

                var damageSystem = vehicle.GetComponentInChildren<CarDamageSystem>(true) ?? vehicle.GetComponentInParent<CarDamageSystem>();
                if (damageSystem != null) damageSystem.SetDamageModifier(1.0f);
            }
            catch {}
        }

        private static void ScanDefenderRecursive(object obj, int depth)
        {
            if (obj == null || depth > 5 || defenderParams.Count >= 12) return;
            var type = obj.GetType();

            try
            {
                var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var p in props)
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    var pType = p.PropertyType;

                    if (pType == typeof(float))
                    {
                        string name = p.Name.ToLower();
                        float val = (float)p.GetValue(obj, null);

                        string cnName = "";
                        float min = 0f; float max = 100f;

                        float step = GetDefenderStepForValue(val);
                        if (name == "maxsteerangle") { cnName = "[转向] 方向盘传动角极限"; min = Mathf.Max(5f, val * 0.70f); max = Mathf.Max(min + step, val * 1.50f); step = 1f; }
                        else if (name == "maxsuspensiontravel") { cnName = "[悬挂] 避震极限行程(米)"; min = Mathf.Max(0.05f, val * 0.70f); max = Mathf.Max(min + 0.01f, val * 1.50f); step = 0.01f; }
                        else if (name == "maxsuspensionspring") { cnName = "[悬挂] 弹簧刚度承载上限"; min = Mathf.Max(1000f, val * 0.70f); max = Mathf.Max(min + 1000f, val * 1.50f); step = 1000f; }
                        else if (name == "maxsuspensiondamper") { cnName = "[悬挂] 减震阻尼吸能上限"; min = Mathf.Max(100f, val * 0.70f); max = Mathf.Max(min + 1000f, val * 1.50f); step = val >= 1000f ? 1000f : 100f; }
                        else if (name == "rollingresistance") { cnName = "[轮胎] 自由滑行滚动阻力"; min = Mathf.Max(0f, val * 0.40f); max = Mathf.Max(0.02f, val * 1.50f); step = 0.001f; }

                        if (!string.IsNullOrEmpty(cnName))
                        {
                            AddDefenderParam(obj, p, cnName, val, min, max, step);
                        }
                    }
                    else if (pType.Namespace != null && pType.Namespace.Contains("VehiclePhysics") && !pType.IsArray && !pType.IsEnum)
                    {
                        var sub = p.GetValue(obj, null);
                        if (sub != null && sub != obj) ScanDefenderRecursive(sub, depth + 1);
                    }
                }
            }
            catch {}
        }

        private static void ScanWheelComponents(MonoBehaviour vehicle)
        {
            try
            {
                var components = vehicle.GetComponentsInChildren<MonoBehaviour>(true);
                foreach (var wheel in components)
                {
                    if (wheel == null) continue;
                    var typeName = wheel.GetType().FullName ?? "";
                    if (!typeName.StartsWith("VehiclePhysics.") || !typeName.Contains("WheelCollider")) continue;

                    ScanWheelParam(wheel, "suspensionTravel", "[悬挂] 避震行程(全部车轮)", 0.05f, 1.5f, 0.70f, 1.50f, 0.01f);
                    ScanWheelParam(wheel, "springRate", "[悬挂] 弹簧刚度(全部车轮)", 1000f, 220000f, 0.70f, 1.50f, 1000f);
                    ScanWheelParam(wheel, "damperRate", "[悬挂] 减震阻尼(全部车轮)", 100f, 24000f, 0.70f, 1.50f, 1000f);
                    ScanWheelParam(wheel, "groundPenetration", "[轮胎] 地面穿透容差", 0f, 0.3f, 0f, 1.5f, 0.01f);
                }
            }
            catch {}
        }

        private static void ScanWheelParam(object target, string propName, string cnName, float baseMin, float baseMax, float minMultiplier, float maxMultiplier, float step)
        {
            try
            {
                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null || prop.PropertyType != typeof(float) || !prop.CanWrite) return;
                float value = (float)prop.GetValue(target, null);
                float min = minMultiplier <= 0f ? baseMin : Mathf.Max(baseMin, value * minMultiplier);
                float max = Mathf.Min(baseMax, Mathf.Max(min + step, value * maxMultiplier));
                AddDefenderParam(target, prop, cnName, value, min, max, step);
            }
            catch {}
        }

        private static void AddDefenderParam(object target, PropertyInfo prop, string cnName, float value, float min, float max, float step)
        {
            var existing = defenderParams.FirstOrDefault(x => x.NameInChinese == cnName);
            if (existing != null)
            {
                existing.TargetObjects.Add(target);
                existing.Props.Add(prop);
                existing.MinLimit = Mathf.Min(existing.MinLimit, min);
                existing.MaxLimit = Mathf.Max(existing.MaxLimit, max);
                existing.StepSize = Mathf.Max(existing.StepSize, step);
                return;
            }

            value = QuantizeDefenderValue(value, step);

            defenderParams.Add(new DefenderParam {
                TargetObject = target,
                Prop = prop,
                TargetObjects = new List<object> { target },
                Props = new List<PropertyInfo> { prop },
                NameInChinese = cnName,
                OriginalValue = value,
                CurrentValue = value,
                MinLimit = min,
                MaxLimit = max,
                StepSize = step
            });
        }

        private static void ApplyDefenderParamValue(DefenderParam param, float value)
        {
            value = Mathf.Clamp(QuantizeDefenderValue(value, param.StepSize), param.MinLimit, param.MaxLimit);
            if (param.TargetObjects.Count == 0 && param.TargetObject != null)
            {
                param.TargetObjects.Add(param.TargetObject);
                param.Props.Add(param.Prop);
            }

            for (int i = 0; i < param.TargetObjects.Count; i++)
            {
                try { param.Props[i].SetValue(param.TargetObjects[i], value, null); } catch {}
            }
        }

        private static float QuantizeDefenderValue(float value, float step)
        {
            if (step <= 0.000001f) return value;
            return Mathf.Round(value / step) * step;
        }

        private static string FormatDefenderValue(float value, float step)
        {
            if (step >= 100f) return value.ToString("F0");
            if (step >= 1f) return value.ToString("F1");
            if (step >= 0.01f) return value.ToString("F2");
            return value.ToString("F3");
        }

        private static float GetDefenderStepForValue(float value)
        {
            float abs = Mathf.Abs(value);
            if (abs >= 1000f) return 1000f;
            if (abs >= 100f) return 100f;
            if (abs >= 10f) return 1f;
            if (abs >= 1f) return 0.1f;
            if (abs >= 0.05f) return 0.01f;
            return 0.001f;
        }

        private static void CacheRigidbodyOriginals(Rigidbody rb)
        {
            int id = rb.GetInstanceID();
            if (!originalMass.ContainsKey(id)) originalMass[id] = rb.mass;
            if (!originalCenterOfMass.ContainsKey(id)) originalCenterOfMass[id] = rb.centerOfMass;
            if (!originalDrag.ContainsKey(id)) originalDrag[id] = rb.drag;
            if (!originalAngularDrag.ContainsKey(id)) originalAngularDrag[id] = rb.angularDrag;
        }

        private static void RestoreRuntimePhysics(MonoBehaviour vehicle)
        {
            try
            {
                var rb = vehicle.GetComponent<Rigidbody>();
                if (rb == null) rb = vehicle.GetType().GetProperty("cachedRigidbody")?.GetValue(vehicle, null) as Rigidbody;
                if (rb != null)
                {
                    int id = rb.GetInstanceID();
                    if (originalMass.TryGetValue(id, out var mass)) rb.mass = mass;
                    if (originalCenterOfMass.TryGetValue(id, out var center)) rb.centerOfMass = center;
                    if (originalDrag.TryGetValue(id, out var drag)) rb.drag = drag;
                    if (originalAngularDrag.TryGetValue(id, out var angularDrag)) rb.angularDrag = angularDrag;
                }

                RestoreOriginalFloatProperties();
                RestoreOriginalObjectProperties();
            }
            catch {}
        }

        private static void ApplyRigidbodyDrag(Rigidbody rb)
        {
            if (rb == null) return;
            CacheRigidbodyOriginals(rb);
            int id = rb.GetInstanceID();
            rb.drag = Mathf.Clamp(originalDrag[id] * Mathf.Clamp(MyPlugin.LinearDragMult.Value, 0.9f, 1.5f), 0f, 5f);
            rb.angularDrag = Mathf.Clamp(originalAngularDrag[id] * Mathf.Clamp(MyPlugin.AngularDragMult.Value, 0.9f, 1.5f), 0.01f, 10f);
        }

        private static void ApplySafeVelocityLimits(Rigidbody rb)
        {
            if (rb == null || !MyPlugin.SafeSpeedLimiter.Value) return;

            float maxSpeed = Mathf.Clamp(MyPlugin.MaxSafeSpeedKph.Value, 60f, 140f) / 3.6f;
            if (rb.velocity.sqrMagnitude > maxSpeed * maxSpeed)
            {
                rb.velocity = rb.velocity.normalized * maxSpeed;
            }

            float maxAngularVelocity = Mathf.Clamp(MyPlugin.MaxSafeAngularVelocity.Value, 4f, 10f);
            if (rb.angularVelocity.sqrMagnitude > maxAngularVelocity * maxAngularVelocity)
            {
                rb.angularVelocity = rb.angularVelocity.normalized * maxAngularVelocity;
            }
        }

        private static void ApplyAdvancedHandling(MonoBehaviour vehicle)
        {
            if (vehicle == null || !MyPlugin.EnableMod.Value) return;

            ApplyScaledFloatProperty(GetPropertyValue(vehicle, "brakes"), "maxBrakeTorque", Mathf.Clamp(MyPlugin.BrakeTorqueMult.Value, 0.8f, 1.5f));
            ApplyScaledFloatProperty(GetPropertyValue(GetPropertyValue(vehicle, "m_brakes"), "settings"), "maxBrakeTorque", Mathf.Clamp(MyPlugin.BrakeTorqueMult.Value, 0.8f, 1.5f));

            ApplyDirectFloatProperty(GetPropertyValue(vehicle, "steeringAids"), "helpRatio", MyPlugin.SteeringAssistRatio.Value, -1.0f);
            ApplyDirectFloatProperty(GetPropertyValue(GetPropertyValue(vehicle, "m_steering"), "steeringAids"), "helpRatio", MyPlugin.SteeringAssistRatio.Value, -1.0f);

            if (MyPlugin.DrivingAidsOverride.Value)
            {
                ApplyDirectBoolProperty(GetPropertyValue(vehicle, "antiLock"), "enabled", MyPlugin.AbsEnabled.Value);
                ApplyDirectBoolProperty(GetPropertyValue(vehicle, "tractionControl"), "enabled", MyPlugin.TractionControlEnabled.Value);
                ApplyDirectBoolProperty(GetPropertyValue(vehicle, "stabilityControl"), "enabled", MyPlugin.StabilityControlEnabled.Value);
            }
            else
            {
                RestoreObjectProperty(GetPropertyValue(vehicle, "antiLock"), "enabled");
                RestoreObjectProperty(GetPropertyValue(vehicle, "tractionControl"), "enabled");
                RestoreObjectProperty(GetPropertyValue(vehicle, "stabilityControl"), "enabled");
            }

            try
            {
                var damageSystem = vehicle.GetComponentInChildren<CarDamageSystem>(true) ?? vehicle.GetComponentInParent<CarDamageSystem>();
                if (damageSystem != null)
                {
                    damageSystem.SetDamageModifier(Mathf.Clamp(MyPlugin.DamageTakenMult.Value, 0.75f, 1.5f));
                }
            }
            catch {}
        }

        public static void ApplyEngineRpmMultiplier(MonoBehaviour vehicle)
        {
            float rpmMultiplier = Mathf.Clamp(MyPlugin.RpmMult.Value, 1.0f, 1.2f);

            ApplyScaledFloatProperty(GetPropertyValue(vehicle, "engine"), "maxRpm", rpmMultiplier);
            ApplyScaledFloatProperty(GetPropertyValue(vehicle, "engine"), "peakRpm", rpmMultiplier);
            ApplyScaledFloatProperty(GetPropertyValue(vehicle, "engine"), "rpmLimiterMax", rpmMultiplier);
            ApplyScaledFloatProperty(GetPropertyValue(vehicle, "gearbox"), "automaticMaxRpm", rpmMultiplier);

            var runtimeEngine = GetPropertyValue(vehicle, "m_engine") ?? vehicle.GetComponentInChildren<VehiclePhysics.Engine>();
            ApplyScaledFloatProperty(runtimeEngine, "maxRpm", rpmMultiplier);
            ApplyScaledFloatProperty(runtimeEngine, "peakRpm", rpmMultiplier);
            ApplyScaledFloatProperty(runtimeEngine, "rpmLimiterMax", rpmMultiplier);
        }

        private static object GetPropertyValue(object target, string propName)
        {
            if (target == null) return null;
            try
            {
                return target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(target, null);
            }
            catch { return null; }
        }

        private static void ApplyScaledFloatProperty(object target, string propName, float multiplier)
        {
            if (target == null) return;
            try
            {
                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null || prop.PropertyType != typeof(float) || !prop.CanRead || !prop.CanWrite) return;

                string key = target.GetHashCode() + ":" + propName;
                if (!originalFloatValues.ContainsKey(key))
                {
                    originalFloatValues[key] = (float)prop.GetValue(target, null);
                }

                float originalValue = originalFloatValues[key];
                if (!IsReasonableFinite(originalValue)) return;

                float newValue = originalValue * multiplier;
                newValue = ClampScaledPropertyValue(propName, originalValue, newValue);
                if (!IsReasonableFinite(newValue)) return;

                prop.SetValue(target, newValue, null);
            }
            catch {}
        }

        private static bool IsReasonableFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Mathf.Abs(value) < 10000000f;
        }

        private static float ClampScaledPropertyValue(string propName, float originalValue, float value)
        {
            string name = (propName ?? string.Empty).ToLowerInvariant();
            float min = Mathf.Min(originalValue * 0.75f, originalValue * 1.35f);
            float max = Mathf.Max(originalValue * 0.75f, originalValue * 1.35f);

            if (name.Contains("rpm"))
            {
                min = Mathf.Max(500f, min);
                max = Mathf.Min(9000f, max);
            }
            else if (name.Contains("brake"))
            {
                min = Mathf.Max(100f, Mathf.Min(originalValue * 0.8f, originalValue * 1.5f));
                max = Mathf.Min(200000f, Mathf.Max(originalValue * 0.8f, originalValue * 1.5f));
            }
            else if (name.Contains("torque"))
            {
                min = Mathf.Max(0f, min);
                max = Mathf.Min(200000f, max);
            }

            if (Mathf.Approximately(min, max))
            {
                min = originalValue - Mathf.Max(1f, Mathf.Abs(originalValue) * 0.25f);
                max = originalValue + Mathf.Max(1f, Mathf.Abs(originalValue) * 0.25f);
            }

            return Mathf.Clamp(value, min, max);
        }

        private static void ApplyDirectFloatProperty(object target, string propName, float value, float disabledSentinel)
        {
            if (target == null) return;
            if (Mathf.Approximately(value, disabledSentinel))
            {
                RestoreObjectProperty(target, propName);
                return;
            }

            try
            {
                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null || prop.PropertyType != typeof(float) || !prop.CanRead || !prop.CanWrite) return;

                string key = GetObjectPropertyKey(target, propName);
                if (!originalObjectValues.ContainsKey(key))
                {
                    originalObjectValues[key] = prop.GetValue(target, null);
                }

                prop.SetValue(target, Mathf.Clamp01(value), null);
            }
            catch {}
        }

        private static void ApplyDirectBoolProperty(object target, string propName, bool value)
        {
            if (target == null) return;
            try
            {
                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null || prop.PropertyType != typeof(bool) || !prop.CanRead || !prop.CanWrite) return;

                string key = GetObjectPropertyKey(target, propName);
                if (!originalObjectValues.ContainsKey(key))
                {
                    originalObjectValues[key] = prop.GetValue(target, null);
                }

                prop.SetValue(target, value, null);
            }
            catch {}
        }

        private static void RestoreOriginalFloatProperties()
        {
            if (currentVehicle == null) return;

            RestoreScaledFloatProperty(GetPropertyValue(currentVehicle, "engine"), "maxRpm");
            RestoreScaledFloatProperty(GetPropertyValue(currentVehicle, "engine"), "peakRpm");
            RestoreScaledFloatProperty(GetPropertyValue(currentVehicle, "engine"), "rpmLimiterMax");
            RestoreScaledFloatProperty(GetPropertyValue(currentVehicle, "gearbox"), "automaticMaxRpm");

            var runtimeEngine = GetPropertyValue(currentVehicle, "m_engine") ?? currentVehicle.GetComponentInChildren<VehiclePhysics.Engine>();
            RestoreScaledFloatProperty(runtimeEngine, "maxRpm");
            RestoreScaledFloatProperty(runtimeEngine, "peakRpm");
            RestoreScaledFloatProperty(runtimeEngine, "rpmLimiterMax");

            RestoreScaledFloatProperty(GetPropertyValue(currentVehicle, "brakes"), "maxBrakeTorque");
            RestoreScaledFloatProperty(GetPropertyValue(GetPropertyValue(currentVehicle, "m_brakes"), "settings"), "maxBrakeTorque");
        }

        private static void RestoreScaledFloatProperty(object target, string propName)
        {
            if (target == null) return;
            try
            {
                string key = target.GetHashCode() + ":" + propName;
                if (!originalFloatValues.TryGetValue(key, out var originalValue)) return;

                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null || prop.PropertyType != typeof(float) || !prop.CanWrite) return;
                prop.SetValue(target, originalValue, null);
            }
            catch {}
        }

        private static void RestoreOriginalObjectProperties()
        {
            if (currentVehicle == null) return;

            RestoreObjectProperty(GetPropertyValue(currentVehicle, "steeringAids"), "helpRatio");
            RestoreObjectProperty(GetPropertyValue(GetPropertyValue(currentVehicle, "m_steering"), "steeringAids"), "helpRatio");
            RestoreObjectProperty(GetPropertyValue(currentVehicle, "antiLock"), "enabled");
            RestoreObjectProperty(GetPropertyValue(currentVehicle, "tractionControl"), "enabled");
            RestoreObjectProperty(GetPropertyValue(currentVehicle, "stabilityControl"), "enabled");

            try
            {
                var damageSystem = currentVehicle.GetComponentInChildren<CarDamageSystem>(true) ?? currentVehicle.GetComponentInParent<CarDamageSystem>();
                if (damageSystem != null) damageSystem.SetDamageModifier(1.0f);
            }
            catch {}
        }

        private static void RestoreObjectProperty(object target, string propName)
        {
            if (target == null) return;
            try
            {
                string key = GetObjectPropertyKey(target, propName);
                if (!originalObjectValues.TryGetValue(key, out var originalValue)) return;

                var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop == null || !prop.CanWrite) return;
                prop.SetValue(target, originalValue, null);
            }
            catch {}
        }

        private static string GetObjectPropertyKey(object target, string propName)
        {
            return target.GetHashCode() + ":" + propName;
        }
    }

    [HarmonyPatch] 
    public class StabilityPatch
    {
        static MethodBase TargetMethod() => AccessTools.Method(typeof(VPVehicleController), "OnInitialize");
        [HarmonyPostfix] public static void Postfix(object __instance) 
        { 
            if (__instance is MonoBehaviour controller) 
            {
                ModUI.currentVehicle = controller; 
                var runner = new GameObject("Runner").AddComponent<DelayRunner>();
                runner.Init(controller);
            }
        }
    }

    public class DelayRunner : MonoBehaviour
    {
        public DelayRunner(IntPtr ptr) : base(ptr) { }
        private MonoBehaviour _target;
        public void Init(MonoBehaviour controller) { _target = controller; Invoke("Apply", 0.4f); }
        
        private void Apply() { 
            if (_target == null)
            {
                Destroy(this.gameObject);
                return;
            }

            if (MyPlugin.EnableMod.Value)
            {
                ModUI.ApplyEngineRpmMultiplier(_target);
            }

            ModUI.TargetDefenderHandling(_target);
            Destroy(this.gameObject);
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal class IsExternalInit { }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Parameter | AttributeTargets.ReturnValue | AttributeTargets.GenericParameter, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableAttribute : Attribute { public readonly byte[] NullableFlags; public NullableAttribute(byte P_0) { this.NullableFlags = new byte[] { P_0 }; } public NullableAttribute(byte[] P_0) { this.NullableFlags = P_0; } }
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Delegate, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute { public readonly byte Flag; public NullableContextAttribute(byte P_0) { this.Flag = P_0; } }
}
