// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 쉬운 설정의 크기 세 단계(안드로이드 EasySize, #408)와, 자세한 설정에서 맞춘 크기면 선택을 비우는 규칙(#425), '자세한 설정' 칸이
/// 보여 줄 칸(#429). 화면은 이 규칙대로 칸을 고르기만 하므로 여기서 지킨다.
/// </summary>
public class EasySizesTests
{
    [Fact]
    public void 단계_값은_안드로이드와_같고_보통은_처음_값이다()
    {
        var defaults = new VisualModeSettings();
        Assert.Equal(new[] { EasySize.Small, EasySize.Medium, EasySize.Large }, EasySizes.All);
        Assert.Equal((30.0, 40.0), (EasySize.Small.Intensity(), EasySize.Small.Opacity()));
        Assert.Equal((defaults.Intensity, defaults.VisualOpacity), (EasySize.Medium.Intensity(), EasySize.Medium.Opacity()));
        Assert.Equal((75.0, 70.0), (EasySize.Large.Intensity(), EasySize.Large.Opacity()));

        // 새로 설치한 사람에게는 보통이 선택되어 보인다.
        Assert.Equal(EasySize.Medium, EasySizes.Of(defaults));
    }

    [Fact]
    public void 단계를_쓰면_크기와_진하기만_바꾸고_크기_고정을_끈다()
    {
        foreach (var step in EasySizes.All)
        {
            var settings = new VisualModeSettings
            {
                IntensityAsOpacity = true,
                PositionSpeed = 77,
                Sensitivity = 9,
                IsGlowMode = true,
                GlowIntensity = 40,
                CircleRadius = 90,
                OpacityFixedSize = 55,
                OpacityFixedMaxOpacity = 60,
            };

            step.ApplyTo(settings);

            Assert.Equal(step, EasySizes.Of(settings));
            Assert.Equal(step.Intensity(), settings.Intensity);
            Assert.Equal(step.Opacity(), settings.VisualOpacity);
            // 크기 고정을 켜 둔 채면 누른 크기가 보이지 않는다.
            Assert.False(settings.IntensityAsOpacity);
            // 자세한 설정에만 있는 값은 그대로 남아 계속 적용된다.
            Assert.Equal((77.0, 9.0, true, 40.0, 90.0, 55.0, 60.0),
                (settings.PositionSpeed, settings.Sensitivity, settings.IsGlowMode, settings.GlowIntensity, settings.CircleRadius,
                 settings.OpacityFixedSize, settings.OpacityFixedMaxOpacity));
        }
    }

    [Theory]
    [InlineData(63, 50, false)] // 크기가 어느 단계와도 다르다
    [InlineData(50, 90, false)] // 크기는 보통인데 진하기가 다르다
    [InlineData(30, 70, false)] // 작게의 크기에 크게의 진하기
    [InlineData(50, 50, true)]  // 보통인데 크기 고정을 켰다
    public void 자세한_설정에서_단계와_다르게_맞춘_크기는_어느_단계도_아니다(double intensity, double opacity, bool sizeLock)
    {
        // 가장 가까운 단계로 보이면 실제 크기와 다르고, 그 칸을 누르면 오히려 크기가 바뀐다(안드로이드 #424).
        var settings = new VisualModeSettings { Intensity = intensity, VisualOpacity = opacity, IntensityAsOpacity = sizeLock };

        Assert.Null(EasySizes.Of(settings));
    }

    [Theory]
    [InlineData(49.6, 50.4, EasySize.Medium)]
    [InlineData(75.49, 69.51, EasySize.Large)]
    [InlineData(29.5001, 40, EasySize.Small)]
    public void 자세한_설정에서_같은_정수로_보이는_값은_그_단계다(double intensity, double opacity, EasySize expected)
    {
        // 슬라이더를 끌면 소수 값이 되지만 화면에는 정수로 보인다. 자세한 설정에서 50 으로 보이는데 쉬운 설정이 "자세한 설정에서 맞춘
        // 크기"라고 하면 엇갈린다.
        Assert.Equal(expected, EasySizes.Of(new VisualModeSettings { Intensity = intensity, VisualOpacity = opacity }));
    }

    [Theory]
    [InlineData(49.4, 50)]
    [InlineData(50, 50.6)]
    public void 다른_정수로_보이는_값은_단계가_아니다(double intensity, double opacity)
    {
        Assert.Null(EasySizes.Of(new VisualModeSettings { Intensity = intensity, VisualOpacity = opacity }));
    }

