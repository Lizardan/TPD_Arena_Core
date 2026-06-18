using System;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace TPD.Arena.Editor
{
    internal static class BattleExportGameViewHelper
    {
        private static int savedSizeIndex = -1;

        public static bool TrySetResolution(int width, int height)
        {
            try
            {
                savedSizeIndex = GetSelectedSizeIndex();
                int index = FindOrAddFixedSize(width, height);
                if (index < 0)
                    return false;

                SetSelectedSizeIndex(index);
                EditorApplication.QueuePlayerLoopUpdate();
                InternalEditorUtility.RepaintAllViews();
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Restore()
        {
            if (savedSizeIndex < 0)
                return;

            try
            {
                SetSelectedSizeIndex(savedSizeIndex);
                InternalEditorUtility.RepaintAllViews();
            }
            catch
            {
                // ignore restore failures
            }
            finally
            {
                savedSizeIndex = -1;
            }
        }

        public static void ReportProgress(int current, int total)
        {
            if (total <= 0)
                return;

            float progress = Mathf.Clamp01(current / (float)total);
            EditorUtility.DisplayProgressBar("Exporting MP4", $"Frame {current}/{total}", progress);
        }

        public static void ClearProgress()
        {
            EditorUtility.ClearProgressBar();
        }

        private static int GetSelectedSizeIndex()
        {
            Type gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            PropertyInfo prop = gameViewType.GetProperty(
                "selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
            return (int)prop.GetValue(gameView);
        }

        private static void SetSelectedSizeIndex(int index)
        {
            Type gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            PropertyInfo prop = gameViewType.GetProperty(
                "selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
            prop.SetValue(gameView, index);
        }

        private static int FindOrAddFixedSize(int width, int height)
        {
            object group = GetSizeGroup(GameViewSizeGroupType.Standalone);
            Type groupType = group.GetType();

            int builtinCount = (int)groupType.GetMethod("GetBuiltinCount").Invoke(group, null);
            int customCount = (int)groupType.GetMethod("GetCustomCount").Invoke(group, null);

            for (int i = 0; i < builtinCount + customCount; i++)
            {
                if (TryGetSizeDimensions(group, groupType, i, out int w, out int h) && w == width && h == height)
                    return i;
            }

            string label = $"BattleExport {width}x{height}";
            object sizeEntry = CreateFixedSizeEntry(width, height, label);
            groupType.GetMethod("AddCustomSize").Invoke(group, new[] { sizeEntry });

            return builtinCount + customCount;
        }

        private static bool TryGetSizeDimensions(object group, Type groupType, int index, out int width, out int height)
        {
            width = 0;
            height = 0;
            object size = groupType.GetMethod("GetGameViewSize").Invoke(group, new object[] { index });
            if (size == null)
                return false;

            Type sizeType = size.GetType();
            width = (int)sizeType.GetProperty("width").GetValue(size);
            height = (int)sizeType.GetProperty("height").GetValue(size);
            return true;
        }

        private static object CreateFixedSizeEntry(int width, int height, string label)
        {
            Type gameViewSizeType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSize");
            Type enumType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizeType");
            const int fixedResolution = 1;

            ConstructorInfo ctor = gameViewSizeType.GetConstructor(new[]
            {
                enumType, typeof(int), typeof(int), typeof(string)
            });
            return ctor.Invoke(new object[] { fixedResolution, width, height, label });
        }

        private static object GetSizeGroup(GameViewSizeGroupType groupType)
        {
            Type sizesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizes");
            Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
            object instance = singletonType.GetProperty("instance").GetValue(null);
            MethodInfo getGroup = sizesType.GetMethod("GetGroup");
            return getGroup.Invoke(instance, new object[] { (int)groupType });
        }
    }
}
