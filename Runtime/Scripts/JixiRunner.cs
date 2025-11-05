using UnityEngine;

namespace JixiAI
{
    internal class JixiRunner : MonoBehaviour
    {
        private void Update() => Jixi.Instance.Tick();
    }
}