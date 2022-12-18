using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Pixelism {

    public readonly struct KernelData {
        public readonly int kernel;
        public readonly uint3 threadGroupSizes;
        public readonly ComputeShader shader;
        public readonly CustomSampler sampler;

        public KernelData(ComputeShader cs, string name, string scope) {
            shader = cs;
            kernel = cs.FindKernel(name, out threadGroupSizes);
            sampler = CustomSampler.Create(scope + '.' + name, true);
        }

        public KernelData(ComputeShader cs, string name) : this(cs, name, "Pixelism." + cs.name) {
        }

        public readonly GPUProfilerScope SamplingScope(CommandBuffer command) {
            return new GPUProfilerScope(command, sampler);
        }
    }
}
