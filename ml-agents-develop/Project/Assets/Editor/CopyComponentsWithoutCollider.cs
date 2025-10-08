using UnityEngine;
using UnityEditor;

public class CopyComponentsWithoutCollider : EditorWindow
{
    private GameObject sourceObject;
    private GameObject targetObject;

    [MenuItem("Tools/Copy Components Without Collider")]
    public static void ShowWindow()
    {
        GetWindow<CopyComponentsWithoutCollider>("Copy Components");
    }

    private void OnGUI()
    {
        GUILayout.Label("Copy Components (excluding Collider)", EditorStyles.boldLabel);

        sourceObject = (GameObject)EditorGUILayout.ObjectField("Source Object", sourceObject, typeof(GameObject), true);
        targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);

        if (GUILayout.Button("Copy Components"))
        {
            if (sourceObject == null || targetObject == null)
            {
                Debug.LogError("Source or Target is null!");
                return;
            }

            CopyComponents(sourceObject, targetObject);
        }
    }

    private void CopyComponents(GameObject source, GameObject target)
    {
        Component[] comps = source.GetComponents<Component>();

        foreach (Component comp in comps)
        {
            if (comp is Transform || comp is Collider) continue; // Transform & Collider 제외

            System.Type type = comp.GetType();
            Component copy = target.GetComponent(type);

            if (copy == null)
                copy = target.AddComponent(type);

            // SerializedObject/SerializedProperty 이용해 필드값 복사
            SerializedObject srcSO = new SerializedObject(comp);
            SerializedObject dstSO = new SerializedObject(copy);

            SerializedProperty prop = srcSO.GetIterator();
            while (prop.NextVisible(true))
            {
                if (prop.name == "m_Script") continue; // 스크립트 참조 제외
                dstSO.CopyFromSerializedProperty(prop);
            }
            dstSO.ApplyModifiedProperties();
        }

        Debug.Log("Components copied from " + source.name + " to " + target.name);
    }
}
