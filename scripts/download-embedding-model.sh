#!/usr/bin/env bash
# Downloads the embedding model of the semantic layer (DA-174): intfloat/multilingual-e5-small,
# its ONNX export and its SentencePiece tokenizer, at a pinned revision, and checks each file
# against a pinned SHA-256. The model never goes into git; in the devcontainer it lands in the
# lifegraph_models_dev volume, so a rebuild keeps it.
#
#   scripts/download-embedding-model.sh            # into $Semantic__Embeddings__ModelDirectory, or
#                                                  # ~/models/multilingual-e5-small (appsettings.Development.json)
#   scripts/download-embedding-model.sh <dir>      # into another directory
#
# Idempotent: a file already there with the right hash is not downloaded again.

set -euo pipefail

REPOSITORY="intfloat/multilingual-e5-small"
REVISION="614241f622f53c4eeff9890bdc4f31cfecc418b3"
FILES=(
  "onnx/model.onnx ca456c06b3a9505ddfd9131408916dd79290368331e7d76bb621f1cba6bc8665"
  "onnx/sentencepiece.bpe.model cfc8146abe2a0488e9e2a0c56de7952f7c11ab059eca145a0a727afce0db2865"
)

log() { echo "[embedding-model] $*"; }
fail() { echo "[embedding-model] ERROR: $*" >&2; exit 1; }

TARGET="${1:-${Semantic__Embeddings__ModelDirectory:-$HOME/models/multilingual-e5-small}}"
command -v curl > /dev/null || fail "curl is required."
command -v sha256sum > /dev/null || fail "sha256sum is required."
mkdir -p "$TARGET"

for entry in "${FILES[@]}"; do
  path="${entry% *}"
  expected="${entry#* }"
  file="$TARGET/$(basename "$path")"
  if [ -f "$file" ] && [ "$(sha256sum "$file" | cut -d' ' -f1)" = "$expected" ]; then
    log "$(basename "$path") already present."
    continue
  fi

  log "Downloading $path (revision ${REVISION:0:12})..."
  curl --fail --location --silent --show-error --retry 3 --max-time 1800 \
    --output "$file.partial" \
    "https://huggingface.co/$REPOSITORY/resolve/$REVISION/$path"
  actual="$(sha256sum "$file.partial" | cut -d' ' -f1)"
  if [ "$actual" != "$expected" ]; then
    rm -f "$file.partial"
    fail "$path has SHA-256 $actual, expected $expected."
  fi
  mv "$file.partial" "$file"
done

log "Model ready in $TARGET."
