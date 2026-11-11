// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Globalization;

namespace SoundVisualizer
{
    /// <summary>
    /// 슬라이더 줄의 값 글자를 적는 규칙. 화면(Ui/ValueFormatConverter)이 이 함수로 글자를 만들고, "화면에 같은 수로 보이는가"를 따지는 쪽
    /// (<see cref="EasySizes"/>)도 같은 함수로 견준다. 반올림 규칙을 두 곳에 따로 적으면 꼭 x.5 인 값에서 어긋난다. 슬라이더는 49.5 를
    /// "50" 으로 적는데, 차이가 0.5 보다 작은지로 따지던 쪽은 50 이 아니라고 했다. .NET 의 F0 는 꼭 .5 인 값을 짝수 쪽으로 적어
    /// (49.5 와 50.5 는 "50", 74.5 는 "74", 75.5 는 "76") 부등호 하나로는 흉내 낼 수 없다. 그래서 수를 따로 계산하지 않고 글자를 견준다.
    /// </summary>
    public static class ValueText
    {
        /// <summary>꼴을 주지 않은 슬라이더 줄의 꼴. 정수로 적는다. "{0:F0}%", "{0:F0} FPS" 처럼 단위를 붙인 꼴도 수는 같은 정수다.</summary>
        public const string DefaultFormat = "{0:F0}";

        /// <summary>
        /// 값을 <paramref name="format"/> 꼴(string.Format)로 적는다. 꼴이 없으면 <see cref="DefaultFormat"/> 이다.
        /// 숫자는 Windows 지역 설정과 상관없이 같은 꼴(InvariantCulture)이다.
        /// </summary>
        public static string Format(double value, string? format = null) =>
            string.Format(CultureInfo.InvariantCulture, string.IsNullOrEmpty(format) ? DefaultFormat : format, value);

        /// <summary>두 값이 슬라이더 줄에 같은 수로 보이는지(기본 꼴의 글자가 같은지).</summary>
        public static bool ShowsSameNumber(double a, double b) => Format(a) == Format(b);
    }
}
