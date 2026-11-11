// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Collections.Generic;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using SoundVisualizer.Ai;
using SoundVisualizer.Rendering;

namespace SoundVisualizer.Ui
{
    /*
     * 쉬운 설정(안드로이드 EasyIcons.kt, #408)의 그림. 글 대신 모양으로 무엇인지 알아볼 수 있게 한다.
     * 아이콘 글꼴(Segoe MDL2 Assets)에는 말풍선·물결 동그라미 같은 모양이 없고 Windows 판마다 모양이 달라, 안드로이드와 같은 모양을 직접 그린다.
     * 그림은 옆이나 아래의 낱말이 말하는 것을 보여 주기만 하므로 화면 읽어주기에서는 건너뛴다(EasyIcon). 이름은 그 칸이 단다.
     * 오른쪽에서 왼쪽으로 쓰는 언어에서는 런처가 뒤집는 대로 함께 뒤집힌다(말풍선 꼬리가 반대쪽에 온다). 모양의 뜻은 그대로다.
     */

    /// <summary>쉬운 설정의 그림 하나. 글자색(<see cref="Foreground"/>)으로 그리고, 화면 읽어주기에는 보이지 않는다.</summary>
    public abstract class EasyIcon : FrameworkElement
    {
        public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
            nameof(Foreground), typeof(Brush), typeof(EasyIcon),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>그림의 색. 칸의 글자색을 넘긴다(선택한 칸은 흰색).</summary>
        public Brush Foreground
        {
            get => (Brush)GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        protected EasyIcon(double width, double height)
        {
            Width = width;
            Height = height;
            SnapsToDevicePixels = true;
        }

        protected override AutomationPeer OnCreateAutomationPeer() => new HiddenPeer(this);

        protected static Pen RoundPen(Brush brush, double thickness)
        {
            var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            pen.Freeze();
            return pen;
        }

        /// <summary>자식 없는 보이지 않는 요소로 알린다(튜토리얼 그림의 TutorialDecoration 과 같다).</summary>
        internal sealed class HiddenPeer : FrameworkElementAutomationPeer
        {
            public HiddenPeer(FrameworkElement owner) : base(owner)
            {
            }

            protected override List<AutomationPeer>? GetChildrenCore() => null;

            protected override bool IsControlElementCore() => false;

            protected override bool IsContentElementCore() => false;
        }
    }

    /// <summary>
    /// 소리 종류의 그림: 위협음은 경고 삼각형, 대화음은 말풍선, 환경음은 물결이 든 동그라미. 사용자가 정한 종류 색(<see cref="Fill"/>)으로 칠한다.
    /// 모양이 종류마다 달라 색을 구분하기 어려워도 알아볼 수 있다. 어두운 색을 정해도 보이도록 설정 탭의 색 동그라미처럼 옅은 테두리를 두른다.
    /// </summary>
    public sealed class SoundTypeBadge : EasyIcon
    {
        public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
            nameof(Kind), typeof(SoundType), typeof(SoundTypeBadge),
            new FrameworkPropertyMetadata(SoundType.Ambient, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill), typeof(Brush), typeof(SoundTypeBadge),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>그림 안 기호의 색. 밝은 종류 색(기본값 흰색·노랑·빨강) 위에서 잘 보이는 어두운 색이다.</summary>
        private static readonly Brush GlyphBrush = Frozen(Color.FromRgb(0x10, 0x12, 0x16));

        /// <summary>테두리: 보조 글자색의 절반 진하기.</summary>
        private static readonly Brush EdgeBrush = Frozen(Color.FromArgb(0x80, 0x8B, 0x95, 0xA1));

        public SoundTypeBadge() : base(36, 36)
        {
        }

        public SoundType Kind
        {
            get => (SoundType)GetValue(KindProperty);
            set => SetValue(KindProperty, value);
        }

