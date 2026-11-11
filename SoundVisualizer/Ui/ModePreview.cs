// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SoundVisualizer.Ai;
using SoundVisualizer.Rendering;
using SoundVisualizer.Tutorial;

namespace SoundVisualizer.Ui
{
    /// <summary>
    /// 쉬운 설정의 모양 카드(#408) 하나의 그림(안드로이드 ModePreviewPhone). 튜토리얼과 같은 어두운 장면 위에 실제 오버레이 엔진이
    /// <see cref="Mode"/> 를 사용자의 그 모드 설정으로 그린다(TutorialDemo, TutorialDemoRenderer).
    ///
    /// - 평소에는 멈춘 그림이다. 대본은 튜토리얼의 종류 쪽이고, 멈춘 그림은 오른쪽으로 기운 위협음이다.
    /// - <see cref="Play"/> 하면 대본의 처음부터 <see cref="TutorialDemo.PreviewPlaySec"/> 동안 움직인다. 환경음 → 대화음 → 위협음 순서로 세 종류의
    ///   색이 나오고, 멈춘 그림과 같은 위협음에 닿을 때 멈춘다. 저절로 5초 넘게 움직이지 않으므로 멈춤 단추를 따로 두지 않는다(WCAG 2.2.2).
    /// - Windows 에서 애니메이션 효과를 꺼 두었으면(SystemParameters.ClientAreaAnimation) 움직이지 않는다. 튜토리얼과 같은 규칙이고,
    ///   그 설정은 앱을 다시 켜지 않아도 바뀌므로 움직이기 시작할 때마다 읽는다.
    /// - 그림은 카드 이름이 말하는 것을 보여 주기만 하므로 화면 읽어주기에서는 건너뛴다. 왼쪽·오른쪽은 실제 방향이라 오른쪽에서 왼쪽으로
    ///   쓰는 언어에서도 뒤집지 않는다(튜토리얼과 같다).
    /// - 크기는 카드 너비에 맞춘 16:9 다. 너비를 모르고 잴 때(선택지 칸이 한 줄에 몇 칸 들어갈지 볼 때)는 가장 작은 너비를 알려 카드 수를
    ///   이름 길이로만 정하게 한다.
    /// </summary>
    public sealed class ModePreview : Decorator
    {
        /// <summary>그림 너비의 범위(DIP). 런처에서 네 장을 한 줄에 둘 때 한 장이 160 쯤이고, 두 장씩 둘 때도 지나치게 커지지 않게 막는다.</summary>
        private const double MinPreviewWidth = 120;
        private const double MaxPreviewWidth = 220;
        private const double CornerRadius = 10;

        /// <summary>움직이는 동안 그림을 새로 계산하는 가장 짧은 간격(밀리초). 작은 그림이라 1초에 30번쯤이면 충분하다(안드로이드와 같다).</summary>
        private const double MinFrameMs = 33;

        /// <summary>한 프레임에 대본을 가장 많이 진행하는 시간(초). 잠깐 멈췄다 돌아와도 그림이 크게 건너뛰지 않는다(튜토리얼과 같다).</summary>
        private const double MaxFrameSec = 0.1;

        /// <summary>
        /// 외곽선 모드의 선 두께(그림 화면 DIP). 카드의 그림은 그림 화면(1120)을 160 쯤으로 줄여, 튜토리얼의 두께(8)로는 1px 남짓이다.
        /// 튜토리얼 그림(1920 을 448 로 줄임)과 같은 2px 쯤으로 보이게 굵힌다.
        /// </summary>
        private const double OutlineThickness = 13;

        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
            nameof(Mode), typeof(int), typeof(ModePreview), new FrameworkPropertyMetadata(VisualizerEngine.ModeWave, OnModeChanged));

        private readonly Grid _frame = new();
        private readonly Path _shape = new() { Visibility = Visibility.Collapsed };
        private readonly TutorialDemoRenderer _renderer = new(OutlineThickness);
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        private VisualModeSettings? _settings; // 그리는 설정(PreviewSettings 를 거친 것)
        private int _settingsMode = -1;        // 그 설정을 받은 모드
        private TutorialDemo? _still;
        private TutorialDemo? _playing;
        private double _lastFrameMs;
        private Color _ambient = Colors.White, _speech = Colors.Yellow, _danger = Colors.Red;

