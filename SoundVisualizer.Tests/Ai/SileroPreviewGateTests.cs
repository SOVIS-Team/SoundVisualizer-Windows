// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using SoundVisualizer.Ai;

namespace SoundVisualizer.Tests.Ai;

/// <summary>말소리 미리보기를 받아들이는 규칙. 모바일 SileroPreviewGateTest 다섯 개를 그대로 옮겼다(#422).</summary>
public class SileroPreviewGateTests
{
    /// <summary>프레임 100 뒤의 소리만 보고, 1000 전에만 받아들이며, 1300 에 사라지는 세대 7 의 후보.</summary>
    private static SileroPreviewGate<string> Gate()
    {
        var gate = new SileroPreviewGate<string>();
        gate.Regular(new SileroPreviewGate<string>.Candidate("parent", 100, 1000, 1300, 7));
        return gate;
    }

    [Fact] // needsThreeContiguousFreshBlocks
    public void 새로_들어온_이어진_덩어리_세_개가_필요하다()
    {
        var gate = Gate();
        Assert.Null(gate.Block(7, 0, 100, 1f, 100)); // 정규 추론이 이미 본 소리
        Assert.Null(gate.Block(7, 100, 200, 1f, 200));
        Assert.Null(gate.Block(7, 200, 300, 1f, 300));
        Assert.NotNull(gate.Block(7, 300, 400, 1f, 400));
        Assert.Null(gate.Block(7, 400, 500, 1f, 500)); // 한 후보에 한 번만
    }

    [Fact] // lowNonfiniteOrMissingBlockResetsEvidence
    public void 낮거나_유한하지_않거나_빠진_덩어리는_증거를_처음부터_다시_센다()
    {
        foreach (float p in new[] { .89f, float.NaN, float.PositiveInfinity })
        {
            var gate = Gate();
            gate.Block(7, 100, 200, 1f, 200);
            gate.Block(7, 200, 300, p, 300);
            Assert.Null(gate.Block(7, 300, 400, 1f, 400));
        }
        var skipped = Gate();
        skipped.Block(7, 100, 200, 1f, 200);
        skipped.Block(7, 200, 300, 1f, 300);
        Assert.Null(skipped.Block(7, 400, 500, 1f, 500)); // 300~400 이 빠졌다
    }

    [Fact] // deadlineAndGenerationRejectLateWork
    public void 마감이_지났거나_세대가_다른_덩어리는_받아들이지_않는다()
    {
        var gate = Gate();
        gate.Block(7, 100, 200, 1f, 200);
        gate.Block(7, 200, 300, 1f, 300);
        Assert.Null(gate.Block(6, 300, 400, 1f, 400));
        Assert.Null(gate.Block(7, 300, 400, 1f, 1000));
        Assert.Null(gate.View(1001));
    }

    [Fact] // nextRegularStartCannotAcceptMoreButDoesNotPrematurelyRetract
    public void 다음_정규_추론이_시작되면_새로_받지_않지만_받아들인_것을_일찍_거두지도_않는다()
    {
        var gate = Gate();
        for (int i = 1; i <= 3; i++) gate.Block(7, i * 100L, (i + 1) * 100L, 1f, i * 100L);
        gate.RegularStarted();
        Assert.Equal("parent", gate.View(1100)?.Parent);
        Assert.Null(gate.View(1300));
        gate.Regular(null); // 새 정규 결과, 위협음, 멈춤, 초기화
        Assert.Null(gate.View(1100));
    }

    [Fact] // newParentDoesNotInheritOldEvidence
    public void 새_후보는_이전_후보의_증거를_물려받지_않는다()
    {
        var gate = Gate();
        gate.Block(7, 100, 200, 1f, 200);
        gate.Block(7, 200, 300, 1f, 300);
        gate.Regular(new SileroPreviewGate<string>.Candidate("new", 300, 1000, 1300, 7));
        Assert.Null(gate.Block(7, 300, 400, 1f, 400));
    }

    [Fact]
    public void 받아들인_것은_후보의_부모와_만료와_마감을_그대로_담는다()
    {
        var gate = Gate();
        gate.Block(7, 100, 200, .9f, 200); // 0.9 는 넘는 것으로 본다
        gate.Block(7, 200, 300, .95f, 300);
        var accepted = gate.Block(7, 300, 400, 1f, 400);

        Assert.Equal(new SileroPreviewGate<string>.Accepted("parent", 1300, 400, 1000), accepted);
        Assert.Same(accepted, gate.View(1299));
    }
}
