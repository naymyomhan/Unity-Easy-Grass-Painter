using UnityEngine;
using UnityEngine.Pool;

namespace ModernGrassTool
{
    [RequireComponent(typeof(ParticleSystem))]
    public class ModernReturnToCutPool : MonoBehaviour
    {
        public ParticleSystem system;
        public IObjectPool<ParticleSystem> pool;

        private void Awake()
        {
            if (system == null) system = GetComponent<ParticleSystem>();
            var main = system.main;
            main.stopAction = ParticleSystemStopAction.Callback;
        }

        private void OnParticleSystemStopped()
        {
            if (pool != null && system != null)
            {
                pool.Release(system);
            }
        }
    }
}
