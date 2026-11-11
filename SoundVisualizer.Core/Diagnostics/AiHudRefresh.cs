// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
namespace SoundVisualizer.Diagnostics
{
    /// <summary>
    /// AI 가 새 결과를 게시했다는 알림을 개발자 HUD 의 "지금 한 번 읽기"로 바꾼다(모바일 AiHudRefresh.kt, #412).
    ///
    /// HUD 는 100ms 타이머로 읽는다. 그런데 말소리 미리보기는 다음 정규 결과가 오면 사라져 100ms 도 못 사는 일이 있어, 타이머만으로는
    /// 화면에도 기록(CSV)에도 한 번도 남지 않을 수 있다. 그래서 결과가 게시되면 타이머를 기다리지 않고 바로 읽는다. 타이머는 그대로 둔다.
    /// 결과가 오지 않는 동안(무음)의 나이와 소리 크기는 타이머가 읽는다.
    ///
    /// - <see cref="Notify"/> 는 추론 스레드(틱, 미리보기)에서 불린다. 읽는 일을 UI 스레드에 넘기기만 하고 바로 돌아온다. 넘기다 실패해도
    ///   던지지 않는다.
    /// - 넘긴 읽기가 아직 돌지 않았으면 다시 넘기지 않는다. 알림이 몰려도 UI 스레드에는 하나만 쌓이고, 읽을 때는 가장 새 결과를 읽는다
    ///   (알림은 값을 싣지 않는 깨우기다). 읽기 시작하면서 표시를 내리므로, 읽는 사이에 온 알림은 한 번 더 넘겨져 새 결과를 놓치지 않는다.
    /// - HUD 를 읽지 않는 동안(<see cref="Active"/> 가 false: 개발자 모드가 꺼졌거나 오버레이가 닫혔다)에는 아무것도 넘기지 않는다.
    ///   일반 사용자에게는 추론마다 UI 스레드를 깨우는 일이 생기지 않는다.
    /// </summary>
    public sealed class AiHudRefresh
    {
        private const string Tag = nameof(AiHudRefresh);

        private readonly Action<Action> _post;
        private readonly Action _read;
        private readonly Action _run;
        private volatile bool _active;
        private int _queued;
        private int _postFailed;

        /// <param name="post">받은 일을 UI 스레드에서 돌게 넘기고 기다리지 않고 돌아온다(오버레이는 <c>Dispatcher.InvokeAsync</c>).</param>
        /// <param name="read">HUD 를 한 번 읽는다. <paramref name="post"/> 가 넘긴 UI 스레드에서 불린다.</param>
        public AiHudRefresh(Action<Action> post, Action read)
        {
            ArgumentNullException.ThrowIfNull(post);
            ArgumentNullException.ThrowIfNull(read);
            _post = post;
            _read = read;
            _run = Run;
        }

        /// <summary>
        /// HUD 를 읽는 중인지(<see cref="AiDebugHud.IsPolling"/>). 기록은 HUD 를 읽는 동안에만 돌므로 이것 하나로 둘을 모두 가린다.
        /// UI 스레드가 바꾸고 추론 스레드가 읽는다.
        /// </summary>
        public bool Active
        {
            get => _active;
            set => _active = value;
        }

        /// <summary>새 결과가 게시됐다. 어느 스레드에서나 부른다. 기다리지 않고, 던지지 않는다.</summary>
        public void Notify()
        {
            if (!_active) return;
            // 이미 넘긴 읽기가 아직 돌지 않았다. 그 읽기가 이 결과도 읽는다.
            if (Interlocked.Exchange(ref _queued, 1) != 0) return;
            try
            {
                _post(_run);
            }
            catch (Exception ex)
            {
                // 창이 닫히는 중이거나 넘길 곳이 사라졌다. 추론 스레드로 예외가 새면 추론이 멈춘다. 다음 알림이 다시 넘겨 보게 표시를 내린다.
                Volatile.Write(ref _queued, 0);
                if (Interlocked.Exchange(ref _postFailed, 1) == 0) AppLog.Warn(Tag, "HUD 읽기를 UI 스레드에 넘기지 못했습니다.", ex);
            }
        }

        private void Run()
        {
            // 읽기 전에 내린다. 읽고 나서 내리면 읽는 사이에 게시된 결과의 알림이 "이미 넘겼다"에 걸려 버려진다.
            Volatile.Write(ref _queued, 0);
            if (!_active) return;
            _read();
        }
    }
}
