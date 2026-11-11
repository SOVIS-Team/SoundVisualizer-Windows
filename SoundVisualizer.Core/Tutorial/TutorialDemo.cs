// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using SoundVisualizer.Ai;
using SoundVisualizer.Rendering;

namespace SoundVisualizer.Tutorial
{
    /// <summary>
    /// 튜토리얼 한 쪽의 그림을 계산한다(Android TutorialDemoInputs 와 DemoPhone 의 엔진 부분). <see cref="TutorialScript"/> 의 소리를
    /// 오버레이와 같은 <see cref="VisualizerEngine"/> 에 흘리므로, 그림은 앱이 실제로 그리는 모양 그대로다.
    ///
    /// - 엔진은 기준 화면(<see cref="ReferenceWidth"/>×<see cref="ReferenceHeight"/>, 16:9 모니터)의 크기로 돈다. 튜토리얼 창은 그 결과를
    ///   그림 크기로 줄여 그린다. 그래서 여백·두께·보이기 시작하는 깊이가 실제 화면을 줄여 놓은 비율 그대로다(안드로이드는 엔진의
    ///   density 를 줄여 같은 일을 한다). 모니터마다 달리 잡지 않고 한 화면으로 고정해 어느 PC 에서나 같은 그림이 된다.
    /// - 사운드 모드는 2.0 으로 고정한다. 대본이 좌우 두 채널이다.
    /// - 사용자가 꺼 둔 종류도 모두 그린다. 튜토리얼은 앱이 무엇을 하는지 보여 주는 곳이라 크기·진하기·반응도 지금 설정이 아니라
    ///   <see cref="DemoSettings"/> 로 그린다. 표현 모드와 색만 사용자 것을 쓴다(색은 창이 라벨마다 고른다).
    /// - 실제 소리·오버레이와는 닿지 않는다. 튜토리얼을 연 채 오버레이가 돌아도 둘이 서로를 건드리지 않는다.
    ///
    /// 쪽마다 하나씩 만들고, 처음에는 그 쪽이 말하려는 장면(<see cref="TutorialScript.StillAtSec"/>)까지 미리 돌려 둔다. 움직이기
    /// 시작하면 그 장면에서 이어서 움직이고, 멈추면 그 자리에서 멈춘다(안드로이드 #331). UI 스레드에서만 쓴다.
    /// </summary>
    public sealed class TutorialDemo
    {
        /// <summary>엔진이 그리는 기준 화면(DIP). 16:9 모니터를 100% 배율로 본 크기다.</summary>
        public const double ReferenceWidth = 1920;
        public const double ReferenceHeight = 1080;

        /// <summary>미리 돌릴 때의 프레임 간격(60fps).</summary>
        private const double PrerollFps = 60;

        /// <summary>
        /// 쉬운 설정의 모양 카드(#408)가 그리는 화면(DIP). 카드의 그림은 튜토리얼 그림보다 훨씬 작아, 기준 화면을 그대로 줄이면 패드의 막대와
        /// 외곽선이 1px 아래로 가늘어진다. 그래서 더 작은 화면(16:9)을 줄인 것으로 보아 띠와 선을 굵게 그린다(안드로이드 모양 카드가
        /// 튜토리얼의 짧은 변 411dp 대신 240dp 로 보는 것과 같은 비율, 1080 × 240 / 411 ≈ 630). 크기 단계의 차이는 그대로다.
        /// </summary>
        public const double PreviewWidth = 1120;
        public const double PreviewHeight = 630;

        /// <summary>
        /// 모양 카드가 한 번 움직이는 시간(초, 안드로이드 PREVIEW_PLAY_SEC). 종류 쪽 대본의 처음부터 환경음·대화음을 지나 첫 위협음이 울린
        /// 직후까지라, 멈춘 그림(<see cref="TutorialScript.StillAtSec"/> 의 위협음)과 이어진다. 저절로 5초 넘게 움직이지 않아야 멈춤 단추
        /// 없이 둘 수 있다(WCAG 2.2.2).
        /// </summary>
        public const double PreviewPlaySec = 4.9;

        private readonly float[] _frame = new float[VisualizerEngine.Channels];
        private readonly VisualModeSettings _settings;

        /// <param name="scene">그릴 쪽.</param>
        /// <param name="visualMode">표현 모드.</param>
        /// <param name="runToStill">멈춰 둘 장면까지 미리 돌려 둔다. 끄면 아무것도 흘리지 않은 엔진으로 시작한다(시험이 0초부터 흘릴 때).</param>
        /// <param name="settings">그릴 모드 설정. 없으면 튜토리얼 그림의 설정(<see cref="DemoSettings"/>)이다. 받은 객체를 고치지 않는다.</param>
        /// <param name="width">엔진이 그리는 화면의 너비(DIP). 없으면 기준 화면이다.</param>
        /// <param name="height">엔진이 그리는 화면의 높이(DIP).</param>
        public TutorialDemo(TutorialScene scene, int visualMode, bool runToStill = true, VisualModeSettings? settings = null,
            double width = ReferenceWidth, double height = ReferenceHeight)
        {
            Scene = scene;
            VisualMode = visualMode;
            _settings = settings?.Copy() ?? DemoSettings();
            Width = width;
            Height = height;
            Engine.SetSurfaceSize(width, height);
            if (runToStill) RunTo(scene.StillAtSec());
        }

