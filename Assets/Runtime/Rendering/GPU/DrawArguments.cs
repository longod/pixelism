using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEngine;

namespace Pixelism {
    // for indirect drawing
    // https://docs.microsoft.com/en-us/windows/win32/direct3d12/indirect-drawing

    /// <summary>
    /// https://docs.microsoft.com/en-us/windows/win32/api/d3d12/ns-d3d12-d3d12_draw_arguments
    /// https://docs.unity3d.com/ScriptReference/Graphics.DrawProceduralIndirect.html
    /// Buffer with arguments, bufferWithArgs, has to have four integer numbers at given argsOffset offset:
    /// vertex count per instance, instance count, start vertex location, and start instance location.
    /// This maps to Direct3D11 DrawInstancedIndirect and equivalent functions on other graphics APIs.
    /// On OpenGL versions before 4.2 and all OpenGL ES versions that support indirect draw, the last argument is reserved and therefore must be zero.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DrawArguments {
        public uint vertexCountPerInstance;
        public uint instanceCount;
        public uint startVertexLocation;
        public uint startInstanceLocation;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf() {
            return Marshal.SizeOf<DrawArguments>();
        }

        public static ComputeBuffer Create(int count) {
            return new ComputeBuffer(count, SizeOf(), ComputeBufferType.Raw | ComputeBufferType.IndirectArguments);
        }
    }

    /// <summary>
    /// https://docs.microsoft.com/en-us/windows/win32/api/d3d12/ns-d3d12-d3d12_draw_indexed_arguments
    /// https://docs.unity3d.com/ScriptReference/Graphics.DrawMeshInstancedIndirect.html
    /// Buffer with arguments, bufferWithArgs, has to have five integer numbers at given argsOffset offset:
    /// index count per instance, instance count, start index location, base vertex location, start instance location.
    /// Unity only needs the submeshIndex argument if submeshes within the mesh have different topologies(e.g.Triangles and Lines).
    /// Otherwise, all the information about which submesh to draw comes from the bufferWithArgs argument.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DrawIndexedArguments {
        public uint indexCountPerInstance;
        public uint instanceCount;
        public uint startIndexLocation;
        public int baseVertexLocation;
        public uint startInstanceLocation;
        private uint3 padding;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf() {
            return Marshal.SizeOf<DrawIndexedArguments>();
        }

        public static ComputeBuffer Create(int count) {
            return new ComputeBuffer(count, SizeOf(), ComputeBufferType.Raw | ComputeBufferType.IndirectArguments);
        }
    }

    /// <summary>
    /// https://docs.microsoft.com/en-us/windows/win32/api/d3d12/ns-d3d12-d3d12_dispatch_arguments
    /// https://docs.unity3d.com/ScriptReference/ComputeShader.DispatchIndirect.html
    /// Buffer with arguments, argsBuffer, has to have three integer numbers at given argsOffset offset:
    /// number of work groups in X dimension, number of work groups in Y dimension, number of work groups in Z dimension.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct DispatchArguments : IEquatable<DispatchArguments> {
        public uint threadGroupCountX;
        public uint threadGroupCountY;
        public uint threadGroupCountZ;
        private uint padding; // ByteAddressなら不要だが、structuredでも扱いやすいように

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf() {
            return Marshal.SizeOf<DispatchArguments>();
        }

        // 少なくとも、 Intel HD Graphics 620 では、 ComputeBufferType.IndirectArguments を指定すると、Structured で正常に値を書き込めない不具合がある（GPU or driver?）
        // 強制的にByteAddressになるような挙動に見える。ByteAddressだと書けるようだ。
        // IndirectArguments未指定だとstrucuredでも書けるようになるし、indirectとしても実行できるようになるが、GPUによってはそうでないケースがあるかもしれない。
        // ここでRaw (ByteAddress)指定、GPUでStructuredでも機能するが、整合上はよろしくない。
        // ここでvenderidからフラグを切り替えてもよいが、共通した挙動にするためGPU上ではByteAddressで扱う。
        // そもそも IndirectArguments はなに？native apiに相当するものがない。
        // これ以外のsturucured, constantはアライメント指定、raw, append, counterはflagsが存在する。PC以外のplatformで意味があることがあるのか？
        public static ComputeBuffer Create(int count) {
            return new ComputeBuffer(count, SizeOf(), ComputeBufferType.Raw | ComputeBufferType.IndirectArguments);
        }

        public static ComputeBuffer Create(ComputeBuffer buffer, int count) {
            // ComputeBufferType.IndirectArguments のチェックはできない
            if (buffer != null && buffer.count == count && buffer.stride == SizeOf()) {
                return buffer;
            }
            buffer?.Dispose();
            buffer = Create(count);
            return buffer;
        }

        public bool Equals(DispatchArguments other) {
            // paddingは不問
            return threadGroupCountX.Equals(other.threadGroupCountX) |
                threadGroupCountY.Equals(other.threadGroupCountY) |
                threadGroupCountZ.Equals(other.threadGroupCountZ);
        }
    }
}
