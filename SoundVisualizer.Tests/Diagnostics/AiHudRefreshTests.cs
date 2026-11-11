// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Collections.Concurrent;
using SoundVisualizer.Diagnostics;

namespace SoundVisualizer.Tests.Diagnostics;

/// <summary>
/// AI 의 결과 알림을 개발자 HUD 의 읽기로 바꾸는 규칙(모바일 AiHudRefreshTest, #412/#413). 알림은 타이머를 기다리지 않고, 몰려 와도
/// UI 스레드에 하나만 쌓이며, HUD 를 읽지 않는 동안에는 아무것도 넘기지 않는다. UI 스레드는 넘겨받은 일을 담아 두었다 돌리는 줄로 흉내 낸다.
/// </summary>
public class AiHudRefreshTests
{
    /// <summary>넘겨받은 일을 담아 두는 UI 스레드 흉내. 테스트가 원하는 때에 하나씩 돌린다.</summary>
    private sealed class FakeUiThread
    {
        private readonly ConcurrentQueue<Action> _posted = new();

        public int Pending => _posted.Count;

        public void Post(Action action) => _posted.Enqueue(action);

        public bool RunOne()
        {
            if (!_posted.TryDequeue(out var action)) return false;
            action();
            return true;
        }

        public int RunAll()
        {
            int ran = 0;
            while (RunOne()) ran++;
            return ran;
        }
    }

    [Fact] // notificationDoesNotWaitForHeartbeat
    public void 알림은_타이머를_기다리지_않고_바로_읽기를_넘긴다()
    {
        var ui = new FakeUiThread();
        int reads = 0;
        var refresh = new AiHudRefresh(ui.Post, () => reads++) { Active = true };

        refresh.Notify();

        // 알린 스레드에서는 읽지 않는다(추론 스레드가 HUD 의 일을 하지 않는다). UI 스레드가 돌릴 때 읽는다.
        Assert.Equal(0, reads);
        Assert.Equal(1, ui.Pending);
        ui.RunAll();
        Assert.Equal(1, reads);
    }

    [Fact] // slowHudCoalescesBurstsAndReadsNewestValue
    public void 몰려_온_알림은_하나로_합치고_읽을_때는_가장_새_값을_읽는다()
    {
        var ui = new FakeUiThread();
        int latest = 0;
        var seen = new List<int>();
        var refresh = new AiHudRefresh(ui.Post, () => seen.Add(latest)) { Active = true };

        for (int i = 1; i <= 1000; i++)
        {
            latest = i;
            refresh.Notify();
        }

        // UI 스레드가 바쁜 동안 결과 천 개가 지나가도 쌓인 읽기는 하나다.
        Assert.Equal(1, ui.Pending);
        ui.RunAll();
        Assert.Equal(new[] { 1000 }, seen);
    }

    [Fact]
    public void 읽는_사이에_온_알림은_한_번_더_넘겨_새_결과를_놓치지_않는다()
    {
        var ui = new FakeUiThread();
        int latest = 1;
        var seen = new List<int>();
        AiHudRefresh? refresh = null;
        refresh = new AiHudRefresh(ui.Post, () =>
        {
            seen.Add(latest);
            if (latest == 1)
            {
                // 값을 읽은 바로 뒤에 다음 결과가 게시됐다. 이 알림을 "이미 넘겼다"로 버리면 2 는 다음 타이머까지 보이지 않는다.
                latest = 2;
                refresh!.Notify();
                refresh.Notify();
            }
        })
        { Active = true };

        refresh.Notify();
        Assert.Equal(2, ui.RunAll());

        Assert.Equal(new[] { 1, 2 }, seen);
        Assert.Equal(0, ui.Pending);
    }

    [Fact] // pauseUnsubscribesAndResumeReadsCurrentSource
    public void HUD_를_읽지_않는_동안에는_넘기지_않고_넘겨_둔_읽기도_하지_않는다()
    {
        var ui = new FakeUiThread();
        int reads = 0;
        var refresh = new AiHudRefresh(ui.Post, () => reads++);

        // 개발자 모드가 꺼져 있으면 추론마다 UI 스레드를 깨우지 않는다.
        Assert.False(refresh.Active);
        refresh.Notify();
        Assert.Equal(0, ui.Pending);

        // 넘긴 뒤 돌기 전에 꺼졌으면(개발자 모드를 끄거나 오버레이를 닫았다) 읽지 않는다.
        refresh.Active = true;
        refresh.Notify();
        refresh.Active = false;
        ui.RunAll();
        Assert.Equal(0, reads);

        // 다시 켜면 다음 알림부터 넘긴다. 꺼져 있던 동안의 표시가 남아 막지 않는다.
        refresh.Active = true;
        refresh.Notify();
        Assert.Equal(1, ui.Pending);
        ui.RunAll();
        Assert.Equal(1, reads);
    }

    [Fact]
    public void 넘기다_실패해도_알린_스레드로_던지지_않고_다음_알림은_다시_넘긴다()
    {
        var ui = new FakeUiThread();
        bool broken = true;
        int reads = 0;
        var refresh = new AiHudRefresh(
            action =>
            {
                if (broken) throw new InvalidOperationException("dispatcher is gone");
                ui.Post(action);
            },
            () => reads++)
        { Active = true };

        // 추론 스레드로 예외가 새면 그 틱이나 미리보기 스레드가 끝난다.
        refresh.Notify();
        refresh.Notify();
        Assert.Equal(0, ui.Pending);

        broken = false;
        refresh.Notify();
        Assert.Equal(1, ui.Pending);
        ui.RunAll();
        Assert.Equal(1, reads);
    }

    [Fact]
    public void 여러_스레드가_한꺼번에_알려도_마지막_결과는_반드시_읽힌다()
    {
        // 틱 스레드와 미리보기 스레드가 함께 알린다. 어느 알림이든 돌아온 뒤에는 그 뒤에 도는 읽기가 하나는 남아 있어야 한다.
        var ui = new FakeUiThread();
        long latest = 0;
        long seen = 0;
        int reads = 0;
        var refresh = new AiHudRefresh(ui.Post, () =>
        {
            Interlocked.Exchange(ref seen, Interlocked.Read(ref latest));
            Interlocked.Increment(ref reads);
        })
        { Active = true };

        const int perThread = 20_000;
        using var stop = new CancellationTokenSource();
        var uiThread = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                if (!ui.RunOne()) Thread.Yield();
            }
        });
        uiThread.Start();
        var notifiers = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
        {
            for (int i = 0; i < perThread; i++)
            {
                Interlocked.Increment(ref latest);
                refresh.Notify();
            }
        })).ToArray();
        foreach (var thread in notifiers) thread.Start();
        foreach (var thread in notifiers) thread.Join();
        stop.Cancel();
        uiThread.Join();
        ui.RunAll();

        Assert.Equal(2L * perThread, Interlocked.Read(ref seen));
        // 알림 4만 번이 읽기 4만 번이 되지 않는다(합쳐진다). 몇 번인지는 실행마다 다르다.
        Assert.InRange(Volatile.Read(ref reads), 1, 2 * perThread);
    }

    [Fact]
    public void 넘길_곳과_읽을_것이_없으면_만들지_않는다()
    {
        Assert.Throws<ArgumentNullException>(() => new AiHudRefresh(null!, () => { }));
        Assert.Throws<ArgumentNullException>(() => new AiHudRefresh(_ => { }, null!));
    }
}
