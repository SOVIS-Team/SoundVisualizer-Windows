// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using SoundVisualizer.Ai;
using Xunit.Abstractions;

namespace SoundVisualizer.Tests.Ai;

/// <summary>
/// 앱과 함께 배포하는 실제 Silero VAD. 모바일 계측 테스트 SileroPreviewInstrumentedTest 의 모델 확인을 옮기고(PC 의 ONNX Runtime 은
/// 단위 테스트에서 돈다), 실제 말소리(튜토리얼의 말소리, 퍼블릭 도메인)와 무음에서의 확률, 고정값과 깨진 파일을 더했다.
/// </summary>
public class SileroInferenceTests(ITestOutputHelper output)
{
    private const int Block = SileroInference.BlockSize;

    [Fact]
    public void 배포하는_모델_파일은_Android_와_같은_크기와_SHA256이다()
    {
        // 두 앱이 같은 모델을 쓴다는 약속이다. 모델을 바꾸면 SileroInference.kt 의 SHA256 과 함께 여기도 고친다.
        Assert.Equal(
            ("silero_vad.onnx", 2_327_524L, "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3"),
            (SileroModelFile.Onnx.Name, SileroModelFile.Onnx.ByteCount, SileroModelFile.Onnx.Sha256));
        string path = Path.Combine(AiTestData.ModelDirectory, SileroModelFile.Onnx.Name);
        Assert.Equal(SileroModelFile.Onnx.ByteCount, new FileInfo(path).Length);
        Assert.Equal(SileroModelFile.Onnx.Sha256, ModelFileIntegrity.Sha256Hex(path));
    }

    [Fact] // pinnedRealModelLoadsAndRecurrentStateResets
    public void 고정한_실제_모델을_불러오고_상태를_비우면_처음과_같은_값을_낸다()
    {
        using var model = SileroInference.Create(AiTestData.ModelDirectory);
        var chunk = new float[Block];
        for (int i = 0; i < chunk.Length; i++) chunk[i] = (float)Math.Sin(i * .07) * .4f;

        float first = model.Score(chunk);
        Assert.InRange(first, 0f, 1f);
        for (int i = 0; i < 4; i++) model.Score(chunk);
        model.Reset();
        Assert.Equal(first, model.Score(chunk));
    }

    [Fact]
    public void 실제_말소리에서는_받아들일_만큼_이어서_높고_무음에서는_낮다()
    {
        // 튜토리얼의 말소리("Anna Pete, hi! Hi, we are here!", 44.1kHz 모노)를 미리보기 스레드와 같은 스트림 리샘플로 16kHz 덩어리로 바꾼다.
        float[] speech = Load44100Mono("tutorial_talk.wav");
        float[] speech16k = StreamTo16k(speech, 44100);
        using var model = SileroInference.Create(AiTestData.ModelDirectory);

        var probabilities = new List<float>();
        long started = Stopwatch.GetTimestamp();
        for (int start = 0; start + Block <= speech16k.Length; start += Block)
        {
            probabilities.Add(model.Score(speech16k.AsSpan(start, Block)));
        }
        double perBlockMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds / probabilities.Count;
        output.WriteLine($"speech blocks={probabilities.Count} max={probabilities.Max():F3} per block {perBlockMs:F3} ms");
        output.WriteLine(string.Join(" ", probabilities.Select(p => p.ToString("F2", System.Globalization.CultureInfo.InvariantCulture))));

        int longest = LongestRun(probabilities, SileroPreviewGate<string>.SpeechProbability);
        Assert.True(longest >= SileroPreviewGate<string>.RequiredBlocks, $"longest run of p >= 0.9 is {longest} blocks");

        model.Reset();
        var silence = new float[Block];
        var quiet = Enumerable.Range(0, 30).Select(_ => model.Score(silence)).ToArray();
        output.WriteLine($"silence max={quiet.Max():F5}");
        Assert.All(quiet, p => Assert.True(p < 0.1f, $"silence p={p}"));
    }

