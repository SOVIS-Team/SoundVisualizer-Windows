// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using SoundVisualizer.Ai;

namespace SoundVisualizer.Tests.Ai;

/// <summary>
/// 파이프라인의 결과 알림(Android 의 onClassificationChanged, #412). 개발자 HUD 가 100ms 타이머를 기다리지 않고 새 결과를 바로 읽게 하는
/// 깨우기다. 정규 결과를 게시하거나 지운 틱에서만 알리고, 받는 쪽이 던져도 추론은 이어진다. 미리보기를 게시할 때의 알림은
/// RealtimeAiPipelineSpeechPreviewTests 에 있다.
/// </summary>
public class RealtimeAiPipelineChangeNotificationTests
{
    private static readonly YamnetClassMap Names = AiTestData.ClassMap;
    private static readonly int SpeechIndex = Names.IndexOf("Speech");
    private static readonly int GunshotIndex = Names.IndexOf("Gunshot, gunfire");

    [Fact]
    public void 정규_결과를_게시한_틱은_그_결과가_보이는_뒤에_한_번_알린다()
    {
        var model = new FakeYamnetModel { TopClass = GunshotIndex };
        var clock = new ManualClock();
        RealtimeAiPipeline? pipeline = null;
        var seenAtNotify = new List<AiClassificationResult?>();
        using var created = pipeline = new RealtimeAiPipeline(16000, 1, model, Names, clock: clock.Now, runScheduler: false,
            classificationChanged: () => seenAtNotify.Add(pipeline!.LastDisplayClassification()));
        pipeline.Start();
        Assert.Empty(seenAtNotify); // 시작은 알리지 않는다(부르는 쪽이 아는 일이다)

        pipeline.Ingest(PipelineTestAudio.Sine(PipelineTestAudio.WindowFrames(16000)));
        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(0));