    [Theory]
    [InlineData(49.5, 50, EasySize.Medium)]   // 슬라이더에 "50" 으로 보인다
    [InlineData(50.5, 50, EasySize.Medium)]   // 이것도 "50" 이다(.NET 의 F0 는 꼭 .5 인 값을 짝수 쪽으로 적는다)
    [InlineData(50, 49.5, EasySize.Medium)]
    [InlineData(50, 50.5, EasySize.Medium)]
    [InlineData(29.5, 39.5, EasySize.Small)]
    [InlineData(30.5, 40.5, EasySize.Small)]
    [InlineData(75, 69.5, EasySize.Large)]
    [InlineData(75, 70.5, EasySize.Large)]
    public void 꼭_x점5_인_값도_슬라이더에_단계의_수로_보이면_그_단계다(double intensity, double opacity, EasySize expected)
    {
        // 예전에는 단계 값과의 차이가 0.5 보다 작아야 했다. 49.5 는 슬라이더에 "50" 으로 보이는데도 보통이 아니어서, 자세한 설정은 50 이라
        // 하고 쉬운 설정은 "자세한 설정에서 맞춘 크기"라고 했다.
        var settings = new VisualModeSettings { Intensity = intensity, VisualOpacity = opacity };
        Assert.Equal(ValueText.Format(expected.Intensity()), ValueText.Format(intensity));
        Assert.Equal(ValueText.Format(expected.Opacity()), ValueText.Format(opacity));

        Assert.Equal(expected, EasySizes.Of(settings));
        Assert.Equal(EasySizeControl.Size, EasySizes.DetailControl(settings));
    }

    [Theory]
    [InlineData(74.5, 70, "74")]   // 홀수인 75 의 양옆 .5 는 짝수인 74·76 으로 보인다
    [InlineData(75.5, 70, "76")]
    public void 꼭_x점5_인_값이_다른_수로_보이면_단계가_아니다(double intensity, double opacity, string shown)
    {
        // 차이가 0.5 이하인지로 따지면 이 값들이 크게가 되어, 자세한 설정은 74 라 하고 쉬운 설정은 크게(75)를 선택해 보인다.
        var settings = new VisualModeSettings { Intensity = intensity, VisualOpacity = opacity };
        Assert.Equal(shown, ValueText.Format(intensity));

        Assert.Null(EasySizes.Of(settings));
        Assert.Equal(EasySizeControl.Size, EasySizes.DetailControl(settings));
    }

    [Fact]
    public void 단계인지는_슬라이더의_값_글자가_단계의_수와_같은지와_늘_같다()
    {
        // 단계마다 크기와 진하기를 ±0.7 안에서 0.01 씩 움직여 본다. 꼭 .5 인 값(1/100 로 나누어떨어지지 않는 이진 소수 곁의 값 포함)도 지나간다.
        foreach (var step in EasySizes.All)
        {
            for (int i = -70; i <= 70; i++)
            {
                double size = step.Intensity() + i / 100.0;
                double opacity = step.Opacity() + i / 100.0;
                bool sizeShownAsStep = ValueText.Format(size) == ValueText.Format(step.Intensity());
                bool opacityShownAsStep = ValueText.Format(opacity) == ValueText.Format(step.Opacity());

                Assert.Equal(sizeShownAsStep, EasySizes.Of(new VisualModeSettings { Intensity = size, VisualOpacity = step.Opacity() }) == step);
                Assert.Equal(opacityShownAsStep, EasySizes.Of(new VisualModeSettings { Intensity = step.Intensity(), VisualOpacity = opacity }) == step);
            }
        }
    }

    [Theory]
    [InlineData(63, 50, false, EasySizeControl.Size)]
    [InlineData(50, 90, false, EasySizeControl.Opacity)]
    [InlineData(30, 70, false, EasySizeControl.Opacity)]
    [InlineData(63, 90, false, EasySizeControl.Size)]
    [InlineData(50, 50, true, EasySizeControl.SizeLock)]
    [InlineData(63, 90, true, EasySizeControl.SizeLock)]
    [InlineData(75, 70, false, EasySizeControl.Size)]
    public void 자세한_설정_칸은_단계에_맞지_않게_만든_첫_칸으로_간다(double intensity, double opacity, bool sizeLock, EasySizeControl expected)
    {
        // 모드 카드 안의 차례(크기 고정 → 크기 → 진하기)로 고른다. 단계에 맞으면 크기다(#427).
        var settings = new VisualModeSettings { Intensity = intensity, VisualOpacity = opacity, IntensityAsOpacity = sizeLock };

        Assert.Equal(expected, EasySizes.DetailControl(settings));
    }

    [Fact]
    public void 처음_값인지는_모든_속성을_본다()
    {
        // 모드 설정에 값을 더해도 쉬운 설정의 기본 화면 판단(AppSettings.EasySettings)이 그 값을 빠뜨리지 않는다.
        Assert.True(new VisualModeSettings().HasDefaultValues());
        foreach (var property in typeof(VisualModeSettings).GetProperties())
        {
            var settings = new VisualModeSettings();
            object changed = property.PropertyType == typeof(bool) ? !(bool)property.GetValue(settings)! : (double)property.GetValue(settings)! + 1;
            property.SetValue(settings, changed);

            Assert.False(settings.HasDefaultValues(), property.Name);
            Assert.False(settings.HasSameValues(new VisualModeSettings()), property.Name);
        }
    }

    [Fact]
    public void 복사본은_원본과_따로_움직인다()
    {
        var original = new VisualModeSettings { Intensity = 70, IsGlowMode = true };

        var copy = original.Copy();
        copy.Intensity = 20;

        Assert.True(original.Copy().HasSameValues(original));
        Assert.Equal(70, original.Intensity);
        Assert.True(copy.IsGlowMode);
    }
}
