﻿#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.EditorVR.Core;
using UnityEngine;

namespace UnityEditor.Experimental.EditorVR.Modules
{
    sealed class HierarchyModule : MonoBehaviour, ISelectionChanged
    {
        readonly List<IUsesHierarchyData> m_HierarchyLists = new List<IUsesHierarchyData>();
        readonly List<HierarchyData> m_HierarchyData = new List<HierarchyData>();
        HierarchyProperty m_HierarchyProperty;

        readonly List<IFilterUI> m_FilterUIs = new List<IFilterUI>();
        readonly HashSet<string> m_ObjectTypes = new HashSet<string>();
        readonly List<GameObject> m_IgnoreList = new List<GameObject>();

        // Local method use only -- created here to reduce garbage collection
        readonly Stack<HierarchyData> m_DataStack = new Stack<HierarchyData>();
        readonly Stack<int> m_SiblingIndexStack = new Stack<int>();

        void Awake()
        {
            m_IgnoreList.Add(gameObject); // Ignore EditorVR
            foreach (var manager in Resources.FindObjectsOfTypeAll<InputManager>())
            {
                m_IgnoreList.Add(manager.gameObject);
            }

            foreach (var manager in Resources.FindObjectsOfTypeAll<EditingContextManager>())
            {
                m_IgnoreList.Add(manager.gameObject);
            }
        }

        void OnEnable()
        {
            EditorApplication.hierarchyWindowChanged += UpdateHierarchyData;
            UpdateHierarchyData();
        }

        void OnDisable()
        {
            EditorApplication.hierarchyWindowChanged -= UpdateHierarchyData;
        }

        public void OnSelectionChanged()
        {
            UpdateHierarchyData();
        }

        public void AddConsumer(IUsesHierarchyData consumer)
        {
            consumer.hierarchyData = GetHierarchyData();
            m_HierarchyLists.Add(consumer);
        }

        public void RemoveConsumer(IUsesHierarchyData consumer)
        {
            m_HierarchyLists.Remove(consumer);
        }

        public void AddConsumer(IFilterUI consumer)
        {
            consumer.filterList = GetFilterList();
            m_FilterUIs.Add(consumer);
        }

        public void RemoveConsumer(IFilterUI consumer)
        {
            m_FilterUIs.Remove(consumer);
        }

        List<string> GetFilterList()
        {
            return m_ObjectTypes.ToList();
        }

        List<HierarchyData> GetHierarchyData()
        {
            return m_HierarchyData ?? new List<HierarchyData>();
        }

        void UpdateHierarchyData()
        {
            m_ObjectTypes.Clear();

            if (m_HierarchyProperty == null)
                m_HierarchyProperty = new HierarchyProperty(HierarchyType.GameObjects);
            else
                m_HierarchyProperty.Reset();

            var hasChanged = false;
            var lastDepth = 0;
            m_DataStack.Clear();
            m_SiblingIndexStack.Clear();
            m_DataStack.Push(null);
            m_SiblingIndexStack.Push(0);
            while (m_HierarchyProperty.Next(null))
            {
                var instanceID = m_HierarchyProperty.instanceID;
                var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
                var currentDepth = m_HierarchyProperty.depth;
                if (m_IgnoreList.Contains(go))
                {
                    var depth = currentDepth;

                    // skip children of EVR to prevent the display of EVR contents
                    while (m_HierarchyProperty.Next(null) && m_HierarchyProperty.depth > depth) { }

                    currentDepth = m_HierarchyProperty.depth;
                    instanceID = m_HierarchyProperty.instanceID;

                    // If EVR is the last object, early out
                    if (instanceID == 0)
                        break;
                }

                if (currentDepth <= lastDepth)
                {
                    if (m_DataStack.Count > 1) // Pop off last sibling
                    {
                        if (CleanUpHierarchyData(m_DataStack.Pop(), m_SiblingIndexStack.Pop()))
                            hasChanged = true;
                    }

                    var count = lastDepth - currentDepth;
                    while (count-- > 0)
                    {
                        if (CleanUpHierarchyData(m_DataStack.Pop(), m_SiblingIndexStack.Pop()))
                            hasChanged = true;
                    }
                }

                var parent = m_DataStack.Peek();
                var siblingIndex = m_SiblingIndexStack.Pop();

                if (parent != null && parent.children == null)
                    parent.children = new List<HierarchyData>();

                var children = parent == null ? m_HierarchyData : parent.children;

                HierarchyData currentHierarchyData;
                if (siblingIndex >= children.Count)
                {
                    currentHierarchyData = new HierarchyData(m_HierarchyProperty);
                    var types = new HashSet<string>();
                    InstanceIDToComponentTypes(instanceID, types, m_ObjectTypes);
                    currentHierarchyData.types = types;
                    children.Add(currentHierarchyData);
                    hasChanged = true;
                }
                else if (children[siblingIndex].index != instanceID)
                {
                    currentHierarchyData = new HierarchyData(m_HierarchyProperty);
                    var types = new HashSet<string>();
                    InstanceIDToComponentTypes(instanceID, types, m_ObjectTypes);
                    currentHierarchyData.types = types;
                    children[siblingIndex] = currentHierarchyData;
                    hasChanged = true;
                }
                else
                {
                    currentHierarchyData = children[siblingIndex];
                    InstanceIDToComponentTypes(instanceID, currentHierarchyData.types, m_ObjectTypes);
                }

                m_DataStack.Push(currentHierarchyData);
                m_SiblingIndexStack.Push(siblingIndex + 1);
                m_SiblingIndexStack.Push(0);
                lastDepth = currentDepth;
            }

            while (m_SiblingIndexStack.Count > 0 && m_DataStack.Count > 0)
            {
                if (CleanUpHierarchyData(m_DataStack.Pop(), m_SiblingIndexStack.Pop()))
                    hasChanged = true;
            }

            if (hasChanged)
            {
                foreach (var list in m_HierarchyLists)
                {
                    list.hierarchyData = GetHierarchyData();
                }

                // Send new data to existing filterUIs
                foreach (var filterUI in m_FilterUIs)
                {
                    filterUI.filterList = GetFilterList();
                }
            }
        }

        bool CleanUpHierarchyData(HierarchyData data, int lastSiblingIndex)
        {
            var children = data == null ? m_HierarchyData : data.children;
            var childrenCount = children == null ? 0 : children.Count;
            if (children != null && lastSiblingIndex < childrenCount)
            {
                children.RemoveRange(lastSiblingIndex, childrenCount - lastSiblingIndex);
                if (data != null && children.Count == 0)
                    data.children = null;

                return true;
            }

            return false;
        }

        static void InstanceIDToComponentTypes(int instanceID, HashSet<string> types, HashSet<string> allTypes)
        {
            types.Clear();
            var go = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
            if (go)
            {
                var components = go.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (!component)
                        continue;

                    if (component is Transform)
                        continue;

                    var typeName = component.GetType().Name;
                    if (component is MonoBehaviour)
                        typeName = "MonoBehaviour";

                    types.Add(typeName);
                    allTypes.Add(typeName);
                }
            }
        }
    }
}
#endif
