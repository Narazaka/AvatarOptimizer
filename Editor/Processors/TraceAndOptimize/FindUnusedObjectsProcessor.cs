using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using Debug = System.Diagnostics.Debug;
using Object = UnityEngine.Object;

namespace Anatawa12.AvatarOptimizer.Processors.TraceAndOptimizes
{
    class FindUnusedObjectsProcessor
    {
        private readonly ImmutableModificationsContainer _modifications;
        private readonly OptimizerSession _session;
        private readonly HashSet<GameObject> _exclusions;
        private readonly bool _preserveEndBone;
        private readonly bool _useLegacyGC;
        private readonly bool _noConfigureMergeBone;
        private readonly bool _gcDebug;

        public FindUnusedObjectsProcessor(ImmutableModificationsContainer modifications, OptimizerSession session,
            bool preserveEndBone,
            bool useLegacyGC,
            bool noConfigureMergeBone,
            bool gcDebug,
            HashSet<GameObject> exclusions)
        {
            _modifications = modifications;
            _session = session;
            _preserveEndBone = preserveEndBone;
            _useLegacyGC = useLegacyGC;
            _noConfigureMergeBone = noConfigureMergeBone;
            _gcDebug = gcDebug;
            _exclusions = exclusions;
        }

        public void Process()
        {
            if (_useLegacyGC)
                ProcessLegacy();
            else if (_gcDebug)
                CollectDataForGc();
            else
                ProcessNew();
        }

        // Mark & Sweep Variables
        private readonly Dictionary<Component, ComponentDependencyCollector.DependencyType> _marked =
            new Dictionary<Component, ComponentDependencyCollector.DependencyType>();
        private readonly Queue<(Component, bool)> _processPending = new Queue<(Component, bool)>();
        private readonly Dictionary<Component, bool?> _activeNessCache = new Dictionary<Component, bool?>();

        private bool? GetActiveness(Component component)
        {
            if (_activeNessCache.TryGetValue(component, out var activeness))
                return activeness;
            activeness = ComputeActiveness(component);
            _activeNessCache.Add(component, activeness);
            return activeness;
        }

        private bool? ComputeActiveness(Component component)
        {
            if (_session.GetRootComponent<Transform>() == component) return true;
            bool? parentActiveness;
            if (component is Transform t)
                parentActiveness = t.parent == null ? true : GetActiveness(t.parent);
            else
                parentActiveness = GetActiveness(component.transform);
            if (parentActiveness == false) return false;

            bool? activeness;
            switch (component)
            {
                case Transform transform:
                    var gameObject = transform.gameObject;
                    activeness = _modifications.GetConstantValue(gameObject, "m_IsActive", gameObject.activeSelf);
                    break;
                case Behaviour behaviour:
                    activeness = _modifications.GetConstantValue(behaviour, "m_Enabled", behaviour.enabled);
                    break;
                case Cloth cloth:
                    activeness = _modifications.GetConstantValue(cloth, "m_Enabled", cloth.enabled);
                    break;
                case Collider collider:
                    activeness = _modifications.GetConstantValue(collider, "m_Enabled", collider.enabled);
                    break;
                case LODGroup lodGroup:
                    activeness = _modifications.GetConstantValue(lodGroup, "m_Enabled", lodGroup.enabled);
                    break;
                case Renderer renderer:
                    activeness = _modifications.GetConstantValue(renderer, "m_Enabled", renderer.enabled);
                    break;
                // components without isEnable
                case CanvasRenderer _:
                case Joint _:
                case MeshFilter _:
                case OcclusionArea _:
                case OcclusionPortal _:
                case ParticleSystem _:
                case ParticleSystemForceField _:
                case Rigidbody _:
                case Rigidbody2D _:
                case TextMesh _:
                case Tree _:
                case WindZone _:
                case UnityEngine.XR.WSA.WorldAnchor _:
                    activeness = true;
                    break;
                case Component _:
                case null:
                    // fallback: all components type should be proceed with above switch
                    activeness = null;
                    break;
            }

            if (activeness == false) return false;
            if (parentActiveness == true && activeness == true) return true;

            return null;
        }

