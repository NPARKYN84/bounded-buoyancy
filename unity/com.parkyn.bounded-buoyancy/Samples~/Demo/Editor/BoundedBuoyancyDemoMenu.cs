// One click to see the demos: Tools > Bounded Buoyancy > Play 3D Demo (or 2D). It opens a new, unsaved scene with
// the demo in it and presses Play. Copyright (c) 2026 Nicholas Parkyn. MIT licence.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BoundedBuoyancy.Samples.EditorTools
{
    static class BoundedBuoyancyDemoMenu
    {
        [MenuItem("Tools/Bounded Buoyancy/Play 3D Demo")]
        static void Play3D()
        {
            Open<BoundedBuoyancyDemo>("Bounded Buoyancy Demo");
        }

        [MenuItem("Tools/Bounded Buoyancy/Play 2D Demo")]
        static void Play2D()
        {
            Open<BoundedBuoyancyDemo2D>("Bounded Buoyancy Demo 2D");
        }

        static void Open<T>(string name) where T : Component
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject(name).AddComponent<T>();
            EditorApplication.isPlaying = true;
        }
    }
}
