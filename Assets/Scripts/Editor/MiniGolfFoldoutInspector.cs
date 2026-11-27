using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MonoBehaviour), true, isFallback = true)]
[CanEditMultipleObjects]
internal sealed class MiniGolfFoldoutInspector : Editor
{
    private readonly Dictionary<string, string> sectionTitles = new();

    private void OnEnable()
    {
        sectionTitles.Clear();
        for (Type type = target.GetType(); type != typeof(MonoBehaviour) && type != null; type = type.BaseType)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags))
            {
                var section = field.GetCustomAttribute<MiniGolfFoldoutAttribute>();
                if (section != null)
                    sectionTitles.TryAdd(field.Name, section.Title);
            }
        }
    }

    public override void OnInspectorGUI()
    {
        if (sectionTitles.Count == 0)
        {
            base.OnInspectorGUI();
            return;
        }

        serializedObject.Update();
        var property = serializedObject.GetIterator();
        bool sectionOpen = true;
        string keyPrefix = "MiniGolf.Inspector." + target.GetType().FullName + ".";
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (sectionTitles.TryGetValue(property.name, out string title))
            {
                string key = keyPrefix + property.name;
                bool wasOpen = SessionState.GetBool(key, false);
                sectionOpen = EditorGUILayout.Foldout(wasOpen, title, true);
                if (sectionOpen != wasOpen)
                    SessionState.SetBool(key, sectionOpen);
            }

            if (!sectionOpen)
                continue;

            using (new EditorGUI.DisabledScope(property.name == "m_Script"))
                EditorGUILayout.PropertyField(property, includeChildren: true);
        }

        serializedObject.ApplyModifiedProperties();
    }
}
