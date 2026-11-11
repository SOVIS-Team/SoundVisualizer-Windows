// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using System.Globalization;
using System.Text.RegularExpressions;

namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 슬라이더 줄의 값 글자(Core 의 ValueText). 화면(Ui/ValueFormatConverter)과 쉬운 설정의 크기 단계(EasySizes)가 같은 함수를 써야
/// "슬라이더에 50 으로 보이는데 보통이 아니다" 같은 엇갈림이 생기지 않는다. 화면 쪽은 WPF 라 소스를 읽어 확인한다.
/// </summary>
public class ValueTextTests
{
    private static string Source(params string[] parts) => File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root, "SoundVisualizer" }.Concat(parts).ToArray()));

    [Theory]
    [InlineData(49.4, "49")]
    [InlineData(49.5, "50")]   // 꼭 .5 인 값은 짝수 쪽으로 적는다(.NET 의 F0). 49.5 와 50.5 가 모두 "50" 이다.
    [InlineData(49.6, "50")]
    [InlineData(50.4999, "50")]
    [InlineData(50.5, "50")]
    [InlineData(50.5001, "51")]
    [InlineData(74.5, "74")]
    [InlineData(75.5, "76")]
    [InlineData(100, "100")]
    public void 기본_꼴은_정수로_적는다(double value, string expected)
    {
        Assert.Equal(expected, ValueText.Format(value));
        Assert.Equal(expected, ValueText.Format(value, ""));
        Assert.Equal(expected + "%", ValueText.Format(value, "{0:F0}%"));
    }

    [Fact]
    public void 숫자는_Windows_지역_설정과_상관없이_같은_꼴이다()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("ar-EG");
            Assert.Equal("7.5", ValueText.Format(7.5, "{0:F1}"));
            Assert.Equal("60 FPS", ValueText.Format(60, "{0:F0} FPS"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void 같은_수로_보이는지는_기본_꼴의_글자로_견준다()
    {
        // 반올림을 따로 계산하지 않고 화면에 적는 글자 그대로 견준다. 그래서 꼭 .5 인 값을 어느 쪽으로 적든 화면과 어긋나지 않는다.
        Assert.True(ValueText.ShowsSameNumber(49.5, 50));
        Assert.True(ValueText.ShowsSameNumber(50.49, 50));
        Assert.True(ValueText.ShowsSameNumber(50.5, 50));
        Assert.False(ValueText.ShowsSameNumber(49.49, 50));
        Assert.False(ValueText.ShowsSameNumber(50.51, 50));
        Assert.False(ValueText.ShowsSameNumber(74.5, 75));
        Assert.False(ValueText.ShowsSameNumber(75.5, 75));
    }

    [Fact]
    public void 슬라이더_줄은_같은_함수로_글자를_만든다()
    {
        // 화면이 따로 반올림하면 크기 단계 판정과 다시 어긋난다.
        string converter = Source("Ui", "ValueFormatConverter.cs");
        Assert.Contains("ValueText.Format(value,", converter);
        Assert.DoesNotContain("string.Format(", converter);
        Assert.Contains("DefaultFormat = ValueText.DefaultFormat;", converter);
    }

    [Fact]
    public void 슬라이더마다_준_꼴도_수는_정수로_적는다()
    {
        // 크기 단계는 기본 꼴의 수로 견준다(ValueText.ShowsSameNumber). 크기·진하기 슬라이더에 소수 꼴("{0:F1}")을 주면 화면의 수와
        // 단계 판정이 다시 갈리므로, 지금 쓰는 꼴이 모두 정수 꼴에 단위만 붙인 것임을 지킨다.
        var formats = new[] { "LauncherWindow.xaml", "MainWindow.xaml" }
            .SelectMany(file => Regex.Matches(Source(file), "ui:SettingRow\\.ValueFormat=\"([^\"]*)\"").Select(match => match.Groups[1].Value))
            .ToArray();

        Assert.NotEmpty(formats);
        Assert.All(formats, format => Assert.Contains(ValueText.DefaultFormat, format));
    }
}
