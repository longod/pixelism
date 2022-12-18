using System;
using System.Collections.Generic;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace Pixelism {

    // do not copy. structだとコピーできないようにするか、所有権をうつせるようにした方がよいが、現状のC#にその機能はないので
    public readonly struct GPUProfilerScope : IDisposable {
        private readonly CommandBuffer command;
        private readonly CustomSampler sampler;

        private static Dictionary<string, CustomSampler> cache = new Dictionary<string, CustomSampler>();

        internal GPUProfilerScope(CommandBuffer command, CustomSampler sampler) {
            this.command = command;
            this.sampler = sampler;
            this.command.BeginSample(sampler);
        }

        internal GPUProfilerScope(CommandBuffer command, string name) {
            this.command = command;
            if (!cache.TryGetValue(name, out sampler)) {
                sampler = CustomSampler.Create(name, true);
                cache.Add(name, sampler); // キャッシュして同じのがネストしたら成立する？
            }
            this.command.BeginSample(sampler);
        }

        public void Dispose() {
            command.EndSample(sampler);
        }
    }

}
