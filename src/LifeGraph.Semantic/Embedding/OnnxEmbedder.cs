using LifeGraph.Graph.Contracts;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.Tokenizers;

namespace LifeGraph.Semantic.Embedding;

/// <summary>
/// multilingual-e5-small in the API process, through ONNX Runtime and the model's own XLM-R
/// SentencePiece tokenizer (DA-174): no text leaves the host, so a Node hidden from agents may be
/// embedded (DA-072). The model is loaded on the first embedding, not at start, so neither the
/// start nor readiness waits on 470 MB (BE-050); a load that fails is tried again by the next
/// job. Every failure of the runtime reaches the job as <see cref="EmbedderUnavailableException"/>
/// (BE-033), and a call past its deadline is stopped inside the runtime (BE-030).
/// </summary>
internal sealed class OnnxEmbedder(EmbeddingOptions options) : IEmbedder, IDisposable
{
    private const string HiddenStateOutput = "last_hidden_state";

    // The loaded model is this singleton's only state, guarded by the lock (GEN-024).
    private readonly SemaphoreSlim _loading = new(1, 1);
    private LoadedModel? _model;

    public string ModelId => options.ModelId!;

    public bool SendsTextOffHost => false;

    public async Task<ReadOnlyMemory<float>> EmbedPassageAsync(string text, CancellationToken cancellationToken)
    {
        var model = await LoadAsync(cancellationToken);
        return await Task.Run(() => model.Embed(E5Encoding.PassagePrefix + text, cancellationToken), cancellationToken);
    }

    public async Task<ReadOnlyMemory<float>> EmbedQueryAsync(string text, CancellationToken cancellationToken)
    {
        var model = await LoadAsync(cancellationToken);
        return await Task.Run(() => model.Embed(E5Encoding.QueryPrefix + text, cancellationToken), cancellationToken);
    }

    public void Dispose()
    {
        _model?.Dispose();
        _loading.Dispose();
    }

    private async Task<LoadedModel> LoadAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _model) is { } loaded)
        {
            return loaded;
        }

        await _loading.WaitAsync(cancellationToken);
        try
        {
            if (_model is null)
            {
                var model = await Task.Run(() => LoadedModel.From(options), cancellationToken);
                Volatile.Write(ref _model, model);
            }

            return _model!;
        }
        finally
        {
            _loading.Release();
        }
    }

    private sealed class LoadedModel : IDisposable
    {
        // The input tensors of a BERT-family ONNX export, as the model names them.
        private const string InputIdsInput = "input_ids";
        private const string AttentionMaskInput = "attention_mask";
        private const string TokenTypeIdsInput = "token_type_ids";

        private readonly SentencePieceTokenizer _tokenizer;
        private readonly InferenceSession _session;
        private readonly bool _takesTokenTypes;

        private LoadedModel(SentencePieceTokenizer tokenizer, InferenceSession session)
        {
            _tokenizer = tokenizer;
            _session = session;
            _takesTokenTypes = session.InputMetadata.ContainsKey(TokenTypeIdsInput);
        }

        public static LoadedModel From(EmbeddingOptions options)
        {
            if (!File.Exists(options.ModelPath) || !File.Exists(options.TokenizerPath))
            {
                throw new EmbedderUnavailableException("The embedding model files are missing; run scripts/download-embedding-model.sh.");
            }

            InferenceSession? session = null;
            try
            {
                using var tokenizerModel = File.OpenRead(options.TokenizerPath);
                var tokenizer = SentencePieceTokenizer.Create(tokenizerModel, addBeginningOfSentence: false, addEndOfSentence: false);
                session = new InferenceSession(options.ModelPath);
                var hiddenState = session.OutputMetadata.TryGetValue(HiddenStateOutput, out var metadata)
                    ? metadata
                    : throw new EmbedderUnavailableException($"The model has no {HiddenStateOutput} output.");
                if (hiddenState.Dimensions[^1] != ISemanticGraph.EmbeddingDimension)
                {
                    throw new EmbedderUnavailableException(
                        $"The model gives {hiddenState.Dimensions[^1]} numbers per token; the embeddings store {ISemanticGraph.EmbeddingDimension}.");
                }

                return new LoadedModel(tokenizer, session);
            }
            catch (Exception exception) when (exception is not EmbedderUnavailableException)
            {
                session?.Dispose();
                throw new EmbedderUnavailableException("The embedding model could not be loaded.", exception);
            }
            catch
            {
                session?.Dispose();
                throw;
            }
        }

        public ReadOnlyMemory<float> Embed(string text, CancellationToken cancellationToken)
        {
            var ids = E5Encoding.ModelInputIds(_tokenizer.EncodeToIds(text, addBeginningOfSentence: false, addEndOfSentence: false));
            var mask = new long[ids.Length];
            Array.Fill(mask, 1L);
            long[] shape = [1, ids.Length];

            using var runOptions = new RunOptions();
            using var stop = cancellationToken.Register(() => runOptions.Terminate = true);
            using var idsValue = OrtValue.CreateTensorValueFromMemory(ids, shape);
            using var maskValue = OrtValue.CreateTensorValueFromMemory(mask, shape);
            using var tokenTypesValue = _takesTokenTypes ? OrtValue.CreateTensorValueFromMemory(new long[ids.Length], shape) : null;
            var inputs = new Dictionary<string, OrtValue> { [InputIdsInput] = idsValue, [AttentionMaskInput] = maskValue };
            if (tokenTypesValue is not null)
            {
                inputs[TokenTypeIdsInput] = tokenTypesValue;
            }

            try
            {
                using var outputs = _session.Run(runOptions, inputs, [HiddenStateOutput]);
                return E5Encoding.MeanPooledUnitVector(outputs[0].GetTensorDataAsSpan<float>(), mask, ISemanticGraph.EmbeddingDimension);
            }
            catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (OnnxRuntimeException exception)
            {
                throw new EmbedderUnavailableException("The embedding model failed on a text.", exception);
            }
        }

        public void Dispose() => _session.Dispose();
    }
}