        /// <summary>엔진이 그리는 화면의 너비(DIP). 그리는 쪽이 이 크기의 도형을 그림 크기로 줄인다.</summary>
        public double Width { get; }

        /// <summary>엔진이 그리는 화면의 높이(DIP).</summary>
        public double Height { get; }

        public TutorialScene Scene { get; }

        /// <summary>그리는 표현 모드(<see cref="VisualizerEngine.ModeWave"/> 등). 패드·원형의 움직임은 그리는 모드만 계산한다.</summary>
        public int VisualMode { get; set; }

        /// <summary>오버레이와 같은 엔진. 기준 화면 크기로 돈다.</summary>
        public VisualizerEngine Engine { get; } = new();

        /// <summary>마지막으로 엔진에 흘린 대본 시각(초). 처음에는 멈춰 둘 장면의 시각이다.</summary>
        public double TimeSec { get; private set; }

        /// <summary>이 그림의 설정에 든 원형 고리 반지름(10~100). 그리는 쪽이 원형 시각화에 넘긴다(전역 설정이 아니라 이 그림의 값).</summary>
        public double CircleRadius => _settings.CircleRadius;

        /// <summary>지금 그리는 소리의 종류. 창이 이것으로 색을 고르고 종류 쪽의 범례를 밝힌다.</summary>
        public SoundType Label => TutorialScript.Label(Scene, TimeSec);

        /// <summary>
        /// 그림의 모드 설정. 기본값(<see cref="VisualModeSettings"/>)에서 크기·진하기·반응을 조금씩 올렸다(안드로이드와 같은 값, 민감도는 그
        /// 표시값 40 의 내부값). 그림이 실제 화면보다 훨씬 작아서, 기본값 그대로면 어두운 장면 위의 빨간 위협음이 흐리고 박자가 잘 읽히지 않는다.
        /// </summary>
        public static VisualModeSettings DemoSettings() => new()
        {
            Intensity = 55,
            VisualOpacity = 80,
            PositionSpeed = 20,
            Sensitivity = 10,
        };

        /// <summary>
        /// 쉬운 설정의 모양 카드가 그릴 설정(안드로이드 previewSettings): 사용자의 그 모드 설정이되 진하기와 반응(민감도)은 튜토리얼 그림만큼은
        /// 올린다. 작은 그림에서 기본값 그대로면 어두운 장면 위의 색이 흐리고 박자가 잘 읽히지 않는다(<see cref="DemoSettings"/> 와 같은 까닭).
        /// 크기는 그대로라 크기 단계를 바꾸면 그림도 바뀐다. 사용자 설정과 섞이지 않게 새로 만든다.
        /// </summary>
        public static VisualModeSettings PreviewSettings(VisualModeSettings user)
        {
            var demo = DemoSettings();
            var shown = user.Copy();
            shown.VisualOpacity = Math.Max(user.VisualOpacity, demo.VisualOpacity);
            shown.Sensitivity = Math.Max(user.Sensitivity, demo.Sensitivity);
            return shown;
        }

        /// <summary>대본 시각 <paramref name="t"/>(초)의 프레임 하나를 엔진에 흘린다. 시각은 앞으로만 간다.</summary>
        public void Step(double t)
        {
            TutorialScript.PeakFrame(Scene, t, _frame);
            var input = new OverlayInput(_settings, VisualMode, SoundMode: 0, TutorialScript.Label(Scene, t), Shown: true);
            // 엔진의 시계도 대본 시각으로 간다. 멈췄다 다시 움직여도 대본이 이어지므로 엔진이 한꺼번에 건너뛰지 않는다.
            // 그림은 조용한 틈에도 쉬지 않는다(쉬면 쉬는 동안의 확인을 따로 돌려야 한다).
            Engine.Tick(t * 1000.0, _frame, input, keepAwake: true);
            TimeSec = t;
        }

        /// <summary>멈춰 둘 때: 0초부터 <paramref name="seconds"/> 까지 60fps 로 미리 돌려 그 순간의 모양을 만든다.</summary>
        public void RunTo(double seconds)
        {
            // 2.4 × 60 처럼 소수 곱이 정수 바로 아래로 떨어져도 한 프레임을 빠뜨리지 않게 아주 조금 올려서 자른다.
            int frames = (int)Math.Floor(seconds * PrerollFps + 1e-9);
            for (int i = 0; i <= frames; i++) Step(i / PrerollFps);
        }
    }
}
