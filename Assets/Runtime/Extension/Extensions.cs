using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Pixelism {

    public static class GameObjectExtensions {

        /// <summary>
        /// utility of unity object to destory
        /// </summary>
        public static void DestoryOnRuntime(this UnityEngine.Object obj) {
#if UNITY_EDITOR
            if (Application.isPlaying) {
                UnityEngine.Object.Destroy(obj);
            } else {
                UnityEngine.Object.DestroyImmediate(obj);
            }
#else
            UnityEngine.Object.Destroy( obj );
#endif
        }

        public static T GetOrAddComponent<T>(this GameObject go) where T : Component {
            var c = go.GetComponent<T>();
            if (c == null) {
                c = go.AddComponent<T>();
            }
            return c;
        }

        public static Component GetOrAddComponent(this GameObject go, Type type) {
            var c = go.GetComponent(type);
            if (c == null) {
                c = go.AddComponent(type);
            }
            return c;
        }
    }

    public static class ComponentExtensions {

        public static T GetOrAddComponent<T>(this Component component) where T : Component {
            var c = component.GetComponent<T>();
            if (c == null) {
                c = component.gameObject.AddComponent<T>();
            }
            return c;
        }

        public static Component GetOrAddComponent(this Component component, Type type) {
            var c = component.GetComponent(type);
            if (c == null) {
                c = component.gameObject.AddComponent(type);
            }
            return c;
        }
    }

    public static class NativeArrayExtensions {

        public static void DisposeIfIsCreated<T>(this NativeArray<T> a) where T : struct {
            if (a.IsCreated) {
                a.Dispose();
            }
        }
    }

    public static class ComputeShaderExtensions {

        public static int FindKernel(this ComputeShader cs, string name, out uint3 threadGroupSizes) {
            var kernelIndex = cs.FindKernel(name);
            cs.GetKernelThreadGroupSizes(kernelIndex, out threadGroupSizes.x, out threadGroupSizes.y, out threadGroupSizes.z);
            return kernelIndex;
        }
    }

    public static class ComputeBufferExtensions {

        // set single variable
        public static void SetData<T>(this ComputeBuffer buffer, T data) where T : struct {
            buffer.SetData(new T[] { data });
        }

        // get single vairable
        public static T GetData<T>(this ComputeBuffer buffer) where T : struct {
            var array = new T[1];
            buffer.GetData(array);
            return array[0];
        }
    }
}
