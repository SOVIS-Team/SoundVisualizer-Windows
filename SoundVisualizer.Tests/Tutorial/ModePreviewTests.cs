// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using SoundVisualizer.Ai;
using SoundVisualizer.Rendering;
using SoundVisualizer.Tutorial;

namespace SoundVisualizer.Tests.Tutorial;

/// <summary>
/// 쉬운 설정의 모양 카드(안드로이드 ModePreviewPhone, #408)가 실제 엔진으로 그리는 그림. 카드는 사용자의 그 모드 설정으로 그려
/// 크기 단계를 바꾸면 그림도 바뀌어야 하고, 멈춘 그림이 비면 애니메이션을 꺼 둔 사람에게는 빈 카드가 된다.
/// </summary>
public class ModePreviewTests
{
    private static readonly int[] Modes = { VisualizerEngine.ModeWave, VisualizerEngine.ModePad, VisualizerEngine.ModeCircle, VisualizerEngine.ModeOutline };

    private static TutorialDemo Still(int mode, VisualModeSettings user) =>
        new(TutorialScene.Types, mode, runToStill: true, TutorialDemo.PreviewSettings(user), TutorialDemo.PreviewWidth, TutorialDemo.PreviewHeight);

    private static VisualModeSettings At(EasySize step)
    {
        var settings = new VisualModeSettings();
        step.ApplyTo(settings);
        return settings;
    }

    [Fact]
    public void 카드는_진하기와_반응만_튜토리얼만큼_올리고_크기는_사용자_것이다()
    {
        var user = new VisualModeSettings { Intensity = 75, VisualOpacity = 20, Sensitivity = 1, PositionSpeed = 60 };
        var demo = TutorialDemo.DemoSettings();

        var shown = TutorialDemo.PreviewSettings(user);

        Assert.Equal(75, shown.Intensity);
        Assert.Equal(60, shown.PositionSpeed);
        Assert.Equal(demo.VisualOpacity, shown.VisualOpacity);
        Assert.Equal(demo.Sensitivity, shown.Sensitivity);
        // 사용자 설정과 섞이지 않는다. 오버레이가 프레임마다 읽는 객체다.
        Assert.Equal(20, user.VisualOpacity);
        Assert.Equal(1, user.Sensitivity);

        // 사용자가 더 진하게 해 두었으면 그대로다.
        Assert.Equal(95, TutorialDemo.PreviewSettings(new VisualModeSettings { VisualOpacity = 95 }).VisualOpacity);
    }

    [Fact]
    public void 한_번_움직이는_시간은_5초를_넘지_않고_멈춘_그림과_같은_위협음에서_멈춘다()
    {
        // 저절로 5초 넘게 움직이지 않아야 멈춤 단추 없이 둘 수 있다(WCAG 2.2.2).
        Assert.InRange(TutorialDemo.PreviewPlaySec, 4.0, 5.0);
        Assert.Equal(SoundType.Danger, TutorialScript.Label(TutorialScene.Types, TutorialDemo.PreviewPlaySec));
        Assert.Equal(SoundType.Danger, TutorialScript.Label(TutorialScene.Types, TutorialScene.Types.StillAtSec()));

        // 처음부터 움직이면 환경음 → 대화음 → 위협음 세 종류의 색이 모두 나온다.
        var seen = new HashSet<SoundType>();
        for (double t = 0; t <= TutorialDemo.PreviewPlaySec; t += 1 / 30.0) seen.Add(TutorialScript.Label(TutorialScene.Types, t));
        Assert.Equal(new[] { SoundType.Ambient, SoundType.Speech, SoundType.Danger }, seen.Order());
    }

    [Fact]
    public void 멈춘_그림은_모든_모드와_단계에서_비지_않는다()
    {
        foreach (int mode in Modes)
        {
            foreach (var step in EasySizes.All)
            {
                var still = Still(mode, At(step));
                Assert.True(still.Engine.Visible, $"모드 {mode}, {step} 의 멈춘 그림이 비었다");
                Assert.Equal(SoundType.Danger, still.Label);
            }
        }
    }

    [Fact]
    public void 크기_단계를_올리면_그림도_커진다()
    {
        foreach (int mode in Modes)
        {
            double[] baseDepths = EasySizes.All.Select(step => Still(mode, At(step)).Engine.BaseDepth).ToArray();
            Assert.True(baseDepths[0] < baseDepths[1] && baseDepths[1] < baseDepths[2], $"모드 {mode}: {string.Join(", ", baseDepths)}");
        }
    }

    [Fact]
    public void 카드는_작은_화면으로_그리고_넘긴_설정을_고치지_않는다()
    {
        var user = At(EasySize.Small);
        var demo = new TutorialDemo(TutorialScene.Types, VisualizerEngine.ModeWave, runToStill: true, user, TutorialDemo.PreviewWidth, TutorialDemo.PreviewHeight);

        Assert.Equal((TutorialDemo.PreviewWidth, TutorialDemo.PreviewHeight), (demo.Width, demo.Height));
        Assert.Equal(16.0 / 9.0, TutorialDemo.PreviewWidth / TutorialDemo.PreviewHeight, 3);
        Assert.True(user.HasSameValues(At(EasySize.Small)));

        // 튜토리얼은 그대로 기준 화면과 튜토리얼 설정으로 그린다.
        var tutorial = new TutorialDemo(TutorialScene.Types, VisualizerEngine.ModeWave);
        Assert.Equal((TutorialDemo.ReferenceWidth, TutorialDemo.ReferenceHeight), (tutorial.Width, tutorial.Height));
    }
}