        private void MarkComponent(Component component,
            bool ifTargetCanBeEnabled,
            ComponentDependencyCollector.DependencyType type)
        {
            bool? activeness = GetActiveness(component);

            if (ifTargetCanBeEnabled && activeness == false)
                return; // The Target is not active so not dependency

            if (_marked.TryGetValue(component, out var existingFlags))
            {
                _marked[component] = existingFlags | type;
            }
            else
            {
                _processPending.Enqueue((component, activeness != false));
                _marked.Add(component, type);
            }
        }

        private void ProcessNew()
        {
            MarkAndSweep();
            if (!_noConfigureMergeBone) ConfigureMergeBone();
        }

        private void MarkAndSweep()
        {
            // first, collect usages
            var collector = new ComponentDependencyCollector(_session, _preserveEndBone);
            collector.CollectAllUsages();

            // then, mark and sweep.

            // entrypoint for mark & sweep is active-able GameObjects
            foreach (var gameObject in CollectAllActiveAbleGameObjects())
            foreach (var component in gameObject.GetComponents<Component>())
                if (collector.GetDependencies(component).EntrypointComponent)
                    MarkComponent(component, true, ComponentDependencyCollector.DependencyType.Normal);

            // excluded GameObjects must be exists
            foreach (var gameObject in _exclusions)
            foreach (var component in gameObject.GetComponents<Component>())
                MarkComponent(component, true, ComponentDependencyCollector.DependencyType.Normal);

            while (_processPending.Count != 0)
            {
                var (component, canBeActive) = _processPending.Dequeue();
                var dependencies = collector.TryGetDependencies(component);
                if (dependencies == null) continue; // not part of this Hierarchy Tree

                foreach (var (dependency, flags) in dependencies.Dependencies)
                {
                    var ifActive =
                        (flags.flags & ComponentDependencyCollector.DependencyFlags.EvenIfThisIsDisabled) == 0;
                    if (ifActive && !canBeActive) continue;
                    var ifTargetCanBeEnabled =
                        (flags.flags & ComponentDependencyCollector.DependencyFlags.EvenIfTargetIsDisabled) == 0;
                    MarkComponent(dependency, ifTargetCanBeEnabled, flags.type);
                }
            }

            foreach (var component in _session.GetComponents<Component>())
            {
                // null values are ignored
                if (!component) continue;

                if (component is Transform)
                {
                    // Treat Transform Component as GameObject because they are two sides of the same coin
                    if (!_marked.ContainsKey(component))
                        Object.DestroyImmediate(component.gameObject);
                }
                else
                {
                    if (!_marked.ContainsKey(component))
                        Object.DestroyImmediate(component);
                }
            }
        }

        private void CollectDataForGc()
        {
            // first, collect usages
            var collector = new ComponentDependencyCollector(_session, _preserveEndBone);
            collector.CollectAllUsages();

            var componentDataMap = new Dictionary<Component, GCData.ComponentData>();

            foreach (var component in _session.GetComponents<Component>())
            {
                var componentData = new GCData.ComponentData { component = component };
                componentDataMap.Add(component, componentData);

                switch (ComputeActiveness(component))
                {
                    case false:
                        componentData.activeness = GCData.ActiveNess.False;
                        break;
                    case true:
                        componentData.activeness = GCData.ActiveNess.True;
                        break;
                    case null:
                        componentData.activeness = GCData.ActiveNess.Variable;
                        break;
                }

                var dependencies = collector.GetDependencies(component);
                foreach (var (key, (flags, type)) in dependencies.Dependencies)
                    componentData.dependencies.Add(new GCData.DependencyInfo(key, flags, type));
            }

            foreach (var gameObject in CollectAllActiveAbleGameObjects())
            foreach (var component in gameObject.GetComponents<Component>())
                if (collector.GetDependencies(component).EntrypointComponent)
                    componentDataMap[component].entrypoint = true;

            foreach (var gameObject in _exclusions)
            foreach (var component in gameObject.GetComponents<Component>())
                componentDataMap[component].entrypoint = true;

            foreach (var component in _session.GetComponents<Component>())
            {
                var dependencies = collector.GetDependencies(component);
                foreach (var (key, (flags, type)) in dependencies.Dependencies)
                    if (componentDataMap.TryGetValue(key, out var info))
                        info.dependants.Add(new GCData.DependencyInfo(component, flags, type));
            }

            
            foreach (var component in _session.GetComponents<Component>())
                component.gameObject.GetOrAddComponent<GCData>().data.Add(componentDataMap[component]);
        }

