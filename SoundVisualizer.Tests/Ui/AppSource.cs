// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Tests.Ui;

/// <summary>
/// 앱(SoundVisualizer 프로젝트)의 소스를 읽는 시험이 같이 쓰는 도우미. 런처와 오버레이는 WPF 창이라 단위 시험에서 띄우지 않고,
/// 화면과 규칙을 잇는 배선은 소스를 읽어 지킨다.
/// </summary>
internal static class AppSource
{
    /// <summary>SoundVisualizer 프로젝트 안의 파일 하나.</summary>
    public static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepositoryPaths.Root, "SoundVisualizer" }.Concat(parts).ToArray()));

    /// <summary>
    /// <paramref name="source"/> 에서 이름이 <paramref name="name"/> 인 void 메서드의 본문(여는 중괄호부터 짝이 맞는 닫는 중괄호까지).
    /// 부르는 곳이 아니라 정의를 찾도록 "void 이름(" 으로 찾는다. 문자열이나 주석 안의 중괄호는 가리지 않으므로, 본문에 짝이 맞지 않는
    /// 중괄호 글자가 든 메서드에는 쓰지 않는다.
    /// </summary>
    public static string MethodBody(string source, string name)
    {
        int start = source.IndexOf("void " + name + "(", StringComparison.Ordinal);
        Assert.True(start >= 0, name + " 를 찾지 못했습니다.");

        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException(name + " 의 끝을 찾지 못했습니다.");
    }
}
