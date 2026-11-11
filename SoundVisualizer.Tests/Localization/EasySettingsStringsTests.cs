// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using SoundVisualizer.Localization;

namespace SoundVisualizer.Tests.Localization;

/// <summary>
/// 쉬운 설정(안드로이드 EasySettingsStringsTest, #408)의 문구. 쉬운 설정은 글을 덜 읽게 하려고 만든 화면이라, 한 언어라도 빠지면 그 언어
/// 사용자만 쉬운 화면에서 영어 낱말을 읽게 된다. 키가 모든 표에 같은지는 StringTableFilesTests 도 보지만, 여기서는 쉬운 설정의 문구가
/// 실제로 들어왔는지와 문구끼리의 약속(단추 글자 그대로 인용, 화면 읽어주기 이름)을 본다.
/// </summary>
public class EasySettingsStringsTests
{
    /// <summary>맨 위 두 칸(settings.view.*)과 쉬운 화면(easy.*)의 키.</summary>
    private static readonly string[] EasyKeys = StringTables.Get(UiLanguages.DefaultCode)!.Entries.Keys
        .Where(key => key.StartsWith("settings.view.", StringComparison.Ordinal) || key.StartsWith("easy.", StringComparison.Ordinal))
        .Order(StringComparer.Ordinal)
        .ToArray();

    private static readonly string[] Tabs = { "launcher.tab.home", "launcher.tab.settings", "launcher.tab.sounds", "launcher.tab.help", "launcher.tab.more_info" };

    public static TheoryData<string> Codes()
    {
        var data = new TheoryData<string>();
        foreach (var code in StringTables.AvailableCodes) data.Add(code);
        return data;
    }

    private static string Get(string code, string key)
    {
        Assert.True(StringTables.Get(code)!.TryGet(key, out var text), $"{code}: '{key}' 가 없습니다.");
        return text;
    }

    [Fact]
    public void 쉬운_설정_문구는_18개_언어_모두에_있다()
    {
        Assert.True(EasyKeys.Length >= 10, "쉬운 설정 문구를 찾지 못했습니다: " + string.Join(", ", EasyKeys));
        Assert.Equal(18, StringTables.AvailableCodes.Count);
        var missing = StringTables.AvailableCodes
            .SelectMany(code => EasyKeys.Where(key => !StringTables.Get(code)!.TryGet(key, out _)).Select(key => $"{code}: {key}"))
            .ToArray();
        Assert.True(missing.Length == 0, "쉬운 설정 문구는 모든 언어에 넣습니다: " + string.Join(", ", missing));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void 두_칸의_이름은_서로_다르고_탭_이름과도_다르다(string code)
    {
        string easy = Get(code, "settings.view.easy");
        string detailed = Get(code, "settings.view.detailed");
        Assert.NotEqual(easy, detailed);
        foreach (var tab in Tabs)
        {
            Assert.NotEqual(Get(code, tab), easy);
            Assert.NotEqual(Get(code, tab), detailed);
        }
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void 크기_세_단계의_낱말은_서로_다르다(string code)
    {
        var words = new[] { "easy.size.small", "easy.size.medium", "easy.size.large" }.Select(key => Get(code, key)).ToArray();
        Assert.Equal(3, words.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void 크기_안내는_자세한_설정을_단추_글자_그대로_인용한다(string code)
    {
        // 인용한 이름이 단추 글자와 다르면 사용자가 그 단추를 찾지 못한다.
        Assert.Contains(Get(code, "settings.view.detailed"), Get(code, "easy.size.custom"), StringComparison.Ordinal);
    }

    [Fact]
    public void 한국어_문구는_고르다_대신_선택을_쓴다()
    {
        // 화면의 동사는 '선택'이다(고르다·골라·고른 같은 꼴을 쓰지 않는다).
        var wrong = EasyKeys.Where(key => Get("ko", key) is var text && (text.Contains("고르", StringComparison.Ordinal)
            || text.Contains("골라", StringComparison.Ordinal) || text.Contains("고른", StringComparison.Ordinal) || text.Contains("골랐", StringComparison.Ordinal)))
            .ToArray();
        Assert.True(wrong.Length == 0, "고르다 대신 선택을 씁니다: " + string.Join(", ", wrong));
        Assert.Contains("선택", Get("ko", "easy.size.custom"), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void 소리_종류_줄의_읽는_이름에는_보이는_종류_이름이_들어_있다(string code)
    {
        // 보이는 글자를 말해 누르는 음성 조작이 이 줄을 찾을 수 있어야 한다(WCAG 2.5.3, 안드로이드 #434).
        // 종류 이름은 소리 탭의 것(sounds.type.*)을 그대로 쓰므로 그 이름이 바뀌어도 함께 따라간다.
        string pair = Get(code, "easy.name_pair");
        foreach (var type in new[] { "danger", "speech", "ambient" })
        {
            string shown = Get(code, "sounds.type." + type);
            string name = EasyNames.TypeRow(shown, Get(code, "settings.show_" + type), pair);
            Assert.Contains(shown, name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void 아랍어의_사람_소리_줄은_같은_말을_두_번_읽지_않는다()
    {
        // 종류 이름에만 관사가 없으면 스위치 이름(إظهار الأصوات البشرية) 안에서 찾지 못해 "사람 소리, 사람 소리 표시" 꼴로 겹쳐 읽힌다.
        // 환경음(الأصوات المحيطة)처럼 관사를 붙인 이름이어야 스위치 이름 하나로 읽힌다.
        Assert.Equal(Get("ar", "settings.show_speech"),
            EasyNames.TypeRow(Get("ar", "sounds.type.speech"), Get("ar", "settings.show_speech"), Get("ar", "easy.name_pair")));
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void 튜토리얼_범례의_종류_이름과_예시는_같은_글자가_아니다(string code)
    {
        // 범례는 이름과 예시를 나란히 보인다("사람 소리  사람 목소리"). 종류 이름을 바꾸면서 일본어·인도네시아어·태국어·터키어는
        // 새 이름이 원래 예시와 같은 낱말이 되어 "人の声  人の声" 처럼 겹쳤다. 이름을 바꿀 때는 예시도 함께 본다.
        foreach (var type in new[] { "ambient", "speech", "danger" })
        {
            Assert.False(
                string.Equals(Get(code, "sounds.type." + type).Trim(), Get(code, "tutorial.type." + type + "_examples").Trim(), StringComparison.OrdinalIgnoreCase),
                $"{code}: {type} 의 이름과 예시가 같습니다.");
        }
    }

    [Fact]
    public void 읽는_이름은_들어_있으면_자세한_설정의_스위치_이름_그대로이고_없으면_앞에_붙인다()
    {
        Assert.Equal(Get("ko", "settings.show_danger"),
            EasyNames.TypeRow(Get("ko", "sounds.type.danger"), Get("ko", "settings.show_danger"), Get("ko", "easy.name_pair")));
        Assert.Equal("Som de perigo, Mostrar sons de perigo", EasyNames.TypeRow("Som de perigo", "Mostrar sons de perigo", "{0}, {1}"));
        Assert.Equal("Voz", EasyNames.TypeRow("voz", "Voz", "{0}, {1}"));
        // 쉼표는 언어마다 다르다.
        Assert.Equal("a، b", EasyNames.TypeRow("a", "b", Get("ar", "easy.name_pair")));
    }
}
