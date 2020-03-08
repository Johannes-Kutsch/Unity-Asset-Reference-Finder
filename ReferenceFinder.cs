using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Plugins.ScriptableVariables.Editor {
    public class ReferenceFinder : EditorWindow
    {
        private Vector2 scrollViewPosition = Vector2.zero;

        private readonly List<GameObject> prefabReferences = new List<GameObject>();
        private readonly List<ScriptableObject> scriptableObjectReferences = new List<ScriptableObject>();
        private readonly List<MonoBehaviour> sceneReferences = new List<MonoBehaviour>();
    
        private List<string> paths;
        private Object[] sceneObjects;


        private Object[] toFindObjects;
        private Object[] toFindObjectsAfterLayout;

        private bool subscribedToUpdate;
        private const float FrameTime = 0.05f;
        private readonly Stopwatch stopwatch = new Stopwatch();

        private int outerSearchIndex;
        private int innerSearchIndex;
        private bool assetsSearched;

        private string progressBarText;
        private float progressBarFill;
    
        [MenuItem("Assets/Find References", false, 39)]
        private static void FindSelectedAssetReferences()
        {
            if( Selection.objects.Length > 1) {
                FindObjectReferences(Selection.objects);
            
            } else {
                FindObjectReferences(Selection.activeObject);
            }
        }

        public static void FindObjectReferences(Object asset)
        {
            FindObjectReferences(new[] {asset});
        }
    
        public static void FindObjectReferences(Object[] assets)
        {
            ReferenceFinder window = GetWindow<ReferenceFinder>(true, "Find References", true);
            window.position = new Rect(new Vector2(200, 200), new Vector2(300, 350));
            window.toFindObjects = assets;
            window.FindObjectReferences();
        }

        #region EditorWindow
    
        private void OnGUI()
        {
            GUILayout.Space(5);
            scrollViewPosition = EditorGUILayout.BeginScrollView(scrollViewPosition);

            if (toFindObjects != null)
            {
                GUILayout.BeginHorizontal();
                if (toFindObjects.Length == 1)
                {
                    DrawLayoutObjectButton(toFindObjects[0]);
                    GUILayout.Label($" is referenced by...");
                }
                else
                {
                    GUILayout.Label($"{toFindObjects.Length} items");
                    if (GUILayout.Button("\u25B6", EditorStyles.miniButtonRight, GUILayout.MaxWidth(20)))
                    {
                        toFindObjectsAfterLayout = toFindObjects;
                    }
                    GUILayout.Label($"are referenced by...");
                }
                GUILayout.EndHorizontal();
            }

            if (subscribedToUpdate) {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), progressBarFill, assetsSearched ? "Finding Scene References..." : "Finding Asset References...");
            }

            DrawLayoutGroup(prefabReferences, "Prefabs" + (prefabReferences.Count <= 0 ? "" : ":"));
            DrawLayoutGroup(scriptableObjectReferences, "Scriptable Objects" + (scriptableObjectReferences.Count <= 0 ? "" : ":"));
            DrawLayoutGroup(sceneReferences, "Scene References" + (sceneReferences.Count <= 0 ? "" : ":"));

            EditorGUILayout.EndScrollView();

            if (toFindObjectsAfterLayout == null) {
                return;
            }

            toFindObjects = toFindObjectsAfterLayout;
            toFindObjectsAfterLayout = null;
            FindObjectReferences();
        }

        private void DrawLayoutGroup<T>(IReadOnlyList<T> references, string footer) where T: Object
        {
            GUILayout.Space(5);

            GUILayout.BeginHorizontal();
            GUILayout.Label($" {references.Count} {footer}", EditorStyles.boldLabel);
            GUILayout.EndHorizontal();

            GUILayout.Space(5);

            for (int i = references.Count - 1; i >= 0; --i)
            {
                DrawLayoutItem(i, references[i]);
            }
        }

        private void DrawLayoutItem(int i, Object obj)
        {
            if (obj == null) {
                return;
            }

            GUILayout.BeginHorizontal();
            DrawLayoutObjectButton(obj);
            GUILayout.EndHorizontal();
        }

        private void DrawLayoutObjectButton(Object obj)
        {
            GUIStyle style = EditorStyles.miniButtonLeft;
            style.alignment = TextAnchor.MiddleLeft;

            if (GUILayout.Button(obj.name, style))
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            if (!GUILayout.Button("\u25B6", EditorStyles.miniButtonRight, GUILayout.MaxWidth(20))) {
                return;
            }

            var objs = new Object[1];
            objs[0] = obj;
            toFindObjectsAfterLayout = objs;
        }
    
        #endregion

        #region search
    
        private void FindObjectReferences()
        {
            outerSearchIndex = 0;
            innerSearchIndex = 0;
            assetsSearched = false;
        
            prefabReferences.Clear();
            scriptableObjectReferences.Clear();
            sceneReferences.Clear();
        
            sceneObjects = FindObjectsOfType(typeof(MonoBehaviour));
            paths = new List<string>();
            UpdateFilePaths("Assets", ref paths, ".prefab", ".asset");
        
            FindObjectReferencesAsync();
        }

        private void FindObjectReferencesAsync()
        {
            if (!subscribedToUpdate) {
                SubscribeAsyncSearch();
            }
        
            stopwatch.Start();

            bool completed = false;
        
            if (!assetsSearched) {
                SearchReferencingAssets(out completed);
            }

            if (assetsSearched) {
                SearchReferencingMonoBehaviours(out completed);
            }

            if (!completed) {
                Repaint();
                return;
            }
        
            EditorUtility.ClearProgressBar();
            UnsubscribeAsyncSearch();
            Repaint();
        }

        private void SearchReferencingAssets(out bool completed)
        {
            while (outerSearchIndex < paths.Count) {
                Object asset = AssetDatabase.LoadMainAssetAtPath(paths[outerSearchIndex]);
            
                while (innerSearchIndex < toFindObjects.Length) {
                    ParseAsset(asset, toFindObjects[innerSearchIndex]);
                    innerSearchIndex++;
                }

                innerSearchIndex = 0;
                outerSearchIndex++;
                progressBarFill = (float) outerSearchIndex / paths.Count / 2;

                if (FrameTimeElapsed()) {
                    completed = false;
                    return;
                }
            }
        
            outerSearchIndex = 0;
            assetsSearched = true;
            completed = true;
        }
    
        private void SearchReferencingMonoBehaviours(out bool completed)
        {
            while (outerSearchIndex < sceneObjects.Length) {
                while (innerSearchIndex < toFindObjects.Length) {
                    if (sceneObjects[outerSearchIndex] != null && sceneObjects[outerSearchIndex] != toFindObjects[innerSearchIndex]) {
                        ParseSceneObject(sceneObjects[outerSearchIndex], toFindObjects[innerSearchIndex]);
                    }

                    innerSearchIndex++;
                }
            
                innerSearchIndex = 0;
                outerSearchIndex++;
                progressBarFill = 0.5f + (float) outerSearchIndex / sceneObjects.Length / 2;

                if (FrameTimeElapsed()) {
                    completed = false;
                    return;
                }
            }
        
            outerSearchIndex = 0;
            completed = true;
        }

        private bool FrameTimeElapsed()
        {
            if (stopwatch.Elapsed.TotalSeconds <= FrameTime) {
                return false;
            }

            stopwatch.Stop();
            stopwatch.Reset();
            return true;
        }

        private void ParseAsset(Object asset, Object toFind)
        {
            if (asset != null && asset != toFind) {
                Object[] dependencies = EditorUtility.CollectDependencies(new [] {asset});
                    
                if (dependencies.Contains(toFind)) {
                    TryAddReference(asset);
                }
            }
        }
    
        private void ParseSceneObject(Object sceneObject, Object objectToFind)
        {
            SerializedObject serializedObject = new SerializedObject(sceneObject);
            SerializedProperty property = serializedObject.GetIterator();

            property.Next(true);
                    
            do
            {
                if (property.propertyType != SerializedPropertyType.ObjectReference || property.objectReferenceValue != objectToFind) {
                    continue;
                }

                TryAddReference(sceneObject);
                break;
            } while (property.Next(property.propertyType != SerializedPropertyType.ObjectReference));
        }

        private void TryAddReference(Object asset)
        {
            switch (asset) {
                case GameObject gameObject:
                    if (!prefabReferences.Contains(gameObject)) {
                        prefabReferences.Add(gameObject);
                    }
                    break;
                case ScriptableObject scriptableObject:
                    if (!scriptableObjectReferences.Contains(scriptableObject)) {
                        scriptableObjectReferences.Add(scriptableObject);
                    }
                    break;
                case MonoBehaviour monoBehaviour:
                    if (!sceneReferences.Contains(monoBehaviour)) {
                        sceneReferences.Add(monoBehaviour);
                    }
                    break;
                default:
                    Debug.LogErrorFormat($"Unexpected Type. Found Object [{asset}], is {asset.GetType()} with base Type {asset.GetType().BaseType}.");
                    break;
            }
        }

        private void UpdateFilePaths(string startingDirectory, ref List<string> paths, params string[] extensions)
        {
            try
            {
                string[] files = Directory.GetFiles(startingDirectory);
                paths.AddRange(files.Where(file => extensions.Any(file.EndsWith)));

                string[] directories = Directory.GetDirectories(startingDirectory);
                foreach (var directory in directories) {
                    UpdateFilePaths(directory, ref paths, extensions);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError(e.Message);
            }
        }

        private void SubscribeAsyncSearch()
        {
            EditorApplication.update += FindObjectReferencesAsync;
            subscribedToUpdate = true;
        }

        private void UnsubscribeAsyncSearch()
        {
            EditorApplication.update -= FindObjectReferencesAsync;
            subscribedToUpdate = false;
        }

        #endregion
    }
}
