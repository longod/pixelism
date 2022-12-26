using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;

namespace Pixelism.Test {
#nullable enable

    // Assertのオーバーヘッドが大きい
    // messageが成功時も生成される
    // 未対応の型への対応
    // runtime test, editor testでの共有をどうするか。テスト用専用別アセンブリ？
    public static class AssertHelper {

        public static void AreEqual(float3 expected, float3 actual) {
            if (!expected.Equals(actual)) {
                Assert.Fail($"Expected: {expected}\r\n  But was:  {actual}");
            }
        }

        public static void AreEqual(float3 expected, float3 actual, float delta) {
            if (!math.all(Math.Approximately(expected, actual, delta))) {
                Assert.Fail($"Expected: {expected}\r\n  But was:  {actual}");
            }
        }

        public static void AreEqual(ReadOnlySpan<float> expected, ReadOnlySpan<float> actual, float delta) {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, float, float)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!Math.Approximately(actual[i], expected[i], delta)) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2:r}\r\n    But was:  {i.Item3:r}"));
                Assert.Fail(message);
            }
        }

        public static void AreEqual<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual) where T : IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!actual[i].Equals(expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        public static void AreEqual<T>(NativeSlice<T> expected, ReadOnlySpan<T> actual) where T : struct, IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!actual[i].Equals(expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        public static void AreEqual<T>(ReadOnlySpan<T> expected, NativeSlice<T> actual) where T : struct, IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!actual[i].Equals(expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        // NativeArray -> ReadOnlySpanに対応してくれ
        public static void AreEqual<T>(NativeSlice<T> expected, NativeSlice<T> actual) where T : struct, IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!actual[i].Equals(expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        public static void AreEqual<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T> eq) where T : IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!eq.Equals(actual[i], expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        public static void AreEqual<T>(NativeSlice<T> expected, ReadOnlySpan<T> actual, IEqualityComparer<T> eq) where T : struct, IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!eq.Equals(actual[i], expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        public static void AreEqual<T>(ReadOnlySpan<T> expected, NativeSlice<T> actual, IEqualityComparer<T> eq) where T : struct, IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!eq.Equals(actual[i], expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        public static void AreEqual<T>(NativeSlice<T> expected, NativeSlice<T> actual, IEqualityComparer<T> eq) where T : struct, IEquatable<T> {
            Assert.AreEqual(expected.Length, actual.Length);
            var notEqual = new List<(int, T, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!eq.Equals(actual[i], expected[i])) {
                    notEqual.Add((i, expected[i], actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => $"{i.Item1}:\r\n  Expected: {i.Item2}\r\n    But was:  {i.Item3}"));
                Assert.Fail(message);
            }
        }

        // same
        public static void AreEqual<T>(T expected, ReadOnlySpan<T> actual) where T : IEquatable<T> {
            var notEqual = new List<(int, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!actual[i].Equals(expected)) {
                    notEqual.Add((i, actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => i.Item1.ToString() + ": " + i.Item2.ToString()));
                Assert.Fail($"Expected: {expected}\r\n  But was:  {message}");
            }
        }

        public static void AreEqual<T>(T expected, NativeSlice<T> actual) where T : struct, IEquatable<T> {
            var notEqual = new List<(int, T)>(actual.Length);
            for (int i = 0; i < actual.Length; ++i) {
                if (!actual[i].Equals(expected)) {
                    notEqual.Add((i, actual[i]));
                }
            }
            if (notEqual.Count > 0) {
                var message = string.Join("\r\n", notEqual.Select(i => i.Item1.ToString() + ": " + i.Item2.ToString()));
                Assert.Fail($"Expected: {expected}\r\n  But was:  {message}");
            }
        }

    }
}
