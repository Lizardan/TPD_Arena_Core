using System.IO;
using UnityEditor;
using UnityEngine;

namespace TPD.Arena.Editor
{
    public static class BattleExportCliMenu
    {
        private const string SampleRequestPath = "tools/sample-battle-request.json";

        [MenuItem("TPD Arena/Export Battle From Sample JSON (FFmpeg)")]
        public static void ExportFromSampleJson()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string jsonPath = Path.Combine(projectRoot, SampleRequestPath);
            string outputPath = Path.Combine(projectRoot, "Exports", "cli_sample_battle.mp4");

            if (!File.Exists(jsonPath))
            {
                EditorUtility.DisplayDialog(
                    "Battle Export CLI",
                    $"Sample request not found:\n{jsonPath}",
                    "OK");
                return;
            }

            BattleVideoEncoderRuntimeRegistration.RegisterFfmpegEncoder();

            if (!BattleExportCli.TryRunWithPaths(jsonPath, outputPath, out string error, out string outputFile))
            {
                EditorUtility.DisplayDialog("Battle Export CLI", error ?? "Export failed.", "OK");
                return;
            }

            EditorUtility.DisplayDialog("Battle Export CLI", $"Saved:\n{outputFile}", "OK");
            EditorUtility.RevealInFinder(outputFile);
        }
    }
}