        public Brush Fill
        {
            get => (Brush)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double s = Math.Min(ActualWidth, ActualHeight);
            if (s <= 0) return;
            var fill = Fill;
            var border = new Pen(EdgeBrush, 1);
            border.Freeze();
            switch (Kind)
            {
                case SoundType.Danger:
                {
                    double round = s * 0.12;
                    var triangle = Polygon(new Point(s / 2, round), new Point(s - round * 0.8, s - round * 0.9), new Point(round * 0.8, s - round * 0.9));
                    // 꼭짓점을 둥글게: 삼각형을 채우고 같은 색의 둥근 이음 선으로 감싼다. 테두리는 그보다 굵은 선을 먼저 깔아 바깥에 가는 띠로 남긴다.
                    dc.DrawGeometry(null, RoundPen(EdgeBrush, round * 2 + 2), triangle);
                    dc.DrawGeometry(fill, RoundPen(fill, round * 2), triangle);
                    // 느낌표
                    double bar = s * 0.11;
                    dc.DrawLine(RoundPen(GlyphBrush, bar), new Point(s / 2, s * 0.38), new Point(s / 2, s * 0.64));
                    dc.DrawEllipse(GlyphBrush, null, new Point(s / 2, s * 0.8), bar * 0.62, bar * 0.62);
                    break;
                }
                case SoundType.Speech:
                {
                    // 몸통과 꼬리를 한 모양으로 합친다. 따로 그리면 테두리가 말풍선 안에 선으로 남는다.
                    var body = new RectangleGeometry(new Rect(s * 0.04, s * 0.1, s * 0.92, s * 0.62), s * 0.2, s * 0.2);
                    var tail = Polygon(new Point(s * 0.22, s * 0.66), new Point(s * 0.46, s * 0.66), new Point(s * 0.16, s * 0.94));
                    var bubble = new CombinedGeometry(GeometryCombineMode.Union, body, tail);
                    bubble.Freeze();
                    dc.DrawGeometry(fill, border, bubble);
                    // 말줄임 점 셋
                    foreach (double x in new[] { 0.3, 0.5, 0.7 }) dc.DrawEllipse(GlyphBrush, null, new Point(s * x, s * 0.41), s * 0.07, s * 0.07);
                    break;
                }
                default:
                {
                    var center = new Point(s / 2, s / 2);
                    dc.DrawEllipse(fill, border, center, s * 0.46, s * 0.46);
                    // 물결 한 줄
                    var wave = new StreamGeometry();
                    using (var ctx = wave.Open())
                    {
                        double left = s * 0.2, right = s * 0.8, amp = s * 0.11, step = (right - left) / 4;
                        ctx.BeginFigure(new Point(left, center.Y), false, false);
                        for (int i = 0; i < 4; i++)
                        {
                            double x0 = left + step * i;
                            double dir = i % 2 == 0 ? -1 : 1;
                            ctx.QuadraticBezierTo(new Point(x0 + step / 2, center.Y + dir * amp * 2), new Point(x0 + step, center.Y), true, true);
                        }
                    }
                    wave.Freeze();
                    dc.DrawGeometry(null, RoundPen(GlyphBrush, s * 0.085), wave);
                    break;
                }
            }
        }

        private static Geometry Polygon(params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Length; i++) ctx.LineTo(points[i], true, true);
            }
            geometry.Freeze();
            return geometry;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    /// <summary>화면 표시의 그림: 뜬 눈. 꺼져 있으면 눈 위에 빗금을 긋는다.</summary>
    public sealed class ShowIcon : EasyIcon
    {
        public static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
            nameof(Shown), typeof(bool), typeof(ShowIcon), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public ShowIcon() : base(24, 24)
        {
        }

        public bool Shown
        {
            get => (bool)GetValue(ShownProperty);
            set => SetValue(ShownProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double s = Math.Min(ActualWidth, ActualHeight);
            if (s <= 0) return;
            var line = RoundPen(Foreground, s * 0.08);
            var eye = new StreamGeometry();
            using (var ctx = eye.Open())
            {
                ctx.BeginFigure(new Point(s * 0.06, s * 0.5), false, true);
                ctx.QuadraticBezierTo(new Point(s * 0.5, s * 0.04), new Point(s * 0.94, s * 0.5), true, true);
                ctx.QuadraticBezierTo(new Point(s * 0.5, s * 0.96), new Point(s * 0.06, s * 0.5), true, true);
            }
            eye.Freeze();
            dc.DrawGeometry(null, line, eye);
            dc.DrawEllipse(Foreground, null, new Point(s / 2, s / 2), s * 0.13, s * 0.13);
            if (!Shown) dc.DrawLine(line, new Point(s * 0.14, s * 0.86), new Point(s * 0.86, s * 0.14));
        }
    }

    /// <summary>
    /// 크기 단계의 그림: 가로로 눕힌 화면에 <see cref="Mode"/> 의 모양을 그 단계의 크기로 줄여 그린다. 크기는 모드마다 따로라 선택한 모드의
    /// 모양으로 보여 준다. 파도는 테두리를 따라 채운 띠, 외곽선은 테두리 안쪽의 선, 패드는 네 변 가운데의 막대, 원형은 가운데 고리이고,
    /// 단계가 클수록 두껍거나 안쪽으로 들어오거나 크다.
    /// </summary>
    public sealed class SizeStepIcon : EasyIcon
    {
        public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
            nameof(Step), typeof(EasySize), typeof(SizeStepIcon), new FrameworkPropertyMetadata(EasySize.Medium, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
            nameof(Mode), typeof(int), typeof(SizeStepIcon), new FrameworkPropertyMetadata(VisualizerEngine.ModeWave, FrameworkPropertyMetadataOptions.AffectsRender));

