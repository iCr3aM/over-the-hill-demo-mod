using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.InteropTypes;
using HarmonyLib;

namespace OverTheHillMod
{
    public static class WeatherManager
    {
        public static void Initialize()
        {
            // 注册天气与时间控制组件。
            ClassInjector.RegisterTypeInIl2Cpp<WeatherComponent>();
            
            // 使用持久对象承载运行时面板逻辑。
            var weatherObj = new GameObject("ModWeather_Object");
            GameObject.DontDestroyOnLoad(weatherObj);
            weatherObj.AddComponent<WeatherComponent>();
            
            MyPlugin.LogSource.LogInfo("天气与时间控制模块已载入。F9 打开面板。");
        }
    }

    public class WeatherComponent : MonoBehaviour
    {
        public WeatherComponent(IntPtr ptr) : base(ptr) { }

        private bool showUI = false;
        private Rect windowRect = new Rect(260, 20, 780, 820);
        private Vector2 weatherPanelScrollPosition = Vector2.zero;
        private static GUIStyle wrapLabelStyle;

        // Runtime reflection cache.
        private Type cozyType;
        private Type weatherProfileType;
        private object cozyInstance;
        private object cozyWeatherModule;
        private object cozyClimateModule;
        private object cozyMicrosplatModule;
        private object cozyTVEModule;
        private object cozyEcosystem;
        private object timeManager;
        private object offlineTimeManager;
        private object cozyWeatherProfileIds;
        private List<object> weatherProfiles = new List<object>();
        private List<string> weatherNames = new List<string>();
        private readonly Dictionary<int, object> weatherProfilesById = new Dictionary<int, object>();
        private readonly Dictionary<int, string> nativeWeatherNamesById = new Dictionary<int, string>();
        private MethodInfo transitionMethod;
        private readonly Dictionary<string, float> forcedWeatherFloats = new Dictionary<string, float>();
        private readonly Dictionary<string, bool> forcedWeatherBools = new Dictionary<string, bool>();
        private bool directWeatherOverride = false;
        private bool experimentalMaterialScan = false;
        private int weatherApplyFrame = 0;
        private int precipitationFxFrame = 0;
        private int materialWriteFrame = 0;
        private int timeApplyFrame = 0;
        private int lastWeatherMaterialWrites = 0;
        private bool applyingHarmonyOverride = false;
        private bool lockGameTime = false;
        private bool showAllNativeWeatherProfiles = false;
        private float forcedDayTime = -1f;
        private static WeatherComponent activeInstance;
        private static int? forcedNativeWeatherId;
        private static int forcedWeatherMacroGroup = 0;
        
        private bool isInitialized = false;
        private string statusMessage = "等待初始化（请先进入游戏场景）...";

        void Awake()
        {
            activeInstance = this;
        }

        void Update()
        {
            // F9 toggles the weather and time panel.
            if (Input.GetKeyDown(KeyCode.F9))
            {
                showUI = !showUI;
                if (showUI && !isInitialized)
                {
                    TryBindCozyWeather();
                }
            }

            if (isInitialized && directWeatherOverride && (weatherApplyFrame++ % 20 == 0))
            {
                ApplyForcedWeatherValues();
            }

            if (isInitialized && lockGameTime && forcedDayTime >= 0f && (timeApplyFrame++ % 10 == 0))
            {
                ApplyGameTimeFromFraction(forcedDayTime, false);
            }
        }

        void LateUpdate()
        {
        }

        private void TryBindCozyWeather()
        {
            try
            {
                activeInstance = this;
                // Locate the active Cozy weather type from loaded assemblies.
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    cozyType = assembly.GetType("DistantLands.Cozy.CozyWeather");
                    if (cozyType != null) break;
                }

                if (cozyType == null)
                {
                    statusMessage = "错误：未在游戏中找到 CozyWeather 天气核心组件。";
                    return;
                }

                // Read the singleton instance exposed by Cozy.
                var instanceProp = cozyType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                                 ?? cozyType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                
                if (instanceProp != null) cozyInstance = instanceProp.GetValue(null);

                if (cozyInstance == null)
                {
                    statusMessage = "已找到天气类，但当前主场景中没有激活的天气实例（请进入地图）。";
                    return;
                }

                // Resolve weather transition entry points.
                cozyWeatherModule = GetMemberValue(cozyInstance, "weatherModule");
                cozyClimateModule = GetMemberValue(cozyInstance, "climateModule");
                cozyMicrosplatModule = FindCozyModule("DistantLands.Cozy.CozyMicrosplatModule");
                cozyTVEModule = FindCozyModule("DistantLands.Cozy.CozyTVEModule");
                cozyEcosystem = GetMemberValue(cozyWeatherModule, "ecosystem") ?? GetMemberValue(cozyWeatherModule, "Ecosystem");
                timeManager = FindSingletonOrObject("TimeManager");
                offlineTimeManager = FindSingletonOrObject("OfflineTimeManager");
                cozyWeatherProfileIds = GetMemberValue(timeManager, "cozyWeatherProfileIds")
                                     ?? GetMemberValue(offlineTimeManager, "cozyWeatherProfileIds")
                                     ?? GetMemberValue(FindSingletonOrObject("CheatsController"), "cozyWeatherProfileIDs");
                weatherProfileType = cozyType.Assembly.GetType("DistantLands.Cozy.Data.WeatherProfile")
                                  ?? cozyType.Assembly.GetType("DistantLands.Cozy.WeatherProfile");

                transitionMethod = null;
                if (cozyEcosystem != null && weatherProfileType != null)
                {
                    var ecosystemType = cozyEcosystem.GetType();
                    transitionMethod = ecosystemType.GetMethod("SetWeather", new Type[] { weatherProfileType, typeof(float) })
                                    ?? ecosystemType.GetMethod("SetWeather", new Type[] { weatherProfileType });
                }
                if (transitionMethod == null)
                {
                    MyPlugin.LogSource.LogWarning("[WeatherMod] 未能定位到 CozyEcosystem.SetWeather，天气按钮可能无法切换。");
                }

                // Collect weather profiles currently available in the scene.
                weatherProfiles.Clear();
                weatherNames.Clear();
                weatherProfilesById.Clear();
                nativeWeatherNamesById.Clear();

                var relationList = GetMemberValue(cozyEcosystem, "weightedWeatherProfiles")
                                ?? GetMemberValue(cozyWeatherModule, "currentWeatherProfiles");
                if (relationList is System.Collections.IEnumerable relationEnumerable)
                {
                    foreach (var relation in relationEnumerable)
                    {
                        var profile = GetMemberValue(relation, "profile");
                        if (profile == null || weatherProfiles.Contains(profile)) continue;
                        AddWeatherProfile(profile);
                    }
                }

                var profilesField = cozyType.GetField("weatherProfiles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                                 ?? cozyType.GetField("m_WeatherProfiles", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                if (weatherProfiles.Count == 0 && profilesField != null)
                {
                    var listObj = profilesField.GetValue(cozyInstance);
                    if (listObj is System.Collections.IEnumerable enumerable)
                    {
                        weatherProfiles.Clear();
                        weatherNames.Clear();
                        foreach (var item in enumerable)
                        {
                            if (item == null) continue;
                            AddWeatherProfile(item);
                        }
                    }
                }

                ScanWeatherProfileAssets();
                ScanNativeWeatherIdNames();
                isInitialized = true;
                statusMessage = $"成功连接！检测到场景中有 {weatherProfiles.Count} 种天气可供切换，原生天气ID {nativeWeatherNamesById.Count} 个。";
            }
            catch (Exception ex)
            {
                statusMessage = $"异常: {ex.Message}";
                MyPlugin.LogSource.LogError($"[WeatherMod] 绑定天气失败: {ex}");
            }
        }

        void OnGUI()
        {
            if (!showUI) return;
            windowRect = ClampWindowToScreen(windowRect, 720f, 700f);
            windowRect = GUI.Window(888, windowRect, (GUI.WindowFunction)DrawWeatherUI, MyPlugin.L("天气与时间面板 (F9)", "Weather and Time Panel (F9)"));
        }

