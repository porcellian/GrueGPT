#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
story_path="${1:-$repo_root/stories/zork1.z3}"

if [[ ! -f "$story_path" ]]; then
    printf 'SKIP: Zork I story file not found: %s\n' "$story_path"
    exit 0
fi

temp_dir="$(mktemp -d "${TMPDIR:-/tmp}/gruegpt-zork-regression.XXXXXX")"
trap 'rm -r "$temp_dir"' EXIT
transcript_path="$temp_dir/input.txt"
output_path="$temp_dir/output.txt"
error_path="$temp_dir/error.txt"
printf 'look\ninventory\nnorth\nsouth\nquit\nyes\n' > "$transcript_path"

if ! dotnet run \
    --project "$repo_root/src/GrueGPT.Cli/GrueGPT.Cli.csproj" \
    --configuration Release \
    -- "$story_path" \
    < "$transcript_path" > "$output_path" 2> "$error_path"; then
    printf 'FAIL: Zork I CLI session returned a failure status. Re-run the CLI command directly to inspect local output.\n' >&2
    exit 1
fi

for command in look inventory north south; do
    if ! grep -Fq "> $command" "$output_path"; then
        printf 'FAIL: CLI transcript did not echo the expected command prompt for "%s".\n' "$command" >&2
        exit 1
    fi
done

if grep -Fq 'Story execution stopped at' "$error_path"; then
    printf 'FAIL: Zork I execution reported an interpreter error.\n' >&2
    exit 1
fi

printf 'PASS: Zork I completed the local command regression transcript.\n'