        // 알림은 값을 싣지 않는다. 받는 쪽이 다시 읽으면 방금 게시한 결과가 이미 보여야 한다.
        var first = pipeline.LastClassification();
        Assert.NotNull(first);
        Assert.Equal(new[] { first }, seenAtNotify);

        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(250));
        Assert.Equal(2, seenAtNotify.Count);
        Assert.Same(pipeline.LastClassification(), seenAtNotify[1]);
    }

    [Fact]
    public void 결과가_바뀌지_않은_틱은_알리지_않는다()
    {
        var model = new FakeYamnetModel { TopClass = SpeechIndex };
        var clock = new ManualClock(10_000);
        int notified = 0;
        using var pipeline = new RealtimeAiPipeline(16000, 1, model, Names, clock: clock.Now, runScheduler: false,
            classificationChanged: () => notified++);
        pipeline.Start();

        // 한 창이 모이지 않았다.
        Assert.Equal(AiTickOutcome.NotEnoughAudio, pipeline.RunTickOnce(10_000));
        Assert.Equal(0, notified);

        pipeline.Ingest(PipelineTestAudio.Sine(PipelineTestAudio.WindowFrames(16000)));
        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(10_000));
        Assert.Equal(1, notified);

        // 추론이 실패하면 결과는 그대로다.
        model.OnInfer = _ => throw new InvalidOperationException("tick failed");
        Assert.Equal(AiTickOutcome.Failed, pipeline.RunTickOnce(10_250));
        Assert.Equal(1, notified);
        model.OnInfer = null;

        // 소리가 그쳐 추론을 건너뛰는 틱은 마지막 결과를 그대로 든다. 그동안의 나이는 HUD 의 타이머가 읽는다.
        int completed = 0;
        long now = 10_500;
        while (pipeline.RunTickOnce(now) == AiTickOutcome.Completed)
        {
            completed++;
            now += 250;
            Assert.True(completed < 20, "the silence gate never closed");
        }
        Assert.Equal(AiTickOutcome.SkippedForSilence, pipeline.RunTickOnce(now));
        Assert.Equal(AiTickOutcome.SkippedForSilence, pipeline.RunTickOnce(now + 250));
        Assert.Equal(1 + completed, notified);

        // 멈춤과 닫기도 알리지 않는다.
        pipeline.Stop();
        pipeline.Dispose();
        Assert.Equal(AiTickOutcome.Closed, pipeline.RunTickOnce(now + 500));
        Assert.Equal(1 + completed, notified);
    }

    [Fact]
    public void 사용자_지정이_바뀌어_결과를_지운_틱은_추론을_건너뛰어도_알린다()
    {
        IReadOnlyDictionary<string, string> overrides = new Dictionary<string, string>();
        var model = new FakeYamnetModel { TopClass = GunshotIndex };
        var clock = new ManualClock(10_000);
        int notified = 0;
        using var pipeline = new RealtimeAiPipeline(16000, 1, model, Names, () => overrides, clock: clock.Now, runScheduler: false,
            classificationChanged: () => notified++);
        pipeline.Start();
        pipeline.Ingest(PipelineTestAudio.Sine(PipelineTestAudio.WindowFrames(16000)));
        long now = 10_000;
        while (pipeline.RunTickOnce(now) == AiTickOutcome.Completed)
        {
            now += 250;
            Assert.True(now < 20_000, "the silence gate never closed");
        }
        int before = notified;
        Assert.NotNull(pipeline.LastClassification());

        // 무음으로 건너뛰는 틱에도 새 매핑을 반영해 옛 결과를 지운다. 화면용 결과가 바뀌었으므로 알린다.
        overrides = new Dictionary<string, string> { ["Gunshot, gunfire"] = "ambient" };
        Assert.Equal(AiTickOutcome.SkippedForSilence, pipeline.RunTickOnce(now + 250));
        Assert.Null(pipeline.LastClassification());
        Assert.Equal(before + 1, notified);

        // 같은 지정으로는 다시 지우지 않으므로 알리지도 않는다.
        Assert.Equal(AiTickOutcome.SkippedForSilence, pipeline.RunTickOnce(now + 500));
        Assert.Equal(before + 1, notified);
    }

    [Fact]
    public void 받는_쪽이_던져도_틱은_결과를_게시하고_예외를_내지_않는다()
    {
        var model = new FakeYamnetModel { TopClass = GunshotIndex };
        var clock = new ManualClock();
        int notified = 0;
        using var pipeline = new RealtimeAiPipeline(16000, 1, model, Names, clock: clock.Now, runScheduler: false,
            classificationChanged: () =>
            {
                notified++;
                throw new InvalidOperationException("HUD is broken");
            });
        pipeline.Start();
        pipeline.Ingest(PipelineTestAudio.Sine(PipelineTestAudio.WindowFrames(16000)));

        // HUD 의 결함이 추론 스레드를 끝내면 화면의 색 구분까지 멈춘다.
        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(0));
        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(250));

        Assert.Equal(2, notified);
        Assert.Equal(SoundType.Danger, pipeline.LastClassification()?.Coarse);
        Assert.Equal(new RealtimeAiPipeline.InferenceStats(2, 2, 0), pipeline.Stats);
    }

    [Fact]
    public void 알림은_추론_잠금을_놓은_뒤에_온다()
    {
        // 받는 쪽이 알림 안에서 파이프라인을 닫아도(오버레이가 닫히는 중) 추론 잠금에서 서로 기다리지 않는다. 잠금 안에서 알리면
        // 닫기가 "돌고 있는 추론"을 1초 기다린 뒤 포기한다.
        var model = new FakeYamnetModel { TopClass = GunshotIndex };
        var clock = new ManualClock();
        RealtimeAiPipeline? pipeline = null;
        double closeMs = -1;
        pipeline = new RealtimeAiPipeline(16000, 1, model, Names, clock: clock.Now, runScheduler: false,
            classificationChanged: () =>
            {
                // 다른 스레드에서 닫는다. 같은 스레드에서는 잠금을 다시 잡을 수 있어 잠금 안팎을 가르지 못한다.
                long started = System.Diagnostics.Stopwatch.GetTimestamp();
                var close = new Thread(pipeline!.Dispose);
                close.Start();
                close.Join();
                closeMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            });
        pipeline.Start();
        pipeline.Ingest(PipelineTestAudio.Sine(PipelineTestAudio.WindowFrames(16000)));

        Assert.Equal(AiTickOutcome.Completed, pipeline.RunTickOnce(0));

        Assert.InRange(closeMs, 0, RealtimeAiPipeline.CloseWaitMs / 2);
        Assert.Equal(1, model.DisposeCalls);
        Assert.False(model.DisposedDuringInference);
    }
}
