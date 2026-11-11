// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Ai
{
    /// <summary>
    /// 말소리 미리보기를 받아들일지 정한다(Android 의 SileroPreviewGate.kt, #422).
    ///
    /// 정규 추론이 첫 대화음 후보(확정까지 한 프레임 남음)를 내면 후보를 건다(<see cref="Regular"/>). 그 뒤에 새로 들어온 소리로
    /// Silero 가 연달아 이어진 덩어리 세 개를 모두 확률 0.9 이상으로 보면 받아들인다. 받아들이는 것은 정해 둔 시각(다음 정규 틱 무렵)
    /// 전까지만이고, 받아들인 미리보기도 만료 시각까지만 보인다. 다음 정규 추론이 시작되면 새로 받아들이지 않지만, 이미 받아들인 것은
    /// 만료나 다음 정규 결과까지 둔다(<see cref="RegularStarted"/>).
    ///
    /// 상태는 잠금 하나로 지킨다. 잠금 안에서는 모델·PCM·입출력·대기를 하지 않는다. 시각 단위는 부르는 쪽이 정한다(파이프라인은 나노초).
    /// </summary>
    /// <typeparam name="T">후보의 부모(파이프라인은 그 정규 결과와 대화음 이름·확신도).</typeparam>
    internal sealed class SileroPreviewGate<T> where T : class
    {
        /// <summary>덩어리 하나를 말소리로 보는 확률.</summary>
        public const float SpeechProbability = 0.9f;

        /// <summary>받아들이는 데 필요한 연달아 이어진 덩어리 수(32ms × 3).</summary>
        public const int RequiredBlocks = 3;

        /// <param name="Parent">이 후보를 낸 정규 결과 쪽 정보.</param>
        /// <param name="FreshAfterFrame">이 캡처 프레임부터 들어온 소리만 본다(정규 추론이 본 소리는 다시 세지 않는다).</param>
        /// <param name="AcceptBeforeNs">이 시각 전에만 받아들인다.</param>
        /// <param name="ExpiresNs">받아들인 미리보기가 사라지는 시각.</param>
        /// <param name="Generation">링의 세대(<see cref="AiAudioBuffer.Cursor"/>). 캡처를 다시 시작한 뒤의 소리는 세지 않는다.</param>
        internal sealed record Candidate(T Parent, long FreshAfterFrame, long AcceptBeforeNs, long ExpiresNs, long Generation);

        /// <param name="Parent">받아들인 후보의 부모.</param>
        /// <param name="ExpiresNs">미리보기가 사라지는 시각.</param>
        /// <param name="AtNs">받아들인 시각.</param>
        /// <param name="AcceptBeforeNs">후보의 받아들임 마감. 게시하기 직전에 한 번 더 본다.</param>
        internal sealed record Accepted(T Parent, long ExpiresNs, long AtNs, long AcceptBeforeNs);

        private readonly object _lock = new();
        private Candidate? _candidate;
        private Accepted? _accepted;
        private int _streak;
        private long _previousEnd = -1;

        /// <summary>
        /// 새 정규 결과가 나왔다(또는 멈춤·초기화·매핑 변경·밀림으로 비운다). 후보를 <paramref name="candidate"/> 로 바꾸고 받아들인 것과
        /// 모아 둔 증거를 모두 버린다. 새 후보는 이전 후보의 증거를 물려받지 않는다.
        /// </summary>
        public void Regular(Candidate? candidate)
        {
            lock (_lock)
            {
                _candidate = candidate;
                _accepted = null;
                _streak = 0;
                _previousEnd = -1;
            }
        }

        /// <summary>정규 추론이 시작됐다. 새로 받아들이지 않되, 이미 받아들인 미리보기는 만료까지 둔다(일찍 거두지 않는다).</summary>
        public void RegularStarted()
        {
            lock (_lock)
            {
                _candidate = null;
                _streak = 0;
                _previousEnd = -1;
            }
        }

        /// <summary>
        /// Silero 덩어리 하나의 결과. 캡처 프레임 [<paramref name="start"/>, <paramref name="end"/>) 를 본 확률이다.
        /// 이번 덩어리로 받아들였으면 그 결과를, 아니면 null 을 돌려준다(한 후보에 한 번만 받아들인다).
        /// </summary>
        public Accepted? Block(long generation, long start, long end, float probability, long nowNs)
        {
            lock (_lock)
            {
                var candidate = _candidate;
                if (candidate is null) return null;
                if (_accepted is not null || candidate.Generation != generation || start < candidate.FreshAfterFrame) return null;
                if (nowNs >= candidate.AcceptBeforeNs)
                {
                    _candidate = null;
                    return null;
                }
                // 덩어리가 이어지지 않으면(밀려 건너뛰었으면) 처음부터 다시 센다.
                if (_previousEnd != -1 && _previousEnd != start) _streak = 0;
                _previousEnd = end;
                _streak = float.IsFinite(probability) && probability >= SpeechProbability ? _streak + 1 : 0;
                if (_streak < RequiredBlocks) return null;
                _accepted = new Accepted(candidate.Parent, candidate.ExpiresNs, nowNs, candidate.AcceptBeforeNs);
                return _accepted;
            }
        }

        /// <summary>지금 보일 미리보기. 받아들인 것이 없거나 만료됐으면 null.</summary>
        public Accepted? View(long nowNs)
        {
            lock (_lock)
            {
                return _accepted is { } accepted && nowNs < accepted.ExpiresNs ? accepted : null;
            }
        }
    }
}
