using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace BikeGame.EditorTools
{
    /// <summary>
    /// Меню Bike → Android: настройка проекта под телефон и сборка одной кнопкой.
    ///   1. Настроить проект для Android — имя, пакет, иконка, горизонтальный экран, IL2CPP + ARM64, Android 7.0+.
    ///   2. Собрать APK — файл, который ставится прямо на телефон (для проверки).
    ///   3. Собрать AAB — файл для загрузки в Google Play (нужен свой ключ подписи, см. README).
    /// Нужен модуль «Android Build Support» в Unity Hub (с Android SDK & NDK Tools и OpenJDK).
    /// Названия и пакет, которые вы поменяли сами в Player Settings, не перезаписываются.
    /// </summary>
    public static class AndroidBuild
    {
        public const string Company = "BikeGame";
        public const string Product = "Mountain Bike Free Ride";
        public const string PackageId = "com.bikegame.freeride";
        private const string IconFolder = "Assets/_Project/Art/Icon";
        private const string BuildFolder = "Builds/Android";

        [MenuItem("Bike/Android/1. Настроить проект для Android", priority = 100)]
        private static void ConfigureMenu()
        {
            Configure();
            EditorUtility.DisplayDialog("Android",
                "Project is set up for Android:\n" +
                $"• name: {PlayerSettings.productName}\n" +
                $"• package: {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)}\n" +
                "• landscape, fullscreen, buttons stay clear of the camera notch\n" +
                "• IL2CPP + ARM64, Android 7.0 and newer\n" +
                "• app icon from Art/Icon\n\n" +
                "Next: Bike → Android → 2. Build APK.", "OK");
        }

        [MenuItem("Bike/Android/2. Собрать APK (поставить на телефон)", priority = 101)]
        private static void BuildApk() => Build(false);

        [MenuItem("Bike/Android/3. Собрать AAB (для Google Play)", priority = 102)]
        private static void BuildAab() => Build(true);

        [MenuItem("Bike/Android/Открыть папку со сборками", priority = 120)]
        private static void OpenFolder()
        {
            Directory.CreateDirectory(BuildFolder);
            EditorUtility.RevealInFinder(BuildFolder);
        }

        /// <summary>Настройки Player Settings для Android (можно вызывать сколько угодно раз).</summary>
        public static void Configure()
        {
            var android = NamedBuildTarget.Android;

            // названия и пакет — только если остались стандартными (свои не трогаем)
            if (string.IsNullOrEmpty(PlayerSettings.companyName) || PlayerSettings.companyName == "DefaultCompany")
                PlayerSettings.companyName = Company;
            string folder = Path.GetFileName(Path.GetDirectoryName(Application.dataPath));
            if (string.IsNullOrEmpty(PlayerSettings.productName) || PlayerSettings.productName == folder)
                PlayerSettings.productName = Product;
            string id = PlayerSettings.GetApplicationIdentifier(android);
            if (string.IsNullOrEmpty(id) || id.StartsWith("com.DefaultCompany", StringComparison.OrdinalIgnoreCase) ||
                id.StartsWith("com.Company", StringComparison.OrdinalIgnoreCase) || id.StartsWith("com.unity", StringComparison.OrdinalIgnoreCase))
                PlayerSettings.SetApplicationIdentifier(android, PackageId);
            if (PlayerSettings.Android.bundleVersionCode < 1) PlayerSettings.Android.bundleVersionCode = 1;

            // экран: только горизонтально (телефон можно перевернуть в обе стороны), на весь экран
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.Android.startInFullscreen = true;
            PlayerSettings.Android.renderOutsideSafeArea = true;      // картинка под вырезом, кнопки — внутри SafeArea
            PlayerSettings.gcIncremental = true;                       // сборка мусора по кусочкам между кадрами — без рывков
            PlayerSettings.Android.optimizedFramePacing = true;       // ровные 60 кадров без рывков

            // IL2CPP + ARM64: требование Google Play, код работает быстрее
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            if ((int)PlayerSettings.Android.minSdkVersion < (int)AndroidSdkVersions.AndroidApiLevel24)
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;   // Android 7.0+
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;   // самый новый из установленных

            SetIcons();
            if (File.Exists(FreeRideSceneBuilder.ScenePath)) FreeRideSceneBuilder.AddSceneToBuild(FreeRideSceneBuilder.ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Bike] Проект настроен для Android: " + PlayerSettings.GetApplicationIdentifier(android));
        }

        /// <summary>Иконка: общая (app_icon) и адаптивная для Android 8+ (фон + байк отдельными слоями).</summary>
        private static void SetIcons()
        {
            var full = AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + "/app_icon.png");
            var back = AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + "/icon_background.png");
            var front = AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + "/icon_foreground.png");
            if (full == null) { Debug.LogWarning("[Bike] Нет иконки " + IconFolder + "/app_icon.png — останется стандартная."); return; }

            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { full }, IconKind.Any);   // Default Icon

            // типы иконок Android (адаптивная, круглая, обычная) есть, только если установлен модуль Android
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android)) return;
            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                bool adaptive = kind.ToString().IndexOf("Adaptive", StringComparison.OrdinalIgnoreCase) >= 0;
                foreach (PlatformIcon icon in icons)
                {
                    if (adaptive && back != null && front != null && icon.maxLayerCount >= 2) icon.SetTextures(back, front);
                    else icon.SetTextures(full);
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
        }

        private static void Build(bool appBundle)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                EditorUtility.DisplayDialog("No Android module",
                    "To build for a phone, add the Android module:\n\n" +
                    "Unity Hub → Installs → ⚙ on your Unity version → Add modules →\n" +
                    "\"Android Build Support\" with \"OpenJDK\" and \"Android SDK & NDK Tools\".\n\n" +
                    "Then restart Unity and choose Bike → Android again.", "OK");
                return;
            }
            if (!File.Exists(FreeRideSceneBuilder.ScenePath))
            {
                if (!EditorUtility.DisplayDialog("No scene", "The Free Ride scene has not been created yet. Create it now?", "Create", "Cancel")) return;
                FreeRideSceneBuilder.Build();
                if (!File.Exists(FreeRideSceneBuilder.ScenePath)) return;
            }

            Configure();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Debug.LogError("[Bike] Не удалось переключить платформу на Android (File → Build Profiles → Android → Switch Platform).");
                return;
            }

            if (appBundle)
            {
                if (!PlayerSettings.Android.useCustomKeystore &&
                    !EditorUtility.DisplayDialog("Signing key",
                        "For Google Play the file must be signed with your own key (Player Settings → Android → Publishing Settings → Keystore Manager). " +
                        "Without it a test AAB is built that Google Play will not accept.\n\nBuild anyway?", "Build", "Cancel"))
                    return;
                PlayerSettings.Android.bundleVersionCode++;     // Google Play принимает только растущий номер версии
            }
            EditorUserBuildSettings.buildAppBundle = appBundle;

            var scenes = new List<string>();
            foreach (var s in EditorBuildSettings.scenes) if (s.enabled && File.Exists(s.path)) scenes.Add(s.path);
            if (!scenes.Contains(FreeRideSceneBuilder.ScenePath)) scenes.Insert(0, FreeRideSceneBuilder.ScenePath);

            Directory.CreateDirectory(BuildFolder);
            string name = SafeName(PlayerSettings.productName) + "_" + SafeName(PlayerSettings.bundleVersion) +
                          (appBundle ? "_" + PlayerSettings.Android.bundleVersionCode + ".aab" : ".apk");
            string path = Path.Combine(BuildFolder, name);

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });

            if (report.summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[Bike] Готово: {path} ({report.summary.totalSize / (1024f * 1024f):0.0} МБ, {report.summary.totalTime.TotalMinutes:0.0} мин).");
                EditorUtility.RevealInFinder(path);
            }
            else Debug.LogError($"[Bike] Сборка не удалась ({report.summary.result}): ошибки — в Console выше.");
        }

        private static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Game";
            foreach (char ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Replace(' ', '_');
        }
    }
}
