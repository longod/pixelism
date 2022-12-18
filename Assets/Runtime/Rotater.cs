using UnityEngine;

namespace Pixelism {

    // simple rotate
    public class Rotater : MonoBehaviour {
        public float speed = 15.0f;

        private void Update() {
            transform.Rotate(0, speed * Time.deltaTime, 0);
        }
    }
}
