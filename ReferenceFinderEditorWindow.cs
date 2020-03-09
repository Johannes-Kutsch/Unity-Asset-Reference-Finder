using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Plugins.ScriptableVariables.Editor.Utils
{
    public class ReferenceFinderEditorWindow : EditorWindow
    {
        private Vector2 scrollViewPosition = Vector2.zero;
        
        private static ReferenceFinder referenceFinder;

        private Object[] ToFindObjects => referenceFinder.ToFindObjects;
        private List<GameObject> PrefabReferences => referenceFinder.PrefabReferences;
        private List<ScriptableObject> ScriptableObjectReferences => referenceFinder.ScriptableObjectReferences;
        private List<MonoBehaviour> SceneReferences => referenceFinder.SceneReferences;

        private bool subscribedToRepaint;
        
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
            ReferenceFinderEditorWindow window = GetWindow<ReferenceFinderEditorWindow>(true, "Find References", true);
            window.position = new Rect(new Vector2(200, 200), new Vector2(300, 350));
            
            referenceFinder = new ReferenceFinder();
            referenceFinder.FindObjectReferences(assets);
        }

        public static void OpenWindow(ReferenceFinder referenceFinder)
        {
            ReferenceFinderEditorWindow window = GetWindow<ReferenceFinderEditorWindow>(true, "Find References", true);
            window.position = new Rect(new Vector2(200, 200), new Vector2(300, 350));
            
            ReferenceFinderEditorWindow.referenceFinder = referenceFinder;
        }

        private void OnGUI()
        {
            if (!subscribedToRepaint) {
                referenceFinder.repaint += Repaint;
                subscribedToRepaint = true;
            }
            
            GUILayout.Space(5);
            scrollViewPosition = EditorGUILayout.BeginScrollView(scrollViewPosition);

            if (ToFindObjects != null)
            {
                GUILayout.BeginHorizontal();
                if (ToFindObjects.Length == 1)
                {
                    DrawLayoutObjectButton(ToFindObjects[0]);
                    GUILayout.Label($" is referenced by...");
                }
                else
                {
                    GUILayout.Label($"{ToFindObjects.Length} items");
                    if (GUILayout.Button("\u25B6", EditorStyles.miniButtonRight, GUILayout.MaxWidth(20))) {
                        referenceFinder.FindObjectReferences(ToFindObjects);
                    }
                    GUILayout.Label($"are referenced by...");
                }
                GUILayout.EndHorizontal();
            }

            if (referenceFinder.Searching) {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), referenceFinder.SearchProgress, referenceFinder.SearchDescription);
            }

            DrawLayoutGroup(PrefabReferences, "Prefab References" + (PrefabReferences.Count <= 0 ? "" : ":"));
            DrawLayoutGroup(ScriptableObjectReferences, "Scriptable Object References" + (ScriptableObjectReferences.Count <= 0 ? "" : ":"));
            DrawLayoutGroup(SceneReferences, "Scene References" + (SceneReferences.Count <= 0 ? "" : ":"));

            EditorGUILayout.EndScrollView();
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
            referenceFinder.FindObjectReferences(new[] {obj});
        }
    }
}
