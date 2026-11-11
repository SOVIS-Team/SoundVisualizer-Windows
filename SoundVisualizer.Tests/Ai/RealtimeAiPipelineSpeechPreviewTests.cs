// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Diagnostics;
using SoundVisualizer.Ai;
using Xunit.Abstractions;

namespace SoundVisualizer.Tests.Ai;

/// <summary>
/// 파이프라인의 말소리 미리보기(#422). 모바일 계측 테스트 SileroPreviewInstrumentedTest 의 두 경합 시험을 옮기고, 받아들이는 순서·
/// 만료·위협음·사용자 지정·멈춤·닫기·실패와 PC 의 레이트·채널 수를 더했다.
///
/// 틱은 스케줄러 없이 직접 돌리고, 미리보기 시각(나노초)은 <see cref="ManualClock"/> 으로 정한다. 미리보기 스레드는 진짜로 따로 돌므로
/// 소리를 넣은 뒤에는 그 스레드가 링의 완성 덩어리를 다 볼 때까지 기다린다(<see cref="Harness.WaitCaughtUp"/>).
/// </summary>
public class RealtimeAiPipelineSpeechPreviewTests(ITestOutputHelper output)
{
    private const int Block = SileroInference.BlockSize;
    private const long Ms = 1_000_000L;
    private static readonly YamnetClassMap Names = AiTestData.ClassMap;
    private static readonly int SpeechIndex = Names.IndexOf("Speech");
    private static readonly int GunshotIndex = Names.IndexOf("Gunshot, gunfire");
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);
    private static readonly IReadOnlyDictionary<string, string> NoOverrides = new Dictionary<string, string>();

    [Theory]
    [InlineData(16000, 1)]
    [InlineData(44100, 2)]
    [InlineData(48000, 2)]
    [InlineData(88200, 2)]
    [InlineData(96000, 6)]
    [InlineData(176400, 2)]
    [InlineData(192000, 8)]
    public void 첫_대화음_후보_뒤의_새_소리를_Silero_가_확인하면_화면에만_대화음이_한_틱_먼저_보인다(int rate, int channels)
    {
        using var h = new Harness(rate, channels);
        var regular = h.FirstSpeechCandidate();
        Assert.Same(regular, h.Pipeline.LastDisplayClassification()); // 아직 확인 전

        long fresh = h.Frames;
        h.FeedFreshBlocks(2, fresh);
        Assert.Same(regular, h.Pipeline.LastDisplayClassification()); // 두 덩어리로는 모자란다
        h.FeedFreshBlocks(3, fresh);

        var preview = h.Pipeline.LastDisplayClassification();
        Assert.NotNull(preview);
        Assert.True(preview.SpeechPreview);
        Assert.Equal(SoundType.Speech, preview.Coarse);
        Assert.Equal("Speech", preview.Display);
        Assert.InRange(preview.Confidence, AiPostProcessor.DefaultBaseThreshold, 1f);
        Assert.Same(regular.Top5, preview.Top5);
        Assert.Equal(regular.MappingSignature, preview.MappingSignature);
        // 정규 결과는 그대로다(진동·알림이 생기면 이쪽을 읽는다).
        Assert.Same(regular, h.Pipeline.LastClassification());
        Assert.False(regular.SpeechPreview);

        // 다음 정규 틱이 대화음을 확정하면 그 결과가 미리보기를 대신한다.
        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(250));
        var confirmed = h.Pipeline.LastClassification();
        Assert.NotNull(confirmed);
        Assert.Equal(SoundType.Speech, confirmed.Coarse);
        Assert.False(confirmed.SpeechPreview);
        Assert.Same(confirmed, h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 미리보기는_PC_의_실제_틱_주기에서도_다음_정규_결과까지_이어진다()
    {
        // PC 의 틱은 15.6ms 단위 시계와 대기로 재서 다음 정규 결과가 스냅샷 뒤 255~280ms 에 온다(바쁜 PC 는 더 늦다). Android 의 275ms 로
        // 만료하면 그 사이에 미리보기가 먼저 사라져 사람 소리 → 환경음 → 사람 소리로 깜빡였다(v2.1 을 내기 전 검토에서 틱의 7~9%).
        using var h = new Harness(16000, 1);
        var regular = h.FirstSpeechCandidate();
        h.FeedFreshBlocks(3, h.Frames);

        foreach (long ms in new long[] { 255, 266, 275, 281, 300, 330 })
        {
            h.Nanos.Set(ms * 1_000_000L);
            Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview, ms + "ms 에 미리보기가 사라졌다");
        }
        Assert.Same(regular, h.Pipeline.LastClassification()); // 그동안에도 정규 결과는 그대로다
    }

    [Fact]
    public void 미리보기는_만료_시각에_사라지고_정규_결과로_돌아간다()
    {
        using var h = new Harness(16000, 1);
        var regular = h.FirstSpeechCandidate();
        h.FeedFreshBlocks(3, h.Frames);

        h.Nanos.Set(RealtimeAiPipeline.SpeechPreviewExpiryNs - 1);
        Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);
        h.Nanos.Set(RealtimeAiPipeline.SpeechPreviewExpiryNs);
        Assert.Same(regular, h.Pipeline.LastDisplayClassification());
        h.Nanos.Set(0); // 시계를 되돌려도 지운 미리보기는 돌아오지 않는다
        Assert.Same(regular, h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 받아들임_마감_250ms_가_지난_뒤에_모인_증거로는_미리보기를_만들지_않는다()
    {
        using var h = new Harness(16000, 1);
        var regular = h.FirstSpeechCandidate();
        h.Nanos.Set(RealtimeAiPipeline.SpeechPreviewAcceptNs);
        h.FeedFreshBlocks(3, h.Frames);
        Assert.Same(regular, h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 다음_정규_추론이_시작되면_새로_받지_않지만_이미_보인_미리보기는_만료까지_둔다()
    {
        // 다음 틱이 결과 없이 끝나는(실패) 경우로 본다. 결과가 나오면 그 결과가 미리보기를 대신한다.
        using (var h = new Harness(16000, 1))
        {
            var regular = h.FirstSpeechCandidate();
            long fresh = h.Frames;
            h.FeedFreshBlocks(2, fresh);
            h.Yamnet.OnInfer = _ => throw new InvalidOperationException("tick failed");
            Assert.Equal(AiTickOutcome.Failed, h.Pipeline.RunTickOnce(250));
            h.FeedFreshBlocks(3, fresh);
            Assert.Same(regular, h.Pipeline.LastDisplayClassification());
        }
        using (var h = new Harness(16000, 1))
        {
            h.FirstSpeechCandidate();
            h.FeedFreshBlocks(3, h.Frames);
            h.Yamnet.OnInfer = _ => throw new InvalidOperationException("tick failed");
            Assert.Equal(AiTickOutcome.Failed, h.Pipeline.RunTickOnce(250));
            Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);
        }
    }

    [Fact]
    public void 정규_위협음이_오면_미리보기_대신_위협음이_보이고_위협음_뒤의_대화음은_후보를_거치지_않는다()
    {
        using var h = new Harness(16000, 1);
        h.FirstSpeechCandidate();
        h.FeedFreshBlocks(3, h.Frames);
        Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);

        h.Yamnet.TopClass = GunshotIndex;
        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(250));
        var danger = h.Pipeline.LastClassification();
        Assert.Equal(SoundType.Danger, danger?.Coarse);
        Assert.Same(danger, h.Pipeline.LastDisplayClassification());
        h.FeedFreshBlocks(3, h.Frames);
        Assert.Same(danger, h.Pipeline.LastDisplayClassification());

        // 위협음에서 확신 있는 대화음으로는 한 프레임에 확정되므로(후처리의 위협음 빠져나가기) 후보도 미리보기도 없다.
        h.Yamnet.TopClass = SpeechIndex;
        h.Nanos.Set(300 * Ms);
        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(500));
        var speech = h.Pipeline.LastClassification();
        Assert.Equal(SoundType.Speech, speech?.Coarse);
        h.FeedFreshBlocks(3, h.Frames);
        Assert.Same(speech, h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 사용자_지정이_하나라도_있으면_후보를_만들지_않는다()
    {
        // 기본과 같은 값으로 지정해도 사용자 지정이 있는 것이다(모바일의 mappingOverrides().isEmpty()).
        IReadOnlyDictionary<string, string> overrides = new Dictionary<string, string> { ["Speech"] = "speech" };
        using var h = new Harness(16000, 1, () => overrides);
        var regular = h.FirstSpeechCandidate();
        h.FeedFreshBlocks(3, h.Frames);

        Assert.True(h.Silero.ScoreCalls > 0, "the worker still runs");
        Assert.Same(regular, h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 후보를_건_뒤에_사용자_지정이_생기면_받아들여도_미리보기를_만들지_않는다()
    {
        IReadOnlyDictionary<string, string> overrides = NoOverrides;
        using var h = new Harness(16000, 1, () => overrides);
        var regular = h.FirstSpeechCandidate();
        overrides = new Dictionary<string, string> { ["Speech"] = "speech" };
        h.FeedFreshBlocks(3, h.Frames);
        Assert.Null(h.Pipeline.LastDisplayClassification()); // 매핑이 바뀐 결과는 화면에서도 바로 숨는다

        overrides = NoOverrides;
        Assert.Same(regular, h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 미리보기가_보이는_중에_사용자_지정이_생기면_바로_사라진다()
    {
        IReadOnlyDictionary<string, string> overrides = NoOverrides;
        using var h = new Harness(16000, 1, () => overrides);
        h.FirstSpeechCandidate();
        h.FeedFreshBlocks(3, h.Frames);
        Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);

        overrides = new Dictionary<string, string> { ["Speech"] = "speech" };
        Assert.Null(h.Pipeline.LastDisplayClassification());
        // 다음 틱이 새 매핑을 반영하며 미리보기도 지운다. 지정을 되돌려도 지난 미리보기는 돌아오지 않는다.
        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(250));
        overrides = NoOverrides;
        Assert.Null(h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 멈추면_미리보기가_사라지고_다시_시작하면_이전_캡처의_후보로_받아들이지_않는다()
    {
        using var h = new Harness(16000, 1);
        h.FirstSpeechCandidate();
        h.FeedFreshBlocks(3, h.Frames);
        Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);

        h.Pipeline.Stop();
        Assert.Null(h.Pipeline.LastDisplayClassification());
        int resets = h.Silero.ResetCalls;
        h.Pipeline.Start();
        Assert.Null(h.Pipeline.LastDisplayClassification());

        // 새 캡처의 첫 후보는 다시 처음부터 확인한다. 미리보기 스레드는 세대가 바뀐 것을 보고 모델 상태를 비운다.
        var regular = h.FirstSpeechCandidate();
        Assert.True(h.Silero.ResetCalls > resets);
        Assert.Same(regular, h.Pipeline.LastDisplayClassification());
        h.FeedFreshBlocks(3, h.Frames);
        Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);
    }

    [Fact]
    public void 확률이_0점9_아래인_덩어리가_끼면_받아들이지_않는다()
    {
        // 새 덩어리 가운데 두 번째만 0.89 다. 이어진 세 개가 모이지 않는다.
        var silero = new FakeSileroModel();
        using var h = new Harness(16000, 1, silero: silero);
        h.FirstSpeechCandidate();
        int before = silero.ScoreCalls;
        long fresh = h.Frames;
        // 다음 덩어리는 정규 추론이 본 소리와 걸친 덩어리 하나, 그 뒤가 새 덩어리다.
        silero.Probability = call => call == before + 3 ? .89f : 1f;
        h.FeedFreshBlocks(3, fresh);
        Assert.False(h.Pipeline.LastDisplayClassification()?.SpeechPreview);
        h.FeedFreshBlocks(5, fresh); // 세 번째부터 다섯 번째 새 덩어리가 이어지면 받아들인다
        Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);
    }

    [Fact]
    public void 닫으면_미리보기_스레드가_끝나고_Silero_를_닫는다()
    {
        var h = new Harness(44100, 2);
        h.FirstSpeechCandidate();
        h.FeedFreshBlocks(3, h.Frames);
        var worker = h.Worker;

        h.Pipeline.Dispose();

        Assert.False(worker.IsAlive);
        Assert.Equal(1, h.Silero.DisposeCalls);
        Assert.False(h.Silero.UsedAfterDispose);
        Assert.Null(h.Pipeline.LastDisplayClassification());
        // 닫은 뒤의 캡처와 깨우기는 아무것도 하지 않는다(캡처 스레드가 닫기와 엇갈려도 터지지 않는다).
        h.Pipeline.Ingest(PipelineTestAudio.Sine(1024, 2));
        worker.Signal();
        worker.RequestClose();
        Assert.Equal(1, h.Silero.DisposeCalls);
    }

    [Fact]
    public void 모델을_만드는_중에_닫아도_1초_안에_돌아오고_스레드가_끝나며_모델을_닫는다()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var silero = new FakeSileroModel();
        var pipeline = new RealtimeAiPipeline(16000, 1, new FakeYamnetModel(), Names, clock: new ManualClock().Now, runScheduler: false,
            speechModel: () =>
            {
                entered.Set();
                release.Wait(WaitLimit);
                return silero;
            });
        Assert.True(entered.Wait(WaitLimit));
        var worker = pipeline.SpeechWorkerForTest!;

        long started = Stopwatch.GetTimestamp();
        pipeline.Dispose();
        double elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        output.WriteLine($"Dispose while Silero is loading: {elapsedMs:F0} ms");
        Assert.InRange(elapsedMs, 0, RealtimeAiPipeline.CloseWaitMs + 2000);
        Assert.True(worker.IsAlive);

        release.Set();
        Assert.True(worker.Join(5000));
        Assert.Equal(1, silero.DisposeCalls);
        Assert.Equal(0, silero.ScoreCalls);
    }

    [Fact]
    public void Silero_를_만들지_못하면_미리보기만_꺼지고_정규_분류는_돈다()
    {
        var model = new FakeYamnetModel { TopClass = SpeechIndex };
        var clock = new ManualClock();
        using var pipeline = new RealtimeAiPipeline(16000, 1, model, Names, clock: clock.Now, runScheduler: false,
            speechModel: () => throw new FileNotFoundException("silero_vad.onnx"));
        var worker = pipeline.SpeechWorkerForTest!;
        Assert.True(worker.Join(5000));
        Assert.True(worker.Failed);

        pipeline.Start();
        pipeline.Ingest(PipelineTestAudio.Sine(CaptureAudioMath.RequiredMono16kSamples)); // 끝난 스레드를 깨워도 아무 일도 없다
        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(0));
        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(250));
        Assert.Equal(SoundType.Speech, pipeline.LastClassification()?.Coarse);
        Assert.Same(pipeline.LastClassification(), pipeline.LastDisplayClassification());
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    public void Silero_가_깨진_값을_한_번_내면_상태와_후보를_비우고_미리보기는_이어_간다(float broken)
    {
        // 깨진 값 하나를 모델 실패로 치면 캡처를 다시 시작할 때까지 미리보기가 조용히 꺼진다(Android #456). 지금 모델은 NaN 이 섞인
        // 소리에도 쓸 수 있는 값을 내지만(SileroInferenceTests), 값을 믿는 대신 여기서 막는다.
        var silero = new FakeSileroModel();
        using var h = new Harness(16000, 1, silero: silero);
        h.FirstSpeechCandidate();
        int before = silero.ScoreCalls;
        int modelResets = silero.ResetCalls;
        long workerResets = h.Worker.Resets;
        long fresh = h.Frames;
        // 정규 추론이 본 소리와 걸친 덩어리 하나 뒤의 두 번째 새 덩어리에서만 깨진 값을 낸다.
        silero.Probability = call => call == before + 3 ? broken : 1f;
        // 한꺼번에 넣으면 다섯 덩어리 넘게 밀려 건너뛸 수 있으므로 세 덩어리씩 넣고 따라잡기를 기다린다.
        h.FeedFreshBlocks(3, fresh);
        h.FeedFreshBlocks(6, fresh);

        // 스레드는 살아 있고, 이어받던 상태를 한 번 비웠다. 깨진 값은 증거로 넘기지 않는다(1 보다 큰 값을 말소리로 세지 않는다).
        Assert.False(h.Worker.Failed);
        Assert.True(h.Worker.IsAlive);
        Assert.Equal(modelResets + 1, silero.ResetCalls);
        Assert.Equal(workerResets + 1, h.Worker.Resets);
        Assert.Equal(1, h.Worker.InvalidScores);
        Assert.Equal(0, silero.DisposeCalls);
        // 밀린 것을 버릴 때처럼 걸려 있던 후보도 버린다. 뒤의 덩어리 넷이 모두 말소리여도 이 후보로는 받아들이지 않는다.
        Assert.False(h.Pipeline.LastDisplayClassification()?.SpeechPreview);
        Assert.True(silero.ScoreCalls >= before + 6, "the worker kept scoring after the broken value");

        // 다음 후보부터는 다시 받아들인다. 캡처를 다시 시작하지 않아도 된다(같은 스레드, 같은 모델).
        h.Yamnet.TopClass = Names.IndexOf("Music");
        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(250));
        h.Yamnet.TopClass = SpeechIndex;
        h.Nanos.Set(500 * Ms);
        h.WaitCaughtUp();
        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(500));
        var regular = h.Pipeline.LastClassification();
        Assert.Equal(SoundType.Ambient, regular?.Coarse);
        h.FeedFreshBlocks(3, h.Frames);
        Assert.True(h.Pipeline.LastDisplayClassification()?.SpeechPreview);
        Assert.Equal(1, h.Worker.InvalidScores);
        Assert.Equal(0, silero.DisposeCalls);
    }

    [Fact]
    public void Silero_가_돌다_실패하면_미리보기만_꺼지고_정규_분류는_돈다()
    {
        // 모델을 더 쓸 수 없는 실패(ONNX Runtime 의 예외 등)다. 깨진 값 하나와 달리 미리보기를 끈다.
        var silero = new FakeSileroModel { OnScore = _ => throw new InvalidOperationException("onnxruntime failed") };
        using var h = new Harness(16000, 1, silero: silero);
        h.Feed(PipelineTestAudio.WindowFrames(16000));
        SpinUntil(() => h.Worker.Failed);
        Assert.True(h.Worker.Join(5000));
        Assert.Equal(1, silero.DisposeCalls);

        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(0));
        h.Feed(Block * 4);
        Assert.Equal(AiTickOutcome.Completed, h.Pipeline.RunTickOnce(250));
        Assert.Equal(SoundType.Speech, h.Pipeline.LastClassification()?.Coarse);
        Assert.Same(h.Pipeline.LastClassification(), h.Pipeline.LastDisplayClassification());
    }

    [Fact]
    public void 미리보기_스레드가_밀리면_밀린_소리를_버리고_상태를_비운_뒤_최근_쪽부터_본다()
    {
        using var hold = new ManualResetEventSlim();
        using var entered = new ManualResetEventSlim();
        var silero = new FakeSileroModel();
        using var h = new Harness(16000, 1, silero: silero);
        h.Feed(PipelineTestAudio.WindowFrames(16000));
        h.WaitCaughtUp();
        long resets = h.Worker.Resets;
        int before = silero.ScoreCalls;

        // 덩어리 하나 안에 붙잡아 둔 채 1초를 넣는다. 풀면 다섯 덩어리(160ms)보다 많이 밀렸으므로 건너뛴다.
        h.Worker.BeforeScoreForTest = () =>
        {
            entered.Set();
            hold.Wait(WaitLimit);
        };
        h.Feed(Block);
        Assert.True(entered.Wait(WaitLimit));
        h.Feed(16000);
        h.Worker.BeforeScoreForTest = null;
        hold.Set();
        h.WaitCaughtUp();

        // 붙잡힌 덩어리를 포함해 한 번 깰 때 본 다섯 개, 건너뛴 뒤의 한두 개만 본다. 건너뛰지 않았다면 서른 개 넘게 본다.
        Assert.Equal(resets + 1, h.Worker.Resets);
        int seen = silero.ScoreCalls - before;
        Assert.InRange(seen, 6, 8);
    }

    [Fact] // stalledOptionalOwnerCannotBlockRegularDangerOrClose
    public void 덩어리_안에서_멈춘_미리보기_스레드는_정규_위협음과_닫기를_막지_못한다()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // 이 경합 시험만을 위해 모든 이름을 위협음으로 지정한다. 실제 경보 인식의 근거가 아니다(모바일과 같다).
        IReadOnlyDictionary<string, string> overrides = Names.ToDictionary(name => name, _ => "danger");
        var clock = new ManualClock();
        var pipeline = new RealtimeAiPipeline(16000, 1, YamnetInference.Create(AiTestData.ModelDirectory), Names, () => overrides,
            clock: clock.Now, runScheduler: false, speechModel: () => SileroInference.Create(AiTestData.ModelDirectory));
        try
        {
            pipeline.SpeechWorkerForTest!.BeforeScoreForTest = () =>
            {
                entered.Set();
                release.Wait(WaitLimit);
            };
            pipeline.Start();
            var mono = new float[16000];
            for (int i = 0; i < mono.Length; i++) mono[i] = (float)Math.Sin(i * (2 * Math.PI * 440 / 16000)) * .4f;
            pipeline.Ingest(mono);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "Silero did not start");

            AiTickOutcome? outcome = null;
            var tick = new Thread(() => outcome = pipeline.RunTickOnce(0)) { IsBackground = true };
            tick.Start();
            Assert.True(tick.Join(TimeSpan.FromSeconds(5)), "a stalled Silero blocked the regular tick");
            Assert.Equal(AiTickOutcome.Completed, outcome);
            Assert.Equal(SoundType.Danger, pipeline.LastClassification()?.Coarse);
            Assert.Equal(SoundType.Danger, pipeline.LastDisplayClassification()?.Coarse);

            var close = new Thread(pipeline.Dispose) { IsBackground = true };
            long started = Stopwatch.GetTimestamp();
            close.Start();
            Assert.True(close.Join(TimeSpan.FromMilliseconds(RealtimeAiPipeline.CloseWaitMs + 4000)), "a stalled Silero blocked Dispose");
            output.WriteLine($"Dispose with Silero stalled inside a block: {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms");
        }
        finally
        {
            release.Set();
            pipeline.Dispose();
        }
        Assert.True(pipeline.SpeechWorkerForTest!.Join(5000), "the Silero thread did not end after release");
    }

    [Fact] // enabledWorkerDoesNotChangeRegularOnnxOrMappingInvalidation
    public void 미리보기를_켜도_정규_ONNX_결과는_그대로이고_멈추거나_다시_시작하면_사라진다()
    {
        float[] mono = PipelineTestAudio.Sine(16000);
        var clock = new ManualClock();
        using var baseline = new RealtimeAiPipeline(16000, 1, YamnetInference.Create(AiTestData.ModelDirectory), Names,
            clock: clock.Now, runScheduler: false);
        using var candidate = new RealtimeAiPipeline(16000, 1, YamnetInference.Create(AiTestData.ModelDirectory), Names,
            clock: clock.Now, runScheduler: false, speechModel: () => SileroInference.Create(AiTestData.ModelDirectory));
        baseline.Start();
        candidate.Start();
        baseline.Ingest(mono);
        candidate.Ingest(mono);
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(AiTickOutcome.Completed, baseline.RunTickOnce(i * 250));
            Assert.Equal(AiTickOutcome.Completed, candidate.RunTickOnce(i * 250));
            Assert.Equal(baseline.ProbabilitiesForTest.ToArray(), candidate.ProbabilitiesForTest.ToArray());
            var a = baseline.LastClassification();
            var b = candidate.LastClassification();
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal((a.Coarse, a.Display, a.Confidence, a.MeetsThreshold), (b.Coarse, b.Display, b.Confidence, b.MeetsThreshold));
            Assert.False(b.SpeechPreview);
        }
        candidate.Stop();
        Assert.False(candidate.LastDisplayClassification()?.SpeechPreview ?? false);
        candidate.Start();
        Assert.Null(candidate.LastDisplayClassification());
        Assert.Null(baseline.SpeechWorkerForTest);
        Assert.NotNull(candidate.SpeechWorkerForTest);
        Assert.False(candidate.SpeechWorkerForTest.Failed);
    }

    [Fact]
    public void 앱이_만드는_파이프라인은_기본으로_미리보기를_켜고_끌_수도_있다()
    {
        Assert.True(RealtimeAiPipeline.SpeechPreviewEnabled);
        using (var on = RealtimeAiPipeline.Create(48000, 2, AiTestData.ModelDirectory, Names))
        {
            Assert.NotNull(on.SpeechWorkerForTest);
        }
        using (var off = RealtimeAiPipeline.Create(48000, 2, AiTestData.ModelDirectory, Names, speechPreview: false))
        {
            Assert.Null(off.SpeechWorkerForTest);
        }
    }

    [Fact]
    public void 실제_말소리를_실시간처럼_넣으면_정규_결과는_그대로이고_미리보기는_정규_대화음보다_먼저_온다()
    {
        // 1초 무음 → 튜토리얼 말소리(2.2초) → 0.5초 무음을 44.1kHz 스테레오 10ms 버퍼로 넣고, 소리 시각 250ms 마다 틱을 돌린다.
        // 시계는 넣은 소리의 시각이다. 미리보기를 끈 파이프라인과 정규 결과를 틱마다 맞춰 본다.
        const int rate = 44100;
        float[] talk = SileroInferenceTests.Load44100Mono("tutorial_talk.wav");
        float[] mono = new float[rate + talk.Length + rate / 2];
        talk.CopyTo(mono, rate);

        var ms = new ManualClock();
        var nanos = new ManualClock();
        using var baseline = new RealtimeAiPipeline(rate, 2, YamnetInference.Create(AiTestData.ModelDirectory), Names,
            clock: ms.Now, runScheduler: false);
        using var candidate = new RealtimeAiPipeline(rate, 2, YamnetInference.Create(AiTestData.ModelDirectory), Names,
            clock: ms.Now, runScheduler: false, speechModel: () => SileroInference.Create(AiTestData.ModelDirectory), nanoClock: nanos.Now);
        baseline.Start();
        candidate.Start();
        var worker = candidate.SpeechWorkerForTest!;

        const int chunkFrames = 441;
        var stereo = new float[chunkFrames * 2];
        long nextTickMs = 1000;
        long? firstPreviewMs = null, firstRegularSpeechMs = null;
        for (int offset = 0; offset + chunkFrames <= mono.Length; offset += chunkFrames)
        {
            long endMs = (offset + chunkFrames) * 1000L / rate;
            ms.Set(endMs);
            nanos.Set((offset + chunkFrames) * 1_000_000_000L / rate);
            for (int i = 0; i < chunkFrames; i++) stereo[2 * i] = stereo[2 * i + 1] = mono[offset + i];
            baseline.Ingest(stereo);
            candidate.Ingest(stereo);
            WaitCaughtUp(worker, candidate.AudioBufferForTest, rate);

            if (endMs >= nextTickMs)
            {
                var a = baseline.RunTickOnce(nextTickMs);
                var b = candidate.RunTickOnce(nextTickMs);
                Assert.Equal(a, b);
                var regularA = baseline.LastClassification();
                var regularB = candidate.LastClassification();
                Assert.Equal((regularA?.Coarse, regularA?.Display, regularA?.Confidence), (regularB?.Coarse, regularB?.Display, regularB?.Confidence));
                Assert.False(regularB?.SpeechPreview ?? false);
                if (regularB?.Coarse == SoundType.Speech) firstRegularSpeechMs ??= endMs;
                output.WriteLine($"tick {nextTickMs,5} ms: {b} {regularB?.Coarse}/{regularB?.Display}/{regularB?.Confidence:F2} thr={regularB?.MeetsThreshold}");
                nextTickMs += RealtimeAiPipeline.PredictIntervalMs;
            }

            var shown = candidate.LastDisplayClassification();
            if (shown?.SpeechPreview == true)
            {
                Assert.Equal(SoundType.Ambient, candidate.LastClassification()?.Coarse);
                if (firstPreviewMs is null) output.WriteLine($"preview at {endMs} ms: {shown.Display}/{shown.Confidence:F2}");
                firstPreviewMs ??= endMs;
            }
        }

        output.WriteLine($"first preview {firstPreviewMs} ms, first regular speech {firstRegularSpeechMs} ms, Silero blocks {worker.Calls}");
        Assert.False(worker.Failed);
        Assert.NotNull(firstRegularSpeechMs);
        Assert.NotNull(firstPreviewMs);
        Assert.True(firstPreviewMs < firstRegularSpeechMs, $"preview {firstPreviewMs} ms is not earlier than regular {firstRegularSpeechMs} ms");
    }

    private static void SpinUntil(Func<bool> condition)
    {
        Assert.True(SpinWait.SpinUntil(condition, WaitLimit), "condition not reached");
    }

    /// <summary>미리보기 스레드가 지금 링에 있는 완성 덩어리를 모두 볼 때까지 기다린다.</summary>
    private static void WaitCaughtUp(SileroStreamWorker worker, AiAudioBuffer buffer, int rate)
    {
        long expected = CaughtUpPosition(buffer.Cursor().Frames, rate);
        var watch = Stopwatch.StartNew();
        while (worker.NextOutputSample < expected)
        {
            Assert.False(worker.Failed, "Silero worker failed");
            Assert.True(watch.Elapsed < WaitLimit, $"worker stuck at {worker.NextOutputSample}, expected {expected}");
            // 한 번 깰 때 다섯 덩어리까지만 보므로, 남은 것이 있으면 다음 캡처 버퍼처럼 다시 깨운다.
            worker.Signal();
            Thread.Sleep(1);
        }
    }

    /// <summary>캡처 프레임 <paramref name="frames"/> 개로 다 볼 수 있는 덩어리들의 바로 뒤(16kHz 샘플).</summary>
    private static long CaughtUpPosition(long frames, int rate)
    {
        long next = 0;
        while (CaptureAudioMath.StreamSourceRange(next, Block, rate).End <= frames) next += Block;
        return next;
    }

    /// <summary>가짜 YAMNet(늘 Speech)과 가짜 Silero(늘 1)로 만든 파이프라인. 시작해 둔다.</summary>
    private sealed class Harness : IDisposable
    {
        public Harness(int rate, int channels, Func<IReadOnlyDictionary<string, string>>? overrides = null, FakeSileroModel? silero = null)
        {
            Rate = rate;
            Channels = channels;
            Silero = silero ?? new FakeSileroModel();
            Pipeline = new RealtimeAiPipeline(rate, channels, Yamnet, Names, overrides, clock: Clock.Now, runScheduler: false,
                speechModel: () => Silero, nanoClock: Nanos.Now);
            Pipeline.Start();
        }

        public int Rate { get; }

        public int Channels { get; }

        public ManualClock Clock { get; } = new();

        public ManualClock Nanos { get; } = new();

        public FakeYamnetModel Yamnet { get; } = new() { TopClass = SpeechIndex };

        public FakeSileroModel Silero { get; }

        public RealtimeAiPipeline Pipeline { get; }

        public SileroStreamWorker Worker => Pipeline.SpeechWorkerForTest!;

        public long Frames => Pipeline.AudioBufferForTest.Cursor().Frames;

        /// <summary>
        /// 한 창을 넣고 첫 틱을 돌린다. 가짜 YAMNet 이 대화음을 내므로 확정은 아직 환경음이고 대화음이 첫 후보다.
        /// 그 정규 결과를 돌려준다. 틱을 돌리기 전에 미리보기 스레드가 창을 다 보게 해, 후보 뒤의 덩어리만 남게 한다.
        /// </summary>
        public AiClassificationResult FirstSpeechCandidate()
        {
            Feed(PipelineTestAudio.WindowFrames(Rate));
            WaitCaughtUp();
            Assert.Equal(AiTickOutcome.Completed, Pipeline.RunTickOnce(Clock.Now()));
            var regular = Pipeline.LastClassification();
            Assert.NotNull(regular);
            Assert.Equal(SoundType.Ambient, regular.Coarse);
            Assert.True(regular.MeetsThreshold);
            Assert.False(regular.SpeechPreview);
            return regular;
        }

        /// <summary>사인 <paramref name="frames"/> 프레임을 10ms 남짓 버퍼로 나눠 넣는다.</summary>
        public void Feed(long frames)
        {
            if (frames <= 0) return;
            float[] pcm = PipelineTestAudio.Sine((int)frames, Channels);
            PipelineTestAudio.IngestInChunks(Pipeline, pcm, chunk: 480 * Channels);
        }

        /// <summary>프레임 <paramref name="freshAfter"/> 뒤에서 시작하는 새 덩어리 <paramref name="count"/> 개가 다 들어오게 넣고 따라잡기를 기다린다.</summary>
        public void FeedFreshBlocks(int count, long freshAfter)
        {
            long first = 0;
            while (first * Rate / CaptureAudioMath.TargetSampleRate < freshAfter) first += Block;
            long needed = CaptureAudioMath.StreamSourceRange(first + (count - 1L) * Block, Block, Rate).End;
            Feed(needed - Frames);
            WaitCaughtUp();
        }

        public void WaitCaughtUp() => RealtimeAiPipelineSpeechPreviewTests.WaitCaughtUp(Worker, Pipeline.AudioBufferForTest, Rate);

        public void Dispose() => Pipeline.Dispose();
    }
}
