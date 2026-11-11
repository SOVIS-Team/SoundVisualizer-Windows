// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using SoundVisualizer.Ai;

namespace SoundVisualizer.Tests.Ai;

/// <summary>
/// 말소리 미리보기의 입력. 모바일 SileroStreamInputTest 를 옮기고(#422), PC 레이트(88.2~192kHz, 늘린 FIR)와
/// 덩어리 범위·작업 버퍼 크기를 더했다.
/// </summary>
public class SileroStreamInputTests
{
    private const int Block = SileroInference.BlockSize;

    [Theory] // sequentialBlocksMatchRegularFirExactlyAtAllSupportedRates
    [InlineData(16000)]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(88200)]
    [InlineData(96000)]
    [InlineData(176400)]
    [InlineData(192000)]
    public void 차례로_만든_덩어리는_정규_리샘플러와_비트까지_같다(int rate)
    {
        float[] source = new float[rate / 2];
        for (int i = 0; i < source.Length; i++) source[i] = (float)Math.Sin(i * .07) * .3f;
        var expected = new float[Block * 5];
        CaptureAudioMath.ResampleMonoFloatTo16kCustom(source, rate, expected);

        var actual = new float[Block * 5];
        var capture = new float[CaptureAudioMath.StreamSourceCapacity(Block, rate)];
        for (int start = 0; start < actual.Length; start += Block)
        {
            var (first, end) = CaptureAudioMath.StreamSourceRange(start, Block, rate);
            int size = (int)(end - first);
            Assert.InRange(size, 1, capture.Length);
            for (int i = 0; i < size; i++) capture[i] = first + i < 0 ? 0f : source[first + i];
            CaptureAudioMath.ResampleStreamBlock(capture.AsSpan(0, size), first, start, rate, actual.AsSpan(start, Block));
        }

        Assert.Equal(expected, actual); // 허용 오차 없이
    }

    [Theory]
    [InlineData(16000, 0, 512)]
    [InlineData(44100, -47, 1457)]  // 0..511 의 중심은 0..1408, 왼쪽 47·오른쪽 48 (모바일과 같은 수)
    [InlineData(48000, -47, 1582)]
    [InlineData(96000, -95, 3163)]  // 탭 192: 왼쪽 95·오른쪽 96
    [InlineData(192000, -191, 6325)] // 탭 384: 왼쪽 191·오른쪽 192
    public void 첫_덩어리의_원본_범위는_레이트의_탭_수를_따른다(int rate, long first, long end)
    {
        Assert.Equal((first, end), CaptureAudioMath.StreamSourceRange(0, Block, rate));
    }

    [Theory]
    [InlineData(16000)]
    [InlineData(44100)]
    [InlineData(48000)]
    [InlineData(88200)]
    [InlineData(96000)]
    [InlineData(176400)]
    [InlineData(192000)]
    public void 작업_버퍼는_어느_덩어리의_범위도_담는다(int rate)
    {
        int capacity = CaptureAudioMath.StreamSourceCapacity(Block, rate);
        long largest = 0;
        // 위상이 한 바퀴 도는 동안(16kHz 출력 16,000샘플 = 1초)의 모든 덩어리 시작을 본다.
        for (long start = 0; start < 16000 * 2; start += Block)
        {
            var (first, end) = CaptureAudioMath.StreamSourceRange(start, Block, rate);
            largest = Math.Max(largest, end - first);
        }
        Assert.InRange(largest, 1, capacity);
        Assert.True(capacity - largest <= 1, $"capacity {capacity} is much larger than needed {largest}");
    }

    [Theory]
    [InlineData(16000, 15600, 14848)]  // 30 덩어리가 다 들어왔다. 마지막의 바로 앞(29번째) 덩어리부터
    [InlineData(16000, 1000, 0)]
    [InlineData(16000, 0, 0)]
    [InlineData(44100, 48, 0)]
    [InlineData(48000, 48_000, 15360)] // 1초, 오른쪽 48 을 빼면 15,984샘플 → 31 덩어리, 그 앞(30번째)부터
    public void 밀린_것을_버리고_다시_시작할_곳은_마지막_완성_덩어리의_앞이다(int rate, long frames, long expected)
    {
        Assert.Equal(expected, CaptureAudioMath.NewestStreamBlockStart(frames, Block, rate));
    }

    [Fact] // ringRangesCannotReadFutureOrPreviousSession
    public void 링_범위로는_미래의_소리나_이전_캡처의_소리를_읽지_못한다()
    {
        var ring = new AiAudioBuffer(48000, 1);
        ring.IngestMono(Enumerable.Range(0, 100).Select(i => (float)i).ToArray());
        var cursor = ring.Cursor();
        Assert.Equal(100, cursor.Frames);
        var dst = new float[10];

        Assert.True(ring.CopyRange(cursor.Generation, -3, dst));
        Assert.Equal(new float[] { 0, 0, 0, 0, 1, 2, 3, 4, 5, 6 }, dst);
        Assert.False(ring.CopyRange(cursor.Generation, 95, dst));

        ring.Reset();
        ring.IngestMono(Enumerable.Repeat(999f, 100).ToArray());
        Assert.False(ring.CopyRange(cursor.Generation, 0, dst));
        Assert.NotEqual(cursor.Generation, ring.Cursor().Generation);
        Assert.Equal(100, ring.Cursor().Frames);
    }

    [Fact] // overwrittenPcmCannotBeReplayedAsFresh
    public void 덮어쓴_소리는_새_소리로_다시_읽지_못한다()
    {
        var ring = new AiAudioBuffer(48000, 1);
        ring.IngestMono(Enumerable.Range(0, 100_000).Select(i => (float)i).ToArray());
        var cursor = ring.Cursor();
        var dst = new float[2];

        Assert.False(ring.CopyRange(cursor.Generation, 0, dst));
        Assert.True(ring.CopyRange(cursor.Generation, 99_998, dst));
        Assert.Equal(new float[] { 99_998, 99_999 }, dst);
    }

    [Fact]
    public void 링을_한_바퀴_넘게_돌아도_범위는_이어진_순서로_읽힌다()
    {
        // 192kHz 링(524,288칸)을 여러 번 감아 끝과 처음에 걸친 범위를 읽는다. 2초(384,000) 안쪽만 남아 있다.
        var ring = new AiAudioBuffer(192000, 1);
        var chunk = new float[50_000];
        long written = 0;
        for (int round = 0; round < 25; round++)
        {
            for (int i = 0; i < chunk.Length; i++) chunk[i] = (written + i) % 1_000_003;
            ring.IngestMono(chunk);
            written += chunk.Length;
        }
        var cursor = ring.Cursor();
        Assert.Equal(written, cursor.Frames);
        long wrapStart = written - (written % ring.RingCapacity) - 100; // 링 끝을 가로지르는 곳
        var dst = new float[300];

        Assert.True(ring.CopyRange(cursor.Generation, wrapStart, dst));
        for (int i = 0; i < dst.Length; i++) Assert.Equal((wrapStart + i) % 1_000_003, dst[i]);
        Assert.False(ring.CopyRange(cursor.Generation, written - 384_001, new float[1]));
        Assert.True(ring.CopyRange(cursor.Generation, written - 384_000, new float[1]));
    }

    [Fact]
    public void 여러_채널은_정규_경로와_같게_섞은_소리를_읽는다()
    {
        // 5.1 의 앞 가운데만 소리가 있으면 ITU 계수(0.707)로 섞인 모노가 링에 남는다. 미리보기도 같은 모노를 읽는다.
        var ring = new AiAudioBuffer(48000, 6);
        var pcm = new float[6 * 4];
        for (int frame = 0; frame < 4; frame++) pcm[frame * 6 + 2] = 1f;
        ring.IngestInterleaved(pcm);
        var dst = new float[4];

        Assert.True(ring.CopyRange(ring.Cursor().Generation, 0, dst));
        Assert.All(dst, value => Assert.Equal(0.707f, value, 1e-6f));
    }
}
