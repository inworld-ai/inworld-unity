/*************************************************************************************************
 * Copyright 2022-2024 Theai, Inc. dba Inworld AI
 *
 * Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
 * that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
 *************************************************************************************************/

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Inworld
{
    [InitializeOnLoad]
    public class InworldBuildProcessor : IPreprocessBuildWithReport
    {
        static InworldBuildProcessor()
        {
            AssetDatabase.importPackageCompleted += _ =>
            {
                if (InworldAI.Initialized)
                    return;
                _UpgradeIntensity();
                _AddDebugMacro();
                VersionChecker.CheckVersionUpdates();
                if (VersionChecker.IsLegacyPackage)
                    VersionChecker.NoticeLegacyPackage();
                InworldAI.Initialized = true;
                _SetDefaultUserName();
            };
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        static void _UpgradeIntensity()
        {
            if (GraphicsSettings.currentRenderPipeline == null)
                return;
            string[] data = AssetDatabase.FindAssets("t:SceneAsset", new[] { "Assets/Inworld/Inworld.Samples.Innequin", "Assets/Inworld/Inworld.Samples.RPM" });
            foreach (string str in data)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(str);
                Debug.Log($"Process on {scenePath}");
                Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    light.intensity = 100f;
                }
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
        }
        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.EnteredEditMode:
                    if (InworldAI.IsDebugMode)
                        _AddDebugMacro();
                    else
                        _RemoveDebugMacro();
                    break;
            }
        }
        /// <summary>
        /// Remove all the Inworld logs. those log will not be printed out in the runtime.
        /// Needs to be public to be called outside Unity.
        /// </summary>
        /// <param name="report"></param>
        public void OnPreprocessBuild(BuildReport report)
        {
            if (Debug.isDebugBuild || InworldAI.IsDebugMode)
                return;
            _RemoveDebugMacro();
        }

#if UNITY_WEBGL
        [UnityEditor.Callbacks.PostProcessBuild(1)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            string indexPath = $"{pathToBuiltProject}/index.html";

            if (!System.IO.File.Exists(indexPath))
            {
                Debug.LogError("Cannot load index file.");
                return;
            }
            string indexData = System.IO.File.ReadAllText(indexPath);

            string dependencies = "<script src='./InworldMicrophone.js'></script>";

            if (!indexData.Contains(dependencies))
            {
                indexData = indexData.Insert(indexData.IndexOf("</head>", System.StringComparison.Ordinal), $"\n{dependencies}\n");
                System.IO.File.WriteAllText(indexPath, indexData);
            }
            System.IO.File.WriteAllText($"{pathToBuiltProject}/InworldMicrophone.js", InworldAI.WebGLMicModule.text);
            System.IO.File.WriteAllText($"{pathToBuiltProject}/AudioResampler.js", InworldAI.WebGLMicResampler.text);
        }
#endif
        static void _SetDefaultUserName()
        {
            string userName = CloudProjectSettings.userName;
            InworldAI.User.Name = !string.IsNullOrEmpty(userName) && userName.Split('@').Length > 1 ? userName.Split('@')[0] : userName;
        }
        static void _AddDebugMacro()
        {
            BuildTargetGroup buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
            string strSymbols = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            if (!strSymbols.Contains("INWORLD_DEBUG"))
                strSymbols = string.IsNullOrEmpty(strSymbols) ? "INWORLD_DEBUG" : strSymbols + ";INWORLD_DEBUG";
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, strSymbols);
        }
        static void _RemoveDebugMacro()
        {
            BuildTargetGroup buildTargetGroup = BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget);
            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(buildTargetGroup);
            string strSymbols = PlayerSettings.GetScriptingDefineSymbols(namedBuildTarget);
            strSymbols = strSymbols.Replace(";INWORLD_DEBUG", "").Replace("INWORLD_DEBUG", "");
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, strSymbols);
        }
        public int callbackOrder { get; }
    }
}
#endif