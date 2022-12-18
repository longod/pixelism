using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Pixelism {

    public static class AddressablesHelper {

        public static AsyncOperationHandle<TObject> LoadAssetAsync<TObject>(object key, Action<TObject> onSucceeded) {
            //Addressables.InitializeAsync().WaitForCompletion();
            var op = Addressables.LoadAssetAsync<TObject>(key);
            op.Completed += obj => {
                if (obj.Status == AsyncOperationStatus.Succeeded) {
                    onSucceeded(obj.Result);
                } else {
                    Debug.LogError($"Addressable failed to load. {key}");
                }
            };
            return op;
        }

        public static AsyncOperationHandle<TObject> Collect<TObject>(this AsyncOperationHandle<TObject> op, HandleCollector disposable) {
            disposable.Add(op);
            return op;
        }

        // ICollection<AsyncOperationHandleDisposable> があったほうがよい
        public class HandleCollector : IDisposable {

            private List<AsyncOperationHandle> ops = new List<AsyncOperationHandle>();

            public void Dispose() {
                foreach (var op in ops) {
                    Addressables.Release(op);
                }
                ops = null;
            }

            public void Add(AsyncOperationHandle op) {
                ops.Add(op);
            }

            public IList<AsyncOperationHandle> WaitForCompletion() {
                return Addressables.ResourceManager.CreateGenericGroupOperation(ops).WaitForCompletion();
            }
        }
    }
}