        void DrawWeatherUI(int id)
        {
            GUILayout.BeginVertical();
            weatherPanelScrollPosition = GUILayout.BeginScrollView(weatherPanelScrollPosition, GUILayout.Height(Mathf.Max(120f, windowRect.height - 32f)));
            
            GUILayout.BeginHorizontal();
            GUILayout.Box($"{MyPlugin.L("状态", "Status")}:\n{GetLocalizedStatusMessage()}", GUILayout.ExpandWidth(true));
            if (GUILayout.Button(MyPlugin.L("语言: 中文", "Language: English"), GUILayout.Width(140), GUILayout.Height(42)))
            {
                MyPlugin.ToggleUILanguage();
            }
            GUILayout.EndHorizontal();
            
            if (GUILayout.Button(MyPlugin.L("刷新天气系统", "Refresh weather system")))
            {
                isInitialized = false;
                TryBindCozyWeather();
            }

            GUILayout.Space(10);
            GUILayout.Label(MyPlugin.L("天气预设", "Weather presets"));

            DrawNativeWeatherProfileList();

            GUILayout.Space(10);
            DrawDirectWeatherControls();
            GUILayout.Space(10);
            
            if (isInitialized && cozyInstance != null)
            {
                if (GUILayout.Button(MyPlugin.L("记录天气参数", "Log weather parameters"), GUILayout.Height(30)))
                {
                    DumpAllWeatherParameters();
                }

                if (GUILayout.Button(MyPlugin.L("记录天气ID", "Log weather IDs"), GUILayout.Height(26)))
                {
                    DumpNativeWeatherIds();
                }
            }

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

        private string GetLocalizedStatusMessage()
        {
            if (!MyPlugin.IsEnglishUI) return statusMessage;
            if (string.IsNullOrEmpty(statusMessage)) return string.Empty;
            if (statusMessage.StartsWith("等待初始化")) return "Waiting for initialization. Enter a game scene first.";
            if (statusMessage.StartsWith("错误：未在游戏中找到")) return "CozyWeather component was not found in the game.";
            if (statusMessage.StartsWith("已找到天气类")) return "Weather type found, but no active weather instance is available. Enter the map first.";
            if (statusMessage.StartsWith("成功连接")) return $"Connected. Weather profiles: {weatherProfiles.Count}, native weather IDs: {nativeWeatherNamesById.Count}.";
            if (statusMessage.StartsWith("异常:")) return "Error: " + statusMessage.Substring("异常:".Length).Trim();
            if (statusMessage.StartsWith("已应用天气参数预设：")) return "Weather preset applied: " + statusMessage.Substring("已应用天气参数预设：".Length).Trim();
            if (statusMessage.StartsWith("天气切换失败")) return "Weather switch failed. Refresh the weather system and try again.";
            if (statusMessage.StartsWith("正在平滑过渡")) return "Transitioning to the selected weather.";
            if (statusMessage.StartsWith("已瞬间强制替换")) return "Weather replaced immediately.";
            if (statusMessage.StartsWith("指令发送失败:")) return "Command failed: " + statusMessage.Substring("指令发送失败:".Length).Trim();
            if (statusMessage.StartsWith("Native weather IDs dumped")) return statusMessage;
            if (statusMessage.StartsWith("天气参数已记录")) return "Weather parameters were written to the BepInEx log.";
            if (statusMessage.StartsWith("抓取异常:")) return "Log failed: " + statusMessage.Substring("抓取异常:".Length).Trim();
            return statusMessage;
        }

        private void DrawNativeWeatherProfileList()
        {
            if (!isInitialized || weatherProfiles.Count == 0)
            {
                GUILayout.Label(MyPlugin.L("< 暂无可用原生天气，请先进入地图后刷新 >", "< No native weather is available. Enter a session and refresh. >"), GUI.skin.box);
                return;
            }

            int columns = GetWeatherColumnCount();
            int collapsedCount = columns * 4;
            int visibleCount = showAllNativeWeatherProfiles ? weatherProfiles.Count : Mathf.Min(weatherProfiles.Count, collapsedCount);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{MyPlugin.L("原生天气", "Native weather")}: {weatherProfiles.Count}", GUILayout.Width(150));
            if (weatherProfiles.Count > collapsedCount)
            {
                string toggleText = showAllNativeWeatherProfiles ? MyPlugin.L("收起", "Collapse") : MyPlugin.L($"显示全部 {weatherProfiles.Count}", $"Show all {weatherProfiles.Count}");
                if (GUILayout.Button(toggleText, GUILayout.Height(24)))
                {
                    showAllNativeWeatherProfiles = !showAllNativeWeatherProfiles;
                }
            }
            GUILayout.EndHorizontal();

            float buttonWidth = Mathf.Max(150f, (windowRect.width - 48f) / columns - 8f);
            for (int i = 0; i < visibleCount; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int index = i + column;
                    if (index >= visibleCount)
                    {
                        GUILayout.Space(buttonWidth + 6f);
                        continue;
                    }

                    string name = GetCompactWeatherName(weatherNames[index]);
                    object profile = weatherProfiles[index];

                    if (GUILayout.Button(name, GUILayout.Height(28), GUILayout.Width(buttonWidth)))
                    {
                        ExecuteWeatherChange(profile);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private int GetWeatherColumnCount()
        {
            return windowRect.width >= 960f ? 3 : 2;
        }

        private static string GetCompactWeatherName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Unknown";

            string compact = name.Replace("_FMOD", string.Empty)
                                 .Replace("DistantLands.Cozy.Data.WeatherProfile", string.Empty)
                                 .Replace("(", " (");

            if (compact.Length > 48)
            {
                compact = compact.Substring(0, 45) + "...";
            }

            return compact;
        }

        private void DrawDirectWeatherControls()
        {
            if (!isInitialized || cozyInstance == null)
            {
                GUILayout.Label(MyPlugin.L("直接参数：请先刷新天气系统。", "Direct parameters: refresh the weather system first."), GUI.skin.box);
                return;
            }

            GUILayout.Label("<color=cyan><b>" + MyPlugin.L("直接参数", "Direct Parameters") + "</b></color>");
            directWeatherOverride = GUILayout.Toggle(directWeatherOverride, MyPlugin.L("持续写入直接参数", "Continuously write direct parameters"));
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{MyPlugin.L("覆盖参数", "Overrides")}: {forcedWeatherFloats.Count + forcedWeatherBools.Count}", GUILayout.Width(150));
            if (GUILayout.Button(MyPlugin.L("清除覆盖", "Clear overrides"), GUILayout.Height(24)))
            {
                forcedWeatherFloats.Clear();
                forcedWeatherBools.Clear();
                forcedNativeWeatherId = null;
                forcedWeatherMacroGroup = 0;
                lastWeatherMaterialWrites = 0;
                RefreshWeatherVisuals();
            }
            GUILayout.EndHorizontal();

            GUILayout.Label($"{MyPlugin.L("材质参数命中", "Material writes")}: {lastWeatherMaterialWrites}", GUI.skin.box);

            experimentalMaterialScan = GUILayout.Toggle(experimentalMaterialScan, MyPlugin.L("扫描场景材质（可能影响性能）", "Scan scene materials (may affect performance)"));
            DrawWeatherPresetGrid();

            int columns = GetWeatherColumnCount();
            float columnWidth = Mathf.Max(230f, (windowRect.width - 52f) / columns - 6f);

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(columnWidth));
            DrawPrecipitationWeatherControls();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(columnWidth));
            DrawCloudFogWeatherControls();
            GUILayout.EndVertical();

            if (columns >= 3)
            {
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(columnWidth));
                DrawLightTimeWindControls();
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();

            if (columns < 3)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                DrawLightTimeWindControls();
                GUILayout.EndVertical();
            }
        }

