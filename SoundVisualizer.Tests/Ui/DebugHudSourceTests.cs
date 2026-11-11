// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 오버레이가 AI 의 결과 알림을 개발자 HUD 의 읽기로 잇는 배선(MainWindow.xaml.cs, MainWindow.DebugHud.cs)을 소스로 확인한다.
/// 알림을 합치는 규칙은 AiHudRefreshTests 가, 파이프라인이 언제 알리는지는 RealtimeAiPipelineChangeNotificationTests 가 본다.
/// 오버레이는 화면 전체를 덮는 WPF 창이고 출력 장치가 있어야 떠서 단위 시험에서 띄우지 않는다.
/// </summary>
public class DebugHudSourceTests
{
    [Fact]
    public void 오버레이는_파이프라인을_만들_때_결과_알림을_HUD_읽기에_잇는다()
    {
        // 100ms 타이머만으로는 그보다 짧게 사는 말소리 미리보기가 화면과 기록에서 통째로 빠진다(안드로이드 #412).
        string main = AppSource.Read("MainWindow.xaml.cs");
        int create = main.IndexOf("RealtimeAiPipeline.Create(", StringComparison.Ordinal);
        Assert.True(create >= 0, "RealtimeAiPipeline.Create 호출을 찾지 못했습니다.");
        string call = main.Substring(create, main.IndexOf(";", create, StringComparison.Ordinal) - create);
        Assert.Contains("classificationChanged: _hudRefresh.Notify", call);

        // 추론 스레드는 UI 스레드를 기다리지 않는다(Invoke 가 아니라 InvokeAsync). 읽는 일은 타이머와 같은 PollDebugHud 다.
        Assert.Contains("_hudRefresh = new AiHudRefresh(read => Dispatcher.InvokeAsync(read), PollDebugHud);", main);
        // AI 는 BootSequence 가 배경에서 만든다. 그 전에 채워 두어야 한다.
        int assigned = main.IndexOf("_hudRefresh = new AiHudRefresh(", StringComparison.Ordinal);
        int boot = main.IndexOf("BootSequence();", StringComparison.Ordinal);
        Assert.True(assigned >= 0 && assigned < boot, "AI 를 만들기 전에 _hudRefresh 를 채워야 합니다.");
    }

    [Fact]
    public void 결과_알림은_HUD_를_읽는_동안에만_받고_타이머는_그대로_둔다()
    {
        string hud = AppSource.Read("MainWindow.DebugHud.cs");

        // 개발자 모드가 꺼져 있으면 추론마다 UI 스레드를 깨우지 않는다. 기록은 HUD 를 읽는 동안에만 돈다.
        string sync = AppSource.MethodBody(hud, "SyncDebugHud");
        Assert.Contains("_hudRefresh.Active = _debugHud.IsPolling;", sync);
        // 결과가 오지 않는 동안(무음)의 나이와 소리 크기는 타이머가 읽는다.
        Assert.Contains("_debugHudTimer.Start();", sync);
        Assert.Contains("_debugHudTimer.Tick += (s, e) => PollDebugHud();", hud);

        // 창을 닫으면 더 받지 않는다.
        Assert.Contains("_hudRefresh.Active = false;", AppSource.MethodBody(hud, "InitDebugHud"));
    }
}
