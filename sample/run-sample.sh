#!/usr/bin/env bash
# Demonstrates using the run-once CLI tool against Sample.Migrations.dll
# Requires SAMPLE_CONNECTION_STRING env var (defaults to local SQL Server dev instance)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
TOOLS_DIR="${SCRIPT_DIR}/.tools"
NUPKG_DIR="${REPO_ROOT}/src/RunOnce.Cli/bin/Release"
CONNECTION_STRING="${SAMPLE_CONNECTION_STRING:-Server=localhost;Database=RunOnceSample;Integrated Security=true;TrustServerCertificate=true}"
ASSEMBLY="${SCRIPT_DIR}/Sample.Migrations/bin/Release/net8.0/Sample.Migrations.dll"

echo "==> Building solution..."
dotnet build "${REPO_ROOT}/RunOnce.sln" -c Release

echo "==> Packing CLI tool..."
dotnet pack "${REPO_ROOT}/src/RunOnce.Cli/RunOnce.Cli.csproj" -c Release --no-build

echo "==> Installing CLI tool locally to ${TOOLS_DIR}..."
rm -rf "${TOOLS_DIR}"
dotnet tool install RunOnce \
  --tool-path "${TOOLS_DIR}" \
  --add-source "${NUPKG_DIR}" \
  --version 1.0.0

RUN_ONCE="${TOOLS_DIR}/run-once"

echo ""
echo "==> run-once --version"
"${RUN_ONCE}" --version

echo ""
echo "==> run-once up (all work items, first run)"
UP_OUTPUT=$("${RUN_ONCE}" up \
  --assembly "${ASSEMBLY}" \
  --provider sql \
  --connection-string "${CONNECTION_STRING}" 2>&1)
echo "$UP_OUTPUT"
BATCH_ID=$(echo "$UP_OUTPUT" | grep '^Batch:' | awk '{print $2}')

echo ""
echo "==> run-once list"
"${RUN_ONCE}" list \
  --provider sql \
  --connection-string "${CONNECTION_STRING}"

echo ""
echo "==> run-once list-batches"
"${RUN_ONCE}" list-batches \
  --provider sql \
  --connection-string "${CONNECTION_STRING}"

echo ""
echo "==> run-once up --tags seed (only seed-tagged + untagged items; all already executed, so skipped)"
"${RUN_ONCE}" up \
  --assembly "${ASSEMBLY}" \
  --provider sql \
  --connection-string "${CONNECTION_STRING}" \
  --tags seed

echo ""
echo "==> run-once down --batch ${BATCH_ID}"
"${RUN_ONCE}" down \
  --assembly "${ASSEMBLY}" \
  --provider sql \
  --connection-string "${CONNECTION_STRING}" \
  --batch "${BATCH_ID}"

echo ""
echo "==> run-once up (re-run after rollback — all items execute again)"
"${RUN_ONCE}" up \
  --assembly "${ASSEMBLY}" \
  --provider sql \
  --connection-string "${CONNECTION_STRING}"

echo ""
echo "Done. Sample completed successfully."