        private void DrawWeatherPresetGrid()
        {
            string[] labels = MyPlugin.IsEnglishUI
                ? new[] { "Clear", "Cloudy", "Fog", "Overcast", "Light Rain", "Heavy Rain", "Snow", "Snowstorm", "Sandstorm", "Dust Storm" }
                : new[] { "晴天", "多云", "浓雾", "阴天", "小雨", "大雨", "雪天", "暴雪", "沙暴", "尘暴" };
            string[] presets = { "clear", "cloudy", "fog", "overcast", "light_rain", "heavy_rain", "snow", "snowstorm", "sandstorm", "duststorm" };
            int columns = GetWeatherColumnCount();
            float buttonWidth = Mathf.Max(120f, (windowRect.width - 48f) / columns - 8f);

            for (int i = 0; i < labels.Length; i += columns)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < columns; column++)
                {
                    int index = i + column;
                    if (index >= labels.Length)
                    {
                        GUILayout.Space(buttonWidth + 6f);
                        continue;
                    }

                    if (GUILayout.Button(labels[index], GUILayout.Height(28), GUILayout.Width(buttonWidth)))
                    {
                        ApplyWeatherPreset(presets[index]);
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPrecipitationWeatherControls()
        {
            GUILayout.Label("<color=yellow>" + MyPlugin.L("降水 / 雪 / 地表", "Precipitation / Snow / Ground") + "</color>");
            DrawWeatherSlider(MyPlugin.L("降水量", "Precipitation"), "currentPrecipitation", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("降水偏移", "Precipitation offset"), "precipitationOffset", -1f, 1f);
            DrawWeatherSlider(MyPlugin.L("降雪量", "Snow amount"), "snowAmount", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("材质雨量", "Material rain"), "materialRainAmount", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("材质湿度", "Material wetness"), "materialWetnessAmount", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("材质雪量", "Material snow"), "materialSnowAmount", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("降雨速度", "Rain speed"), "rainSpeed", 0f, 3f);
            DrawWeatherSlider(MyPlugin.L("积雪速度", "Snow build speed"), "snowSpeed", 0f, 3f);
            DrawWeatherSlider(MyPlugin.L("融雪速度", "Snow melt speed"), "snowMeltSpeed", 0f, 3f);
            DrawWeatherSlider(MyPlugin.L("地表积水", "Ground water"), "groundwaterAmount", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("地面湿度最小", "Min wetness"), "minWetness", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("地面湿度最大", "Max wetness"), "maxWetness", 0f, 1f);
            DrawWeatherToggle(MyPlugin.L("更新积雪", "Update snow"), "updateSnow");
            DrawWeatherToggle(MyPlugin.L("TVE积雪控制", "TVE snow control"), "enableSnowControl");
            DrawWeatherToggle(MyPlugin.L("更新湿地", "Update wetness"), "updateWetness");
            DrawWeatherToggle(MyPlugin.L("雨滴水纹", "Rain ripples"), "updateRainRipples");
            DrawWeatherToggle(MyPlugin.L("水坑", "Puddles"), "updatePuddles");
        }

        private void DrawCloudFogWeatherControls()
        {
            GUILayout.Label("<color=yellow>" + MyPlugin.L("云层 / 雾", "Clouds / Fog") + "</color>");
            DrawWeatherSlider(MyPlugin.L("总云量", "Cloud coverage"), "cloudCoverage", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("积云", "Cumulus"), "cumulus", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("卷云", "Cirrus"), "cirrus", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("高积云", "Altocumulus"), "altocumulus", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("卷层云", "Cirrostratus"), "cirrostratus", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("飞机云/尘带", "Chemtrails / dust"), "chemtrails", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("雨云/乌云", "Nimbus"), "nimbus", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("云细节", "Cloud detail"), "cloudDetailAmount", 0f, 40f);
            DrawWeatherSlider(MyPlugin.L("云厚度", "Cloud thickness"), "cloudThickness", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("云速", "Cloud speed"), "cloudWindSpeed", 0f, 5f);
            DrawWeatherSlider(MyPlugin.L("雾密度", "Fog density"), "fogDensity", 0f, 3f);
            DrawWeatherSlider(MyPlugin.L("雾倍率", "Fog multiplier"), "fogDensityMultiplier", 0f, 2f);
            DrawWeatherSlider(MyPlugin.L("高度雾", "Height fog"), "heightFogIntensity", 0f, 2f);
            DrawWeatherSlider(MyPlugin.L("天空雾", "Sky fog"), "skyFogAmount", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("云雾", "Cloud fog"), "cloudsFogAmount", 0f, 1f);
        }

        private void DrawLightTimeWindControls()
        {
            GUILayout.Label("<color=yellow>" + MyPlugin.L("光照 / 时间 / 风", "Light / Time / Wind") + "</color>");
            DrawGameTimeControls();
            DrawWeatherSlider(MyPlugin.L("滤镜亮度", "Filter value"), "filterValue", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("滤镜饱和度", "Filter saturation"), "filterSaturation", -1f, 1f);
            DrawWeatherSlider(MyPlugin.L("环境光", "Ambient light"), "ambientLightMultiplier", 0f, 3f);
            DrawWeatherSlider(MyPlugin.L("彩虹强度", "Rainbow intensity"), "rainbowIntensity", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("一天时间", "Day percentage"), "dayPercentage", 0f, 1f);
            DrawWeatherSlider(MyPlugin.L("太阳方向", "Sun direction"), "sunDirection", 0f, 360f);
            DrawWeatherSlider(MyPlugin.L("太阳高度", "Sun pitch"), "sunPitch", -10f, 90f);
            DrawWeatherSlider(MyPlugin.L("风速", "Wind speed"), "windSpeed", 0f, 30f);
            DrawWeatherSlider(MyPlugin.L("阵风", "Wind gusting"), "windGusting", 0f, 2f);
            DrawWeatherSlider(MyPlugin.L("风量", "Wind amount"), "windAmount", 0f, 2f);
            DrawWeatherToggle(MyPlugin.L("使用彩虹", "Use rainbow"), "useRainbow");
            DrawWeatherToggle(MyPlugin.L("物理太阳高度", "Physical sun height"), "usePhysicalSunHeight");
        }

        private void ApplyWeatherPreset(string preset)
        {
            if (cozyInstance == null) return;

            ApplyNativeWeatherPreset(preset);

            switch (preset)
            {
                case "clear":
                    SetWeatherFloat("cloudCoverage", 0.15f);
                    SetWeatherFloat("cumulus", 0.15f);
                    SetWeatherFloat("cirrus", 0.1f);
                    SetWeatherFloat("nimbus", 0f);
                    SetWeatherFloat("currentPrecipitation", 0f);
                    SetWeatherFloat("snowAmount", 0f);
                    SetWeatherFloat("materialRainAmount", 0f);
                    SetWeatherFloat("materialWetnessAmount", 0f);
                    SetWeatherFloat("materialSnowAmount", 0f);
                    SetWeatherFloat("fogDensity", 0.25f);
                    SetWeatherFloat("heightFogIntensity", 0.25f);
                    SetWeatherFloat("filterValue", 0.25f);
                    SetWeatherBool("updateRainRipples", false);
                    SetWeatherBool("updatePuddles", false);
                    SetWeatherBool("updateSnow", false);
                    SetWeatherBool("enableSnowControl", false);
                    break;
                case "cloudy":
                    SetWeatherFloat("cloudCoverage", 0.65f);
                    SetWeatherFloat("cumulus", 0.7f);
                    SetWeatherFloat("cirrus", 0.25f);
                    SetWeatherFloat("nimbus", 0.15f);
                    SetWeatherFloat("fogDensity", 0.8f);
                    SetWeatherFloat("heightFogIntensity", 0.6f);
                    SetWeatherFloat("filterValue", 0.12f);
                    break;
                case "fog":
                    SetWeatherFloat("cloudCoverage", 0.45f);
                    SetWeatherFloat("cumulus", 0.35f);
                    SetWeatherFloat("nimbus", 0.05f);
                    SetWeatherFloat("fogDensity", 2.4f);
                    SetWeatherFloat("fogDensityMultiplier", 1.6f);
                    SetWeatherFloat("heightFogIntensity", 1.6f);
                    SetWeatherFloat("filterSaturation", -0.55f);
                    SetWeatherFloat("filterValue", 0.08f);
                    break;
                case "overcast":
                    SetWeatherFloat("cloudCoverage", 1f);
                    SetWeatherFloat("cumulus", 0.9f);
                    SetWeatherFloat("nimbus", 0.85f);
                    SetWeatherFloat("fogDensity", 1.4f);
                    SetWeatherFloat("heightFogIntensity", 1.1f);
                    SetWeatherFloat("filterSaturation", -0.7f);
                    SetWeatherFloat("filterValue", 0.05f);
                    break;
                case "light_rain":
                    SetWeatherFloat("cloudCoverage", 0.8f);
                    SetWeatherFloat("cumulus", 0.55f);
                    SetWeatherFloat("nimbus", 0.45f);
                    SetWeatherFloat("currentPrecipitation", 0.35f);
                    SetWeatherFloat("precipitationOffset", 0.35f);
                    SetWeatherFloat("rainSpeed", 0.8f);
                    SetWeatherFloat("snowAmount", 0f);
                    SetWeatherFloat("materialRainAmount", 0.45f);
                    SetWeatherFloat("materialWetnessAmount", 0.55f);
                    SetWeatherFloat("materialSnowAmount", 0f);
                    SetWeatherFloat("fogDensity", 1.0f);
                    SetWeatherFloat("filterSaturation", -0.35f);
                    SetWeatherFloat("filterValue", 0.08f);
                    SetWeatherBool("updateWetness", true);
                    SetWeatherBool("updateRainRipples", true);
                    SetWeatherBool("updatePuddles", true);
                    SetWeatherBool("updateSnow", false);
                    SetWeatherBool("enableSnowControl", false);
                    break;
                case "heavy_rain":
                    SetWeatherFloat("cloudCoverage", 1f);
                    SetWeatherFloat("cumulus", 0.8f);
                    SetWeatherFloat("nimbus", 1f);
                    SetWeatherFloat("currentPrecipitation", 1f);
                    SetWeatherFloat("precipitationOffset", 0.9f);
                    SetWeatherFloat("rainSpeed", 1.6f);
                    SetWeatherFloat("snowAmount", 0f);
                    SetWeatherFloat("materialRainAmount", 1f);
                    SetWeatherFloat("materialWetnessAmount", 1f);
                    SetWeatherFloat("materialSnowAmount", 0f);
                    SetWeatherFloat("fogDensity", 1.6f);
                    SetWeatherFloat("heightFogIntensity", 1.25f);
                    SetWeatherFloat("filterSaturation", -0.65f);
                    SetWeatherFloat("filterValue", 0.04f);
                    SetWeatherBool("updateWetness", true);
                    SetWeatherBool("updateRainRipples", true);
                    SetWeatherBool("updatePuddles", true);
                    SetWeatherBool("updateSnow", false);
                    SetWeatherBool("enableSnowControl", false);
                    break;
                case "snow":
                    SetWeatherFloat("cloudCoverage", 0.9f);
                    SetWeatherFloat("cumulus", 0.65f);
                    SetWeatherFloat("nimbus", 0.55f);
                    SetWeatherFloat("currentPrecipitation", 0.65f);
                    SetWeatherFloat("precipitationOffset", 0.55f);
                    SetWeatherFloat("snowAmount", 0.65f);
                    SetWeatherFloat("materialRainAmount", 0f);
                    SetWeatherFloat("materialWetnessAmount", 0.35f);
                    SetWeatherFloat("materialSnowAmount", 0.65f);
                    SetWeatherFloat("snowSpeed", 0.9f);
                    SetWeatherFloat("snowMeltSpeed", 0f);
                    SetWeatherFloat("fogDensity", 1.3f);
                    SetWeatherFloat("heightFogIntensity", 1.1f);
                    SetWeatherFloat("filterSaturation", -0.75f);
                    SetWeatherFloat("filterValue", 0.12f);
                    SetWeatherBool("updateSnow", true);
                    SetWeatherBool("enableSnowControl", true);
                    SetWeatherBool("updateWetness", true);
                    SetWeatherBool("updateRainRipples", false);
                    break;
                case "snowstorm":
                    SetWeatherFloat("cloudCoverage", 1f);
                    SetWeatherFloat("cumulus", 0.9f);
                    SetWeatherFloat("nimbus", 1f);
                    SetWeatherFloat("currentPrecipitation", 1f);
                    SetWeatherFloat("precipitationOffset", 1f);
                    SetWeatherFloat("snowAmount", 1f);
                    SetWeatherFloat("materialRainAmount", 0f);
                    SetWeatherFloat("materialWetnessAmount", 0.6f);
                    SetWeatherFloat("materialSnowAmount", 1f);
                    SetWeatherFloat("snowSpeed", 1.8f);
                    SetWeatherFloat("snowMeltSpeed", 0f);
                    SetWeatherFloat("fogDensity", 2.3f);
                    SetWeatherFloat("heightFogIntensity", 1.7f);
                    SetWeatherFloat("windSpeed", 18f);
                    SetWeatherFloat("windGusting", 1.6f);
                    SetWeatherFloat("filterSaturation", -0.9f);
                    SetWeatherFloat("filterValue", 0.03f);
                    SetWeatherBool("updateSnow", true);
                    SetWeatherBool("enableSnowControl", true);
                    SetWeatherBool("updateWetness", true);
                    SetWeatherBool("updateRainRipples", false);
                    break;
                case "sandstorm":
                    SetWeatherFloat("cloudCoverage", 0.75f);
                    SetWeatherFloat("cumulus", 0.35f);
                    SetWeatherFloat("chemtrails", 0.75f);
                    SetWeatherFloat("nimbus", 0.2f);
                    SetWeatherFloat("fogDensity", 2.6f);
                    SetWeatherFloat("heightFogIntensity", 1.6f);
                    SetWeatherFloat("windSpeed", 22f);
                    SetWeatherFloat("windGusting", 1.7f);
                    SetWeatherFloat("filterSaturation", -0.15f);
                    SetWeatherFloat("filterValue", 0.18f);
                    SetWeatherFloat("currentPrecipitation", 0f);
                    SetWeatherFloat("snowAmount", 0f);
                    SetWeatherFloat("materialRainAmount", 0f);
                    SetWeatherFloat("materialWetnessAmount", 0f);
                    SetWeatherFloat("materialSnowAmount", 0f);
                    break;
                case "duststorm":
                    SetWeatherFloat("cloudCoverage", 0.6f);
                    SetWeatherFloat("cumulus", 0.25f);
                    SetWeatherFloat("chemtrails", 0.55f);
                    SetWeatherFloat("fogDensity", 2.0f);
                    SetWeatherFloat("heightFogIntensity", 1.4f);
                    SetWeatherFloat("windSpeed", 14f);
                    SetWeatherFloat("windGusting", 1.2f);
                    SetWeatherFloat("filterSaturation", -0.45f);
                    SetWeatherFloat("filterValue", 0.1f);
                    SetWeatherFloat("currentPrecipitation", 0f);
                    SetWeatherFloat("snowAmount", 0f);
                    SetWeatherFloat("materialRainAmount", 0f);
                    SetWeatherFloat("materialWetnessAmount", 0f);
                    SetWeatherFloat("materialSnowAmount", 0f);
                    break;
            }

            RefreshWeatherVisuals();
            UpdatePrecipitationFx(true);
            statusMessage = $"已应用天气参数预设：{preset}";
        }

        private void DrawGameTimeControls()
        {
            float current = forcedDayTime >= 0f ? forcedDayTime : GetCurrentGameTimeFraction();
            current = Mathf.Repeat(current, 1f);
            int totalMinutes = FractionToTotalMinutes(current);
            int hour = totalMinutes / 60;
            int minute = totalMinutes % 60;

            GUILayout.Space(4);
            GUILayout.Label($"{MyPlugin.L("游戏时间", "Game time")}: {hour:00}:{minute:00}");
            lockGameTime = GUILayout.Toggle(lockGameTime, MyPlugin.L("锁定游戏时间（防止游戏自动流逝）", "Lock game time"));

            GUILayout.BeginHorizontal();
            var newTime = GUILayout.HorizontalSlider(current, 0f, 0.999f);
            if (Mathf.Abs(newTime - current) > 0.0007f)
            {
                forcedDayTime = newTime;
                ApplyGameTimeFromFraction(newTime, true);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("06:00")) SetGameTimePreset(6, 0);
            if (GUILayout.Button("12:00")) SetGameTimePreset(12, 0);
            if (GUILayout.Button("18:00")) SetGameTimePreset(18, 0);
            if (GUILayout.Button("00:00")) SetGameTimePreset(0, 0);
            GUILayout.EndHorizontal();
        }

        private void SetGameTimePreset(int hour, int minute)
        {
            forcedDayTime = TotalMinutesToFraction(hour * 60 + minute);
            ApplyGameTime(hour, minute, true);
        }

        private float GetCurrentGameTimeFraction()
        {
            if (TryReadGameTime(timeManager, out int hour, out int minute)
                || TryReadGameTime(offlineTimeManager, out hour, out minute))
            {
                return TotalMinutesToFraction(hour * 60 + minute);
            }

            var timeModule = GetMemberValue(cozyInstance, "timeModule");
            var currentTime = GetMemberValue(timeModule, "currentTime");
            var percent = GetMemberValue(currentTime, "timeAsPercentage");
            if (percent != null)
            {
                try { return Mathf.Repeat(Convert.ToSingle(percent), 1f); } catch { }
            }

            return 0.5f;
        }

        private static bool TryReadGameTime(object manager, out int hour, out int minute)
        {
            hour = 0;
            minute = 0;
            if (manager == null) return false;

            if (!TryGetIntMember(manager, "Hours", out hour) && !TryGetIntMember(manager, "_Hours", out hour) && !TryGetIntMember(manager, "_Hours_k__BackingField", out hour))
            {
                return false;
            }

            if (!TryGetIntMember(manager, "Minutes", out minute) && !TryGetIntMember(manager, "_Minutes", out minute) && !TryGetIntMember(manager, "_Minutes_k__BackingField", out minute))
            {
                minute = 0;
            }

            hour = ((hour % 24) + 24) % 24;
            minute = Mathf.Clamp(minute, 0, 59);
            return true;
        }

        private void ApplyGameTimeFromFraction(float dayFraction, bool refreshVisuals)
        {
            int totalMinutes = FractionToTotalMinutes(dayFraction);
            ApplyGameTime(totalMinutes / 60, totalMinutes % 60, refreshVisuals);
        }

        private void ApplyGameTime(int hour, int minute, bool refreshVisuals)
        {
            hour = ((hour % 24) + 24) % 24;
            minute = Mathf.Clamp(minute, 0, 59);

            SetTimeManagerClock(timeManager, hour, minute);
            SetTimeManagerClock(offlineTimeManager, hour, minute);

            var timeModule = GetMemberValue(cozyInstance, "timeModule");
            TryInvoke(timeModule, "SetHour", hour);
            TryInvoke(timeModule, "SetMinute", minute);
            TryInvokeNoArg(timeModule, "ManageTime");
            TryInvokeNoArg(timeModule, "Update");

            if (refreshVisuals)
            {
                TryInvokeNoArg(timeManager, "ApplyWeatherAndTime");
                TryInvokeNoArg(cozyInstance, "UpdateShaderVariables");
                RefreshWeatherVisuals();
            }
        }

        private static void SetTimeManagerClock(object manager, int hour, int minute)
        {
            if (manager == null) return;

            SetMemberValue(manager, "Hours", hour);
            SetMemberValue(manager, "_Hours", hour);
            SetMemberValue(manager, "_Hours_k__BackingField", hour);
            SetMemberValue(manager, "Minutes", minute);
            SetMemberValue(manager, "_Minutes", minute);
            SetMemberValue(manager, "_Minutes_k__BackingField", minute);
        }

        private static int FractionToTotalMinutes(float dayFraction)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Repeat(dayFraction, 1f) * 1440f), 0, 1439);
        }

