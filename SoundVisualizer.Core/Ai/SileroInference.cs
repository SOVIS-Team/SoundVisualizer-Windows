// Copyright (C) 2026 amophi (SoundVisualizer Contributors)
// This file is part of SoundVisualizer.
// SoundVisualizer is free software: you can redistribute it and/or modify it under the terms of the GNU Affero General Public License as published by the Free Software Foundation, version 3.
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace SoundVisualizer.Ai
{
    /// <summary>
    /// Silero VAD 실행(Android 의 SileroInference.kt, #422). 16kHz 512샘플(32ms) 덩어리 하나마다 말소리일 확률 하나를 낸다.
    /// 소리 종류를 가르는 분류기가 아니다. 말소리 미리보기(<see cref="SileroPreviewGate{T}"/>)가 YAMNet 의 첫 대화음 후보를 확인하는 데만 쓴다.
    ///
    /// 모델은 v6.2.3(MIT)로 고정했고 불러올 때 크기와 SHA-256 을 Android 와 같은 값으로 확인한다(<see cref="SileroModelFile"/>).
    /// 순환 상태(2 × 1 × 128)를 덩어리마다 이어 받고, 입력 앞에 바로 앞 덩어리의 끝 64샘플을 붙인다(576샘플). 그래서 덩어리를
    /// 끊김 없이 차례로 넣어야 하고, 끊기면 <see cref="Reset"/> 한다. 세션 설정은 YAMNet 과 같다(연산 스레드 하나).
    ///
    /// PC 에서 바꾼 것: YAMNet 처럼 버퍼를 한 번만 잡아 IoBinding 으로 묶어 두어 덩어리마다 할당하지 않는다(Android 는 덩어리마다
    /// 텐서를 새로 만든다). 대신 스레드 안전하지 않으므로 한 스레드(<see cref="SileroStreamWorker"/>)만 부른다.
    /// </summary>
    public sealed class SileroInference : ISileroModel
    {
        public const string InputName = "input";
        public const string StateName = "state";
        public const string RateName = "sr";
        public const string OutputName = "output";
        public const string NextStateName = "stateN";

        /// <summary>모델이 받는 레이트. 덩어리 크기 512 는 이 레이트에서만 맞다.</summary>
        public const int SampleRate = 16000;

        /// <summary>덩어리 하나(32ms).</summary>
        public const int BlockSize = 512;

        /// <summary>입력 앞에 붙이는 바로 앞 덩어리의 끝.</summary>
        public const int ContextSize = 64;

        private const int StateLength = 2 * 1 * 128;

        private static readonly long[] InputShape = { 1, ContextSize + BlockSize };
        private static readonly long[] StateShape = { 2, 1, 128 };
        private static readonly long[] OutputShape = { 1, 1 };

        private readonly InferenceSession _session;
        private readonly float[] _input;
        private readonly float[] _state;
        private readonly float[] _nextState;
        private readonly float[] _output;
        private readonly long[] _rate;
        private readonly float[] _contextTail = new float[ContextSize];
        private readonly OrtValue _inputValue;
        private readonly OrtValue _stateValue;
        private readonly OrtValue _rateValue;
        private readonly OrtValue _outputValue;
        private readonly OrtValue _nextStateValue;
        private readonly RunOptions _runOptions;
        private readonly OrtIoBinding _binding;
        private bool _disposed;

        private SileroInference(InferenceSession session)
        {
            ValidateContract(session);
            _session = session;
            // 고정 힙에 둔다. ONNX Runtime 이 배열의 주소를 들고 있는 동안 GC 가 옮기지 않게 한다.
            _input = GC.AllocateArray<float>(ContextSize + BlockSize, pinned: true);
            _state = GC.AllocateArray<float>(StateLength, pinned: true);
            _nextState = GC.AllocateArray<float>(StateLength, pinned: true);
            _output = GC.AllocateArray<float>(1, pinned: true);
            _rate = GC.AllocateArray<long>(1, pinned: true);
            _rate[0] = SampleRate;
            try
            {
                _inputValue = OrtValue.CreateTensorValueFromMemory(_input, InputShape);
                _stateValue = OrtValue.CreateTensorValueFromMemory(_state, StateShape);
                _rateValue = OrtValue.CreateTensorValueFromMemory(_rate, Array.Empty<long>()); // 모양 [] 의 스칼라
                _outputValue = OrtValue.CreateTensorValueFromMemory(_output, OutputShape);
                _nextStateValue = OrtValue.CreateTensorValueFromMemory(_nextState, StateShape);
                _runOptions = new RunOptions();
                // 다음 상태는 입력 상태와 다른 칸에 받는다. 같은 칸을 입력과 출력에 함께 묶으면 계산 도중에 입력이 바뀔 수 있다.
                _binding = session.CreateIoBinding();
                _binding.BindInput(InputName, _inputValue);
                _binding.BindInput(StateName, _stateValue);
                _binding.BindInput(RateName, _rateValue);
                _binding.BindOutput(OutputName, _outputValue);
                _binding.BindOutput(NextStateName, _nextStateValue);
            }
            catch
            {
                ReleaseBuffers();
                throw;
            }
        }

        /// <summary>
        /// <paramref name="modelDirectory"/> 의 silero_vad.onnx 로 세션을 만든다. 파일을 한 번 읽어 확인한 바이트로 만든다.
        /// 실패해도 정규 분류는 그대로 돌고 미리보기만 꺼진다(<see cref="SileroStreamWorker"/>).
        /// </summary>
        /// <exception cref="FileNotFoundException">모델 파일이 없다.</exception>
        /// <exception cref="InvalidDataException">모델 파일이 배포한 것과 다르거나(크기·SHA-256), 입출력이 Silero 계약과 다르다.</exception>
        /// <exception cref="OnnxRuntimeException">ONNX Runtime 이 세션을 만들지 못했다.</exception>
        public static SileroInference Create(string modelDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(modelDirectory);
            byte[] model = ModelFileIntegrity.ReadVerified(Path.GetFullPath(modelDirectory), SileroModelFile.Onnx);

            InferenceSession session;
            using (var options = new SessionOptions
            {
                IntraOpNumThreads = 1,
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            })
            {
                session = new InferenceSession(model, options);
            }

            try
            {
                return new SileroInference(session);
            }
            catch
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    AppLog.Warn(nameof(SileroInference), "Failed to release a rejected Silero session", cleanupFailure);
                }
                throw;
            }
        }

        /// <summary>순환 상태와 앞 덩어리의 끝을 비운다. 덩어리가 끊기거나(밀린 것을 버림) 캡처를 다시 시작했을 때 부른다.</summary>
        public void Reset()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Array.Clear(_state);
            Array.Clear(_contextTail);
        }

        /// <summary>
        /// 16kHz 512샘플로 말소리일 확률(0~1)을 낸다. 상태와 앞 덩어리의 끝을 다음 덩어리로 넘긴다. 할당하지 않는다.
        /// </summary>
        /// <exception cref="ObjectDisposedException">닫은 뒤에 불렀다.</exception>
        /// <exception cref="InvalidDataException">모델이 0~1 밖이나 유한하지 않은 값을 냈다(Android 의 require 와 같다).</exception>
        public float Score(ReadOnlySpan<float> block)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (block.Length != BlockSize)
                throw new ArgumentException($"Expected {BlockSize} samples, got {block.Length}", nameof(block));

            _contextTail.CopyTo(_input, 0);
            block.CopyTo(_input.AsSpan(ContextSize));
            _session.RunWithBinding(_runOptions, _binding);
            float probability = _output[0];
            _nextState.CopyTo(_state, 0);
            block[(BlockSize - ContextSize)..].CopyTo(_contextTail);
            if (!float.IsFinite(probability) || probability < 0f || probability > 1f)
                throw new InvalidDataException($"Silero returned an invalid speech probability {probability}");
            return probability;
        }

        /// <summary>세션과 버퍼를 놓는다. 여러 번 불러도 된다. <see cref="Score"/> 와 겹쳐 부르면 안 된다(같은 스레드에서만 닫는다).</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseBuffers();
            _session.Dispose();
        }

        /// <summary>입력 세 개·출력 두 개의 이름과 자료형, 차원 수를 본다. 길이는 동적이라(-1) 실행할 때 정해진다.</summary>
        private static void ValidateContract(InferenceSession session)
        {
            string[] inputs = { InputName, StateName, RateName };
            string[] outputs = { OutputName, NextStateName };
            if (!session.InputNames.SequenceEqual(inputs))
                throw new InvalidDataException($"Expected inputs [{string.Join(", ", inputs)}], got [{string.Join(", ", session.InputNames)}]");
            if (!session.OutputNames.SequenceEqual(outputs))
                throw new InvalidDataException($"Expected outputs [{string.Join(", ", outputs)}], got [{string.Join(", ", session.OutputNames)}]");
            RequireTensor(session.InputMetadata[InputName], InputName, TensorElementType.Float, InputShape.Length);
            RequireTensor(session.InputMetadata[StateName], StateName, TensorElementType.Float, StateShape.Length);
            RequireTensor(session.InputMetadata[RateName], RateName, TensorElementType.Int64, 0);
            RequireTensor(session.OutputMetadata[OutputName], OutputName, TensorElementType.Float, OutputShape.Length);
            RequireTensor(session.OutputMetadata[NextStateName], NextStateName, TensorElementType.Float, StateShape.Length);
        }

        private static void RequireTensor(NodeMetadata metadata, string name, TensorElementType type, int rank)
        {
            if (!metadata.IsTensor || metadata.ElementDataType != type || metadata.Dimensions.Length != rank)
            {
                throw new InvalidDataException(
                    $"Expected '{name}' as {type} of rank {rank}, got {metadata.ElementDataType}[{string.Join(",", metadata.Dimensions)}]");
            }
        }

        private void ReleaseBuffers()
        {
            // 생성자 도중에 실패하면 일부는 아직 없다.
            _binding?.Dispose();
            _runOptions?.Dispose();
            _nextStateValue?.Dispose();
            _outputValue?.Dispose();
            _rateValue?.Dispose();
            _stateValue?.Dispose();
            _inputValue?.Dispose();
        }
    }

    /// <summary>
    /// 말소리 미리보기가 쓰는 모델 단계. 실제로는 <see cref="SileroInference"/> 이고, 테스트는 확률을 정해 주거나 덩어리 앞에서 붙잡아
    /// 두는 가짜로 바꿔 끼운다.
    /// </summary>
    internal interface ISileroModel : IDisposable
    {
        /// <inheritdoc cref="SileroInference.Reset"/>
        void Reset();

        /// <inheritdoc cref="SileroInference.Score"/>
        float Score(ReadOnlySpan<float> block);
    }

    /// <summary>
    /// 배포하는 Silero VAD 파일의 크기와 SHA-256. Android 의 SileroInference.SHA256 과 글자까지 같아야 하고, 모델을 바꾸면 두 앱에서 함께 고친다.
    /// 원본: snakers4/silero-vad v6.2.3, 커밋 5cd7945676eb32225748052e2e6a0580e4686a08 의 src/silero_vad/data/silero_vad.onnx
    /// (Android tools/prepare_silero_model.py 가 같은 해시로 확인해 받는다).
    /// </summary>
    internal static class SileroModelFile
    {
        public static readonly ModelFileIntegrity.ExpectedFile Onnx = new(
            "silero_vad.onnx",
            2_327_524L,
            "1a153a22f4509e292a94e67d6f9b85e8deb25b4988682b7e174c65279d8788e3");
    }
}