        class GCData : MonoBehaviour
        {
            public List<ComponentData> data = new List<ComponentData>();

            [Serializable]
            public class ComponentData
            {
                public Component component;
                public ActiveNess activeness;
                public bool entrypoint;
                public List<DependencyInfo> dependencies = new List<DependencyInfo>();
                public List<DependencyInfo> dependants = new List<DependencyInfo>();
            }

            [Serializable]
            public class DependencyInfo
            {
                public Component component;
                public ComponentDependencyCollector.DependencyFlags flags;
                public ComponentDependencyCollector.DependencyType type;

                public DependencyInfo(Component component, ComponentDependencyCollector.DependencyFlags flags,
                    ComponentDependencyCollector.DependencyType type)
                {
                    this.component = component;
                    this.flags = flags;
                    this.type = type;
                }
            }

            public enum ActiveNess
            {
                False,
                True,
                Variable
            }
        }

        private void ConfigureMergeBone()
        {
            ConfigureRecursive(_session.GetRootComponent<Transform>(), _modifications);

            // returns true if merged
            bool ConfigureRecursive(Transform transform, ImmutableModificationsContainer modifications)
            {
                var mergedChildren = true;
                foreach (var child in transform.DirectChildrenEnumerable())
                    mergedChildren &= ConfigureRecursive(child, modifications);

                const ComponentDependencyCollector.DependencyType AllowedUsages =
                    ComponentDependencyCollector.DependencyType.Bone
                    | ComponentDependencyCollector.DependencyType.Parent
                    | ComponentDependencyCollector.DependencyType.ComponentToTransform;

                // Already Merged
                if (transform.GetComponent<MergeBone>()) return true;
                // Components must be Transform Only
                if (transform.GetComponents<Component>().Length != 1) return false;
                // The bone cannot be used generally
                if ((_marked[transform] & ~AllowedUsages) != 0) return false;
                // must not be animated
                if (TransformAnimated(transform, modifications)) return false;

                if (!mergedChildren)
                {
                    if (GameObjectAnimated(transform, modifications)) return false;

                    var localScale = transform.localScale;
                    var identityTransform = localScale == Vector3.one && transform.localPosition == Vector3.zero &&
                                            transform.localRotation == Quaternion.identity;

                    if (!identityTransform)
                    {
                        var childrenTransformAnimated =
                            transform.DirectChildrenEnumerable().Any(x => TransformAnimated(x, modifications));
                        if (childrenTransformAnimated)
                            // if this is not identity transform, animating children is not good
                            return false;

                        if (!MergeBoneProcessor.ScaledEvenly(localScale))
                            // non even scaling is not possible to reproduce in children
                            return false;
                    }
                }

                if (!transform.gameObject.GetComponent<MergeBone>())
                    transform.gameObject.AddComponent<MergeBone>().avoidNameConflict = true;

                return true;
            }

            bool TransformAnimated(Transform transform, ImmutableModificationsContainer modifications)
            {
                var transformProperties = modifications.GetModifiedProperties(transform);
                if (transformProperties.Count != 0)
                {
                    // TODO: constant animation detection
                    foreach (var transformProperty in TransformProperties)
                        if (transformProperties.ContainsKey(transformProperty))
                            return true;
                }

                return false;
            }

            bool GameObjectAnimated(Transform transform, ImmutableModificationsContainer modifications)
            {
                var objectProperties = modifications.GetModifiedProperties(transform.gameObject);

                if (objectProperties.ContainsKey("m_IsActive"))
                    return true;

                return false;
            }
        }

        private static readonly string[] TransformProperties =
        {
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", 
            "m_LocalScale.x", "m_LocalScale.y", "m_LocalScale.z", 
            "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z"
        };

        private IEnumerable<GameObject> CollectAllActiveAbleGameObjects()
        {
            var queue = new Queue<GameObject>();
            queue.Enqueue(_session.GetRootComponent<Transform>().gameObject);

            while (queue.Count != 0)
            {
                var gameObject = queue.Dequeue();
                var activeNess = _modifications.GetConstantValue(gameObject, "m_IsActive", gameObject.activeSelf);
                switch (activeNess)
                {
                    case null:
                    case true:
                        // This GameObject can be active
                        yield return gameObject;
                        foreach (var transform in gameObject.transform.DirectChildrenEnumerable())
                            queue.Enqueue(transform.gameObject);
                        break;
                    case false:
                        // This GameObject and their children will never be active
                        break;
                }
            }
        }