        private static float TotalMinutesToFraction(int totalMinutes)
        {
            int wrapped = ((totalMinutes % 1440) + 1440) % 1440;
            return wrapped / 1440f;
        }

        private void DrawWeatherSlider(string label, string memberName, float min, float max)
        {
            if (!TryGetWeatherFloat(memberName, out var value)) return;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:F2}", GetWrapLabelStyle(), GUILayout.Width(118), GUILayout.MinHeight(32));
            var newValue = GUILayout.HorizontalSlider(value, min, max);
            if (Mathf.Abs(newValue - value) > 0.001f)
            {
                SetWeatherFloat(memberName, newValue);
                RefreshWeatherVisuals();
            }
            if (GUILayout.Button(MyPlugin.L("重置", "Reset"), GUILayout.Width(54)))
            {
                SetWeatherFloat(memberName, Mathf.Clamp((min + max) * 0.5f, min, max));
                RefreshWeatherVisuals();
            }
            GUILayout.EndHorizontal();
        }

        private bool TryGetWeatherFloat(string memberName, out float value)
        {
            if (forcedWeatherFloats.TryGetValue(memberName, out value))
            {
                return true;
            }

            if (IsVirtualWeatherFloat(memberName))
            {
                value = 0f;
                return true;
            }

            foreach (var target in GetWeatherTargets())
            {
                var raw = GetMemberValue(target, memberName);
                if (raw == null) continue;

                try
                {
                    value = Convert.ToSingle(raw);
                    return true;
                }
                catch { }
            }

            value = 0f;
            return false;
        }

        private static bool IsVirtualWeatherFloat(string memberName)
        {
            return memberName == "materialRainAmount"
                || memberName == "materialWetnessAmount"
                || memberName == "materialSnowAmount";
        }

        private bool SetWeatherFloat(string memberName, float value)
        {
            directWeatherOverride = true;
            forcedWeatherFloats[memberName] = value;
            return SetWeatherMemberValue(memberName, value);
        }

        private void DrawWeatherToggle(string label, string memberName)
        {
            if (!TryGetWeatherBool(memberName, out var value)) return;

            var newValue = GUILayout.Toggle(value, label);
            if (newValue != value)
            {
                SetWeatherBool(memberName, newValue);
                RefreshWeatherVisuals();
            }
        }

        private bool TryGetWeatherBool(string memberName, out bool value)
        {
            if (forcedWeatherBools.TryGetValue(memberName, out value))
            {
                return true;
            }

            foreach (var target in GetWeatherTargets())
            {
                var raw = GetMemberValue(target, memberName);
                if (raw == null) continue;

                try
                {
                    value = Convert.ToBoolean(raw);
                    return true;
                }
                catch { }
            }

            value = false;
            return false;
        }

        private bool SetWeatherBool(string memberName, bool value)
        {
            directWeatherOverride = true;
            forcedWeatherBools[memberName] = value;
            return SetWeatherMemberValue(memberName, value);
        }

        [HideFromIl2Cpp]
        private bool SetWeatherMemberValue(string memberName, object value)
        {
            if (IsVirtualWeatherFloat(memberName)) return true;

            bool wrote = false;
            foreach (var target in GetPreferredWeatherTargets(memberName))
            {
                wrote |= SetMemberValue(target, memberName, value);
            }

            return wrote;
        }

        [HideFromIl2Cpp]
        private IEnumerable<object> GetPreferredWeatherTargets(string memberName)
        {
            bool IsOneOf(params string[] names) => names.Contains(memberName);
            var targets = new List<object>();

            void Add(object target)
            {
                if (target != null && !targets.Contains(target)) targets.Add(target);
            }

            if (IsOneOf("currentTemperature", "currentPrecipitation", "snowAmount", "snowMeltSpeed", "groundwaterAmount", "dryingSpeed", "snowSpeed", "rainSpeed", "precipitationOffset", "temperatureOffset", "controlMethod"))
            {
                Add(cozyClimateModule);
            }
            else if (IsOneOf("updateWetness", "minWetness", "maxWetness", "updateRainRipples", "updatePuddles", "updateStreams", "updateSnow", "updateWindStrength"))
            {
                Add(cozyMicrosplatModule);
            }
            else if (IsOneOf("enableMotionControl", "enableSeasonControl", "enableWetnessControl", "enableSnowControl"))
            {
                Add(cozyTVEModule);
            }
            else
            {
                Add(cozyWeatherModule);
                Add(cozyInstance);
                Add(GetMemberValue(cozyInstance, "timeModule"));
                Add(GetMemberValue(cozyInstance, "atmosphereModule"));
                Add(GetMemberValue(cozyInstance, "windModule"));
                Add(GetMemberValue(cozyInstance, "interactionsModule"));
            }

            foreach (var target in targets)
            {
                yield return target;
            }
        }

        [HideFromIl2Cpp]
        private IEnumerable<object> GetWeatherTargets()
        {
            var targets = new List<object>();

            void AddTarget(object target)
            {
                if (target != null && !targets.Contains(target)) targets.Add(target);
            }

            AddTarget(cozyInstance);
            AddTarget(cozyWeatherModule);
            AddTarget(cozyClimateModule);
            AddTarget(cozyMicrosplatModule);
            AddTarget(cozyTVEModule);
            AddTarget(cozyEcosystem);

            if (cozyInstance != null)
            {
                string[] moduleNames =
                {
                    "weatherModule",
                    "climateModule",
                    "timeModule",
                    "atmosphereModule",
                    "windModule",
                    "interactionsModule",
                    "overrideWeather"
                };

                foreach (var moduleName in moduleNames)
                {
                    AddTarget(GetMemberValue(cozyInstance, moduleName));
                }

                var modules = GetMemberValue(cozyInstance, "modules");
                if (modules is System.Collections.IEnumerable enumerable)
                {
                    foreach (var module in enumerable)
                    {
                        AddTarget(module);
                    }
                }
            }

            foreach (var target in targets)
            {
                yield return target;
            }
        }

        private void ScanWeatherProfileAssets()
        {
            if (weatherProfileType == null) return;

            try
            {
                foreach (var profile in Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(weatherProfileType)))
                {
                    AddWeatherProfile(profile);
                }
            }
            catch (Exception ex)
            {
                MyPlugin.LogSource.LogWarning($"[WeatherMod] 扫描 WeatherProfile 资产失败: {ex.Message}");
            }
        }

        [HideFromIl2Cpp]
        private void AddWeatherProfile(object profile)
        {
            profile = NormalizeWeatherProfileObject(profile);
            if (profile == null) return;

            string profileName = GetWeatherProfileName(profile);
            if (TryGetIntMember(profile, "WeatherId", out int id))
            {
                if (weatherProfilesById.ContainsKey(id)) return;
                weatherProfilesById[id] = profile;
            }
            else if (weatherNames.Any(name => NormalizeWeatherName(name) == NormalizeWeatherName(profileName)))
            {
                return;
            }

            weatherProfiles.Add(profile);
            weatherNames.Add(profileName);
        }

        [HideFromIl2Cpp]
        private object NormalizeWeatherProfileObject(object profile)
        {
            if (profile == null || weatherProfileType == null) return profile;
            if (weatherProfileType.IsAssignableFrom(profile.GetType())) return profile;

            if (profile is Il2CppObjectBase il2CppObject)
            {
                try
                {
                    var tryCast = typeof(Il2CppObjectBase).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.Name == "TryCast" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
                    var casted = tryCast?.MakeGenericMethod(weatherProfileType).Invoke(il2CppObject, null);
                    if (casted != null && weatherProfileType.IsAssignableFrom(casted.GetType()))
                    {
                        return casted;
                    }
                }
                catch { }
            }

            return null;
        }

        private void ScanNativeWeatherIdNames()
        {
            RefreshNativeWeatherRuntimeReferences();

            foreach (var source in GetNativeWeatherIdSources())
            {
                var map = GetMemberValue(source, "IdToProfileName");
                ReadNativeWeatherIdMap(map);
            }

            foreach (var pair in weatherProfilesById)
            {
                if (!nativeWeatherNamesById.ContainsKey(pair.Key))
                {
                    nativeWeatherNamesById[pair.Key] = GetWeatherProfileName(pair.Value);
                }
            }
        }

        private void RefreshNativeWeatherRuntimeReferences()
        {
            timeManager = FindSingletonOrObject("TimeManager") ?? timeManager;
            offlineTimeManager = FindSingletonOrObject("OfflineTimeManager") ?? offlineTimeManager;

            cozyWeatherProfileIds = cozyWeatherProfileIds
                                  ?? GetMemberValue(timeManager, "cozyWeatherProfileIds")
                                  ?? GetMemberValue(offlineTimeManager, "cozyWeatherProfileIds")
                                  ?? GetMemberValue(FindSingletonOrObject("CheatsController"), "cozyWeatherProfileIDs")
                                  ?? GetMemberValue(FindSingletonOrObject("CozyDebugTool"), "cozyWeatherProfileIDs")
                                  ?? GetMemberValue(FindSingletonOrObject("StatePhotoModeMenu"), "cozyWeatherProfileIDs")
                                  ?? FindSingletonOrObject("CozyWeatherProfileIDs");
        }

        [HideFromIl2Cpp]
        private IEnumerable<object> GetNativeWeatherIdSources()
        {
            var sources = new List<object>();

            void Add(object source)
            {
                if (source != null && !sources.Contains(source)) sources.Add(source);
            }

            Add(cozyWeatherProfileIds);
            Add(GetMemberValue(timeManager, "cozyWeatherProfileIds"));
            Add(GetMemberValue(offlineTimeManager, "cozyWeatherProfileIds"));
            Add(GetMemberValue(FindSingletonOrObject("CheatsController"), "cozyWeatherProfileIDs"));
            Add(GetMemberValue(FindSingletonOrObject("CozyDebugTool"), "cozyWeatherProfileIDs"));
            Add(GetMemberValue(FindSingletonOrObject("StatePhotoModeMenu"), "cozyWeatherProfileIDs"));
            Add(FindSingletonOrObject("CozyWeatherProfileIDs"));

            foreach (var source in sources)
            {
                yield return source;
            }
        }

        [HideFromIl2Cpp]
        private void ReadNativeWeatherIdMap(object map)
        {
            if (map == null) return;

            if (map is System.Collections.IEnumerable entries)
            {
                foreach (var entry in entries)
                {
                    AddNativeWeatherIdName(GetMemberValue(entry, "Key"), GetMemberValue(entry, "Value"));
                }
            }

            var keys = GetMemberValue(map, "Keys") as System.Collections.IEnumerable;
            if (keys == null) return;

            foreach (var key in keys)
            {
                object value = TryInvokeReturn(map, "get_Item", key);
                AddNativeWeatherIdName(key, value);
            }
        }

        [HideFromIl2Cpp]
        private void AddNativeWeatherIdName(object key, object value)
        {
            if (key == null || value == null) return;

            try
            {
                int id = Convert.ToInt32(key);
                if (!nativeWeatherNamesById.ContainsKey(id))
                {
                    nativeWeatherNamesById[id] = value.ToString();
                }
            }
            catch { }
        }

        private void ApplyNativeWeatherPreset(string preset)
        {
            forcedWeatherMacroGroup = GetMacroGroupForPreset(preset);

            if (!TryFindNativeWeatherIdForPreset(preset, out int weatherId))
            {
                MyPlugin.LogSource.LogWarning($"[WeatherMod] 未找到匹配 {preset} 的原生天气ID，仅写入直接参数。");
                return;
            }

            forcedNativeWeatherId = weatherId;
            ApplyNativeWeatherId(weatherId);
        }

        private bool TryFindNativeWeatherIdForPreset(string preset, out int weatherId)
        {
            ScanNativeWeatherIdNames();

            string[][] keywordGroups = GetNativeWeatherKeywords(preset);
            foreach (var keywords in keywordGroups)
            {
                foreach (var pair in nativeWeatherNamesById)
                {
                    string name = NormalizeWeatherName(pair.Value);
                    if (keywords.All(keyword => name.Contains(keyword)))
                    {
                        weatherId = pair.Key;
                        return true;
                    }
                }

                foreach (var pair in weatherProfilesById)
                {
                    string name = NormalizeWeatherName(GetWeatherProfileName(pair.Value));
                    if (keywords.All(keyword => name.Contains(keyword)))
                    {
                        weatherId = pair.Key;
                        return true;
                    }
                }
            }

            int targetMacro = GetMacroGroupForPreset(preset);
            if (targetMacro != 0)
            {
                foreach (var id in nativeWeatherNamesById.Keys.Concat(weatherProfilesById.Keys).Distinct())
                {
                    if (GetWeatherMacroGroupForId(id) == targetMacro)
                    {
                        weatherId = id;
                        return true;
                    }
                }
            }

            weatherId = 0;
            return false;
        }

        private static string[][] GetNativeWeatherKeywords(string preset)
        {
            switch (preset)
            {
                case "clear": return new[] { new[] { "clear" }, new[] { "sun" }, new[] { "lovely" }, new[] { "dry" } };
                case "cloudy": return new[] { new[] { "cloud" } };
                case "fog": return new[] { new[] { "fog" } };
                case "overcast": return new[] { new[] { "overcast" }, new[] { "nimbus" } };
                case "light_rain": return new[] { new[] { "light", "rain" }, new[] { "drizzle" }, new[] { "rain" } };
                case "heavy_rain": return new[] { new[] { "heavy", "rain" }, new[] { "rainstorm" }, new[] { "thunder" }, new[] { "hail" }, new[] { "storm" } };
                case "snow": return new[] { new[] { "snow" } };
                case "snowstorm": return new[] { new[] { "snowstorm" }, new[] { "blizzard" }, new[] { "snow", "storm" } };
                case "sandstorm": return new[] { new[] { "sandstorm" }, new[] { "sand" } };
                case "duststorm": return new[] { new[] { "duststorm" }, new[] { "dust" } };
                default: return Array.Empty<string[]>();
            }
        }

        private static string NormalizeWeatherName(string name)
        {
            return (name ?? string.Empty).ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        }

        private int GetWeatherMacroGroupForId(int weatherId)
        {
            var config = FindSingletonOrObject("WeatherDetectorConfig") ?? GetMemberValue(FindSingletonOrObject("WeatherDetector"), "weatherDetectorConfig");
            if (config == null) return 0;

            try
            {
                var method = config.GetType().GetMethod("GetMacroGroup", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null) return 0;
                return Convert.ToInt32(method.Invoke(config, new object[] { weatherId }));
            }
            catch
            {
                return 0;
            }
        }

        private static int GetMacroGroupForPreset(string preset)
        {
            switch (preset)
            {
                case "light_rain":
                case "heavy_rain":
                    return 2; // WeatherMacroGroup.Wet
                case "snow":
                case "snowstorm":
                    return 4; // WeatherMacroGroup.Snow
                case "sandstorm":
                case "duststorm":
                    return 8; // WeatherMacroGroup.Unsafe
                case "clear":
                case "cloudy":
                case "fog":
                case "overcast":
                    return 1; // WeatherMacroGroup.Dry
                default:
                    return 0;
            }
        }

        private void ApplyNativeWeatherId(int weatherId, bool transitionCozy = true)
        {
            object profile = GetNativeWeatherProfile(weatherId);

            SetTimeManagerWeather(timeManager, weatherId, profile, transitionCozy);
            SetTimeManagerWeather(offlineTimeManager, weatherId, profile, transitionCozy);

            if (transitionCozy && profile != null)
            {
                ExecuteWeatherChange(profile);
            }

            if (transitionCozy)
            {
                string name = nativeWeatherNamesById.TryGetValue(weatherId, out var nativeName) ? nativeName : GetWeatherProfileName(profile);
                MyPlugin.LogSource.LogInfo($"[WeatherMod] Requested native weather switch: {weatherId} {name}");
            }
        }

        [HideFromIl2Cpp]
        private object GetNativeWeatherProfile(int weatherId)
        {
            if (weatherProfilesById.TryGetValue(weatherId, out var profile)) return profile;

            SetMemberValue(timeManager, "CurrentWeatherProfileId", weatherId);
            SetMemberValue(offlineTimeManager, "CurrentWeatherProfileId", weatherId);

            profile = TryInvokeReturn(timeManager, "GetCurrentProfile") ?? TryInvokeReturn(offlineTimeManager, "GetCurrentProfile");
            if (profile != null)
            {
                AddWeatherProfile(profile);
            }

            return profile;
        }

        [HideFromIl2Cpp]
        private void SetTimeManagerWeather(object manager, int weatherId, object profile, bool invokeNativeMethods)
        {
            if (manager == null) return;

            SetMemberValue(manager, "CurrentWeatherProfileId", weatherId);
            SetMemberValue(manager, "_CurrentWeatherProfileId", weatherId);
            SetMemberValue(manager, "_CurrentWeatherProfileId_k__BackingField", weatherId);

            if (!invokeNativeMethods) return;

            TryInvoke(manager, "RPC_SetWeather", weatherId);
            if (profile != null)
            {
                TryInvoke(manager, "SetWeatherImmediately", profile);
                TryInvoke(manager, "SetWeather", profile);
            }
            TryInvokeNoArg(manager, "ApplyWeatherAndTime");
        }

        public static bool HasForcedWeatherDetectorOverride()
        {
            return activeInstance != null && activeInstance.directWeatherOverride && forcedWeatherMacroGroup != 0;
        }

        public static int GetForcedWeatherMacroGroup()
        {
            return forcedWeatherMacroGroup;
        }

        public static int? GetForcedNativeWeatherId()
        {
            return forcedNativeWeatherId;
        }

        private void RefreshWeatherVisuals()
        {
            ApplyRainSnowShaderGlobals();
            TryInvokeNoArg(cozyInstance, "UpdateShaderVariables");
            TryInvokeNoArg(cozyInstance, "UpdateShaderProperties");
            TryInvokeNoArg(cozyWeatherModule, "UpdateShaderProperties");
            TryInvokeNoArg(cozyMicrosplatModule, "UpdateShaderProperties");
            TryInvokeNoArg(cozyTVEModule, "UpdateTVE");
        }

        public static void ApplyHarmonyWeatherOverride()
        {
            activeInstance?.ApplyPostCozyWeatherOverride();
        }

        private void ApplyPostCozyWeatherOverride()
        {
            if (!isInitialized || !directWeatherOverride) return;
            if (forcedWeatherFloats.Count == 0 && forcedWeatherBools.Count == 0) return;
            if (applyingHarmonyOverride) return;

            try
            {
                applyingHarmonyOverride = true;
                ApplyForcedWeatherValues(false);
            }
            finally
            {
                applyingHarmonyOverride = false;
            }
        }

        private void ApplyForcedWeatherValues()
        {
            ApplyForcedWeatherValues(true);
        }

        private void ApplyForcedWeatherValues(bool refreshVisuals)
        {
            if (forcedWeatherFloats.Count == 0 && forcedWeatherBools.Count == 0) return;

            SetMemberValue(cozyClimateModule, "controlMethod", "native");
            if (forcedNativeWeatherId.HasValue)
            {
                ApplyNativeWeatherId(forcedNativeWeatherId.Value, false);
            }

            foreach (var pair in forcedWeatherFloats)
            {
                SetWeatherMemberValue(pair.Key, pair.Value);
            }

            foreach (var pair in forcedWeatherBools)
            {
                SetWeatherMemberValue(pair.Key, pair.Value);
            }

            ApplyRainSnowShaderGlobals();
            if (refreshVisuals)
            {
                UpdatePrecipitationFx();
                RefreshWeatherVisuals();
            }
            else
            {
                TryInvokeNoArg(cozyMicrosplatModule, "UpdateShaderProperties");
                TryInvokeNoArg(cozyTVEModule, "UpdateTVE");
            }
        }

        private void ApplyRainSnowShaderGlobals()
        {
            float rain = Mathf.Max(GetForcedOrCurrentFloat("currentPrecipitation"), GetForcedOrCurrentFloat("materialRainAmount"));
            float snow = Mathf.Max(GetForcedOrCurrentFloat("snowAmount"), GetForcedOrCurrentFloat("materialSnowAmount"));
            float wetness = Mathf.Max(rain, GetForcedOrCurrentFloat("materialWetnessAmount"), GetForcedOrCurrentFloat("groundwaterAmount"), GetForcedOrCurrentFloat("maxWetness"));
            float sand = Mathf.Max(GetForcedOrCurrentFloat("chemtrails"), GetForcedOrCurrentFloat("fogDensity") / 3f);

            SetShaderGlobalMany(rain, "CZY_WetnessAmount", "_CZY_WetnessAmount", "_Rain_Amount", "_Rain_Coverage", "_Rain_Coverage_Tweak", "_GlobalRainIntensity");
            SetShaderGlobalMany(wetness, "_Wet_Amount", "_GlobalWetness", "_WetnessGlobalValue", "_WetnessMeshValue", "_WetnessWaterMeshValue", "_WetnessDropsMeshValue", "_WetnessIntensityValue");
            SetShaderGlobalMany(snow, "CZY_SnowAmount", "_CZY_SnowAmount", "_Snow_Amount", "_GlobalSnowLevel", "_SnowGlobalValue", "_SnowGlobal", "_GlobalSnow");
            SetShaderGlobalMany(sand, "_Sand_Amount");
            SetShaderGlobalMany(Mathf.Max(rain, snow, wetness), "_Total_Coverage");

            SetShaderGlobalFromModuleId("GlobalRainIntensity", rain);
            SetShaderGlobalFromModuleId("GlobalWetnessParams", wetness);
            SetShaderGlobalFromModuleId("GlobalPuddleParams", wetness);
            SetShaderGlobalFromModuleId("GlobalSnowLevel", snow);
            SetShaderGlobalFromModuleId("GlobalSnowParticulateStrength", snow);

            if (experimentalMaterialScan)
            {
                ApplyRainSnowMaterialProperties(rain, wetness, snow, sand);
            }
            else
            {
                lastWeatherMaterialWrites = 0;
            }
            SetShaderKeywordMany(rain > 0.01f || wetness > 0.01f, "TVE_WETNESS", "TVE_WETNESS_ELEMENT");
            SetShaderKeywordMany(snow > 0.01f, "TVE_SNOW", "TVE_SNOW_ELEMENT", "COZY_SNOW");
        }

        private void ApplyRainSnowMaterialProperties(float rain, float wetness, float snow, float sand)
        {
            materialWriteFrame++;
            if (materialWriteFrame % 180 != 0) return;

            int writes = 0;

            try
            {
                var renderers = Resources.FindObjectsOfTypeAll<Renderer>();
                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;

                    var materials = renderer.sharedMaterials;
                    if (materials == null) continue;

                    foreach (var material in materials)
                    {
                        if (material == null) continue;

                        writes += SetMaterialFloatIfExists(material, rain, "_Rain_Amount", "_Rain_Coverage", "_Rain_Coverage_Tweak", "_GlobalRainIntensity");
                        writes += SetMaterialFloatIfExists(material, wetness, "_Wet_Amount", "_GlobalWetness", "_WetnessGlobalValue", "_WetnessMeshValue", "_WetnessWaterMeshValue", "_WetnessDropsMeshValue", "_WetnessIntensityValue");
                        writes += SetMaterialFloatIfExists(material, snow, "_Snow_Amount", "_GlobalSnowLevel", "_SnowGlobalValue", "_SnowGlobal", "_GlobalSnow");
                        writes += SetMaterialFloatIfExists(material, sand, "_Sand_Amount");
                        writes += SetMaterialFloatIfExists(material, Mathf.Max(rain, wetness, snow, sand), "_Total_Coverage");

                        SetMaterialKeyword(material, rain > 0.01f || wetness > 0.01f, "TVE_WETNESS", "TVE_WETNESS_ELEMENT");
                        SetMaterialKeyword(material, snow > 0.01f, "TVE_SNOW", "TVE_SNOW_ELEMENT", "COZY_SNOW");
                    }
                }
            }
            catch (Exception ex)
            {
                MyPlugin.LogSource.LogWarning($"[WeatherMod] 写入雨雪材质参数失败: {ex.Message}");
            }

            lastWeatherMaterialWrites = writes;
        }

        private static int SetMaterialFloatIfExists(Material material, float value, params string[] names)
        {
            int writes = 0;

            foreach (var name in names)
            {
                try
                {
                    if (!material.HasProperty(name)) continue;
                    material.SetFloat(name, value);
                    writes++;
                }
                catch { }
            }

            return writes;
        }

        private static void SetMaterialKeyword(Material material, bool enabled, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                try
                {
                    if (enabled) material.EnableKeyword(keyword);
                    else material.DisableKeyword(keyword);
                }
                catch { }
            }
        }

        private float GetForcedOrCurrentFloat(string memberName)
        {
            if (forcedWeatherFloats.TryGetValue(memberName, out var forcedValue)) return forcedValue;
            return TryGetWeatherFloat(memberName, out var currentValue) ? currentValue : 0f;
        }

        private static void SetShaderGlobalMany(float value, params string[] names)
        {
            foreach (var name in names)
            {
                try { Shader.SetGlobalFloat(name, value); } catch { }
            }
        }

        private static void SetShaderKeywordMany(bool enabled, params string[] keywords)
        {
            foreach (var keyword in keywords)
            {
                try
                {
                    if (enabled) Shader.EnableKeyword(keyword);
                    else Shader.DisableKeyword(keyword);
                }
                catch { }
            }
        }

        private void SetShaderGlobalFromModuleId(string memberName, float value)
        {
            foreach (var target in GetWeatherTargets())
            {
                var raw = GetMemberValue(target, memberName);
                if (raw == null) continue;

                try
                {
                    Shader.SetGlobalFloat(Convert.ToInt32(raw), value);
                    return;
                }
                catch { }
            }
        }

        private void UpdatePrecipitationFx(bool force = false)
        {
            precipitationFxFrame++;
            if (!force && precipitationFxFrame % 120 != 0) return;

            float rain = GetForcedOrCurrentFloat("currentPrecipitation");
            float snow = GetForcedOrCurrentFloat("snowAmount");
            float strongest = Mathf.Max(rain, snow);
            if (strongest <= 0.01f)
            {
                TryStopCozyParticles(cozyInstance);
                TryStopCozyParticles(GetMemberValue(cozyInstance, "particleFXParent"));
                TryStopCozyParticles(GetMemberValue(cozyInstance, "visualFXParent"));
                return;
            }

            TryPlayCozyParticles(cozyInstance, strongest);
            TryPlayCozyParticles(GetMemberValue(cozyInstance, "particleFXParent"), strongest);
            TryPlayCozyParticles(GetMemberValue(cozyInstance, "visualFXParent"), strongest);
        }

        private static void TryStopCozyParticles(object target)
        {
            if (target == null) return;

            try
            {
                var type = target.GetType();
                var stopNoArg = type.GetMethod("Stop", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (stopNoArg != null)
                {
                    stopNoArg.Invoke(target, null);
                }
            }
            catch { }

            TryStopChildComponents(target);
        }

        private static void TryPlayCozyParticles(object target, float intensity)
        {
            if (target == null) return;

            try
            {
                var type = target.GetType();
                var playFloat = type.GetMethod("Play", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new Type[] { typeof(float) }, null);
                if (playFloat != null)
                {
                    playFloat.Invoke(target, new object[] { intensity });
                    return;
                }

                var playNoArg = type.GetMethod("Play", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (playNoArg != null)
                {
                    playNoArg.Invoke(target, null);
                }
            }
            catch { }

            TryPlayChildComponents(target, intensity);
        }

        private static void TryPlayChildComponents(object target, float intensity)
        {
            if (target == null) return;

            try
            {
                var componentType = target.GetType();
                var method = componentType.GetMethod("GetComponentsInChildren", new Type[] { typeof(Type), typeof(bool) });
                if (method == null) return;

                foreach (var typeName in new[] { "UnityEngine.ParticleSystem", "UnityEngine.VFX.VisualEffect", "DistantLands.Cozy.CozyParticles" })
                {
                    var fxType = FindLoadedType(typeName);
                    if (fxType == null) continue;

                    var components = method.Invoke(target, new object[] { fxType, true }) as System.Collections.IEnumerable;
                    if (components == null) continue;

                    foreach (var component in components)
                    {
                        if (component == null) continue;
                        SetMemberValue(component, "intensity", intensity);
                        SetMemberValue(component, "emissionAmount", intensity);
                        SetMemberValue(component, "rainAmount", intensity);
                        SetMemberValue(component, "snowAmount", intensity);
                        TryPlayCozyParticles(component, intensity);
                    }
                }
            }
            catch { }
        }

        private static void TryStopChildComponents(object target)
        {
            if (target == null) return;

            try
            {
                var componentType = target.GetType();
                var method = componentType.GetMethod("GetComponentsInChildren", new Type[] { typeof(Type), typeof(bool) });
                if (method == null) return;

                foreach (var typeName in new[] { "UnityEngine.ParticleSystem", "UnityEngine.VFX.VisualEffect", "DistantLands.Cozy.CozyParticles" })
                {
                    var fxType = FindLoadedType(typeName);
                    if (fxType == null) continue;

                    var components = method.Invoke(target, new object[] { fxType, true }) as System.Collections.IEnumerable;
                    if (components == null) continue;

                    foreach (var component in components)
                    {
                        TryInvoke(component, "Stop");
                        SetMemberValue(component, "intensity", 0f);
                        SetMemberValue(component, "emissionAmount", 0f);
                        SetMemberValue(component, "rainAmount", 0f);
                        SetMemberValue(component, "snowAmount", 0f);
                    }
                }
            }
            catch { }
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName);
                if (type != null) return type;
            }

            return null;
        }

        [HideFromIl2Cpp]
        private object FindCozyModule(string fullTypeName)
        {
            var directName = fullTypeName.Split('.').Last();
            var direct = GetMemberValue(cozyInstance, directName.Substring(0, 1).ToLowerInvariant() + directName.Substring(1));
            if (direct != null && direct.GetType().FullName == fullTypeName) return direct;

            var modules = GetMemberValue(cozyInstance, "modules");
            if (modules is System.Collections.IEnumerable enumerable)
            {
                foreach (var module in enumerable)
                {
                    if (module == null) continue;
                    if (module.GetType().FullName == fullTypeName) return module;
                }
            }

            return null;
        }

        [HideFromIl2Cpp]
        private void ExecuteWeatherChange(object profile)
        {
            profile = NormalizeWeatherProfileObject(profile);
            if (cozyEcosystem == null || transitionMethod == null || profile == null)
            {
                statusMessage = "天气切换失败：尚未绑定 CozyEcosystem.SetWeather，请点击刷新后重试。";
                return;
            }
            try
            {
                if (transitionMethod.GetParameters().Length == 2)
                {
                    // 调用 TransitionToProfile(profile, 2.0f) 在 2 秒内平滑过渡
                    transitionMethod.Invoke(cozyEcosystem, new object[] { profile, 2.0f });
                    statusMessage = "正在平滑过渡到新天气...";
                }
                else
                {
                    // 调用 SetWeather(profile) 瞬间切换
                    transitionMethod.Invoke(cozyEcosystem, new object[] { profile });
                    statusMessage = "已瞬间强制替换当前天气。";
                }
            }
            catch (Exception ex)
            {
                statusMessage = $"指令发送失败: {ex.Message}";
            }
        }

        private static object GetMemberValue(object target, string name)
        {
            if (target == null) return null;

            var type = target as Type ?? target.GetType();
            var instance = target is Type ? null : target;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.GetIndexParameters().Length == 0)
            {
                try { return prop.GetValue(instance, null); } catch { }
            }

            var field = type.GetField(name, flags);
            if (field != null)
            {
                try { return field.GetValue(instance); } catch { }
            }

            return null;
        }

        private static bool SetMemberValue(object target, string name, object value)
        {
            if (target == null) return false;

            var type = target as Type ?? target.GetType();
            var instance = target is Type ? null : target;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            var prop = type.GetProperty(name, flags);
            if (prop != null && prop.GetIndexParameters().Length == 0 && prop.CanWrite)
            {
                try
                {
                    if (!TryConvertWeatherValue(value, prop.PropertyType, out var converted)) return false;
                    prop.SetValue(instance, converted, null);
                    return true;
                }
                catch { }
            }

            var setter = type.GetMethod("set_" + name, flags);
            if (setter != null && setter.GetParameters().Length == 1)
            {
                try
                {
                    var parameterType = setter.GetParameters()[0].ParameterType;
                    if (!TryConvertWeatherValue(value, parameterType, out var converted)) return false;
                    setter.Invoke(instance, new object[] { converted });
                    return true;
                }
                catch { }
            }

            var field = type.GetField(name, flags);
            if (field != null)
            {
                try
                {
                    if (!TryConvertWeatherValue(value, field.FieldType, out var converted)) return false;
                    field.SetValue(instance, converted);
                    return true;
                }
                catch { }
            }

            return false;
        }

        private static bool TryConvertWeatherValue(object value, Type targetType, out object converted)
        {
            converted = null;
            if (value == null) return false;

            try
            {
                if (targetType.IsAssignableFrom(value.GetType()))
                {
                    converted = value;
                    return true;
                }

                if (targetType == typeof(float))
                {
                    float number = Convert.ToSingle(value);
                    if (float.IsNaN(number) || float.IsInfinity(number)) return false;
                    converted = Mathf.Clamp(number, -100000f, 100000f);
                    return true;
                }

                if (targetType == typeof(double))
                {
                    double number = Convert.ToDouble(value);
                    if (double.IsNaN(number) || double.IsInfinity(number)) return false;
                    converted = Math.Max(-100000.0, Math.Min(100000.0, number));
                    return true;
                }

                if (targetType == typeof(int))
                {
                    converted = Convert.ToInt32(value);
                    return true;
                }

                if (targetType == typeof(bool))
                {
                    converted = Convert.ToBoolean(value);
                    return true;
                }

                if (targetType.IsEnum)
                {
                    if (value is string text)
                    {
                        converted = Enum.Parse(targetType, text, true);
                    }
                    else
                    {
                        converted = Enum.ToObject(targetType, Convert.ToInt32(value));
                    }
                    return true;
                }
            }
            catch { }

            return false;
        }

        private static void TryInvokeNoArg(object target, string methodName)
        {
            if (target == null) return;

            try
            {
                var method = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null && method.GetParameters().Length == 0)
                {
                    method.Invoke(target, null);
                }
            }
            catch { }
        }

        private static object TryInvokeReturn(object target, string methodName, params object[] args)
        {
            if (target == null) return null;

            try
            {
                var method = FindMethod(target.GetType(), methodName, args.Length);
                return method?.Invoke(target, args);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryInvoke(object target, string methodName, params object[] args)
        {
            if (target == null) return false;

            try
            {
                var method = FindMethod(target.GetType(), methodName, args.Length);
                if (method == null) return false;
                method.Invoke(target, args);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo FindMethod(Type type, string methodName, int parameterCount)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                       .FirstOrDefault(m => m.Name == methodName && m.GetParameters().Length == parameterCount);
        }

        private static bool TryGetIntMember(object target, string name, out int value)
        {
            value = 0;
            var raw = GetMemberValue(target, name);
            if (raw == null) return false;

            try
            {
                value = Convert.ToInt32(raw);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static object FindSingletonOrObject(string fullOrShortTypeName)
        {
            var type = FindLoadedType(fullOrShortTypeName);
            if (type == null) return null;

            var instance = GetMemberValue(type, "Instance") ?? GetMemberValue(type, "instance");
            if (instance != null) return instance;

            if (!typeof(UnityEngine.Object).IsAssignableFrom(type)) return null;

            try
            {
                var objects = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.From(type));
                return objects != null && objects.Length > 0 ? objects[0] : null;
            }
            catch
            {
                return null;
            }
        }

        private static string GetWeatherProfileName(object profile)
        {
            if (profile == null) return "未知天气";

            var unityName = GetMemberValue(profile, "name") ?? GetMemberValue(profile, "Name");
            var label = GetMemberValue(profile, "WeatherTypeLabel");
            var id = GetMemberValue(profile, "WeatherId");

            if (unityName != null && !string.IsNullOrWhiteSpace(unityName.ToString()))
            {
                return label != null ? $"{unityName} ({label})" : unityName.ToString();
            }

            if (label != null) return label.ToString();
            if (id != null) return $"Weather #{id}";
            return profile.ToString();
        }

        private void DumpNativeWeatherIds()
        {
            ScanWeatherProfileAssets();
            ScanNativeWeatherIdNames();

            MyPlugin.LogSource.LogInfo("=================== Native weather IDs ===================");
            MyPlugin.LogSource.LogInfo($"Profiles by id: {weatherProfilesById.Count}, native names: {nativeWeatherNamesById.Count}");

            foreach (var pair in nativeWeatherNamesById.OrderBy(x => x.Key))
            {
                MyPlugin.LogSource.LogInfo($"[NativeWeather] {pair.Key} = {pair.Value}");
            }

            foreach (var pair in weatherProfilesById.OrderBy(x => x.Key))
            {
                MyPlugin.LogSource.LogInfo($"[WeatherProfile] {pair.Key} = {GetWeatherProfileName(pair.Value)} ({pair.Value?.GetType().FullName})");
            }

            MyPlugin.LogSource.LogInfo("==========================================================");
            statusMessage = $"Native weather IDs dumped: {nativeWeatherNamesById.Count}, profiles: {weatherProfilesById.Count}";
        }

        private void DumpAllWeatherParameters()
        {
            if (cozyInstance == null) return;
            try
            {
                MyPlugin.LogSource.LogInfo("=================== Weather parameter snapshot ===================");
                
                // Record simple scalar values from the active weather system.
                var fields = cozyType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var f in fields)
                {
                    if (f.FieldType.IsPrimitive || f.FieldType == typeof(string) || f.FieldType.IsEnum)
                    {
                        MyPlugin.LogSource.LogInfo($"[字段] {f.Name} = {f.GetValue(cozyInstance)}");
                    }
                }
                
                var props = cozyType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (var p in props)
                {
                    try
                    {
                        if ((p.PropertyType.IsPrimitive || p.PropertyType == typeof(string) || p.PropertyType.IsEnum) && p.GetIndexParameters().Length == 0)
                        {
                            MyPlugin.LogSource.LogInfo($"[属性] {p.Name} = {p.GetValue(cozyInstance)}");
                        }
                    }
                    catch { }
                }
                MyPlugin.LogSource.LogInfo("======================================================");
                statusMessage = "天气参数已记录到 BepInEx 日志。";
            }
            catch (Exception ex)
            {
                statusMessage = $"抓取异常: {ex.Message}";
            }
        }
    }

    [HarmonyPatch]
    public static class CozyWeatherModulePropogatePatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.Method("DistantLands.Cozy.CozyWeatherModule", "PropogateVariables");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() => WeatherComponent.ApplyHarmonyWeatherOverride();
    }

    [HarmonyPatch]
    public static class CozyWeatherModuleWeightsPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.Method("DistantLands.Cozy.CozyWeatherModule", "UpdateFXWeights");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() => WeatherComponent.ApplyHarmonyWeatherOverride();
    }

    [HarmonyPatch]
    public static class CozyClimateModuleLoopPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.Method("DistantLands.Cozy.CozyClimateModule", "CozyUpdateLoop");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() => WeatherComponent.ApplyHarmonyWeatherOverride();
    }

    [HarmonyPatch]
    public static class CozyWeatherShaderPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.Method("DistantLands.Cozy.CozyWeather", "UpdateShaderVariables");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() => WeatherComponent.ApplyHarmonyWeatherOverride();
    }

    [HarmonyPatch]
    public static class CozyEcosystemUpdatePatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.Method("DistantLands.Cozy.CozyEcosystem", "UpdateEcosystem");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix() => WeatherComponent.ApplyHarmonyWeatherOverride();
    }

    [HarmonyPatch]
    public static class WeatherDetectorConfigMacroPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.Method("WeatherDetectorConfig", "GetMacroGroup");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(ref WeatherMacroGroup __result)
        {
            if (!WeatherComponent.HasForcedWeatherDetectorOverride()) return;
            __result = (WeatherMacroGroup)WeatherComponent.GetForcedWeatherMacroGroup();
        }
    }

    [HarmonyPatch]
    public static class WeatherDetectorIsRainingPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.PropertyGetter("WeatherDetector", "IsRaining");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(ref bool __result)
        {
            if (!WeatherComponent.HasForcedWeatherDetectorOverride()) return;
            __result = WeatherComponent.GetForcedWeatherMacroGroup() == 2;
        }
    }

    [HarmonyPatch]
    public static class WeatherDetectorIsSnowingPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.PropertyGetter("WeatherDetector", "IsSnowing");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(ref bool __result)
        {
            if (!WeatherComponent.HasForcedWeatherDetectorOverride()) return;
            __result = WeatherComponent.GetForcedWeatherMacroGroup() == 4;
        }
    }

    [HarmonyPatch]
    public static class WeatherDetectorIsDustyPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.PropertyGetter("WeatherDetector", "IsDusty");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(ref bool __result)
        {
            if (!WeatherComponent.HasForcedWeatherDetectorOverride()) return;
            __result = WeatherComponent.GetForcedWeatherMacroGroup() == 8;
        }
    }

    [HarmonyPatch]
    public static class WeatherDetectorIsSandyPatch
    {
        static MethodBase TargetMethod() => WeatherHarmonyTargets.PropertyGetter("WeatherDetector", "IsSandy");
        static bool Prepare() => TargetMethod() != null;
        static void Postfix(ref bool __result)
        {
            if (!WeatherComponent.HasForcedWeatherDetectorOverride()) return;
            __result = WeatherComponent.GetForcedWeatherMacroGroup() == 8;
        }
    }

    public static class WeatherHarmonyTargets
    {
        public static MethodBase Method(string typeName, string methodName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type == null) continue;
                return AccessTools.Method(type, methodName);
            }

            return null;
        }

        public static MethodBase PropertyGetter(string typeName, string propertyName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type == null) continue;

                var prop = AccessTools.Property(type, propertyName);
                if (prop != null) return prop.GetGetMethod(true);
                return AccessTools.Method(type, "get_" + propertyName);
            }

            return null;
        }
    }
}
