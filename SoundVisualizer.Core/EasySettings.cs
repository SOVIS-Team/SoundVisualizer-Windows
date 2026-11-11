// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Globalization;

namespace SoundVisualizer
{
    /// <summary>
    /// 쉬운 설정의 크기 세 단계(안드로이드 EasySize, #408). 자세한 설정의 크기·진하기 슬라이더 대신 그림 칸 셋으로 선택한다.
    /// 값과 규칙은 <see cref="EasySizes"/> 에 있다. 선언 순서가 화면의 칸 순서다.
    /// </summary>
    public enum EasySize
    {
        Small,
        Medium,
        Large,
    }

    /// <summary>
    /// 쉬운 설정의 '자세한 설정' 칸을 눌렀을 때 자세한 설정에서 보여 줄 칸(안드로이드 #427 의 DetailTarget.Size 를 PC 에 맞춘 것).
    /// 안드로이드는 그 모드의 카드 맨 위로 옮기기만 하지만, PC 는 키보드로 쓰는 사람을 위해 그 칸에 초점까지 옮기므로 칸 하나를 고른다.
    /// 모드 카드 안의 순서(크기 고정 → 크기 → 진하기)와 같다.
    /// </summary>
    public enum EasySizeControl
    {
        /// <summary>크기 고정 스위치. 켜 두면 크기·진하기 슬라이더가 접혀 있다.</summary>
        SizeLock,

        /// <summary>크기 슬라이더.</summary>
        Size,

        /// <summary>진하기 슬라이더.</summary>
        Opacity,
    }

    /// <summary>
    /// 쉬운 설정의 크기 단계 규칙(안드로이드 EasySize). 한 단계가 크기와 진하기를 함께 정한다. 작게는 덜 눈에 띄게, 크게는 더 눈에 띄게 그린다.
    /// 보통은 처음 값(<see cref="VisualModeSettings"/> 의 기본값)을 그대로 가리켜, 새로 설치한 사람에게는 보통이 선택되어 보인다.
    ///
    /// 값은 안드로이드와 똑같다. PC 엔진(Rendering/VisualizerEngine)도 크기를 "화면 짧은 변의 절반에서 가장자리 여백을 뺀 깊이의 몇 %"
    /// (BaseDepthFor), 진하기를 "불투명도 %"(Alpha)로 안드로이드와 같은 식으로 쓰므로, 같은 값이 같은 크기·진하기로 보인다.
    /// 네 모드 모두 같은 두 값(크기·진하기)을 쓰고 모드마다 뜻이 다르지 않아(원형의 반지름은 따로다) 모드별로 값을 달리 두지 않는다.
    ///
    /// 크기는 모드마다 따로다(안드로이드 사용자 결정 2026-10-22). 쉬운 설정의 크기 칸은 지금 표현 모드의 크기를 보여 주고 바꾼다.
    /// </summary>
    public static class EasySizes
    {
        private static readonly VisualModeSettings Defaults = new();

        /// <summary>화면에 놓는 차례(작게, 보통, 크게).</summary>
        public static IReadOnlyList<EasySize> All { get; } = Enum.GetValues<EasySize>();

        /// <summary>이 단계의 크기(0~100).</summary>
        public static double Intensity(this EasySize step) => step switch
        {
            EasySize.Small => 30,
            EasySize.Large => 75,
            _ => Defaults.Intensity,
        };

        /// <summary>이 단계의 진하기(0~100).</summary>
        public static double Opacity(this EasySize step) => step switch
        {
            EasySize.Small => 40,
            EasySize.Large => 70,
            _ => Defaults.VisualOpacity,
        };

        /// <summary>
        /// 이 단계를 <paramref name="settings"/> 에 쓴다. 크기 고정을 켜 둔 모드는 크기가 움직이지 않으므로 끈다. 누른 크기가 실제로 보여야 한다.
        /// 나머지 값(속도·민감도·광원·반지름, 고정 크기·최대 진하기)은 그대로 둔다. 오버레이가 프레임마다 읽는 객체를 그 자리에서 고친다
        /// (자세한 설정의 슬라이더와 같다).
        /// </summary>
        public static void ApplyTo(this EasySize step, VisualModeSettings settings)
        {
            settings.Intensity = step.Intensity();
            settings.VisualOpacity = step.Opacity();
            settings.IntensityAsOpacity = false;
        }