        public ModePreview()
        {
            FlowDirection = FlowDirection.LeftToRight;
            HorizontalAlignment = HorizontalAlignment.Center;
            SnapsToDevicePixels = true;

            // 장면과 엔진의 도형을 따로 줄인다. 도형은 작은 화면(TutorialDemo.PreviewWidth)으로 그려 띠와 선이 굵다.
            var shapes = new Canvas { Width = TutorialDemo.PreviewWidth, Height = TutorialDemo.PreviewHeight, ClipToBounds = true };
            shapes.Children.Add(_shape);
            _frame.Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = Scene() });
            _frame.Children.Add(new Viewbox { Stretch = Stretch.Fill, Child = shapes });
            var edge = new Border { CornerRadius = new CornerRadius(CornerRadius), BorderThickness = new Thickness(1) };
            edge.SetResourceReference(Border.BorderBrushProperty, "ControlBrush");
            _frame.Children.Add(edge);
            Child = _frame;

            // 보이지 않게 되면(탭을 넘기거나 자세한 설정으로 바꾸면) 멈춘 그림으로 돌아간다.
            IsVisibleChanged += (s, e) => { if (!IsVisible) Stop(); };
        }

        /// <summary>표현 모드(<see cref="VisualizerEngine.ModeWave"/> 등).</summary>
        public int Mode
        {
            get => (int)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        /// <summary>
        /// 사용자의 그 모드 설정과 종류 색으로 그린다. 설정이 바뀌었으면 멈춘 그림을 새로 만들고(움직이던 중이면 멈춘다. 크기를 바꾼 뒤에는
        /// 부르는 쪽이 다시 움직인다), 색만 바뀌었으면 지금 그림을 다시 칠한다.
        /// </summary>
        public void Show(VisualModeSettings user, Color ambient, Color speech, Color danger)
        {
            var settings = TutorialDemo.PreviewSettings(user);
            // 색이나 표시처럼 그림의 모양과 상관없는 값만 바뀌었으면 움직이던 그림을 끊지 않는다.
            bool changed = _settings == null || !settings.HasSameValues(_settings) || _settingsMode != Mode;
            _ambient = ambient;
            _speech = speech;
            _danger = danger;
            if (changed)
            {
                _settings = settings;
                _settingsMode = Mode;
                _still = null;
                StopPlaying();
            }
            DrawCurrent();
        }

        /// <summary>
        /// 대본의 처음부터 <see cref="TutorialDemo.PreviewPlaySec"/> 동안 움직인다. 움직이는 중이면 처음부터 다시 한다.
        /// Windows 에서 애니메이션 효과를 꺼 두었거나 보이지 않으면 멈춘 그림 그대로 둔다.
        /// </summary>
        public void Play()
        {
            if (!SystemParameters.ClientAreaAnimation || !IsVisible || _settings == null)
            {
                Stop();
                return;
            }
            bool subscribed = _playing != null;
            _playing = new TutorialDemo(TutorialScene.Types, Mode, runToStill: false, _settings, TutorialDemo.PreviewWidth, TutorialDemo.PreviewHeight);
            _playing.Step(0);
            Draw(_playing);
            _lastFrameMs = _clock.Elapsed.TotalMilliseconds;
            if (!subscribed) CompositionTarget.Rendering += OnRendering;
        }

        /// <summary>움직이는 중이면 멈추고 멈춘 그림을 보인다.</summary>
        public void Stop()
        {
            if (!StopPlaying()) return;
            DrawCurrent();
        }

        private bool StopPlaying()
        {
            if (_playing == null) return false;
            _playing = null;
            CompositionTarget.Rendering -= OnRendering;
            return true;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            var demo = _playing;
            if (demo == null) return;
            double now = _clock.Elapsed.TotalMilliseconds;
            double elapsedMs = now - _lastFrameMs;
            if (elapsedMs < MinFrameMs) return;
            _lastFrameMs = now;

            double t = Math.Min(demo.TimeSec + Math.Min(elapsedMs / 1000.0, MaxFrameSec), TutorialDemo.PreviewPlaySec);
            demo.Step(t);
            Draw(demo);
            // 정해 둔 만큼 다 움직였으면 멈춘 그림으로 돌아간다. 멈춘 그림도 위협음이라 이어진다.
            if (t >= TutorialDemo.PreviewPlaySec) Stop();
        }

        private void DrawCurrent()
        {
            if (_playing != null)
            {
                Draw(_playing);
                return;
            }
            if (_settings == null) return;
            _still ??= new TutorialDemo(TutorialScene.Types, Mode, runToStill: true, _settings, TutorialDemo.PreviewWidth, TutorialDemo.PreviewHeight);
            Draw(_still);
        }

        private void Draw(TutorialDemo demo)
        {
            // 색은 그림이 지금 그리는 소리의 종류로 고른다(오버레이와 같다).
            var color = demo.Label switch
            {
                SoundType.Danger => _danger,
                SoundType.Speech => _speech,
                _ => _ambient,
            };
            _renderer.Draw(demo, _shape, color);
        }

        private static void OnModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var preview = (ModePreview)d;
            preview._still = null;
            preview.StopPlaying();
            preview.DrawCurrent();
        }

        protected override Size MeasureOverride(Size constraint)
        {
            var size = PreviewSize(constraint.Width);
            _frame.Measure(size);
            return size;
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            var size = PreviewSize(arrangeSize.Width);
            _frame.Arrange(new Rect(size));
            _frame.Clip = new RectangleGeometry(new Rect(size), CornerRadius, CornerRadius);
            return size;
        }

        private static Size PreviewSize(double available)
        {
            double width = double.IsInfinity(available) ? MinPreviewWidth : Math.Clamp(available, Math.Min(MinPreviewWidth, available), MaxPreviewWidth);
            return new Size(width, width * TutorialDemo.PreviewHeight / TutorialDemo.PreviewWidth);
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new EasyIcon.HiddenPeer(this);

        /// <summary>
        /// 튜토리얼 그림과 같은 어두운 게임 장면(TutorialWindow.xaml 의 DemoFrame 과 같은 도형, 기준 화면 1920×1080 좌표). 흰 환경음 색도 잘 보인다.
        /// 엔진의 도형과 따로 줄여 그리므로 장면은 튜토리얼 그대로 둔다.
        /// </summary>
        private static Canvas Scene()
        {
            const double w = TutorialDemo.ReferenceWidth, h = TutorialDemo.ReferenceHeight;
            var scene = new Canvas { Width = w, Height = h };
            var sky = new LinearGradientBrush(Color.FromRgb(0x2A, 0x38, 0x54), Color.FromRgb(0x15, 0x1C, 0x2B), 90);
            sky.Freeze();
            scene.Children.Add(new Rectangle { Width = w, Height = h, Fill = sky });
            var moon = new Ellipse { Width = 216, Height = 216, Fill = Frozen(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)) };
            Canvas.SetLeft(moon, 1236);
            Canvas.SetTop(moon, 183.6);
            scene.Children.Add(moon);
            scene.Children.Add(Hill(Color.FromRgb(0x1B, 0x23, 0x35), "M0,756 Q384,540 806.4,712.8 Q1190.4,864 1536,648 Q1766.4,540 1920,669.6 L1920,1080 L0,1080 Z"));
            scene.Children.Add(Hill(Color.FromRgb(0x0E, 0x13, 0x1D), "M0,928.8 Q576,777.6 1056,907.2 Q1497.6,1015.2 1920,864 L1920,1080 L0,1080 Z"));
            return scene;
        }

        private static Path Hill(Color color, string data)
        {
            var geometry = Geometry.Parse(data);
            geometry.Freeze();
            return new Path { Data = geometry, Fill = Frozen(color) };
        }

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