        private void ProcessLegacy() {
            // mark & sweep
            var gameObjects = new HashSet<GameObject>(_session.GetComponents<Transform>().Select(x => x.gameObject));
            var referenced = new HashSet<GameObject>();
            var newReferenced = new Queue<GameObject>();

            void AddGameObject(GameObject gameObject)
            {
                if (gameObject && gameObjects.Contains(gameObject) && referenced.Add(gameObject))
                    newReferenced.Enqueue(gameObject);
            }

            // entry points: active GameObjects
            foreach (var component in gameObjects.Where(x => x.activeInHierarchy))
                AddGameObject(component);

            // entry points: modified enable/disable
            foreach (var keyValuePair in _modifications.ModifiedProperties)
            {
                // TODO: if the any of parent is inactive and kept, it should not be assumed as 
                if (!keyValuePair.Key.AsGameObject(out var gameObject)) continue;
                if (!keyValuePair.Value.TryGetValue("m_IsActive", out _)) continue;

                // TODO: if the child is not activeSelf, it should not be assumed as entry point.
                foreach (var transform in gameObject.GetComponentsInChildren<Transform>())
                    AddGameObject(transform.gameObject);
            }

            // entry points: active GameObjects
            foreach (var gameObject in _exclusions)
                AddGameObject(gameObject);

            while (newReferenced.Count != 0)
            {
                var gameObject = newReferenced.Dequeue();

                foreach (var component in gameObject.GetComponents<Component>())
                {
                    if (component is Transform transform)
                    {
                        if (transform.parent)
                            AddGameObject(transform.parent.gameObject);
                        continue;
                    }

                    if (component is VRCPhysBoneBase)
                    {
                        foreach (var child in component.GetComponentsInChildren<Transform>(true))
                            AddGameObject(child.gameObject);
                    }

                    using (var serialized = new SerializedObject(component))
                    {
                        var iter = serialized.GetIterator();
                        var enterChildren = true;
                        while (iter.Next(enterChildren))
                        {
                            if (iter.propertyType == SerializedPropertyType.ObjectReference)
                            {
                                var value = iter.objectReferenceValue;
                                if (value is Component c && !EditorUtility.IsPersistent(value))
                                    AddGameObject(c.gameObject);
                            }

                            switch (iter.propertyType)
                            {
                                case SerializedPropertyType.Integer:
                                case SerializedPropertyType.Boolean:
                                case SerializedPropertyType.Float:
                                case SerializedPropertyType.String:
                                case SerializedPropertyType.Color:
                                case SerializedPropertyType.ObjectReference:
                                case SerializedPropertyType.Enum:
                                case SerializedPropertyType.Vector2:
                                case SerializedPropertyType.Vector3:
                                case SerializedPropertyType.Vector4:
                                case SerializedPropertyType.Rect:
                                case SerializedPropertyType.ArraySize:
                                case SerializedPropertyType.Character:
                                case SerializedPropertyType.Bounds:
                                case SerializedPropertyType.Quaternion:
                                case SerializedPropertyType.FixedBufferSize:
                                case SerializedPropertyType.Vector2Int:
                                case SerializedPropertyType.Vector3Int:
                                case SerializedPropertyType.RectInt:
                                case SerializedPropertyType.BoundsInt:
                                    enterChildren = false;
                                    break;
                                case SerializedPropertyType.Generic:
                                case SerializedPropertyType.LayerMask:
                                case SerializedPropertyType.AnimationCurve:
                                case SerializedPropertyType.Gradient:
                                case SerializedPropertyType.ExposedReference:
                                case SerializedPropertyType.ManagedReference:
                                default:
                                    enterChildren = true;
                                    break;
                            }
                        }
                    }
                }
            }

            // sweep
            foreach (var gameObject in gameObjects.Where(x => !referenced.Contains(x)))
            {
                if (gameObject)
                    Object.DestroyImmediate(gameObject);
            }
        }
    }
}