        /// <summary>
        /// <paramref name="settings"/> 가 한 단계에 맞으면 그 단계. 자세한 설정에서 크기나 진하기를 단계와 다르게 맞췄거나 크기 고정을 켰으면
        /// 어느 단계도 아니다(null). 그때는 쉬운 설정에서 어느 칸도 선택해 두지 않는다. 가장 가까운 단계로 보이면 실제 크기와 다르고,
        /// 그 칸을 누르면 오히려 크기가 바뀐다(안드로이드 #424).
        /// </summary>
        public static EasySize? Of(VisualModeSettings settings)
        {
            if (settings.IntensityAsOpacity) return null;
            foreach (var step in All)
            {
                if (Near(settings.Intensity, step.Intensity()) && Near(settings.VisualOpacity, step.Opacity())) return step;
            }
            return null;
        }

        /// <summary>
        /// '자세한 설정' 칸을 눌렀을 때 보여 줄 칸. 단계에 맞지 않게 만든 첫 칸을 모드 카드의 차례로 고른다: 크기 고정을 켰으면 그 스위치,
        /// 크기가 어느 단계와도 다르면 크기, 크기는 맞는데 진하기가 그 단계와 다르면 진하기. 단계에 맞으면 크기다.
        /// </summary>
        public static EasySizeControl DetailControl(VisualModeSettings settings)
        {
            if (settings.IntensityAsOpacity) return EasySizeControl.SizeLock;
            foreach (var step in All)
            {
                if (Near(settings.Intensity, step.Intensity()))
                {
                    return Near(settings.VisualOpacity, step.Opacity()) ? EasySizeControl.Size : EasySizeControl.Opacity;
                }
            }
            return EasySizeControl.Size;
        }

        /// <summary>
        /// 슬라이더 값이 단계 값과 같은 것으로 볼 만한지. 자세한 설정의 슬라이더는 끌면 소수 값(49.8)이 되지만 화면에는 정수("50")로 보인다.
        /// 안드로이드처럼 꼭 같은 값만 치면, 자세한 설정에서 50 으로 보이는데 쉬운 설정은 "자세한 설정에서 맞춘 크기"라고 하는 엇갈림이
        /// 생긴다. 그래서 슬라이더 줄에 단계의 수로 보이는 값을 그 단계로 본다. 슬라이더가 글자를 만드는 바로 그 함수(<see cref="ValueText"/>)로
        /// 견주므로 꼭 x.5 인 값에서도 엇갈리지 않는다. 예전에는 차이가 0.5 보다 작은지로 따져, "50" 으로 보이는 49.5 가 보통이 아니었다.
        /// </summary>
        private static bool Near(double value, double step) => ValueText.ShowsSameNumber(value, step);
    }

    /// <summary>쉬운 설정에서 화면에 보이지 않는 이름(화면 읽어주기)을 짓는 규칙.</summary>
    public static class EasyNames
    {
        /// <summary>
        /// 쉬운 설정의 소리 종류 줄을 화면 읽어주기에 알리는 이름(안드로이드 typeRowName, #434). 자세한 설정의 스위치와 같은 이름
        /// (<paramref name="showLabel"/>, "위협음 표시")을 쓰고, 그 안에 화면에 보이는 종류 이름(<paramref name="typeName"/>)이 없으면 앞에 붙인다.
        /// 보이는 글자를 말해 누르는 음성 조작도 이 줄을 찾을 수 있어야 한다(WCAG 2.5.3). 번역에 따라 "Mostrar sons de perigo" 에는
        /// 보이는 "Som de perigo" 가 들어 있지 않다.
        /// </summary>
        /// <param name="pairFormat">두 이름을 잇는 꼴("{0}, {1}"). 언어마다 쉼표가 달라(아랍어 "،", 일본어 "、") 문자열 표에서 받는다.</param>
        public static string TypeRow(string typeName, string showLabel, string pairFormat) =>
            showLabel.Contains(typeName, StringComparison.OrdinalIgnoreCase)
                ? showLabel
                : string.Format(CultureInfo.InvariantCulture, pairFormat, typeName, showLabel);
    }
}
