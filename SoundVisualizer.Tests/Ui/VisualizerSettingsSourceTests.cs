// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using SoundVisualizer.Rendering;
using SoundVisualizer.Tutorial;

namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 시각화(Visualizers)는 그릴 값을 모두 넘겨받는다. 전역 설정(AppSettings)은 "지금 표현 모드"의 값이라, 시각화가 그것을 직접 읽으면
/// 다른 모드를 쓰는 중에 그리는 그림(쉬운 설정의 모양 카드, 튜토리얼)이 엉뚱한 값으로 그려진다. v2.1 을 내기 전, 원형 카드가 원형 모드의
/// 반지름이 아니라 지금 모드의 반지름으로 그려지던 것이 그 예다. 시각화는 WPF 프로젝트에 있어 소스를 읽어 확인한다.
/// </summary>
public class VisualizerSettingsSourceTests
{
    [Fact]
    public void 시각화는_전역_설정을_직접_읽지_않는다()
    {
        string folder = Path.Combine(RepositoryPaths.Root, "SoundVisualizer", "Visualizers");
        var files = Directory.GetFiles(folder, "*.cs");
        Assert.NotEmpty(files);
        Assert.All(files, file => Assert.DoesNotContain("AppSettings.", File.ReadAllText(file)));
    }

    [Fact]
    public void 오버레이와_미리보기_그림은_원형_반지름을_각자의_설정에서_넘긴다()
    {
        string overlay = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "SoundVisualizer", "MainWindow.xaml.cs"));
        string demo = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "SoundVisualizer", "Tutorial", "TutorialDemoRenderer.cs"));

        Assert.Contains("_renderContext.CircleRadius = AppSettings.CircleRadius;", overlay);
        Assert.Contains("_context.CircleRadius = demo.CircleRadius;", demo);
    }

    [Theory]
    [InlineData(10.0)]
    [InlineData(40.0)]
    [InlineData(100.0)]
    public void 그림의_원형_반지름은_전역_설정이_아니라_그_그림의_설정이다(double radius)
    {
        // 전역 설정은 건드리지 않는다. 그림이 받은 설정의 값만 내보여야 한다.
        var settings = new VisualModeSettings { CircleRadius = radius };
        var demo = new TutorialDemo(TutorialScene.Types, VisualizerEngine.ModeCircle, runToStill: true, TutorialDemo.PreviewSettings(settings),
            TutorialDemo.PreviewWidth, TutorialDemo.PreviewHeight);

        Assert.Equal(radius, demo.CircleRadius);
    }
}
