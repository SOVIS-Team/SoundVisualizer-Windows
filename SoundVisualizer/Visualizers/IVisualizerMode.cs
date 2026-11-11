// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Windows.Media;

namespace SoundVisualizer.Visualizers
{
    public interface IVisualizerMode
    {
        Geometry GenerateGeometry(VisualizerContext context);
        Brush GetFillBrush(Color activeColor);
    }

    public class VisualizerContext
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double BaseDepth { get; set; }
        // 0:TopCenter, 1:TopRight, 2:RightCenter, 3:BottomRight, 4:BottomCenter, 5:BottomLeft, 6:LeftCenter, 7:TopLeft
        public double[] ChannelDepths { get; set; } = new double[8];
        public double[] ChannelDists { get; set; } = new double[8];
        // 패드 두께와 원형 깊이. 둘 다 깊이를 한 번 더 부드럽게 따라간 값이고, 엔진(VisualizerEngine)이 시간에 맞춰 움직인다.
        public double[] PadThickness { get; set; } = new double[8];
        public double[] CircleDepths { get; set; } = new double[8];
        public float TotalVolume { get; set; }
        // 원형 모드의 가운데 고리 반지름 설정(10~100). 그리는 쪽이 넘긴다. 시각화가 전역 설정(지금 모드의 값)을 직접 읽으면
        // 쉬운 설정의 원형 카드처럼 다른 모드를 쓰는 중에 그리는 그림이 원형 모드의 반지름이 아닌 값으로 그려진다.
        public double CircleRadius { get; set; } = 40.0;
        public double AnimationTime { get; set; }
    }
}

