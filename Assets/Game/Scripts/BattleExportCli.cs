using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace TPD.Arena
{
    public static class BattleExportCli
    {
        private const string GameScenePath = "Assets/Game/Scenes/Game.unity";

        public static void Run()
        {
            try
            {
                if (HasArg("-forceFFmpeg"))
                    BattleVideoEncoderRuntimeRegistration.RegisterFfmpegEncoder();

                string jsonPath = GetArg("-exportJson");
                string outputPath = GetArg("-output");

                if (string.IsNullOrWhiteSpace(jsonPath) || string.IsNullOrWhiteSpace(outputPath))
                {
                    Fail("Usage: -exportJson <path> -output <path> [-forceFFmpeg]");
                    return;
                }

                RunWithPaths(jsonPath, outputPath);
            }
            catch (Exception ex)
            {
                Fail($"Battle export CLI failed: {ex}");
            }
        }

        public static bool TryRunWithPaths(string jsonPath, string outputPath, out string error, out string outputFile)
        {
            error = null;
            outputFile = null;

            BattleRequestJson request = BattleRequestJson.LoadFromFile(jsonPath, out string parseError);
            if (request == null)
            {
                error = parseError;
                return false;
            }

            EnsureGameSceneLoaded();

            BattleController controller = UnityEngine.Object.FindFirstObjectByType<BattleController>();
            if (controller == null)
            {
                error = "BattleController was not found in the loaded scene.";
                return false;
            }

            try
            {
                BattleRequestResolver.ApplyToController(controller, request);
            }
            catch (InvalidOperationException ex)
            {
                error = ex.Message;
                return false;
            }

            controller.CalculateBattle();

            if (!controller.TryExportBattleVideo(outputPath, out BattleVideoExportService.ExportResult result))
            {
                error = result.error ?? "Battle export failed.";
                return false;
            }

            outputFile = result.outputPath;
            Debug.Log($"Battle video exported to: {result.outputPath}");
            return true;
        }

        public static void RunWithPaths(string jsonPath, string outputPath)
        {
            if (!TryRunWithPaths(jsonPath, outputPath, out string error, out _))
                Fail(error);

            Quit(0);
        }

        private static void EnsureGameSceneLoaded()
        {
#if UNITY_EDITOR
            if (SceneManager.GetActiveScene().name != "Game")
                EditorSceneManager.OpenScene(GameScenePath);
#else
            if (SceneManager.sceneCount == 0 || SceneManager.GetActiveScene().name != "Game")
                SceneManager.LoadScene(0);
#endif
        }

        private static bool HasArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return null;
        }

        private static void Fail(string message)
        {
            Debug.LogError(message);
            Quit(1);
        }

        private static void Quit(int exitCode)
        {
#if UNITY_EDITOR
            EditorApplication.Exit(exitCode);
#else
            Application.Quit(exitCode);
#endif
        }
    }
}
