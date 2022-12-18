using NUnit.Framework;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace Pixelism.Test {

    public class ShaderMathTest {
        private CommandBuffer command = new CommandBuffer();
        private ComputeBuffer buffer;
        private uint4[] actual;

        private AddressablesHelper.HandleCollector collector = new AddressablesHelper.HandleCollector();
        private ComputeShader shader;
        private static readonly int _Input = Shader.PropertyToID("_Input");
        private static readonly int _Result = Shader.PropertyToID("_Result");

        [OneTimeSetUp]
        public void OneTimeSetUp() {
            shader = Addressables.LoadAssetAsync<ComputeShader>("ShaderMathTest").Collect(collector).WaitForCompletion();
            buffer = new ComputeBuffer(1, Marshal.SizeOf<uint4>());
        }

        [OneTimeTearDown]
        public void OneTimeTearDown() {
            collector.Dispose();
            buffer.Dispose();
        }

        [SetUp]
        public void SetUp() {
            actual = new uint4[buffer.count];
            command.Clear();
            command.SetBufferData(buffer, actual); // clear
        }

        [TearDown]
        public void TearDown() {
        }

        [Test]
        [TestCase(0u, 4u)]
        [TestCase(1u, 1u)]
        [TestCase(63u, 16u)]
        [TestCase(64u, 16u)]
        [TestCase(65u, 16u)]
        [TestCase(33u, 17u)]
        [TestCase(34u, 17u)]
        [TestCase(35u, 17u)]
        public void DivRoundUp(uint x, uint div) {
            KernelData pass = new KernelData(shader, "DivRoundUpTest");
            using (pass.SamplingScope(command)) {
                command.SetComputeIntParams(pass.shader, _Input, new int[] { (int)x, (int)div, 0, 0 });
                command.SetComputeBufferParam(pass.shader, pass.kernel, _Result, buffer);
                command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
            }
            Graphics.ExecuteCommandBuffer(command);
            buffer.GetData(actual);
            Assert.AreEqual(Math.DivRoundUp(x, div), actual[0].x); // uint
            Assert.AreEqual(Math.DivRoundUp(new uint3(x), new uint3(div)), actual[0].yzw); // uint3
        }

        [Test]
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(15u)]
        [TestCase(16u)]
        [TestCase(17u)]
        [TestCase(0xFFFFFFFEu)]
        [TestCase(0xFFFFFFFFu)]
        public void NextPowerOfTwo(uint x) {
            KernelData pass = new KernelData(shader, "NextPowerOfTwoTest");
            using (pass.SamplingScope(command)) {
                command.SetComputeIntParams(pass.shader, _Input, new int[] { (int)x, 0, 0, 0 });
                command.SetComputeBufferParam(pass.shader, pass.kernel, _Result, buffer);
                command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
            }
            Graphics.ExecuteCommandBuffer(command);
            buffer.GetData(actual);
            Assert.AreEqual((uint)Mathf.NextPowerOfTwo((int)x), actual[0].x);
        }

        [Test]
        [TestCase(0x0u, 0x0u)] // min
        [TestCase(0x080808u, 0x000000u)] // round edge min
        [TestCase(0x090909u, 0x111111u)] // round edge min
        [TestCase(0xF6F6F6u, 0xEEEEEEu)] // round edge max
        [TestCase(0xF7F7F7u, 0xFFFFFFu)] // round edge max
        [TestCase(0x776655u, 0x776655u)] // just
        [TestCase(0xFFFFFFFFu, 0x00FFFFFFu)] // max
        public void Quantize4Bits(uint x, uint expected) {
            KernelData pass = new KernelData(shader, "Quantize4BitsTest");
            using (pass.SamplingScope(command)) {
                command.SetComputeIntParams(pass.shader, _Input, new int[] { (int)x, 0, 0, 0 });
                command.SetComputeBufferParam(pass.shader, pass.kernel, _Result, buffer);
                command.DispatchCompute(pass.shader, pass.kernel, 1, 1, 1);
            }
            Graphics.ExecuteCommandBuffer(command);
            buffer.GetData(actual);
            Assert.AreEqual(expected, actual[0].x, actual[0].x.ToString("X"));
        }

    }
}
