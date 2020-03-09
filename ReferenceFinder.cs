using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Plugins.ScriptableVariables.Editor.Utils {
    public class ReferenceFinder
    {
        public List<GameObject> PrefabReferences { get; private set; }
        public List<ScriptableObject> ScriptableObjectReferences  { get; private set; }
        public List<MonoBehaviour> SceneReferences { get; private set; }

        public Object[] ToFindObjects { get; private set; }
        public Action repaint;

        public bool Searching => asyncSearchSubscribed;
        public float SearchProgress { get; private set; }
        public string SearchDescription => assetsSearched ? "Finding Scene References..." : "Finding Asset References...";
    
        private List<string> paths;
        private Object[] sceneObjects;

        private bool asyncSearchSubscribed;
        private const float FrameTime = 0.05f;
        private readonly Stopwatch stopwatch = new Stopwatch();

        private int outerSearchIndex;
        private int innerSearchIndex;
        private bool assetsSearched;


        public void FindObjectReferences(Object[] objects)
        {
            ToFindObjects = objects;

            SetupSearch();
            FindObjectReferencesAsync();
        }
        
        public void StopSearch()
        {
            if (asyncSearchSubscribed) {
                UnsubscribeAsyncSearch();
            }
        }

        private void FindObjectReferencesAsync()
        {
            if (!asyncSearchSubscribed) {
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
                repaint?.Invoke();
                return;
            }
        
            EditorUtility.ClearProgressBar();
            UnsubscribeAsyncSearch();
            repaint?.Invoke();
        }

        private void SearchReferencingAssets(out bool completed)
        {
            while (outerSearchIndex < paths.Count) {
                Object asset = AssetDatabase.LoadMainAssetAtPath(paths[outerSearchIndex]);
            
                while (innerSearchIndex < ToFindObjects.Length) {
                    ParseAsset(asset, ToFindObjects[innerSearchIndex]);
                    innerSearchIndex++;
                }

                innerSearchIndex = 0;
                outerSearchIndex++;
                SearchProgress = (float) outerSearchIndex / paths.Count / 2;

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
                while (innerSearchIndex < ToFindObjects.Length) {
                    if (sceneObjects[outerSearchIndex] != null && sceneObjects[outerSearchIndex] != ToFindObjects[innerSearchIndex]) {
                        ParseSceneObject(sceneObjects[outerSearchIndex], ToFindObjects[innerSearchIndex]);
                    }

                    innerSearchIndex++;
                }
            
                innerSearchIndex = 0;
                outerSearchIndex++;
                SearchProgress = 0.5f + (float) outerSearchIndex / sceneObjects.Length / 2;

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
                    if (!PrefabReferences.Contains(gameObject)) {
                        PrefabReferences.Add(gameObject);
                    }
                    break;
                case ScriptableObject scriptableObject:
                    if (!ScriptableObjectReferences.Contains(scriptableObject)) {
                        ScriptableObjectReferences.Add(scriptableObject);
                    }
                    break;
                case MonoBehaviour monoBehaviour:
                    if (!SceneReferences.Contains(monoBehaviour)) {
                        SceneReferences.Add(monoBehaviour);
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
            catch (Exception e)
            {
                Debug.LogError(e.Message);
            }
        }

        private void SubscribeAsyncSearch()
        {
            EditorApplication.update += FindObjectReferencesAsync;
            asyncSearchSubscribed = true;
        }

        private void UnsubscribeAsyncSearch()
        {
            EditorApplication.update -= FindObjectReferencesAsync;
            asyncSearchSubscribed = false;
        }

        private void SetupSearch()
        {
            outerSearchIndex = 0;
            innerSearchIndex = 0;
            assetsSearched = false;
        
            PrefabReferences = new List<GameObject>();
            ScriptableObjectReferences = new List<ScriptableObject>();
            SceneReferences = new List<MonoBehaviour>();
            
            sceneObjects = Object.FindObjectsOfType(typeof(MonoBehaviour));
            paths = new List<string>();
            UpdateFilePaths("Assets", ref paths, ".prefab", ".asset");
        }
    }
}