    [Fact]
    public void 한_바이트만_바뀌었거나_잘렸거나_없는_모델로는_세션을_만들지_않는다()
    {
        string dir = Directory.CreateTempSubdirectory("sv-silero-").FullName;
        try
        {
            string path = Path.Combine(dir, SileroModelFile.Onnx.Name);
            Assert.Equal(path, Assert.Throws<FileNotFoundException>(() => SileroInference.Create(dir)).FileName);

            byte[] bytes = File.ReadAllBytes(Path.Combine(AiTestData.ModelDirectory, SileroModelFile.Onnx.Name));
            bytes[bytes.Length / 2] ^= 0x01;
            File.WriteAllBytes(path, bytes);
            var flipped = Assert.Throws<InvalidDataException>(() => SileroInference.Create(dir));
            Assert.Contains("SHA-256", flipped.Message);

            File.WriteAllBytes(path, bytes[..^1]);
            var truncated = Assert.Throws<InvalidDataException>(() => SileroInference.Create(dir));
            Assert.Contains("2327523 bytes", truncated.Message);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.MaxValue)]
    public void 지금_모델은_깨진_소리에도_쓸_수_있는_확률을_내고_상태를_비우면_처음과_같은_값을_낸다(float sample)
    {
        // 루프백 캡처의 float PCM 에는 NaN 이나 무한대가 섞일 수 있다(소리를 내는 앱의 결함). 고정한 v6.2.3 모델은 그런 덩어리에도
        // 0~1 의 값을 내고 뒤의 덩어리까지 깨뜨리지 않는다. 그래서 깨진 확률을 막는 일(SileroStreamWorker)은 지금 모델에서는 일어나지
        // 않는 경우의 대비다. 모델을 바꿔 이 성질이 달라지면 여기서 먼저 알게 된다.
        using var model = SileroInference.Create(AiTestData.ModelDirectory);
        var chunk = new float[Block];
        for (int i = 0; i < chunk.Length; i++) chunk[i] = (float)Math.Sin(i * .07) * .4f;
        float first = model.Score(chunk);

        var broken = new float[Block];
        Array.Fill(broken, sample);
        Assert.True(SileroInference.IsValidProbability(model.Score(broken)));
        for (int i = 0; i < 4; i++) Assert.True(SileroInference.IsValidProbability(model.Score(chunk)));

        model.Reset();
        Assert.Equal(first, model.Score(chunk));
    }

    [Theory]
    [InlineData(0f, true)]
    [InlineData(0.5f, true)]
    [InlineData(1f, true)]
    [InlineData(-0.0001f, false)]
    [InlineData(1.0001f, false)]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(float.NegativeInfinity, false)]
    public void 확률로_쓸_만한_값은_유한하고_0에서_1_사이다(float probability, bool expected)
    {
        Assert.Equal(expected, SileroInference.IsValidProbability(probability));
    }

    [Fact]
    public void 덩어리_길이가_다르거나_닫은_뒤에는_부르지_않는다()
    {
        var model = SileroInference.Create(AiTestData.ModelDirectory);
        Assert.Throws<ArgumentException>(() => model.Score(new float[Block - 1]));
        model.Dispose();
        model.Dispose(); // 여러 번 닫아도 된다
        Assert.Throws<ObjectDisposedException>(() => model.Score(new float[Block]));
    }

    /// <summary>연달아 <paramref name="threshold"/> 이상인 가장 긴 덩어리 수.</summary>
    internal static int LongestRun(IEnumerable<float> probabilities, float threshold)
    {
        int longest = 0, run = 0;
        foreach (float p in probabilities)
        {
            run = p >= threshold ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }
        return longest;
    }

    /// <summary>미리보기 스레드처럼 덩어리마다 범위를 꺼내 16kHz 로 바꾼다. 끝의 모자란 덩어리는 버린다.</summary>
    internal static float[] StreamTo16k(float[] source, int rate)
    {
        var result = new List<float>();
        var block = new float[Block];
        var capture = new float[CaptureAudioMath.StreamSourceCapacity(Block, rate)];
        for (long start = 0; ; start += Block)
        {
            var (first, end) = CaptureAudioMath.StreamSourceRange(start, Block, rate);
            if (end > source.Length) break;
            int size = (int)(end - first);
            for (int i = 0; i < size; i++) capture[i] = first + i < 0 ? 0f : source[first + i];
            CaptureAudioMath.ResampleStreamBlock(capture.AsSpan(0, size), first, start, rate, block);
            result.AddRange(block);
        }
        return result.ToArray();
    }

    /// <summary>테스트 출력 폴더 Sounds 의 16비트 PCM 모노 WAV(44.1kHz)를 -1~1 로 읽는다.</summary>
    internal static float[] Load44100Mono(string fileName)
    {
        byte[] wav = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Sounds", fileName));
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        int offset = 12;
        float[]? samples = null;
        while (offset + 8 <= wav.Length)
        {
            string id = Encoding.ASCII.GetString(wav, offset, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(offset + 4));
            var body = wav.AsSpan(offset + 8, size);
            if (id == "fmt ")
            {
                Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(body));             // PCM
                Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(body[2..]));        // 모노
                Assert.Equal(44100, BinaryPrimitives.ReadInt32LittleEndian(body[4..]));
                Assert.Equal(16, BinaryPrimitives.ReadInt16LittleEndian(body[14..]));      // 16비트
            }
            else if (id == "data")
            {
                samples = new float[size / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = BinaryPrimitives.ReadInt16LittleEndian(body[(i * 2)..]) / 32768f;
            }
            offset += 8 + size + (size & 1);
        }
        return samples ?? throw new InvalidDataException("no data chunk in " + fileName);
    }
}