        public SizeStepIcon() : base(44, 28)
        {
        }

        public EasySize Step
        {
            get => (EasySize)GetValue(StepProperty);
            set => SetValue(StepProperty, value);
        }

        /// <summary>표현 모드(<see cref="VisualizerEngine.ModeWave"/> 등).</summary>
        public int Mode
        {
            get => (int)GetValue(ModeProperty);
            set => SetValue(ModeProperty, value);
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            const double frameWidth = 1.5;
            const double corner = 5;
            double inset = frameWidth / 2;
            var color = Foreground;
            var frame = new Pen(color, frameWidth);
            frame.Freeze();
            var outer = new Rect(inset, inset, w - inset * 2, h - inset * 2);
            dc.DrawRoundedRectangle(null, frame, outer, corner, corner);

            double innerHeight = h - inset * 2;
            double depth = innerHeight * Step switch { EasySize.Small => 0.14, EasySize.Large => 0.36, _ => 0.24 };
            var fill = color.CloneCurrentValue();
            fill.Opacity *= 0.75;
            fill.Freeze();

            switch (Mode)
            {
                case VisualizerEngine.ModePad:
                {
                    double thick = depth * 0.8;
                    double horizontal = outer.Width * 0.34;
                    double vertical = innerHeight * 0.46;
                    double cx = outer.Left + outer.Width / 2, cy = outer.Top + outer.Height / 2;
                    double r = thick / 2;
                    dc.DrawRoundedRectangle(fill, null, new Rect(cx - horizontal / 2, outer.Top, horizontal, thick), r, r);
                    dc.DrawRoundedRectangle(fill, null, new Rect(cx - horizontal / 2, outer.Bottom - thick, horizontal, thick), r, r);
                    dc.DrawRoundedRectangle(fill, null, new Rect(outer.Left, cy - vertical / 2, thick, vertical), r, r);
                    dc.DrawRoundedRectangle(fill, null, new Rect(outer.Right - thick, cy - vertical / 2, thick, vertical), r, r);
                    break;
                }
                case VisualizerEngine.ModeCircle:
                {
                    double radius = innerHeight * Step switch { EasySize.Small => 0.16, EasySize.Large => 0.38, _ => 0.26 };
                    var ring = new Pen(color, 2);
                    ring.Freeze();
                    dc.DrawEllipse(null, ring, new Point(w / 2, h / 2), radius, radius);
                    break;
                }
                case VisualizerEngine.ModeOutline:
                {
                    var inner = Inset(outer, depth);
                    double r = Math.Max(corner - depth, 1);
                    dc.DrawRoundedRectangle(null, frame, inner, r, r);
                    break;
                }
                default:
                {
                    // 파도: 바깥 사각형에서 안쪽 사각형을 뺀 띠
                    var inner = Inset(outer, depth);
                    double r = Math.Max(corner - depth, 1);
                    var band = new CombinedGeometry(GeometryCombineMode.Exclude,
                        new RectangleGeometry(outer, corner, corner), new RectangleGeometry(inner, r, r));
                    band.Freeze();
                    dc.DrawGeometry(fill, null, band);
                    break;
                }
            }
        }

        private static Rect Inset(Rect outer, double depth) =>
            new(outer.Left + depth, outer.Top + depth, Math.Max(0, outer.Width - depth * 2), Math.Max(0, outer.Height - depth * 2));
    }

    /// <summary>'자세한 설정' 칸의 그림: 손잡이가 달린 가로 막대 셋. 자세한 설정의 슬라이더를 뜻한다.</summary>
    public sealed class DetailedIcon : EasyIcon
    {
        public DetailedIcon() : base(24, 24)
        {
        }

        protected override void OnRender(DrawingContext dc)
        {
            double s = Math.Min(ActualWidth, ActualHeight);
            if (s <= 0) return;
            var line = RoundPen(Foreground, s * 0.08);
            double[] rows = { 0.24, 0.5, 0.76 };
            double[] knobs = { 0.66, 0.32, 0.54 };
            for (int i = 0; i < rows.Length; i++)
            {
                double y = s * rows[i];
                dc.DrawLine(line, new Point(s * 0.08, y), new Point(s * 0.92, y));
                dc.DrawEllipse(Foreground, null, new Point(s * knobs[i], y), s * 0.12, s * 0.12);
            }
        }
    }
}
